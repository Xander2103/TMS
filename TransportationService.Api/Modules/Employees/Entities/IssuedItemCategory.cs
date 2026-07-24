using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// Tenant master data for issued-item/inventory categories (Kleding, Schoenen, IT, ...).
/// Templates reference a category by id and snapshot its name so historical records keep
/// their label after a rename.
/// </summary>
public class IssuedItemCategory : LookupEntity
{
}
