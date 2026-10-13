# Kwaliteitsrapport — dossiergericht TMS-redesign (eindoplevering 2026-08-12)

Branch: `nav-redesign`. Alle werk is additief; geen V2-duplicaatmodules.
Dit rapport dekt de basisoplevering (Waves 0–11) én de afrondingsgolf (P0–P13,
zie §9) die de gaten uit de implementatie-audit sloot.

## 1. Commits (chronologisch)

| Commit | Inhoud |
|--------|--------|
| `e07dca4` | Wave 0 — entiteitscoherentie facturen, klant/chauffeur-statuslabels, invoerschaal |
| `84a80a5` | docs: redesign gap-analyse + Wave 1 implementatieplan |
| `b5f8417` | docs: veldaudit orderformulier |
| `1d5ec7d` | Wave 1 fase 1 — ActivityType + DossierActivity schema, Version-tokens, per-tenant seeder |
| `4b558a9` | Wave 1 fase 2+3 — activiteitstypebeheer (API+UI, rollen v26), dossier-backfill |
| `ae60bb4` | Wave 1 fase 4+5 — snelle dossieraanmaak, activiteitenlaag, auto-wrap, versietokens |
| `1388f0f` | Wave 1 fase 6 — orderformulier in secties met progressieve onthulling |
| `cda79d6` | Wave 1 fase 7 — dossierpagina als centrale werkplek + snelle aanmaak |
| `10fd5cb` | Wave 1 fase 8+9 — rolgerichte navigatie en dashboard, dossier als landing |
| `20e432e` | Wave 2 fase 1 — additief schema commercieel fundament |
| `5c3e292` | Wave 2 fase 2 — verkoopcode-resolutieketen met bevroren snapshots |
| `c787dd1` | Wave 2 fase 3-6 — entiteitenbeleid, taal, groepering, factuurgereedheid (rollen v27) |
| `b9005db` | Wave 3 fase 1 — afstand-/laadmeterinvoer voedt PerKm/PerLdm-tarieven |
| `0e9c574` | Wave 3 fase 2+3 — O/D-zonedimensie, Maut per km, feestdagkalender |
| `ea4646a` | Wave 4 — magazijnlocaties, scans zonder rit, trace/voorraad |
| `c5f2237` | Wave 5 — bewegingsklok, pallet-dagafleiding, opslagoverzicht |
| `8b5a4b0` | Wave 6 — verantwoordelijkheid, doorrekening (rollen v28) en herlevering |
| `07d14db` | Wave 7 — transparante ritvoorstellen per leverzone |
| `492b7df` | Wave 8 — historische stoptijden, verschuivingsdrempel, portaal-ETA |
| `d64fde7` | Wave 9+10 — leveringsbon/CMR-generatie en facturatiecontrole-werkruimte |
| `1080dda` | Wave 11 — portaal-POD-samenvatting en meldingsvoorkeuren |
| _(volgt)_ | Opleveringspakket — documentatie (dit rapport e.a.) |

## 2. Migraties van het redesign (allemaal toegepast op de dev-database)

| Migratie | Wave | Aard |
|----------|------|------|
| `20260811214114_DossierActivityFoundation` | 1 | additief (ActivityTypes, DossierActivities, dossierkolommen) |
| `20260812001932_CommercialFoundation` | 2 | additief (verkoopcodes, snapshots, entiteitenbeleid, groepering) |
| `20260812071320_PricingGeneralization` | 3 | additief (zones, PerKm, feestdagkalender, orderkolommen) |
| `20260812074930_WarehouseLocationsAndStandaloneScans` | 4 | additief (magazijnlocaties, losse scans) |
| `20260812080131_StandaloneScanOrderNullable` | 4 | additief (order nullable op scan-event) |
| `20260812081943_StorageStays` | 5 | additief (opslagverblijven) |
| `20260812083503_ProblemsResponsibilityCharge` | 6 | additief (verantwoordelijkheid, toeslag, herleveringskoppeling) |
| `20260812085739_EtaShiftThreshold` | 8 | additief (ETA-historiek, drempelinstelling) |

Wave 9, 10 en 11 vergen géén schema-wijziging (hergebruik van bestaande entiteiten:
PDF-generatie, leesmodel facturatiecontrole, MessagingProfile/POD).

## 3. Testresultaten (eindstand)

| Poort | Resultaat |
|-------|-----------|
| Backend `dotnet test` | ✅ 1961 geslaagd, 0 gefaald, 0 overgeslagen |
| Frontend `npm test` (vitest) | ✅ 901 geslaagd, 0 gefaald (170 testbestanden) |
| Typecheck `npx tsc --noEmit` | ✅ geen fouten |
| Lint `npm run lint` | ✅ enkel de 3 gekende pre-existing employees-fouten (zie §4) |
| Productiebuild `npm run build` | ✅ geslaagd (gekende, onschadelijke INEFFECTIVE_DYNAMIC_IMPORT-melding voor offlineActions.ts) |

Verloop van de backendsuite tijdens dit traject: 1876 (baseline) → 1959 (na Wave 9+10)
→ **1961** (eindstand, incl. 2 nieuwe Wave 11-tests). Frontend: 900 → **901**.
Tijdens de eindrun bleken 2 navigatietests verouderde verwachtingen te bevatten
(bedoelde toevoegingen Facturatiecontrole/Trace & voorraad); de tests zijn op de
bedoelde boom gezet en de suite is opnieuw volledig groen gedraaid.

## 4. Gekende pre-existing afwijkingen (NIET door het redesign veroorzaakt)

- **Lint:** exact 3 bestaande fouten in het employees-domein (react-hooks-regels);
  aanwezig vóór Wave 0 en bewust niet in scope.
- **NU1903-waarschuwing** (bekende kwetsbaarheidsmelding transitive package) —
  gedocumenteerd in het geheugendossier "known issues", hardening uitgesteld.
- **Auth-integratietest** (uitgesteld hardeningpunt van eerdere sprint).

## 5. Gekende beperkingen van de opgeleverde waves

- Ritvoorstellen (Wave 7) zijn adviserend: geen automatische routeoptimalisatie.
- ETA's (Wave 8) zijn planning-/invoergedreven; geen live GPS-/verkeersintegratie.
- CMR-document (Wave 9) is een generieke layout; geen e-CMR-ketenintegratie.
- Facturatiecontrole (Wave 10) beslist en signaleert; factuuraanmaak zelf loopt via de
  bestaande factuurflows.
- Portaal-sms-meldingen (Wave 11) vereisen een geconfigureerde sms-provider; zonder
  provider blijft het een opgeslagen voorkeur.
- Opslagfacturatie (Wave 5): pallet-dagen worden afgeleid en getoond; periodieke
  opslagfacturen worden handmatig gestart.

## 6. Resterende handmatige workflows

- Facturen aanmaken/verzenden vanuit de voorstellen in de facturatiecontrole.
- Periodieke opslagfacturatie starten vanuit het opslagoverzicht.
- Herleveringsorders opnieuw inplannen na aanmaak vanuit een incident.
- Portaalgebruikers uitnodigen en rechten toekennen per klant.

## 7. Uitgestelde technische schuld

- 3 pre-existing lint-fouten employees-domein.
- NU1903-package-melding.
- Auth-integratietest (zie geheugendossier known issues).
- OPS-checklist van de security-sprint (productie-uitrolstappen) blijft de gids voor
  livegang; zie `docs/security/`.

## 8. Smoke-verificatie

Zie `docs/delivery/tester-checklist.md` (52 scenario's) voor de handmatige
end-to-end-acceptatie; de geautomatiseerde suites hierboven dekken de regressies.

## 9. Afrondingsgolf P0–P13 (2026-08-12)

Gestart vanuit de factuele implementatie-audit; sluit de functionele gaten vóór de
gebruikersacceptatie. Commits:

| Commit | Inhoud |
|--------|--------|
| `1996d4f` | P0 — locatieprojectie bij vertrek + voorkeuren-oversuppressie (2 correctheidsfouten) |
| `255a593` | P1-P3 backend — klantdocumentstrategie, documentregels/resolver, dagbatch per klant |
| `5aa7fef` | P1-P3 frontend — klantinstelling, orderkeuze, dagbatchkaart, regeleditor |
| `cd070a0` | P4-P5 backend — herleveringsautomatisering (werkdaglogica) + doorrekenbeleid |
| `d23e908` | P4-P5 frontend — instellingen, incidentaanbeveling, doorrekenbeleid-pagina |
| `d54e7a0` | P6 — prijsdimensies activiteit/plateau/Moffett/retour (legacy byte-stabiel) |
| `7297919` | P7 — diensthoeveelheden uit echte scans/opslagklok (idempotent) |
| `d6e4491` | P8-P9 — ETA-levenscyclus compleet + controle op gevoelige berichten |
| `f033c1e` | P10 — voorstellen verklaren randvoorwaarden; rit-ADR-chauffeursregel |
| `995179d` | P11 — KPI per activiteit (kraan vs plateau apart) |
| `9c8883a` | P12 — factuur uitstellen, zichtbaar geparkeerd |
| `0ecad13` | P13 — Excel-orderimport (vierde genormaliseerd instroomkanaal) |
| _(slot)_ | frontendafronding (ordervlaggen, regeldimensie, hoeveelheidsbron, controlewachtrij, facturatieselectie, voorstel-voorwaarden) + documentatie |

Nieuwe migraties (allemaal additief en toegepast): `DocumentStrategy`,
`RedeliveryAndChargePolicy`, `PricingDimensions`, `ServiceQuantitySource`,
`OrderImport` (draagt ook NotificationRule.RequiresReview), `InvoiceSnooze`.

Gesloten t.o.v. de audit: klantdocument-onderdrukking (C→A), dagbatch per
klant+datum (C→A), automatische documentstrategie (C→A), uitstellen in facturatie
(C→A), Excel-import (C→A), herleveringsautomatisering (B→A), doorrekenbeleid
(B→A), prijsdimensies plateau/Moffett/retour/activiteit (C→A), scan-gedreven
handling/opslagdiensten (B→A), ETA-drempel-UI en herberekening (B→A),
controlewachtrij gevoelige berichten (B→A), voorstel-randvoorwaarden (B→A),
activiteits-KPI (B→A), plus de twee incidentele defecten.

Bewust open (zie ook traceerbaarheidsmatrix): vooraf-communicatie van
leververwachtingen (venster-/planningsmails zonder producer), reistijdbewuste
venstervalidatie binnen een voorgestelde tour, automatische periodieke
opslagfacturen.

Eindstand na de slotgates van de afrondingsgolf (2026-08-12):

| Poort | Resultaat |
|-------|-----------|
| Backend `dotnet test` | ✅ 2003 geslaagd, 0 gefaald (was 1961 vóór de golf; +42) |
| Frontend `npm test` | ✅ 917 geslaagd, 0 gefaald, 176 testbestanden (was 901; +16) |
| Typecheck `npx tsc --noEmit` | ✅ geen fouten |
| Lint `npm run lint` | ✅ enkel de 3 gekende pre-existing employees-fouten |
| Productiebuild `npm run build` | ✅ geslaagd (gekende onschadelijke dynamic-import-melding) |
| Migraties | ✅ alle 14 redesign-migraties toegepast, geen openstaande |
