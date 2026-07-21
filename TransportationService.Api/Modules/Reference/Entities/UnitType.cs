using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Reference.Entities;

/// <summary>
/// A managed unit of measure for transport-order quantities (Colli, Europallet, Laadmeter,
/// Kilogram, ...). Tenant-editable lookup with a stable Code and a translated display Name,
/// so historical free-text values can be mapped onto a code without breaking.
/// </summary>
public class UnitType : LookupEntity
{
}
