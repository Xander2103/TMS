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
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Unit).HasMaxLength(30);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.StorageLocation).HasMaxLength(150);
        builder.Property(t => t.Version).IsConcurrencyToken();

        builder.HasIndex(t => new { t.TenantId, t.IsActive });
        builder.HasIndex(t => new { t.TenantId, t.CategoryId });
        builder.HasOne<IssuedItemCategory>().WithMany()
            .HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class IssuedItemAttributeDefinitionConfiguration : IEntityTypeConfiguration<IssuedItemAttributeDefinition>
{
    public void Configure(EntityTypeBuilder<IssuedItemAttributeDefinition> builder)
    {
        builder.ToTable("issued_item_attribute_definitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(d => new { d.TenantId, d.IsActive });
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class IssuedItemAttributeOptionConfiguration : IEntityTypeConfiguration<IssuedItemAttributeOption>
{
    public void Configure(EntityTypeBuilder<IssuedItemAttributeOption> builder)
    {
        builder.ToTable("issued_item_attribute_options");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Value).IsRequired().HasMaxLength(100);
        builder.HasIndex(o => new { o.TenantId, o.AttributeDefinitionId });
        builder.HasOne<IssuedItemAttributeDefinition>().WithMany()
            .HasForeignKey(o => o.AttributeDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}

public class IssuedItemTemplateAttributeConfiguration : IEntityTypeConfiguration<IssuedItemTemplateAttribute>
{
    public void Configure(EntityTypeBuilder<IssuedItemTemplateAttribute> builder)
    {
        builder.ToTable("issued_item_template_attributes");
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.TenantId, a.TemplateId, a.AttributeDefinitionId }).IsUnique();
        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(a => a.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemAttributeDefinition>().WithMany()
            .HasForeignKey(a => a.AttributeDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class IssuedItemVariantConfiguration : IEntityTypeConfiguration<IssuedItemVariant>
{
    public void Configure(EntityTypeBuilder<IssuedItemVariant> builder)
    {
        builder.ToTable("issued_item_variants");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Label).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Version).IsConcurrencyToken();
        builder.HasIndex(v => new { v.TenantId, v.TemplateId });
        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}

public class IssuedItemVariantValueConfiguration : IEntityTypeConfiguration<IssuedItemVariantValue>
{
    public void Configure(EntityTypeBuilder<IssuedItemVariantValue> builder)
    {
        builder.ToTable("issued_item_variant_values");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.AttributeNameSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Value).IsRequired().HasMaxLength(100);
        // Filtered on IsDeleted like every other soft-delete-aware unique index: a soft-deleted
        // value row must never keep occupying the (variant, attribute) key (corrections wave §6).
        builder.HasIndex(v => new { v.TenantId, v.VariantId, v.AttributeDefinitionId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasOne<IssuedItemVariant>().WithMany()
            .HasForeignKey(v => v.VariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemAttributeOption>().WithMany()
            .HasForeignKey(v => v.AttributeOptionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Reason).HasMaxLength(300);
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.HasIndex(m => new { m.TenantId, m.TemplateId, m.Timestamp });
        builder.HasIndex(m => new { m.TenantId, m.VariantId });
        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(m => m.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemVariant>().WithMany()
            .HasForeignKey(m => m.VariantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class ReorderProposalConfiguration : IEntityTypeConfiguration<ReorderProposal>
{
    public void Configure(EntityTypeBuilder<ReorderProposal> builder)
    {
        builder.ToTable("reorder_proposals");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        // At most one OPEN proposal per stock target (NULL variant split like inventory_alerts).
        builder.HasIndex(p => new { p.TenantId, p.TemplateId, p.VariantId })
            .IsUnique()
            .HasFilter("\"VariantId\" IS NOT NULL AND \"IsDeleted\" = false AND \"Status\" IN ('Proposed','Reviewed','Approved','Ordered')");
        builder.HasIndex(p => new { p.TenantId, p.TemplateId })
            .IsUnique()
            .HasFilter("\"VariantId\" IS NULL AND \"IsDeleted\" = false AND \"Status\" IN ('Proposed','Reviewed','Approved','Ordered')");
        builder.HasIndex(p => new { p.TenantId, p.Status });

        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(p => p.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemVariant>().WithMany()
            .HasForeignKey(p => p.VariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class InventoryAlertConfiguration : IEntityTypeConfiguration<InventoryAlert>
{
    public void Configure(EntityTypeBuilder<InventoryAlert> builder)
    {
        builder.ToTable("inventory_alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // One state row per stock target. Postgres treats NULLs as distinct, so the
        // template-level rows (VariantId IS NULL) need their own filtered unique index —
        // same split as message_templates.
        builder.HasIndex(a => new { a.TenantId, a.TemplateId, a.VariantId })
            .IsUnique().HasFilter("\"VariantId\" IS NOT NULL AND \"IsDeleted\" = false");
        builder.HasIndex(a => new { a.TenantId, a.TemplateId })
            .IsUnique().HasFilter("\"VariantId\" IS NULL AND \"IsDeleted\" = false");
        builder.HasIndex(a => new { a.TenantId, a.Status });

        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(a => a.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemVariant>().WithMany()
            .HasForeignKey(a => a.VariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(a => !a.IsDeleted);
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
        builder.Property(i => i.ConditionAtIssue).HasMaxLength(200);
        builder.Property(i => i.ReturnDisposition).HasMaxLength(20);

        builder.Property(i => i.VariantSnapshot).HasMaxLength(200);

        builder.HasIndex(i => new { i.TenantId, i.EmployeeId });
        builder.HasIndex(i => new { i.TenantId, i.Status, i.ExpectedReturnDate });

        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(i => i.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IssuedItemTemplate>().WithMany()
            .HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<IssuedItemVariant>().WithMany()
            .HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
