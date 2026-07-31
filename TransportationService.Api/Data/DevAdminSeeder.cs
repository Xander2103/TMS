using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Data;

/// <summary>
/// Development-only: guarantees the seeded administrator (admin@dev.local) has a usable,
/// hashed password so it can log in. The password is only set when it is currently unset,
/// so a password changed deliberately (e.g. via an admin reset) is never overwritten on the
/// next startup. Must never run in Production (Program.cs gates this behind IsDevelopment).
/// </summary>
public static class DevAdminSeeder
{
    public const string Email = "admin@dev.local";
    public const string DevPassword = "Admin123!";

    public static async Task EnsurePasswordAsync(
        TransportationDbContext dbContext,
        IPasswordHasher passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var admin = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == Email, cancellationToken);

        if (admin is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(admin.PasswordHash))
        {
            admin.PasswordHash = passwordHasher.Hash(DevPassword);
            await dbContext.SaveChangesAsync(cancellationToken);
            // L6: credentials never go to a log — logs get shipped/retained far beyond the
            // console. The dev password is documented in docs/security/dev-setup.md.
            logger.LogWarning(
                "Development administrator password initialised for {Email} (see docs/security/dev-setup.md).",
                Email);
        }
        else
        {
            logger.LogInformation("Development administrator login: {Email} (password already set)", Email);
        }
    }
}
