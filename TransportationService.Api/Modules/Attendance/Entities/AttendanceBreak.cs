using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Eén pauze binnen een attendance-sessie. Meerdere pauzes per sessie zijn normaal;
/// maximaal één pauze tegelijk open (gefilterde unieke index). Pauzes mogen over
/// middernacht lopen — duraties worden altijd uit UTC-tijdstippen berekend en kunnen
/// nooit negatief zijn. EmployeeId is gedenormaliseerd voor rapportagequery's.
/// </summary>
public class AttendanceBreak : AuditableTenantEntity
{
    public Guid SessionId { get; set; }
    public Guid EmployeeId { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
