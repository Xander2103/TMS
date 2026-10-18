using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Identity.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(250);
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.PreferredLanguageCode).HasMaxLength(5);
        builder.Property(u => u.ThemePreference).HasMaxLength(10);
        // MD-H11: one account per normalized e-mail per tenant (the typed form stays for display).
        builder.Property(u => u.NormalizedEmail).IsRequired().HasMaxLength(250);
        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
        builder.HasIndex(u => u.NormalizedEmail);
        builder.HasIndex(u => u.TenantId);
    }
}
