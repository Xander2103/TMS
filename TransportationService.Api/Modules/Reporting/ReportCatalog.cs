using TransportationService.Api.Modules.Identity;

namespace TransportationService.Api.Modules.Reporting;

public enum ReportKind
{
    /// <summary>Downloadable file built by an existing export endpoint.</summary>
    Export,
    /// <summary>An existing screen that functions as the report (dashboards, overviews).</summary>
    Page,
    /// <summary>Announced but not yet implemented — shown as "binnenkort" and never runnable.</summary>
    ComingSoon,
}

/// <summary>
/// One catalog entry. Reports are metadata over EXISTING endpoints/pages — the catalog never
/// duplicates report logic; adding a report means adding a definition here (and, for new
/// exports, the endpoint in its owning module per docs/proposals/2026-07-20-reporting-contract.md).
/// </summary>
public sealed record ReportDefinition(
    string Id,
    string Category,
    string Title,
    string Description,
    /// <summary>Any-of permissions required to see/run the report (on top of reports.view).</summary>
    IReadOnlyList<string> Permissions,
    ReportKind Kind,
    /// <summary>API path for Export reports (download target).</summary>
    string? Endpoint = null,
    /// <summary>FE route for Page reports.</summary>
    string? Route = null,
    /// <summary>Filter keys the report supports: dateRange, search, orderStatus.</summary>
    IReadOnlyList<string>? Filters = null,
    /// <summary>Download file type for Export reports: xlsx or csv.</summary>
    string? FileType = null);

/// <summary>
/// The report catalog: pure metadata, grouped client-side by Category. Categories are free
/// Dutch labels; the FE maps them onto cards. All endpoints referenced here enforce their
/// own permission — the catalog only decides visibility.
/// </summary>
public static class ReportCatalog
{
    private static readonly string[] DateRange = ["dateRange"];

    public static IReadOnlyList<ReportDefinition> All { get; } =
    [
        // --- Operationeel ---
        new("orders-export", "Operationeel", "Transportopdrachten (CSV)",
            "Alle opdrachten met route, status en goederen; filterbaar op zoekterm en status.",
            [PermissionCodes.OrdersExport, PermissionCodes.OrdersManage], ReportKind.Export,
            Endpoint: "/api/transport-orders/export", Filters: ["search", "orderStatus"], FileType: "csv"),
        new("order-packages", "Operationeel", "Colli per opdracht",
            "Alle colli per transportopdracht in de periode, met status en scanhistoriek.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/order-packages", Filters: DateRange, FileType: "xlsx"),
        new("trip-packages", "Operationeel", "Colli per rit",
            "Laad- en loslijsten per rit in de periode.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/trip-packages", Filters: DateRange, FileType: "xlsx"),
        new("scan-activity", "Operationeel", "Scanactiviteit",
            "Alle scans in de periode met resultaat, gebruiker en context.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/scan-activity", Filters: DateRange, FileType: "xlsx"),
        new("package-exceptions", "Operationeel", "Colli-afwijkingen",
            "Afwijkingen (vermist, beschadigd, geweigerd) met disposities in de periode.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/package-exceptions", Filters: DateRange, FileType: "xlsx"),
        new("missing-packages", "Operationeel", "Vermiste colli",
            "Actueel overzicht van alle vermiste colli.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/missing-packages", FileType: "xlsx"),
        new("damaged-packages", "Operationeel", "Beschadigde colli",
            "Actueel overzicht van beschadigde en gequarantaineerde colli.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/damaged-packages", FileType: "xlsx"),
        new("returns", "Operationeel", "Retouren",
            "Retourstromen in de periode.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/returns", Filters: DateRange, FileType: "xlsx"),
        new("delivery-performance", "Operationeel", "Leverprestatie",
            "Leverbetrouwbaarheid per periode op basis van colli-uitkomsten.",
            [PermissionCodes.PackageReportsExport], ReportKind.Export,
            Endpoint: "/api/reports/packages/delivery-performance", Filters: DateRange, FileType: "xlsx"),
        new("open-incidents", "Operationeel", "Open incidenten",
            "Het incidentenregister, filterbaar op status en ernst.",
            [PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage], ReportKind.Page,
            Route: "/incidents"),
        new("dossiers", "Operationeel", "Transportdossiers",
            "Dossieroverzicht met opdrachten, incidenten en financiële samenvatting.",
            [PermissionCodes.DossiersView, PermissionCodes.DossiersManage], ReportKind.Page,
            Route: "/dossiers"),

        // --- Financieel ---
        new("kpi-dashboard", "Financieel", "KPI-dashboard",
            "Operationele en financiële kengetallen per periode.",
            [PermissionCodes.KpiView], ReportKind.Page, Route: "/kpi"),
        new("trip-profitability", "Financieel", "Ritwinstgevendheid",
            "Opbrengst, kosten en marge per rit (bestaande winstgevendheidsslice).",
            [PermissionCodes.ProfitabilityView], ReportKind.Page, Route: "/kpi/trips"),
        new("kpi-trip-profitability", "Financieel", "Ritwinstgevendheid (XLSX)",
            "Exporteerbare marge-analyse per rit in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=trip-profitability", Filters: DateRange, FileType: "xlsx"),
        new("kpi-customer-profitability", "Financieel", "Klantwinstgevendheid (XLSX)",
            "Omzet en marge per klant in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=customer-profitability", Filters: DateRange, FileType: "xlsx"),
        new("kpi-fuel", "Financieel", "Brandstof (XLSX)",
            "Brandstofverbruik en -kosten in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=fuel", Filters: DateRange, FileType: "xlsx"),
        new("invoices", "Financieel", "Facturen",
            "Factuuroverzicht met status en vervaldatums.",
            [PermissionCodes.InvoicesView], ReportKind.Page, Route: "/invoices"),

        // --- Vloot ---
        new("fleet-dashboard", "Vloot", "Vlootdashboard",
            "Onderhoud, keuringen, documenten en schades die aandacht vragen.",
            [PermissionCodes.VehiclesView], ReportKind.Page, Route: "/fleet"),
        new("kpi-vehicle-utilisation", "Vloot", "Voertuigbezetting (XLSX)",
            "Inzet en bezetting per voertuig in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=vehicle-utilisation", Filters: DateRange, FileType: "xlsx"),
        new("kpi-empty-km", "Vloot", "Lege kilometers (XLSX)",
            "Lege-km-analyse per rit/voertuig in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=empty-km", Filters: DateRange, FileType: "xlsx"),
        new("kpi-co2", "Vloot", "CO₂-uitstoot (XLSX)",
            "CO₂-rapportage op basis van brandstofdata in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=co2", Filters: DateRange, FileType: "xlsx"),

        // --- Planning ---
        new("kpi-eta-performance", "Planning", "ETA-prestatie (XLSX)",
            "Voorspelde versus werkelijke aankomsttijden in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=eta-performance", Filters: DateRange, FileType: "xlsx"),
        new("kpi-delivery-reliability", "Planning", "Leverbetrouwbaarheid (XLSX)",
            "Op-tijd-percentage per periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=delivery-reliability", Filters: DateRange, FileType: "xlsx"),
        new("planning-utilisation", "Planning", "Planbordbezetting",
            "Bezetting van chauffeurs en voertuigen per dag op het planbord.",
            [PermissionCodes.PlanningView], ReportKind.ComingSoon),

        // --- HR ---
        new("kpi-driver-hours", "HR", "Chauffeursuren (XLSX)",
            "Gewerkte en geplande uren per chauffeur in de periode.",
            [PermissionCodes.KpiExport], ReportKind.Export,
            Endpoint: "/api/kpi/export?report=driver-hours", Filters: DateRange, FileType: "xlsx"),
        new("qualifications", "HR", "Kwalificaties",
            "Kwalificatiestatus per medewerker, inclusief vervaldatums.",
            [PermissionCodes.EmployeeDocumentsView], ReportKind.Page, Route: "/qualifications"),
        new("driver-availability", "HR", "Chauffeursbeschikbaarheid",
            "Beschikbaarheid, afwezigheden en inzetbaarheid per chauffeur.",
            [PermissionCodes.EmployeePlanningView], ReportKind.ComingSoon),

        // --- Klanten ---
        new("customer-overview", "Klanten", "Klantenoverzicht",
            "Actieve klanten met categorie, status en facturatiegegevens.",
            [PermissionCodes.CustomersView], ReportKind.Page, Route: "/customers"),
        new("customer-orderbook", "Klanten", "Orderboek per klant",
            "Opdrachtvolume en omzet per klant per periode.",
            [PermissionCodes.OrdersExport, PermissionCodes.OrdersManage], ReportKind.ComingSoon),
    ];
}
