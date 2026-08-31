using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Seed;

public static class SeedData
{
    public static async Task SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var config = services.GetRequiredService<IConfiguration>();
        var env = services.GetRequiredService<IHostEnvironment>();

        foreach (var role in new[] { Roles.Admin, Roles.Manager, Roles.User })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole { Name = role });

        const string adminEmail = "admin@docuchat.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            // GÜVENLİK — ilk admin şifresi yapılandırmadan (Seed:AdminPassword / env SEED_ADMIN_PASSWORD)
            // okunur. Hardcoded varsayılan bir "default-credentials" açığıdır: her kurulumda aynı bilinen
            // şifreyle admin oluşurdu. Üretimde şifre MUTLAKA verilmeli; verilmezse başlatma durur.
            // Yalnız geliştirmede kolaylık için bilinen bir varsayılana düşülür.
            var adminPassword = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                if (!env.IsDevelopment())
                    throw new InvalidOperationException(
                        "Seed:AdminPassword ayarlı değil. Üretimde ilk admin şifresi SEED_ADMIN_PASSWORD " +
                        "ortam değişkeniyle verilmelidir (güçlü, rastgele bir değer).");
                adminPassword = "Admin123!";  // yalnız geliştirme ortamı
            }

            var admin = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Admin",
            };
            await userManager.CreateAsync(admin, adminPassword);
            await userManager.AddToRoleAsync(admin, Roles.Admin);
        }
    }
}
