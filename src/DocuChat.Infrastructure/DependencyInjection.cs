// DocuChat.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Mappings;
using DocuChat.Application.UseCases;
using DocuChat.Infrastructure.Persistence.Identity;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Services.Ai.Embedding;
using DocuChat.Infrastructure.Services.Ai.Llm;
using DocuChat.Infrastructure.Services.Ai.Reranker;
using DocuChat.Infrastructure.Services.Ai.Retrieval;
using DocuChat.Infrastructure.Services.Auth;
using DocuChat.Infrastructure.Services.BackgroundJobs;
using DocuChat.Infrastructure.Services.Email;
using DocuChat.Infrastructure.Services.Storage;
using DocuChat.Infrastructure.Services.Documents;

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
            o.Password.RequiredLength = 8;
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders()
        .AddErrorDescriber<TurkishIdentityErrorDescriber>();

        // Repositories
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IQuestionCacheRepository, QuestionCacheRepository>();

        // Token sayacı (tiktoken) — chunk + chat history bütçeleme
        services.AddSingleton<ITokenCounter, Services.Documents.Parsing.Chunking.TokenCounter>();

        // Application Use Cases
        services.AddScoped<IDocumentUseCase, DocumentUseCase>();
        services.AddScoped<IChatUseCase, ChatUseCase>();

        // Infrastructure Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IDocumentParser, DocumentParserService>();
        services.AddScoped<IVectorSearch, VectorSearchService>();
        services.AddScoped<IRetrievalPipeline, RetrievalPipelineService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
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

        // Memory cache
        services.AddMemoryCache();

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

        // Background services
        services.AddHostedService<CacheCleanupService>();

        // Mapster
        MappingConfig.Register();
        IdentityMappingConfig.Register();

        return services;
    }
}
