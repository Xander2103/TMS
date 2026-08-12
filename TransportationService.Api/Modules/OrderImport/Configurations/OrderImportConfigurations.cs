using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.OrderImport.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.OrderImport.Configurations;

public class OrderImportProfileConfiguration : IEntityTypeConfiguration<OrderImportProfile>
{
    public void Configure(EntityTypeBuilder<OrderImportProfile> builder)
    {
        builder.ToTable("order_import_profiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.MappingJson).IsRequired().HasMaxLength(4000);

        builder.HasIndex(p => new { p.TenantId, p.Name })
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class OrderImportBatchConfiguration : IEntityTypeConfiguration<OrderImportBatch>
{
    public void Configure(EntityTypeBuilder<OrderImportBatch> builder)
    {
        builder.ToTable("order_import_batches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(260);
        builder.Property(b => b.Sha256).IsRequired().HasMaxLength(64);
        builder.Property(b => b.Status).IsRequired().HasMaxLength(20);

        // Deliberately NOT unique: re-importing a corrected file with the same checksum after a
        // failed/dry run is allowed — the service refuses only when a real run already Processed it.
        builder.HasIndex(b => new { b.TenantId, b.Sha256 });

        builder.HasOne<OrderImportProfile>().WithMany().HasForeignKey(b => b.ProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(b => b.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class OrderImportRowConfiguration : IEntityTypeConfiguration<OrderImportRow>
{
    public void Configure(EntityTypeBuilder<OrderImportRow> builder)
    {
        builder.ToTable("order_import_rows");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Error).HasMaxLength(2000);
        builder.Property(r => r.ExternalReference).HasMaxLength(100);

        builder.HasIndex(r => new { r.BatchId, r.RowNumber });

        builder.HasOne<OrderImportBatch>().WithMany().HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(r => r.CreatedTransportOrderId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
