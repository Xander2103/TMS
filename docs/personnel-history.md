# Personeelshistoriek

> Corrections wave 2026-07-27 §4, uitgebreid in corrections wave 4 (2026-07-30) §2: compacte,
> uitklapbare kaarten met opgeloste id-velden. Nagekeken tegen de implementatie in
> `Modules/Employees/Services/EmployeeHistoryService.cs`; tests: `EmployeeHistoryTests`,
> `employeeHistoryPanel.test.tsx`.

## Model

De historiek is een **leestijd-projectie over de bestaande append-only auditlog** (`audit_logs`,
`IAuditService.RecordAsync`) — er bestaat géén tweede auditsysteem. Bestaande auditrijen worden
**nooit herschreven**: oude, gedeeltelijke entries blijven via hetzelfde pad renderen en tonen
gewoon minder veldregels.

Schrijfzijde (wat er sinds deze wave in de payloads zit):

- `EmployeeService` schrijft bij create/update een **volledig veldsnapshot** (alle betekenisvolle
  profielvelden). Lookup-id's worden **bij het schrijven** naar namen vertaald (afdeling,
  contracttype, functies) zodat de historiek nooit breekt wanneer masterdata later hernoemd of
  verwijderd wordt.
- Vertrouwelijke velden (rijksregisternummer, identiteitskaart, IBAN) worden **gemaskeerd**
  opgeslagen (`•••34`-stijl): de wijziging is zichtbaar, de waarde nooit. BIC is niet gevoelig.
- `QualificationService.UpdateAsync` auditte vroeger niets — dat gat is gedicht (voor/na van
  documentnummer, data, uitgifteland, notities).
- Verlofrechten (§4.1): `SetEntitlementAsync` audit bevat **Verlofcategorie, Jaar, Eenheid
  (dagen), voor/na, Verschil en optionele Reden** (nieuw veld op `SetLeaveEntitlementRequest` +
  dialoog); `AddAdjustmentAsync` bevat categorie, jaar, het aanpassingstotaal voor/na, het
  verschil, de soort en de verplichte reden.

Leeszijde — `GET /api/employees/{id}/history?page=&pageSize=&category=` (permissie `employees.view`):

1. verzamelt de auditrijen van de medewerker **én** van alle kindentiteiten (kwalificaties,
   documenten, bedrijfsmiddelen, afwezigheden, verlofsaldi + aanpassingen, chauffeursprofiel),
   inclusief soft-verwijderde kinderen — historiek overleeft de rij;
2. difft de opgeslagen voor/na-JSON per veld en laat ongewijzigde velden weg;
3. vertaalt technische veldnamen naar Nederlandse labels (een onbekende sleutel wordt
   gehumaniseerd — "SomeWeirdKey" → "Some Weird Key" — nooit ruw getoond), booleans naar Ja/Nee,
   ISO-datums naar `dd-MM-jjjj` en gekende enumwaarden (tewerkstellingsstatus,
   afwezigheidsstatus, dagdeel, itemstatus, documentcategorie, beschikbaarheid, …) naar
   leesbare tekst;
4. **lost id-velden op naar namen, op leestijd, over de opgevraagde pagina**:
   `QualificationTypeId`, `LeaveTypeId`, `BalanceTypeId`, `DepartmentId`, `VerifiedByUserId` en
   `DecidedByUserId` worden gebatcht opgezocht (één query per opzoektype, inclusief
   soft-verwijderde rijen via `IgnoreQueryFilters`) — een onbekende of verwijderde id toont
   "Onbekend (verwijderd)" in plaats van een rauwe GUID. Dit gebeurt op leestijd zodat ook
   **oude auditrijen** (geschreven vóór deze opzoeking bestond) meteen meeprofiteren;
5. lost de actor op naar de gebruikersnaam ("Systeem" wanneer geen gebruiker);
6. filtert "Updated"-entries zonder één enkel gewijzigd veld weg — een opslag zonder echte
   wijziging wordt nooit een misleidende kaart;
7. bouwt een compacte `summary`-tekst per kaart (zie "Samenvatting" hieronder);
8. `category` is optioneel en moet, indien meegegeven, exact één van de chiplabels uit de
   dekkingstabel zijn — een onbekende waarde levert een 400 (validatiefout) op.

Guid→string conversie voor de auditkoppeling gebeurt bewust **client-side** (provider-specifieke
`ToString()`-vertaling — SQLite levert hoofdletters — zou stil niets matchen).

## Samenvatting (`summary`)

Elke kaart krijgt een server-gebouwde Nederlandse samenvattingsregel, ná labelvertaling en
id-opzoeking (dus nooit een rauwe GUID):

- **Verlofsaldo** met een gewijzigd dagenveld (basisrecht of aanpassingentotaal) en gekende
  categorie/jaar: `"Wettelijk verlof 2027: 12 → 20 dagen"`;
- **1 wijziging**: `"Veldnaam: voor → na"` (of `"Veldnaam: na"` wanneer er geen voor-waarde is);
- **>1 wijzigingen**: `"N velden gewijzigd (Veld1, Veld2, Veld3, …)"` — maximaal drie veldnamen,
  `…` bij meer;
- **geen wijzigingen** (bv. een `Deleted`-actie zonder na-snapshot): de actielabel zelf
  (bv. "Verwijderd").

## Dekking

| Categorie (chip) | EntityType(s) |
|---|---|
| Profiel | `Employee` |
| Kwalificaties | `EmployeeQualification` |
| Documenten | `EmployeeDocument` |
| Bedrijfsmiddelen | `EmployeeIssuedItem` |
| Afwezigheden | `Absence` |
| Verlofsaldo | `EmployeeLeaveBalance`, `LeaveBalanceAdjustment` |
| Chauffeursprofiel | `Driver` |

## Voorbeeld

Eén opslag die drie velden wijzigt levert één kaart:

```
27-07-2026 14:32 — Gewijzigd door Xander Van Malder   [Profiel]
Veld                    Voor            Na
Straat                  Oude straat 10  Nieuwe straat 25
Status tewerkstelling   Actief          Met verlof
Notities                Oude notitie    Nieuwe notitie
```

Verlofrecht (§4.1):

```
Verlofsaldo — Gewijzigd door Ann HR
Verlofcategorie   —    Wettelijk verlof
Jaar/periode      —    2027
Basisrecht (dagen) 12  20
Verschil          —    8
Reden             —    Jaarlijks saldo 2027 toegekend
```

## UI

`EmployeeHistoryPanel` (tabblad **Historiek** op de medewerkerfiche) vervangt daar het generieke
`AuditHistoryPanel`: één kaart per opslag, nieuwste eerst, **standaard ingeklapt** — header
(datum·tijd, actielabel, "door {gebruiker}", categoriebadge) + de `summary`-regel + een
"Uitklappen"/"Inklappen"-knop (`aria-expanded`). Uitklappen toont de volledige tabel
Veld/Voor/Na; kaarten zonder wijzigingen (bv. `Deleted`) tonen geen knop. Boven de lijst staan
filterchips (Alles/Profiel/Kwalificaties/Documenten/Bedrijfsmiddelen/Afwezigheden/
Verlofsaldo/Chauffeursprofiel) die de `category`-queryparameter meesturen en de paginering naar
pagina 1 resetten. Het generieke paneel blijft in gebruik voor voertuigen/opleggers/uitzonderingen.
