using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.CustomerPortal.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Configurations;

public class CustomerMessageConfiguration : IEntityTypeConfiguration<CustomerMessage>
{
    public void Configure(EntityTypeBuilder<CustomerMessage> builder)
    {
        builder.ToTable("customer_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body).IsRequired().HasMaxLength(4000);

        builder.HasIndex(m => new { m.TenantId, m.CustomerId, m.TransportOrderId, m.CreatedAt });

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class CustomerMessageReadConfiguration : IEntityTypeConfiguration<CustomerMessageRead>
{
    public void Configure(EntityTypeBuilder<CustomerMessageRead> builder)
    {
        builder.ToTable("customer_message_reads");

        builder.HasKey(r => r.Id);

        // One marker per (user, thread). Two filtered unique indexes — Postgres treats every
        // NULL as distinct, so a single index on the nullable TransportOrderId column would
        // silently allow duplicate general-thread (TransportOrderId IS NULL) marker rows.
        builder.HasIndex(r => new { r.TenantId, r.UserId, r.CustomerId, r.TransportOrderId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"TransportOrderId\" IS NOT NULL");
        builder.HasIndex(r => new { r.TenantId, r.UserId, r.CustomerId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"TransportOrderId\" IS NULL");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class PortalAnnouncementConfiguration : IEntityTypeConfiguration<PortalAnnouncement>
{
    public void Configure(EntityTypeBuilder<PortalAnnouncement> builder)
    {
        builder.ToTable("portal_announcements");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Body).IsRequired().HasMaxLength(4000);

        builder.HasIndex(a => new { a.TenantId, a.IsActive, a.ActiveFrom, a.ActiveUntil });

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
