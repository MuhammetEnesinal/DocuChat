using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using DocuChat.API.Filters;
using DocuChat.API.Middleware;
using DocuChat.Infrastructure;
using DocuChat.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Serilog;

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

    builder.Services.AddCors(o => o.AddPolicy("DefaultCors", p =>
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader()));

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
                error = "Çok fazla istek gönderdiniz.",
                message = $"Lütfen {retryAfter} saniye sonra tekrar deneyin.",
                retryAfterSeconds = retryAfter
            }, cancellationToken);
        };

        static string GetIp(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        options.AddPolicy("login", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        options.AddPolicy("register", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        options.AddPolicy("chat-ask", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        options.AddPolicy("upload", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
    });

    // AddInfrastructure calls AddIdentity<> which sets cookie as default scheme.
    // AddAuthentication (JWT) must come AFTER so it overrides those defaults.
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

    // Static files CORS'tan sonra — resimlere de CORS header'ı uygulanır
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
    app.UseAuthentication();
    app.UseAuthorization();
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
