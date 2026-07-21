using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Configurations;

public class IssuedItemTemplateConfiguration : IEntityTypeConfiguration<IssuedItemTemplate>
{
    public void Configure(EntityTypeBuilder<IssuedItemTemplate> builder)
    {
        builder.ToTable("issued_item_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(80);
        builder.Property(t => t.ApplicableJobFunctionCodes).HasMaxLength(500);

        builder.HasIndex(t => new { t.TenantId, t.IsActive });
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class EmployeeIssuedItemConfiguration : IEntityTypeConfiguration<EmployeeIssuedItem>
{
    public void Configure(EntityTypeBuilder<EmployeeIssuedItem> builder)
    {
        builder.ToTable("employee_issued_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.NameSnapshot).IsRequired().HasMaxLength(150);
        builder.Property(i => i.CategorySnapshot).IsRequired().HasMaxLength(80);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.SerialNumber).HasMaxLength(100);
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.ReturnCondition).HasMaxLength(200);

        builder.HasIndex(i => new { i.TenantId, i.EmployeeId });

        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(i => i.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
