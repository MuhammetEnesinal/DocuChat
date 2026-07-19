// DocuChat.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Repositories.Common;
using DocuChat.Application.Interfaces.Repositories.Chat;
using DocuChat.Application.Interfaces.Repositories.Documents;
using DocuChat.Application.Interfaces.Repositories.Caching;
using DocuChat.Application.Mappings;
using DocuChat.Application.UseCases;
using DocuChat.Infrastructure.Persistence.Identity;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Infrastructure.Persistence.Context;
using DocuChat.Infrastructure.Persistence.Exceptions;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Persistence.Repositories.Common;
using DocuChat.Infrastructure.Persistence.Repositories.Chat;
using DocuChat.Infrastructure.Persistence.Repositories.Documents;
using DocuChat.Infrastructure.Persistence.Repositories.Caching;
using DocuChat.Infrastructure.Services.Ai.Embedding;
using DocuChat.Infrastructure.Services.Ai.Llm.Orchestration;
using DocuChat.Infrastructure.Services.Ai.Reranker;
using DocuChat.Infrastructure.Services.Ai.Retrieval;
using DocuChat.Infrastructure.Services.Auth;
using DocuChat.Infrastructure.Services.UserManagement;
using DocuChat.Infrastructure.Services.BackgroundJobs.DocumentProcessing;
using DocuChat.Infrastructure.Services.BackgroundJobs.Maintenance;
using DocuChat.Infrastructure.Services.Email;
using DocuChat.Infrastructure.Services.Storage;
using DocuChat.Infrastructure.Services.Documents.Parsing.Orchestration;

namespace DocuChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration cfg)
    {
        // DbContext + pgvector
        services.AddDbContext<AppDbContext>(o =>
            o.UseNpgsql(cfg.GetConnectionString("Default"),
                b => b.UseVector()));

        // Identity
        services.AddIdentity<AppUser, AppRole>(o =>
        {
            // Personel kodu (örn. EMP1001) yeni kullanıcının İLK ŞİFRESİ olarak kullanılıyor.
            // Bu yüzden şifre kuralları gevşektir: büyük/küçük harf + sembol zorunluluğu yok,
            // sadece minimum uzunluk. (Kullanıcı ilk girişte şifresini değiştirebilir.)
            o.Password.RequiredLength = 6;
            o.Password.RequireDigit = false;
            o.Password.RequireLowercase = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireNonAlphanumeric = false;
            o.User.RequireUniqueEmail = true;

            // Hesap bazlı kaba kuvvet koruması. Deneme sayacı kullanıcı satırında tutulduğundan
            // koruma IP'den bağımsız çalışır; aynı IP'yi paylaşan kullanıcılar birbirini etkilemez.
            // Kilit süresi kısa tutulur: bir hesaba kasten yanlış şifre gönderilerek o kullanıcının
            // kilitlenmesi mümkün olduğundan, amaç kalıcı engelleme değil denemeleri yavaşlatmaktır.
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders()
        .AddErrorDescriber<TurkishIdentityErrorDescriber>();

        // Repositories — sadece UoW kayıtlı; tüm repo'lara IUnitOfWork üzerinden erişilir.
        // Direct IXxxRepository injection KULLANILMAZ — convention: her zaman _uow.Xxx.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Token sayacı (tiktoken) — chunk + chat history bütçeleme
        services.AddSingleton<ITokenCounter, Services.Documents.Parsing.Chunking.TokenCounter>();

        // Application Use Cases
        services.AddScoped<IDocumentUseCase, DocumentUseCase>();
        services.AddScoped<IChatUseCase, ChatUseCase>();

        // Infrastructure Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<DocuChat.Application.Interfaces.Services.Departments.IDepartmentService,
                           Services.Departments.DepartmentService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IDocumentParser, DocumentParserService>();
        services.AddScoped<IVectorSearch, VectorSearchService>();
        services.AddScoped<IRetrievalPipeline, RetrievalPipelineService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IDbExceptionInspector, PostgresExceptionInspector>();
        services.AddScoped<JwtTokenService>();

        // HttpClient — Embedding
        services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
        {
            client.BaseAddress = new Uri(cfg["Embedding:BaseUrl"]
                ?? throw new InvalidOperationException("Embedding:BaseUrl config eksik."));
            var embeddingTimeout = cfg.GetValue<int>("Embedding:TimeoutSeconds", 300);
            client.Timeout = TimeSpan.FromSeconds(embeddingTimeout);

            var embeddingApiKey = cfg["Embedding:ApiKey"];
            if (!string.IsNullOrEmpty(embeddingApiKey))
                client.DefaultRequestHeaders.Add(
                    "Authorization", $"Bearer {embeddingApiKey}");
        });

        // HttpClient — LLM
        services.AddHttpClient<ILlmService, LlmService>(client =>
        {
            var provider = cfg["Llm:Provider"] ?? "OpenAI";
            
            // Gemini kendi URL'ini kullanıyor (LlmService'de hardcoded), diğerleri BaseUrl'den gelir
            var baseUrl = provider == "Gemini" 
                ? "https://generativelanguage.googleapis.com/v1beta/"  // dummy, LlmService override eder
                : (cfg["Llm:BaseUrl"] ?? throw new InvalidOperationException("Llm:BaseUrl config eksik."));
            
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(5);

            if (provider == "Anthropic")
            {
                client.DefaultRequestHeaders.Add("x-api-key", cfg["Llm:ApiKey"]);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else if (provider is "OpenAI" or "Ollama")
            {
                var llmApiKey = cfg["Llm:ApiKey"];
                if (!string.IsNullOrEmpty(llmApiKey))
                    client.DefaultRequestHeaders.Add(
                        "Authorization", $"Bearer {llmApiKey}");
            }
            // Gemini kendi URL'ini kullanıyor — header burada set edilmez
        });

        // HttpClient — BGE Reranker sidecar (Python FastAPI, port 8085)
        services.AddHttpClient<IRerankerService, BgeRerankerService>(client =>
        {
            client.BaseAddress = new Uri(cfg["Reranker:BaseUrl"] ?? "http://127.0.0.1:8085");
            var timeout = cfg.GetValue<int>("Reranker:TimeoutSeconds", 60);
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });

        // Memory cache — SizeLimit yoksa entry'lerin Size = N ayarı IGNORE edilir, cache sınırsız büyür.
        // 100K entry × ~5 KB (1024-dim BGE embedding) ≈ ~500 MB tavan. Eviction LRU mantığında çalışır.
        services.AddMemoryCache(o => o.SizeLimit = 100_000);

        // HttpClient — Mistral OCR
        services.AddHttpClient("Mistral", client =>
        {
            client.BaseAddress = new Uri(cfg["Mistral:BaseUrl"] ?? "https://api.mistral.ai");
            var timeout = int.TryParse(cfg["Mistral:TimeoutSeconds"], out var t) ? t : 120;
            client.Timeout = TimeSpan.FromSeconds(timeout);
            var apiKey = cfg["Mistral:ApiKey"]
                ?? throw new InvalidOperationException("Mistral:ApiKey config eksik.");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        });

        // Identity helpers
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();

        // Background services + processing queue
        services.AddHostedService<CacheCleanupService>();

        // Bounded concurrency queue — paralel max N belge işlenir (config'den)
        var maxConcurrentDocs = cfg.GetValue<int>("DocumentProcessing:MaxConcurrent", 2);
        var queueCapacity = cfg.GetValue<int>("DocumentProcessing:QueueCapacity", 1000);
        services.AddSingleton(sp => new DocumentProcessingQueue(queueCapacity));
        services.AddSingleton<DocuChat.Application.Interfaces.Services.Documents.IDocumentProcessingScheduler>(
            sp => sp.GetRequiredService<DocumentProcessingQueue>());
        services.AddHostedService(sp => new DocumentProcessingConsumer(
            sp.GetRequiredService<DocumentProcessingQueue>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<DocumentProcessingConsumer>>(),
            maxConcurrentDocs));

        // Recovery service queue'ya enqueue eder (Task.Run değil) — startup'ta rescue
        services.AddHostedService<DocumentRecoveryService>();

        // Mapster
        MappingConfig.Register();
        IdentityMappingConfig.Register();

        return services;
    }
}
