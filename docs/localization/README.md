# Localization (NL / FR / EN)

TransportationService is architecturaal meertalig: Nederlands, Frans (Belgisch) en
Engels zijn volwaardige UI-talen. Kernprincipe overal:

```
stable domain value → localization layer → NL / FR / EN display
```

en **nooit** businesslogica op vertaalde labels. Zie:

- [architecture.md](architecture.md) — canonieke architectuur, taalresolutie, foutcodes,
  notificatie-besluit, wat bewust onvertaald blijft
- [developer-guide.md](developer-guide.md) — hoe je nieuwe tekst/status/fout/e-mail/module
  toevoegt + de twee harde regels
- [glossary.md](glossary.md) — canonieke transport-/HR-/attendance-terminologie
- [style-guide.md](style-guide.md) — aanspreekvorm, casing, interpunctie per taal
- [testing.md](testing.md) — gates en smoke-procedure

## Snel overzicht

| Wat | Waar |
|---|---|
| UI-taal kiezen | Sidebar-taalkiezer (intern) / portaal-switcher — persist via `PUT /api/me/language` |
| Tenant-default | Instellingen → Regionale instellingen → Standaardtaal (`TenantSettings.DefaultLanguage`) |
| Kiosk-taal | Per prikklok (Instellingen → Urenregistratie) + NL\|FR\|EN-knoppen + persoonlijke taal na identificatie |
| Resources | `src/locales/{nl,fr,en}/*.json` (NL = bron; keyset-pariteit afgedwongen) |
| Regionale weergave | Datum/decimaal/tijdzone blijven TENANT-instellingen (`utils/dates.ts`, `utils/numbers.ts`) |
| Foutcodes | `Common/ErrorCodes.cs` → `errors.*`-vertalingen; message blijft fallback |

## Dekkingsstatus (2026-08-23)

**Volledig geconverteerd**: gedeelde UI-kit/layout/navigatie/commands/calendar,
dashboard, intern portaal, Attendance + Kiosk + Driver Activity Card (100%, §87),
HR-cluster (employees/absences/leave/issued-items/…), Locations, Settings/
Systeeminformatie/Backups, auth; klantportaal was al meertalig. Overige modules volgen
de architectuur en migreren per module volgens de developer-guide — nieuwe schermen
mogen sowieso géén hardcoded strings meer introduceren (§82; missing-key-guardtest).

**Bewuste fase-2-items**: veldvalidatieteksten per veld (nu generieke code +
NL-fallback), overige export-/PDF-koppen (attendance = model; pricing-round-trip blijft
contractueel Dutch), notificatie-params-kolom, FR/EN-templates voor de resterende
MessageKinds, seeded referentiedata-labels, 12-uursklok (§34: alleen voorbereid).

**Bundelmeting**: alle talen samen worden statisch gebundeld; herzie lazy loading
wanneer één taal ~100 KB overschrijdt (§56).
