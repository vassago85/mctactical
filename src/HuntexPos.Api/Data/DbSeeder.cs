using System.Linq;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntexDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seedOpt = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await db.Database.EnsureCreatedAsync(ct);

        foreach (var r in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(r))
                await roleManager.CreateAsync(new IdentityRole(r));
        }

        if (!await db.PricingSettings.AnyAsync(ct))
        {
            db.PricingSettings.Add(new PricingSettings
            {
                DefaultMarginPercent = 50,
                DefaultFixedMarkup = 0,
                UseMarginPercent = true,
                DefaultTaxRate = 0,
                HideCostForSalesRole = true
            });
            await db.SaveChangesAsync(ct);
        }

        var ownerEmail = seedOpt.OwnerEmail?.Trim() ?? string.Empty;
        var ownerPassword = seedOpt.OwnerPassword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerEmail) || string.IsNullOrWhiteSpace(ownerPassword))
        {
            log.LogInformation(
                "Seed owner skipped: set Seed:OwnerEmail and Seed:OwnerPassword (e.g. env Seed__OwnerEmail / Seed__OwnerPassword) to create the first owner on startup.");
        }
        else if (await userManager.FindByEmailAsync(ownerEmail) == null)
        {
            var user = new ApplicationUser
            {
                UserName = ownerEmail,
                Email = ownerEmail,
                EmailConfirmed = true,
                DisplayName = "Owner"
            };
            var result = await userManager.CreateAsync(user, ownerPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(user, new[] { Roles.Owner, Roles.Admin, Roles.Dev, Roles.Sales });
                log.LogInformation("Seeded owner account for {Email}.", ownerEmail);
            }
            else
            {
                log.LogWarning("Could not seed owner {Email}: {Errors}", ownerEmail,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
