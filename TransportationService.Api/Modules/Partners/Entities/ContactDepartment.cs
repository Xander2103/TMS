using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>
/// Department of a customer contact (Planning, Boekhouding, Aankoop, ...). Tenant-managed
/// lookup so users can add their own values from the searchable combobox.
/// </summary>
public class ContactDepartment : LookupEntity
{
}
