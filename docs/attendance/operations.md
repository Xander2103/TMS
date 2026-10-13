# Attendance — operations & troubleshooting

## Configuratie (server)

| Sleutel | Verplicht? | Effect |
|---|---|---|
| `Attendance:PinPepper` | Voor kioskgebruik | Base64, ≥ 32 bytes (`openssl rand -base64 32`). Zonder pepper: PIN-beheer en kiosk fail-closed uit (503). Een ongeldige waarde blokkeert de start (StartupSecurityValidator). |

Pepper-rotatie = alle PIN's opnieuw zetten (lookup-hashes worden ongeldig); dat is een
bewuste, operationeel eenvoudige keuze — communiceer een PIN-resetronde.

## Release

Standaardpipeline: migratie `TimeAndAttendanceFoundation` toepassen
(`dotnet ef migrations script --idempotent` per operations-runbook), rolseeding draaien
(rolversie **v30**; `DefaultRoleSeeder.SyncAsync` als releasestap zoals gedocumenteerd in
`docs/delivery/operations.md` §2.3), daarna health + smoke: inloggen → dashboardcard →
`/attendance` → prikklok provisionen → punch.

## Veelvoorkomende situaties

**"Mogelijk vergeten uit te punten"** — de sweep meldt dit één keer per sessie (aan de
medewerker en aan `attendance.correct`-houders). Oplossen: medewerkerfiche → tab
Urenregistratie → "Corrigeren" → werkelijk uitpuntmoment + reden. Een open pauze wordt
traceerbaar mee afgesloten.

**Lange nachtshift is géén fout** — sessies over middernacht zijn normaal; alleen de
duur-drempel (instelling, default 16 u) triggert de melding. Zet de drempel hoger als
ploegen structureel langer werken.

**Auto-close** — staat standaard uit. Alleen inschakelen als het bedrijf dat expliciet
wil: het systeem sluit dan met status "Automatisch afgesloten" + audit, en HR hoort die
registraties na te kijken (notificatie volgt).

**Prikklok offline/"niet herkend"** — check devicelijst (laatste activiteit), daarna:
sleutel geroteerd zonder het device bij te werken? Device uitgeschakeld? Kiosk-setting
uit? Pepper ontbreekt (alle kiosken tonen "niet geconfigureerd")? Het device zelf
opnieuw koppelen kan altijd via een verse sleutelrotatie.

**Prikklok gestolen/kwijt** — onmiddellijk "Sleutel vernieuwen" (oude key per direct
ongeldig) of het device uitschakelen. Er staat niets gevoeligs op het device behalve de
deviceKey; punches vereisen daarnaast altijd een geldige PIN.

**PIN vergeten/geblokkeerd** — HR genereert een nieuwe code (fiche → Prikklokcode);
dat reset ook de lockout. Codes zijn nooit uitleesbaar.

**Medewerker uit dienst** — deactivering trekt de prikklokcode automatisch in en
punch-endpoints weigeren; historiek blijft (en valt onder de bestaande
GDPR-retentie/anonymisatieprocessen van het personeelsdossier).

**Dubbele punch gemeld** — kan niet tot dubbele actieve sessies leiden (DB-invariant);
de tweede poging krijgt "Je bent al ingepunt.". Zie je toch rare data, check de
eventtimeline van de sessie — die is immutable en vertelt exact wat er gebeurde.

## Monitoring

- Sweep logt per tenant aantallen (`Attendance sweep: N ...`); fouten per tenant
  aborteren de andere tenants niet.
- Rate-limit-429's op `/api/attendance/kiosk/*` + device-lockouts zijn de
  bruteforce-indicatoren.
- Exportacties staan als `DataExported` (classificatie Confidential) in de auditlog.
