using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Authentication.Entities;

namespace TransportationService.Api.Modules.Authentication.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(200);
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(200);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        // Reuse detection revokes a whole rotation lineage at once.
        builder.HasIndex(t => new { t.UserId, t.FamilyId });

        // Concurrency guard: two simultaneous refreshes of the SAME token both try to stamp
        // RevokedAt; the loser's UPDATE matches zero rows and throws, so at most one rotation wins.
        builder.Property(t => t.RevokedAt).IsConcurrencyToken();
    }
}
