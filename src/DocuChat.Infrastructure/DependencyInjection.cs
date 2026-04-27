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
using DocuChat.Domain.Entities;

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
        .AddDefaultTokenProviders();

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Application Services
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IChatService, ChatService>();

        // Infrastructure Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDocumentParser, DocumentParserService>();
        services.AddScoped<IVectorSearch, VectorSearchService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();

        // HttpClient — Embedding
        services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
        {
            client.BaseAddress = new Uri(cfg["Embedding:BaseUrl"]!);
            client.Timeout = TimeSpan.FromMinutes(5);

            if (!string.IsNullOrEmpty(cfg["Embedding:ApiKey"]))
                client.DefaultRequestHeaders.Add(
                    "Authorization", $"Bearer {cfg["Embedding:ApiKey"]}");
        });

        // HttpClient — LLM
        services.AddHttpClient<ILlmService, LlmService>(client =>
        {
            client.BaseAddress = new Uri(cfg["Llm:BaseUrl"]!);
            client.Timeout = TimeSpan.FromMinutes(5);

            var provider = cfg["Llm:Provider"];

            if (provider == "Anthropic")
            {
                client.DefaultRequestHeaders.Add("x-api-key", cfg["Llm:ApiKey"]);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else if (provider == "OpenAI")
            {
                client.DefaultRequestHeaders.Add(
                    "Authorization", $"Bearer {cfg["Llm:ApiKey"]}");
            }
        });

        // Identity helpers
        services.AddHttpContextAccessor();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<ICurrentUser, CurrentUserService>();

        // Mapster
        MappingConfig.Register();

        return services;
    }
}