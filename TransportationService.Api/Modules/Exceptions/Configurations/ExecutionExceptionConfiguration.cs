using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Exceptions.Configurations;

public class ExecutionExceptionConfiguration : IEntityTypeConfiguration<ExecutionException>
{
    public void Configure(EntityTypeBuilder<ExecutionException> builder)
    {
        builder.ToTable("execution_exceptions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.Quantity).HasPrecision(12, 2);
        builder.Property(e => e.Latitude).HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasPrecision(9, 6);
        builder.Property(e => e.DispatcherNotes).HasMaxLength(4000);
        builder.Property(e => e.ResolutionNote).HasMaxLength(2000);

        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
        builder.HasIndex(e => new { e.TenantId, e.TripId });

        builder.HasOne<Trip>().WithMany().HasForeignKey(e => e.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TransportOrder>().WithMany().HasForeignKey(e => e.TransportOrderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TransportOrderStop>().WithMany().HasForeignKey(e => e.TransportOrderStopId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<CargoItem>().WithMany().HasForeignKey(e => e.CargoItemId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Photos)
            .WithOne()
            .HasForeignKey(p => p.ExecutionExceptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class ExceptionPhotoConfiguration : IEntityTypeConfiguration<ExceptionPhoto>
{
    public void Configure(EntityTypeBuilder<ExceptionPhoto> builder)
    {
        builder.ToTable("execution_exception_photos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FileName).IsRequired().HasMaxLength(255);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.StoragePath).IsRequired().HasMaxLength(300);

        builder.HasIndex(p => p.ExecutionExceptionId);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
