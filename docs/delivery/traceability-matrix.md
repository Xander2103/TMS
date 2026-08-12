# Traceerbaarheidsmatrix — dossiergericht TMS-redesign (2026-08)

Mastervereiste → implementatie → module/bestand → test → status → beperking.
Commits: Wave 0 `e07dca4` … Wave 11 (zie kwaliteitsrapport voor de volledige lijst).

| # | Mastervereiste | Implementatie | Module / kernbestanden | Test(en) | Status | Beperking |
|---|----------------|---------------|------------------------|----------|--------|-----------|
| 1 | Entiteitscoherentie facturen, statuslabels, invoerschaal (Wave 0) | Coherente eigen-bedrijfskeuze op facturen; NL statuslabels klant/chauffeur; invoerbreedtes | `Modules/Invoicing`, web shared kit | bestaande invoicing-suites | ✅ `e07dca4` | — |
| 2 | Dossier als centrale werkplek | TransportDossier + DossierActivity + ActivityType (per-tenant geseed), dossierdetail als landing, snelle aanmaak | `Modules/Dossiers`, `features/dossiers` (web) | `DossierActivityTests`, dossier-suites | ✅ Wave 1 (`1d5ec7d`…`10fd5cb`) | — |
| 3 | Activiteitstypebeheer | ActivityType CRUD (API+UI), rollen v26 | `Modules/Dossiers`, `/settings/activity-types` | activity-type-tests | ✅ `4b558a9` | — |
| 4 | Orderformulier in secties, progressieve onthulling | SectionedForm-ontleding orderformulier | `features/transport-orders` | vitest form-suites | ✅ `1388f0f` | — |
| 5 | Rolgerichte navigatie + dashboard | Navigatieboom (Vandaag→Parameters), dossier als landing | `navConfig.ts`, `AppRoutes.tsx` | nav-vitests | ✅ `10fd5cb` | — |
| 6 | Verkoopcodes met bevroren snapshots | Resolutieketen dienst→klant→tenant; snapshot bij aanmaak; admin-UI | `Modules/Commercial`, `TransportOrderService` | `CommercialFoundationTests` | ✅ `5c3e292` | — |
| 7 | Eigen-bedrijvenbeleid per klant + override | `CustomerEntityPolicy`, toegestane entiteiten, recht `dossiers.override_entity` (rollen v27) | `Modules/Partners`, `Modules/Invoicing` | entity-policy-tests | ✅ `c787dd1` | — |
| 8 | Factuurtaal volgt klant | `LanguageCode`-keten klant→factuur→PDF | `InvoiceService`, `InvoicePdfRenderer` | invoice-language-tests | ✅ `c787dd1` | — |
| 9 | Groeperingsvoorkeur facturatie | `Customer.InvoiceGrouping` (PerDossier/Weekly/Monthly/ByReference/Manual) + UI-hint | `Modules/Partners`, klantfiche | grouping-tests | ✅ `c787dd1` | — |
| 10 | Factuurgereedheid | `InvoiceReadinessEvaluator` (met `tripExecutedOverride`), stale-invalidatie, typed coverage | `Modules/Orders` | readiness-tests | ✅ `c787dd1` | — |
| 11 | Prijsgeneralisatie: afstand/laadmeter | `DistanceKm`/`LoadingMeters` op order voeden PerKm/PerLdm | `TransportOrderService`, `PricingEngine` | `PricingGeneralizationTests` | ✅ `b9005db` | — |
| 12 | O/D-zones, Maut per km, feestdagen | Herkomstzone-dimensie, PerKm-diensten, Holiday-conditie + kalender | `Modules/Tarification` | `PricingGeneralizationTests` | ✅ `0e9c574` | — |
| 13 | Magazijnlocaties + losse scans | Locaties per magazijn, scans zonder rit (order nullable), trace & voorraad | `Modules/Warehousing`, `/warehouse/trace` | `WarehouseLocationTests` e.a. | ✅ `ea4646a` | — |
| 14 | Opslagverblijven + pallet-dagen | `StorageStay`, `StorageClockInterceptor` (sluit bij vertrek-events), `StorageBillingService` | `Modules/Warehousing` | storage-tests | ✅ `c5f2237` | Facturatie van pallet-dagen is een afgeleide weergave; automatische periodieke opslagfactuur is handmatig te starten |
| 15 | Problemen: verantwoordelijkheid + doorrekening | Incident-verantwoordelijkheid, toeslag met goedkeuring `problems.approve_charge` (rollen v28) → factuurlijn | `Modules/Incidents`, `IncidentChargePanel` | incident-charge-tests | ✅ `8b5a4b0` | — |
| 16 | Herlevering | `createIncidentRedelivery`: nieuwe order in hetzelfde dossier, gekoppeld aan incident | `IncidentService` | redelivery-tests | ✅ `8b5a4b0` | — |
| 17 | Distributieplanning | `PlanningProposalService`: transparante ritvoorstellen per leverzone | `Modules/Planning`, Planbord | proposal-tests | ✅ `07d14db` | Voorstellen zijn adviserend; geen automatische optimalisatie/vrp-solver |
| 18 | ETA + verschuivingsdrempel | `StopEta`-historiek, drempel voor klantmelding, portaal-ETA | `Modules/Eta`, portal detail | ETA-tests | ✅ `492b7df` | ETA's op basis van planning/handmatige invoer; geen live GPS-verkeersdata |
| 19 | Documentgeneratie: leveringsbon + CMR, batch per rit | `TransportDocumentRenderer` (PDFsharp), per order + gebundeld per rit in routevolgorde | `Modules/Orders`, order/ritdetail-knoppen | `TransportDocumentTests` (4) | ✅ `d64fde7` | CMR-layout is generiek; geen e-CMR-koppeling |
| 20 | Facturatiecontrole-werkruimte | `InvoiceControlService`: voorstellen per groeperingsvoorkeur, needs-review-redenen, openstaande toeslagen | `Modules/Invoicing`, `/invoice-control` | `InvoiceControlTests` (2) | ✅ `d64fde7` | Werkruimte is lees-/beslisoverzicht; factuuraanmaak verloopt via bestaande flows |
| 21 | Portaal: POD-samenvatting | `PortalPodSummaryDto` op orderdetail, enkel `IsCurrent && CustomerVisible` | `CustomerPortalService.TrimAsync` | `GetMyOrder_ShowsPodSummary…` | ✅ `1080dda` | Samenvatting (data), geen POD-bestandsdownload op orderdetail (wel via Documenten) |
| 22 | Portaal: meldingsvoorkeuren | GET/PUT `notification-preferences` op klant-MessagingProfile; NL/FR/EN pagina `/klantportaal/voorkeuren` | `CustomerPortalService`, `CustomerPortalPreferencesPage` | `NotificationPreferences_DefaultsThenRoundTrip…` | ✅ `1080dda` | Sms-kanaal vereist geconfigureerde sms-provider |
| 23 | Meertalig portaal (nl/fr/en) | Vertaalbundels incl. nieuwe voorkeuren/POD-sleutels; volledigheidstest | `src/locales/*`, `translations.ts` | i18n-completeness-vitest | ✅ | — |
| 24 | Geen V2-duplicaten; hergebruik bestaande sterktes | Alle waves bouwen additief op TransportDossier/Order, scan-pipeline, pricing engine, Peppol, notificaties | hele codebase | volledige regressiesuites | ✅ | — |
| 25 | Tenant-isolatie, audit, permissies behouden | Elke nieuwe service: tenant-filters, audit-records, fail-closed rechten; service-side codes geregistreerd | `Phase8SupplyChainTests.ServiceSideEnforcedCodes` | Phase8-suite | ✅ | — |

Legende: ✅ = geïmplementeerd, getest en gecommit op branch `nav-redesign`.
