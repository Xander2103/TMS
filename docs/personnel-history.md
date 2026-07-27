# Personeelshistoriek

> Corrections wave 2026-07-27 §4. Nagekeken tegen de implementatie in
> `Modules/Employees/Services/EmployeeHistoryService.cs`; tests: `EmployeeHistoryTests`.

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

Leeszijde — `GET /api/employees/{id}/history?page=&pageSize=` (permissie `employees.view`):

1. verzamelt de auditrijen van de medewerker **én** van alle kindentiteiten (kwalificaties,
   documenten, bedrijfsmiddelen, afwezigheden, verlofsaldi + aanpassingen, chauffeursprofiel),
   inclusief soft-verwijderde kinderen — historiek overleeft de rij;
2. difft de opgeslagen voor/na-JSON per veld en laat ongewijzigde velden weg;
3. vertaalt technische veldnamen naar Nederlandse labels, booleans naar Ja/Nee (of
   Actief/Inactief via de statuslabels), ISO-datums naar `dd-MM-jjjj` en gekende enumwaarden
   (tewerkstellingsstatus, afwezigheidsstatus, dagdeel, aanpassingssoort, …) naar leesbare tekst;
4. lost de actor op naar de gebruikersnaam ("Systeem" wanneer geen gebruiker);
5. filtert "Updated"-entries zonder één enkel gewijzigd veld weg — een opslag zonder echte
   wijziging wordt nooit een misleidende kaart.

Guid→string conversie voor de auditkoppeling gebeurt bewust **client-side** (provider-specifieke
`ToString()`-vertaling — SQLite levert hoofdletters — zou stil niets matchen).

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
`AuditHistoryPanel`: één kaart per opslag, nieuwste eerst, actor + tijdstip altijd zichtbaar,
categoriechip, tabel Veld/Voor/Na, paginering. Het generieke paneel blijft in gebruik voor
voertuigen/opleggers/uitzonderingen.
