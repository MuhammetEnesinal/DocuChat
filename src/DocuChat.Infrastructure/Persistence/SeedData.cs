using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Identity;

namespace DocuChat.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        foreach (var role in new[] { Roles.Admin, Roles.User })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole { Name = role });

        const string adminEmail = "admin@docuchat.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Admin",
            };
            await userManager.CreateAsync(admin, "Admin123!");
            await userManager.AddToRoleAsync(admin, Roles.Admin);
        }
    }
}