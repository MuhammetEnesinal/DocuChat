using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using DocuChat.API.Common;
using DocuChat.Infrastructure;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Exceptions;
using DocuChat.Infrastructure.Persistence.Seed;
using DocuChat.Infrastructure.Services.Auth;
using DocuChat.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

    // Uygulama nginx arkasında çalışmaktadır. X-Forwarded-For başlığı işlenerek gerçek istemci
    // IP'si okunur; bu olmadan Connection.RemoteIpAddress nginx container'ının IP'sini döndürür
    // ve IP bazlı rate limit tüm kullanıcıları tek kovada toplar.
    // Güven sınırı yalnız Docker ağındaki proxy'dir: dışarıdan gelen sahte X-Forwarded-For
    // başlıkları dikkate alınmaz, aksi halde her istek için taze bir kova elde edilebilir.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;                 // yalnız en yakın proxy (nginx) dikkate alınır
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
        // Docker bridge ağı (172.16.0.0/12) — compose içindeki nginx buradan gelir.
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            IPAddress.Parse("172.16.0.0"), 12));
    });

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
            // Kimlik doğrulamalı uçlarda kova kullanıcı bazlı olduğundan, hangi hesabın limitine
            // takıldığını görebilmek için kullanıcı kimliği de loglanır.
            logger.LogWarning("Rate limit aşıldı. IP: {IP}, Kullanıcı: {User}, Path: {Path}, RetryAfter: {RetryAfter}s",
                ctx.HttpContext.Connection.RemoteIpAddress,
                ctx.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "(anonim)",
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

        // Anonim uçların bölümleme anahtarı: gerçek istemci IP'si.
        static string GetIp(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Kimlik doğrulamalı uçların bölümleme anahtarı: kullanıcı kimliği. Aynı ofisten veya NAT
        // arkasından gelen kullanıcılar tek IP paylaştığı için IP bazlı bölümleme onları aynı kovaya
        // toplar; kullanıcı kimliği her hesaba kendi kotasını verir. Kimlik yoksa IP'ye düşülür.
        // Bu anahtarın dolu olabilmesi için UseRateLimiter, UseAuthentication'dan sonra çağrılır.
        static string GetUserKey(HttpContext ctx)
        {
            var userId = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(userId) ? $"ip:{GetIp(ctx)}" : $"u:{userId}";
        }

        // login — istek anında kimlik doğrulanmamıştır, bu yüzden IP bazlıdır.
        // Hedefli kaba kuvvet denemelerini hesap bazlı kilit karşılar (Identity lockout: 5 hatalı
        // deneme, 15 dk). Buradaki sınır ise bir bot'un çok sayıda e-posta deneyerek hesap taraması
        // yapmasını engelleyen geniş bir ağdır; kalabalık bir ofisin tek IP'den giriş yapmasına
        // yetecek kadar geniş tutulur.
        options.AddPolicy("login", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // auth-write — şifre değiştirme. İstek kimlik doğrulamalı olduğu için kullanıcı bazlıdır.
        // "Mevcut şifre" alanının kaba kuvvetle denenmesini yavaşlatır.
        options.AddPolicy("auth-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // password-reset (forgot/reset password) — kimlik doğrulanmadığı için IP bazlıdır.
        // SMTP suistimalini (mail bombardımanı) sınırlar; aynı ofisten birkaç kişinin aynı anda
        // şifre sıfırlamasına yetecek genişlikte tutulur.
        options.AddPolicy("password-reset", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // chat-ask — her istek bir LLM çağrısı doğurduğu için sağlayıcı kotasını ve faturayı korur.
        // Kullanıcı bazlıdır: yoğun soru soran bir kişi diğerlerinin sohbetini engellemez.
        options.AddPolicy("chat-ask", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // feedback (spam koruması — saatte 30/kullanıcı)
        options.AddPolicy("feedback", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromHours(1), QueueLimit = 0
            }));

        // upload — toplu klasör yüklemesine yetecek genişlikte tutulur. Disk dolması ve OCR maliyeti
        // için üst sınır görevi görür; işleme kuyruğu en fazla 2 belgeyi paralel işlediğinden
        // DB ve disk yükü zaten doğal olarak sınırlıdır.
        options.AddPolicy("upload", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // reprocess — OCR, görsel açıklama ve bağlam üretimini birlikte çalıştıran en pahalı işlem.
        // Ele geçirilmiş bir yönetici hesabının API faturasını şişirmesini sınırlar.
        options.AddPolicy("reprocess", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // batch-delete — tek istekte çok sayıda kayıt siler ve disk temizliği yapar; gönderilen
        // kimlik listesi büyük olabildiğinden DB yükü yaratır.
        options.AddPolicy("batch-delete", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // admin-write — departman yönetimi ve tekil belge silme. Tekil silme de disk temizliği,
        // cache geçersizleştirme ve sohbet geçmişi temizliği yaptığından tek başına hafif olsa da
        // döngüye sokulduğunda yük çıkarır. Sınır, elle yapılan yönetim işlerinin çok üstünde
        // kalacak şekilde belirlenmiştir.
        options.AddPolicy("admin-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // user-write — kullanıcı oluşturma ve güncelleme işlemleri karşılama ve bilgilendirme
        // maili gönderdiğinden SMTP suistimaline açıktır.
        options.AddPolicy("user-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // read-heavy — maliyeti yüksek okuma uçları: önizleme diskten dosya akıtır, Excel şablonu
        // her çağrıda bellekte üretilir. Yetki denetiminin arkasında oldukları için veri sızdırmazlar,
        // ancak döngüye sokulduklarında I/O ve CPU yükü yaratırlar. Sayfalanmış hafif okuma uçları
        // bu politikanın dışındadır.
        options.AddPolicy("read-heavy", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // session-write — sohbet oturumunu yeniden adlandırma, arşivleme, sabitleme ve silme.
        // Kullanıcı yalnız kendi verisine dokunur, ancak her çağrı bir DB yazması olduğundan
        // döngüye sokulduğunda gereksiz yük yaratır.
        options.AddPolicy("session-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // Sohbet ucu için sistem geneli üst sınır: tüm kullanıcıların toplam LLM çağrısını sınırlar.
        // Kullanıcı bazlı politika tek bir kişinin taşkınlık yapmasını engeller, bu tavan ise
        // kullanıcı sayısı arttıkça toplam maliyetin büyümesini engeller. İkisi birlikte çalışır.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            ctx.Request.Path.StartsWithSegments("/api/chat/ask-stream")
                ? RateLimitPartition.GetFixedWindowLimiter("global-chat", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                })
                : RateLimitPartition.GetNoLimiter<string>("diger"));

        // Sayfalanmış hafif okuma uçları politikasızdır; korunanlar yazma işlemleri, pahalı
        // operasyonlar ve read-heavy kapsamındaki maliyetli okumalardır.
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

    // API endpoint'lerinde cookie auth yönlendirmelerini (302 login) bastırır.
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
    {
        // Taze kurulumda (Docker/yeni DB) bekleyen migration'lar otomatik uygulanır;
        // güncel DB'de no-op. Seed, şema garanti olduktan sonra çalışır.
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await SeedData.SeedRolesAndAdminAsync(scope.ServiceProvider);
    }

    // Pending/Processing'de kalan belgelerin recovery'si DocumentRecoveryService (IHostedService)
    // tarafından yapılır — orada queue'ya enqueue edilir, DocumentProcessingConsumer bounded
    // concurrency ile işler. Burada manuel Failed işaretleme YAPMA — recovery'i nullify eder.

    // Pipeline'ın başında çağrılır; sonraki middleware'lerin (loglama, rate limit) gerçek istemci
    // IP'sini görmesini sağlar.
    app.UseForwardedHeaders();

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

    // Auth middleware'leri static files'tan ÖNCE — /uploads check'inde ctx.User populated olmalı.
    app.UseAuthentication();
    app.UseAuthorization();

    // Rate limiter kimlik doğrulamadan sonra çalışır: kullanıcı bazlı politikalar bölümleme
    // anahtarını ctx.User üzerinden okur ve bu aşamadan önce ctx.User boştur.
    app.UseRateLimiter();

    // /uploads/* — authentication + DEPARTMAN yetkisi. Görsel içeriği = belge içeriği; departman
    // izolasyonu burada da uygulanmalı. Aksi halde authenticated herhangi bir kullanıcı, başka
    // departmanın görselini path'i bilerek çekebilir (CanAccessDocument'i atlayan tek delik).
    // Path yapısı: /uploads/{documentId}/img_xxx.jpg → ilk segment'ten departman çözülür.
    // JWT bearer header VEYA auth_token cookie üzerinden (OnMessageReceived event).
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/uploads", out var uploadRest))
        {
            if (!(ctx.User.Identity?.IsAuthenticated ?? false))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Admin tüm departmanlara erişir → departman kontrolü atlanır.
            if (!ctx.User.IsInRole(Roles.Admin))
            {
                // İlk path segmenti = belge ID'si (GUID). GUID değilse doğrulanamaz → reddet.
                var firstSeg = uploadRest.Value?.Trim('/').Split('/', 2)[0];
                if (!Guid.TryParse(firstSeg, out var uploadDocId))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
                var docDeptId = await db.Documents
                    .Where(d => d.Id == uploadDocId)
                    .Select(d => (Guid?)d.DepartmentId)
                    .FirstOrDefaultAsync();

                var userDepts = ctx.User.FindAll(AppClaimTypes.Department).Select(c => c.Value);
                if (docDeptId is null || !userDepts.Contains(docDeptId.Value.ToString()))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
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
