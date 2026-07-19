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

    // nginx arkasında çalışıyoruz: Connection.RemoteIpAddress her istekte nginx container'ının
    // IP'sini verir → rate limit tüm kullanıcılar için TEK kovaya düşer (bir kişinin hatalı
    // giriş denemeleri herkesi kilitler). X-Forwarded-For'u işleyerek gerçek istemci IP'sini alıyoruz.
    // GÜVENLİK: yalnız Docker ağındaki proxy'ye güveniliyor. Aksi halde dışarıdan sahte
    // X-Forwarded-For yollayan biri her istekte taze kova alıp rate limit'i tamamen bypass ederdi.
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
            // Kullanıcı da loglanıyor: kimlik doğrulamalı uçlarda kova kullanıcı bazlı olduğu için
            // "hangi IP" tek başına yetmez, kimin limiti dolmuş onu bilmek gerekir.
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

        // ANONİM uçlar için: gerçek istemci IP'si (ForwardedHeaders sayesinde artık doğru).
        static string GetIp(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // KİMLİK DOĞRULAMALI uçlar için: kullanıcı ID'si. IP'ye göre bölmek yetmez — aynı ofisten
        // NAT arkasından giren 20 kişi yine tek kovayı paylaşırdı. Asıl istenen "bir kullanıcı
        // diğerini etkilemesin"; bunu sağlayan tek anahtar kullanıcı kimliğidir.
        // Kimlik yoksa (henüz doğrulanmamış istek) IP'ye düşer.
        // NOT: çalışması için UseRateLimiter, UseAuthentication'dan SONRA gelmeli — yoksa ctx.User boştur.
        static string GetUserKey(HttpContext ctx)
        {
            var userId = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(userId) ? $"ip:{GetIp(ctx)}" : $"u:{userId}";
        }

        // login — kimlik henüz doğrulanmamış, kullanıcı ID'si yok; tek güvenilir anahtar IP.
        // Asıl kaba kuvvet koruması ARTIK BURADA DEĞİL: hesap bazlı lockout (Identity, 5 hatalı
        // deneme → 15 dk kilit) o işi IP'den bağımsız ve isabetli yapıyor.
        // Buradaki IP sınırı yalnız geniş bir ağ: bir bot'un binlerce e-posta deneyerek hesap
        // taraması yapmasını engeller. 5 yerine 20 — aynı ofisten/NAT arkasından giren onlarca
        // kişi birbirini kilitlemesin diye (5 iken tek IP'den dakikada 5 giriş demekti).
        options.AddPolicy("login", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // auth-write — şifre DEĞİŞTİRME (login'den farklı: burada kullanıcı zaten kimlik doğrulamış).
        // IP bazlı olsaydı aynı ofisten/NAT arkasından gelen kullanıcılar birbirini kilitlerdi.
        // Amaç "mevcut şifre" alanının kaba kuvvetle denenmesini yavaşlatmak → kullanıcı bazlı doğru anahtar.
        options.AddPolicy("auth-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // password-reset (forgot/reset password) — kimlik yok, IP bazlı. Amaç SMTP suistimali
        // (mail bombardımanı). 3 → 10: aynı ofisten birkaç kişi aynı anda şifre unutursa
        // kilitlenmesin; mail spam'ini durdurmak için 10/dk hâlâ yeterince dar.
        options.AddPolicy("password-reset", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // chat-ask (LLM cüzdan koruması — Gemini free tier limiti var). Kullanıcı bazlı:
        // bir kişinin çok soru sorması diğerlerinin sohbetini engellememeli.
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

        // upload (admin için yüksek — toplu klasör yüklemede sıkışmayı önle, abuse koruması yine de var:
        // disk dolma + Mistral cost; queue zaten max 2 paralel işliyor → DB/disk yükü doğal sınırlı).
        options.AddPolicy("upload", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // reprocess — Mistral OCR + Pixtral caption + LLM context generation. En pahalı op.
        // Admin hesabı compromise olsa bile API faturasını koruma.
        options.AddPolicy("reprocess", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // batch-delete — bulk DB write + disk cleanup. Ids[] array büyük olabilir, DB yükü.
        options.AddPolicy("batch-delete", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // admin-write — departman CRUD + TEKİL belge silme. Batch uçları ayrıca sınırlı, ama tekil
        // silme de disk temizliği + cache invalidation + sohbet geçmişi temizliği yapıyor: tek tek
        // ağır değil, döngüye sokulursa yük çıkarır. 30/dk insan kullanımına bol (kimse elle dakikada
        // 30 belge silmez), kaçak script'i sınırlar.
        options.AddPolicy("admin-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // user-write — admin user CRUD. Create/Update welcome+notice mail gönderiyor → SMTP spam vektörü.
        options.AddPolicy("user-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // read-heavy — okuma ama "ucuz okuma" değil: preview diskten dosya akıtır (I/O + bant
        // genişliği), chunks belgenin tüm metnini JSON'a serileştirir, bulk-import/template her
        // çağrıda bellekte Excel üretir (CPU). Döngüye sokulursa sistemi yorar — veri sızdırmaz,
        // hepsi yetki duvarının arkasında. 60/dk insan kullanımının çok üstünde (kimse dakikada
        // 60 belge önizlemez), yalnız kaçak script'i keser. Sayfalanmış hafif GET'lere dokunulmadı.
        options.AddPolicy("read-heavy", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // session-write — kendi sohbet oturumunu yeniden adlandırma/arşivleme/sabitleme/silme.
        // Kullanıcı yalnız kendi verisine dokunuyor, ama her çağrı bir DB write; döngüye sokulursa
        // gereksiz yük. Kullanıcı bazlı olduğu için kimse başkasını etkilemez — bu yüzden limit
        // rahat tutuldu (60/dk normal kullanımın çok üstünde, kaçak script'i yine de keser).
        options.AddPolicy("session-write", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(GetUserKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

        // GLOBAL TAVAN — yalnız chat/ask-stream için, tüm kullanıcılar toplamı.
        // Neden gerekli: politikaları kullanıcı bazlı yapınca en kötü durum LLM maliyeti
        // "10/dk" değil "10/dk × kullanıcı sayısı" oldu (50 kullanıcı = 500 çağrı/dk).
        // Eski (bozuk) IP-global kurulum bunu yan etki olarak sınırlıyordu; o koruma kalktı.
        // Bu tavan Mistral faturasını koruyan tek mekanizma. Kullanıcı başına limit ayrıca işler:
        // ikisi birlikte "tek kişi taşkınlık yapamaz + sistem toplamda şu kadarı aşamaz" verir.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            ctx.Request.Path.StartsWithSegments("/api/chat/ask-stream")
                ? RateLimitPartition.GetFixedWindowLimiter("global-chat", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                })
                : RateLimitPartition.GetNoLimiter<string>("diger"));

        // Read endpoint'lerinde rate limit yok (GET ops) — yalnız write/pahalı operasyonlar korunur.
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

    // EN BAŞTA olmalı: sonraki tüm middleware (loglama, rate limit) gerçek istemci IP'sini görsün.
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

    // Rate limiter AUTH'TAN SONRA: kimlik doğrulamalı politikalar kullanıcı ID'sine göre bölünüyor
    // (GetUserKey), bu da ctx.User'ın dolu olmasını gerektirir. Auth'tan önce çağrılırsa tüm
    // authenticated istekler "ip:..." anahtarına düşer ve kullanıcı bazlı ayrım kaybolur.
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
