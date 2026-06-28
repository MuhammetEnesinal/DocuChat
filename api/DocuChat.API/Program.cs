using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using DocuChat.API.Filters;
using DocuChat.API.Middleware;
using DocuChat.Infrastructure;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Infrastructure.Persistence.Seed;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Serilog;

// Windows console varsayılan CP-1252 ile gelir; Serilog UTF-8 yazıyor → Türkçe bozuluyor.
// Hem giriş hem çıkışı UTF-8'e sabitle (log dosyaları zaten UTF-8).
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName());

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "DocuChat API", Version = "v1" });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Token'ı direkt yapıştırın, 'Bearer ' otomatik eklenir."
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id   = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        c.OperationFilter<FileUploadOperationFilter>();
    });

    builder.Services.AddValidatorsFromAssembly(
        Assembly.Load("DocuChat.Application"));

    var allowedOrigins = builder.Configuration
        .GetSection("AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];

    // AllowCredentials: cookie tabanlı JWT için gerekli (frontend axios.withCredentials=true).
    // .WithOrigins specific olmak zorunda — AllowCredentials ile birlikte wildcard izinsiz.
    builder.Services.AddCors(o => o.AddPolicy("DefaultCors", p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyMethod()
         .AllowAnyHeader()
         .AllowCredentials()));

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;

        options.OnRejected = async (ctx, cancellationToken) =>
        {
            ctx.HttpContext.Response.StatusCode = 429;
            ctx.HttpContext.Response.ContentType = "application/json";

            var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                ? (int)retryAfterValue.TotalSeconds
                : 60;

            ctx.HttpContext.Response.Headers.Append("Retry-After", retryAfter.ToString());

            var logger = ctx.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();
            logger.LogWarning("Rate limit aşıldı. IP: {IP}, Path: {Path}, RetryAfter: {RetryAfter}s",
                ctx.HttpContext.Connection.RemoteIpAddress,
                ctx.HttpContext.Request.Path,
                retryAfter);

            await ctx.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    message = $"Çok fazla istek gönderdiniz. Lütfen {retryAfter} saniye sonra tekrar deneyin.",
                    errors = Array.Empty<string>()
                },
                retryAfterSeconds = retryAfter
            }, cancellationToken);
        };

        static string GetIp(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // login (brute-force protection — public endpoint)
        options.AddPolicy("login", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // password-reset (forgot/reset password — public, e-mail spam koruması)
        options.AddPolicy("password-reset", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // chat-ask (LLM cüzdan koruması — Gemini free tier limiti var)
        options.AddPolicy("chat-ask", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // feedback (spam koruması — saatte 30/IP)
        options.AddPolicy("feedback", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromHours(1), QueueLimit = 0
            }));

        // upload (gevşek — toplu yükleme için bol, abuse'a karşı disk/OCR maliyet koruması)
        options.AddPolicy("upload", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // reprocess — Mistral OCR + Pixtral caption + LLM context generation. En pahalı op.
        // Admin hesabı compromise olsa bile API faturasını koruma.
        options.AddPolicy("reprocess", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // batch-delete — bulk DB write + disk cleanup. Ids[] array büyük olabilir, DB yükü.
        options.AddPolicy("batch-delete", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // user-write — admin user CRUD. Create/Update welcome+notice mail gönderiyor → SMTP spam vektörü.
        options.AddPolicy("user-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // NOT: Read endpoint'lerinde rate limit yok (GET ops). Sadece write/expensive ops koruma.
    });

    // AddInfrastructure calls AddIdentity<> which sets cookie as default scheme.
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };

        // Static dosya (/uploads/*) çağrılarında <img> tag'i Authorization header gönderemez —
        // tarayıcı sadece cookie'leri otomatik yollar. Bearer header yoksa auth_token cookie'sinden oku.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token))
                {
                    var cookieToken = ctx.Request.Cookies["auth_token"];
                    if (!string.IsNullOrEmpty(cookieToken))
                        ctx.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        };
    });

    // Suppress cookie auth redirects for API endpoints (belt-and-suspenders)
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
        await SeedData.SeedRolesAndAdminAsync(scope.ServiceProvider);

    // NOT: Pending/Processing'de kalan belgelerin recovery'si DocumentRecoveryService (IHostedService)
    // tarafından yapılır — orada queue'ya enqueue edilir, DocumentProcessingConsumer bounded
    // concurrency ile işler. Burada manuel Failed işaretleme YAPMA — recovery'i nullify eder.

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (diag, http) =>
        {
            diag.Set("IP", http.Connection.RemoteIpAddress?.ToString());
            diag.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
        };
    });

    app.UseCors("DefaultCors");

    app.UseRateLimiter();

    // Auth middleware'leri static files'tan ÖNCE — /uploads check'inde ctx.User populated olmalı.
    app.UseAuthentication();
    app.UseAuthorization();

    // /uploads/* sadece authenticated kullanıcılar için.
    // JWT bearer header VEYA auth_token cookie üzerinden (OnMessageReceived event).
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/uploads")
            && !(ctx.User.Identity?.IsAuthenticated ?? false))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next();
    });

    // Static files (CORS sonrası, auth check sonrası)
    var storagePath = builder.Configuration["Storage:LocalPath"] ?? "uploads";
    Directory.CreateDirectory(Path.GetFullPath(storagePath));
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.GetFullPath(storagePath)),
        RequestPath = "/uploads",
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream"
    });
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama başlatılamadı.");
}
finally
{
    Log.CloseAndFlush();
}
