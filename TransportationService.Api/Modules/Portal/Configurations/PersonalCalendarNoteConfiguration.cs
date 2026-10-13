using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Portal.Entities;

namespace TransportationService.Api.Modules.Portal.Configurations;

public class PersonalCalendarNoteConfiguration : IEntityTypeConfiguration<PersonalCalendarNote>
{
    public void Configure(EntityTypeBuilder<PersonalCalendarNote> builder)
    {
        builder.ToTable("personal_calendar_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Title).IsRequired().HasMaxLength(120);
        builder.Property(n => n.Description).HasMaxLength(1000);
        builder.Property(n => n.Colour).IsRequired().HasMaxLength(20);
        builder.HasIndex(n => new { n.TenantId, n.EmployeeId, n.Date });
        builder.HasQueryFilter(n => !n.IsDeleted);
    }
}
