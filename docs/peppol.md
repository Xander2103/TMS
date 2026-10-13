# Peppol e-facturatie (2026-07-30)

Provider-neutrale Peppol-verzending van facturen en creditnota's (UBL 2.1, Peppol BIS
Billing 3.0), met een deterministische **sandbox-provider** als enige geregistreerde
adapter. Er bestaat en er wordt **geen** echte netwerkverbinding geclaimd; een echt
Access Point sluit later aan via de stappen onderaan.

## Architectuur

```
Invoice (bevroren snapshots) ──► PeppolInvoiceService ──► UblDocumentBuilder (puur, deterministisch)
        │ validatiecatalogus (NL)          │ XML + hash
        ▼                                   ▼
InvoicePeppolController          PeppolTransmissionService ──► PeppolTransmission (Queued)
  validate / preview / xml / send          │ payload in IFileStorageService ("peppol")
                                            ▼
                     PeppolDispatcherHostedService (30s, alle tenants)
                       submit:  Queued ──► IPeppolProvider.SendDocumentAsync
                       poll:    SubmittedToProvider/AcceptedByProvider ──► GetTransmissionStatusAsync
                                            ▼
                     Delivered / Rejected / Failed (alleen via providerantwoorden)
```

- **`IPeppolProvider`** (`Modules/Peppol/Services/IPeppolProvider.cs`) is de enige seam
  naar "Peppol als netwerk": ValidateParticipant, SendDocument, GetTransmissionStatus,
  ValidateDocument. `PeppolProviderFactory` resolvet op `PeppolSettings.ProviderKey`.
- **`SandboxPeppolProvider`** (key `sandbox`) is deterministisch: id eindigend op `999`
  = niet gevonden; payload met marker `SANDBOX-FAIL` = geweigerd; status schuift per
  poll op Submitted → Accepted → Delivered.
- **`PeppolSettings`** per juridische entiteit (uniek per tenant): Enabled, Environment
  (Sandbox/Live), ProviderKey, e-mailterugval, standaardnotitie. Identiteit (Peppol-ID/
  schema), BTW en IBAN worden **altijd live** van de `LegalEntity` gelezen — één bron.
- **Transmissies** zijn append-only gebeurtenisdragers: hoogstens één niet-terminale
  transmissie per factuur (partial unique index + vriendelijke guard), payload
  onveranderlijk (retry hergebruikt dezelfde storage-key en hash, versie = max+1),
  `Environment`/`ProviderKey` per rij gestempeld (instellingswijzigingen herschrijven
  nooit historiek). `Status` is een concurrency-token: een cancel die de dispatcher
  kruist conflicteert in plaats van stil overschreven te worden.
- **Geannuleerde factuur**: de factuur-annulering trekt wachtende transmissies in én de
  dispatcher hercontroleert vóór submit dat de factuur nog Sent/Paid is.

## Documentgeneratie (fase 12)

- `VatTreatmentCatalog.ResolveVatCategory(treatment, rate)` is de enige bron voor de
  UNCL5305-categorie (S/Z/AE/K/G/E) + VATEX-vrijstellingscode + NL wetteksten.
  Verzenden bevriest de categorie per lijn (`InvoiceLine.VatCategoryCode`); bevroren
  waarden worden nooit herschreven ("never guess": zonder btw-regelingsnapshot blokkeert
  verzending).
- `UblDocumentBuilder` is puur en byte-deterministisch (invariant culture, stabiele
  ordening, geen klok): twee builds over dezelfde input zijn identiek — de payload-hash
  steunt daarop. BG-14 InvoicePeriod wordt altijd meegegeven (BR-IC-11).
  Document-niveau kortingen/toeslagen (BG-20/21) bestaan bewust niet: kortingen zijn
  gewone (positieve) lijnen; negatieve prijzen zijn verboden (BR-27, validatieregel).
- Afronding: btw per (categorie, tarief)-groep afgerond en gesommeerd —
  `InvoiceTotals.VatTotal` wordt gedeeld door detail-DTO, PDF en UBL zodat alle drie
  hetzelfde te betalen bedrag tonen.
- **Creditnota's**: `POST /api/invoices/{id}/credit-note` (alleen Sent/Paid, geen
  creditnota van een creditnota, één levende creditnota per factuur — partial unique
  index). Bedragen blijven **positief**; het teken zit in `Invoice.Kind` en wordt in
  boekhoudexport (kolom Type + genegateerde bedragen), dashboardomzet, PDF (titel
  CREDITNOTA) en portaal doorgevoerd. Nummering deelt de maandreeks van facturen met
  prefix `LegalEntity.CreditNotePrefix` (leeg = factuurprefix + "CN").

## Webhook & inkomende documenten (fase 13)

- `POST /api/peppol/webhook/{providerKey}` — anoniem, beveiligd met header
  `X-Peppol-Webhook-Secret` (fixed-time vergelijking) tegen configuratie
  `Peppol:Webhook:Secret`; **zonder geconfigureerd secret weigert het endpoint alles**.
  Requestlimiet 10 MB. Statusupdates zijn providerKey-gescoped, idempotent, en
  regresseren nooit een terminale/afgeleverde status; elke webhook-mutatie wordt
  geauditeerd (`WebhookStatusChanged`). Uniform "Genegeerd." bij onbekende referenties
  (geen existence-oracle). Altijd 200 na persist zodat de provider niet blijft
  herafleveren.
- Inkomende documenten resolven hun tenant via de ontvanger-participant (juridische
  entiteit met dat Peppol-ID). Dubbele dedupe: `ProviderMessageId` (uniek per tenant)
  én inhoudshash+leverancier+documentnummer. Review-wachtrij: Received/NeedsReview →
  Verwerkt (Linked) of Afgewezen (Rejected) met notitie (`peppol.view_incoming`).

## Permissies (rollen v21)

| Code | Omschrijving | Standaard |
|---|---|---|
| `peppol.view` | Overzicht, transmissies, configuratie bekijken | management, boekhouding |
| `peppol.configure` | Instellingen per juridische entiteit beheren | alleen management (accounting.manage-precedent) |
| `peppol.validate` | Klanten/eigen bedrijven valideren, factuur valideren/XML | management, boekhouding |
| `peppol.send` | Versturen/annuleren | management, boekhouding |
| `peppol.retry` | Mislukte verzendingen opnieuw | management, boekhouding |
| `peppol.view_incoming` | Inkomende documenten beoordelen | management, boekhouding |

## Schermen

- `/peppol` (nav Klanten → Peppol): Overzicht (checklist per entiteit + tellers),
  Uitgaand (retry/annuleer), Inkomend (beoordelen), Configuratie (per entiteit +
  verbinding testen), Validatieproblemen.
- Factuurdetail: paneel Peppol met Valideren, Voorbeeld, XML downloaden, Versturen via
  Peppol en de transmissietijdlijn.
- Klant (fiscaal): Peppol-veldgroep + "Peppol-gegevens controleren", bezorgvoorkeur,
  kopersreferentie. Portaal: creditnota-badge + Peppol-status (interne fouten worden
  niet aan klanten getoond).

## Een echte provider aansluiten (exacte stappen)

1. **Adapter implementeren**: nieuwe klasse `XyzPeppolProvider : IPeppolProvider` in
   `Modules/Peppol/Services/` met een unieke `Key` (bv. `"xyz"`). Alle netwerk-I/O en
   authenticatie blijven binnen de adapter; retour-DTO's bevatten nooit credentials.
2. **Configuratie + secrets**: options-sectie `Peppol:Providers:xyz` (API-endpoint,
   key/certificaatreferentie) volgens het `JwtOptions`-patroon; secrets via user-secrets
   of omgevingsvariabelen, nooit in entiteiten, DTO's, audits of frontend.
3. **Registreren in DI** (`Program.cs`): extra
   `builder.Services.AddSingleton<IPeppolProvider, XyzPeppolProvider>();` — de factory
   pikt hem automatisch op via zijn `Key`. Geen andere codewijzigingen.
4. **Instellingen omzetten**: per juridische entiteit in `/peppol` → Configuratie de
   provider op `xyz` en de omgeving op `Live` zetten (`peppol.configure`). Bestaande
   transmissierijen behouden hun oude provider/omgeving-stempel.
5. **Webhook registreren** bij de provider: URL
   `https://<host>/api/peppol/webhook/xyz` met header `X-Peppol-Webhook-Secret` gelijk
   aan `Peppol:Webhook:Secret`. Overweeg per-provider secrets
   (`Peppol:Webhook:{key}:Secret`) zodra er meerdere providers actief zijn.
6. **Verifiëren**: "Verbinding testen" per entiteit (`ValidateParticipantAsync` op de
   eigen identiteit), daarna een testfactuur in sandbox-omgeving en de tijdlijn volgen.

## Bekende beperkingen

- Interne facturenlijst toont (nog) geen Peppol-kolom; het Uitgaand-tabblad is de
  transmissielijst. Eenheidscodes worden niet tegen de volledige UN/ECE rec 20-lijst
  gevalideerd (vrij ≤10 alfanumeriek; `UnEceUnitCodeMap.Selectable` ligt klaar voor een
  toekomstige UI-select maar wordt nog nergens getoond). De inkomende status
  `NeedsReview` is gereserveerd (de webhook slaat alles als `Received` op); de statussen
  `Draft`/`Validated` op transmissies zijn eveneens gereserveerd — rijen ontstaan direct
  als `Queued`. De dispatcher polt zonder per-rij throttle (prima voor sandbox; bij een
  echte provider `NextAttemptAt` hergebruiken voor pollpacing). Leveranciersfacturen
  kennen nog geen eigen module — de inkomende wachtrij is een beoordelingslijst.
