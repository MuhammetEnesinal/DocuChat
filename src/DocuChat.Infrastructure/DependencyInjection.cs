// DocuChat.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Mappings;
using DocuChat.Application.Services;
using DocuChat.Infrastructure.Identity;
using DocuChat.Infrastructure.Persistence;
using DocuChat.Infrastructure.Persistence.Repositories;
using DocuChat.Infrastructure.Services;

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

        // Application Services
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IChatService, ChatService>();

        // Infrastructure Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IDocumentParser, DocumentParserService>();
        services.AddScoped<IVectorSearch, VectorSearchService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<JwtTokenService>();

        // HttpClient — Embedding
        // BaseAddress ve ApiKey burada set edilir — EmbeddingService constructor'da tekrar yapılmaz
        services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
        {
            client.BaseAddress = new Uri(cfg["Embedding:BaseUrl"]
                ?? throw new InvalidOperationException("Embedding:BaseUrl config eksik."));
            client.Timeout = TimeSpan.FromMinutes(5);

            var embeddingApiKey = cfg["Embedding:ApiKey"];
            if (!string.IsNullOrEmpty(embeddingApiKey))
                client.DefaultRequestHeaders.Add(
                    "Authorization", $"Bearer {embeddingApiKey}");
        });

        // HttpClient — LLM
        // Provider'a göre header set edilir — LlmService constructor'da tekrar yapılmaz
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

        // Identity helpers
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();

        // Background services
        services.AddHostedService<CacheCleanupService>();

        // Mapster
        MappingConfig.Register();

        return services;
    }
}