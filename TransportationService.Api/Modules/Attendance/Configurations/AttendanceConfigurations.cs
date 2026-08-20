using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Attendance.Configurations;

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.ToTable("attendance_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ClockInSource).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ClockOutSource).HasConversion<string>().HasMaxLength(20);
        // Race-backstop bovenop de expliciete Version-check in de correctieservice:
        // gelijktijdige uitpunt-/correctiepaden verliezen nooit stil een schrijfactie.
        builder.Property(s => s.Version).IsConcurrencyToken();

        // Harde databankinvariant: maximaal één actieve (niet-uitgepunte) sessie per
        // medewerker — dubbelkliks, dubbele browsers en netwerkretries kunnen nooit een
        // tweede actieve sessie opleveren, ongeacht wat de applicatielaag doet.
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId })
            .IsUnique()
            .HasFilter("\"ClockOutAt\" IS NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("UX_attendance_sessions_active_per_employee");

        builder.HasIndex(s => new { s.TenantId, s.ClockInAt });
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.ClockInAt });
        builder.HasIndex(s => new { s.TenantId, s.Status });

        builder.HasOne<Employee>().WithMany().HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<KioskDevice>().WithMany().HasForeignKey(s => s.KioskDeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Location>().WithMany().HasForeignKey(s => s.LocationId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class AttendanceEventConfiguration : IEntityTypeConfiguration<AttendanceEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceEvent> builder)
    {
        builder.ToTable("attendance_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(1000);

        builder.HasIndex(e => new { e.TenantId, e.SessionId, e.OccurredAt });
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.OccurredAt });

        builder.HasOne<AttendanceSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AttendanceBreakConfiguration : IEntityTypeConfiguration<AttendanceBreak>
{
    public void Configure(EntityTypeBuilder<AttendanceBreak> builder)
    {
        builder.ToTable("attendance_breaks");

        builder.HasKey(b => b.Id);

        // Maximaal één open pauze per sessie (databankinvariant, zie sessies).
        builder.HasIndex(b => new { b.TenantId, b.SessionId })
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("UX_attendance_breaks_open_per_session");

        builder.HasIndex(b => new { b.TenantId, b.SessionId });
        builder.HasIndex(b => new { b.TenantId, b.EmployeeId, b.StartedAt });

        builder.HasOne<AttendanceSession>().WithMany().HasForeignKey(b => b.SessionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.ToTable("attendance_corrections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(500);

        builder.HasIndex(c => new { c.TenantId, c.SessionId });
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId, c.CreatedAt });

        builder.HasOne<AttendanceSession>().WithMany().HasForeignKey(c => c.SessionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class AttendanceCredentialConfiguration : IEntityTypeConfiguration<AttendanceCredential>
{
    public void Configure(EntityTypeBuilder<AttendanceCredential> builder)
    {
        builder.ToTable("attendance_credentials");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.SecretHash).IsRequired().HasMaxLength(500);
        builder.Property(c => c.LookupHash).IsRequired().HasMaxLength(100);

        // Identificatie: de code moet per tenant uniek zijn, anders is een PIN-only
        // kioskflow onmogelijk. Dit dwingt dat af op databankniveau.
        builder.HasIndex(c => new { c.TenantId, c.LookupHash })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_attendance_credentials_lookup");

        // Eén credential per medewerker (v1: één PIN; latere types versoepelen dit bewust).
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_attendance_credentials_employee");

        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class KioskDeviceConfiguration : IEntityTypeConfiguration<KioskDevice>
{
    public void Configure(EntityTypeBuilder<KioskDevice> builder)
    {
        builder.ToTable("kiosk_devices");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.SecretHash).IsRequired().HasMaxLength(100);

        builder.HasIndex(d => new { d.TenantId, d.Name })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<Location>().WithMany().HasForeignKey(d => d.LocationId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class AttendanceSettingsConfiguration : IEntityTypeConfiguration<AttendanceSettings>
{
    public void Configure(EntityTypeBuilder<AttendanceSettings> builder)
    {
        builder.ToTable("attendance_settings");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.TenantId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
