using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Configurations;

public class LeaveBalanceTypeConfiguration : IEntityTypeConfiguration<LeaveBalanceType>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceType> builder)
    {
        builder.ToTable("leave_balance_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.AbsenceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Colour).HasMaxLength(20);
        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        builder.HasOne<LeaveBalanceType>().WithMany().HasForeignKey(t => t.BalanceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class EmployeeLeaveBalanceConfiguration : IEntityTypeConfiguration<EmployeeLeaveBalance>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveBalance> builder)
    {
        builder.ToTable("employee_leave_balances");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.BaseEntitlementDays).HasPrecision(6, 2);
        builder.Property(b => b.CarryOverDays).HasPrecision(6, 2);
        builder.HasIndex(b => new { b.TenantId, b.EmployeeId, b.CalendarYear, b.BalanceTypeId }).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LeaveBalanceType>().WithMany().HasForeignKey(b => b.BalanceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class LeaveBalanceAdjustmentConfiguration : IEntityTypeConfiguration<LeaveBalanceAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceAdjustment> builder)
    {
        builder.ToTable("leave_balance_adjustments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Days).HasPrecision(6, 2);
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(a => new { a.TenantId, a.EmployeeLeaveBalanceId });
        builder.HasOne<EmployeeLeaveBalance>().WithMany().HasForeignKey(a => a.EmployeeLeaveBalanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

public class LeaveEntitlementSettingsConfiguration : IEntityTypeConfiguration<LeaveEntitlementSettings>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlementSettings> builder)
    {
        builder.ToTable("leave_entitlement_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DefaultAnnualEntitlementDays).HasPrecision(6, 2);
        builder.Property(s => s.MaxCarryOverDays).HasPrecision(6, 2);
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
