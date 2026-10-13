using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tasks.Entities;

namespace TransportationService.Api.Modules.Tasks.Configurations;

public class TaskCategoryConfiguration : LookupEntityTypeConfiguration<TaskCategory>
{
    protected override string TableName => "task_categories";

    public override void Configure(EntityTypeBuilder<TaskCategory> builder)
    {
        base.Configure(builder);
        builder.Property(c => c.Color).HasMaxLength(7);
    }
}

public class EmployeeTaskConfiguration : IEntityTypeConfiguration<EmployeeTask>
{
    public void Configure(EntityTypeBuilder<EmployeeTask> builder)
    {
        builder.ToTable("employee_tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.CategorySnapshot).HasMaxLength(150);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.RelatedEntityType).HasMaxLength(50);
        builder.Property(t => t.RelatedEntityId).HasMaxLength(50);
        builder.Property(t => t.BlockedReason).HasMaxLength(1000);
        builder.Property(t => t.CompletionNote).HasMaxLength(2000);
        builder.Property(t => t.ReviewNote).HasMaxLength(1000);
        builder.Property(t => t.RecurrenceDedupeKey).HasMaxLength(200);
        // Race backstop on the status machine (works on Npgsql AND the SQLite test harness).
        builder.Property(t => t.Version).IsConcurrencyToken();

        builder.HasIndex(t => new { t.TenantId, t.AssignedEmployeeId, t.Status });
        builder.HasIndex(t => new { t.TenantId, t.DueAt });
        builder.HasIndex(t => new { t.TenantId, t.CreatedByUserId });
        builder.HasIndex(t => new { t.TenantId, t.RelatedEntityType, t.RelatedEntityId });
        builder.HasIndex(t => new { t.TenantId, t.RecurrenceDedupeKey })
            .IsUnique().HasFilter("\"RecurrenceDedupeKey\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(t => t.AssignedEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskCategory>().WithMany()
            .HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.ToTable("task_attachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Note).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.TaskId });
        builder.HasOne<EmployeeTask>().WithMany()
            .HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class TaskTemplateConfiguration : IEntityTypeConfiguration<TaskTemplate>
{
    public void Configure(EntityTypeBuilder<TaskTemplate> builder)
    {
        builder.ToTable("task_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(150);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.HasIndex(t => new { t.TenantId, t.IsActive });
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class TaskTemplateItemConfiguration : IEntityTypeConfiguration<TaskTemplateItem>
{
    public void Configure(EntityTypeBuilder<TaskTemplateItem> builder)
    {
        builder.ToTable("task_template_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Description).HasMaxLength(4000);
        builder.Property(i => i.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(i => new { i.TenantId, i.TemplateId });
        builder.HasOne<TaskTemplate>().WithMany()
            .HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TaskCategory>().WithMany()
            .HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}

public class TaskRecurrenceConfiguration : IEntityTypeConfiguration<TaskRecurrence>
{
    public void Configure(EntityTypeBuilder<TaskRecurrence> builder)
    {
        builder.ToTable("task_recurrences");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Interval).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => new { r.TenantId, r.IsActive });
        builder.HasOne<TaskTemplate>().WithMany()
            .HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(r => r.AssignedEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
