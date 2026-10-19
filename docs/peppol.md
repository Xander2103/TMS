# Peppol e-facturatie (2026-07-30)

Provider-neutrale Peppol-verzending van facturen en creditnota's (UBL 2.1, Peppol BIS
Billing 3.0), met een deterministische **sandbox-provider** als enige geregistreerde
adapter. Er bestaat en er wordt **geen** echte netwerkverbinding geclaimd; een echt
Access Point sluit later aan via de stappen onderaan. Zolang dat niet gebeurd is, is
**Live uitgeschakeld (fail-closed)** — zie [Live-beleid](#live-beleid-fail-closed).

## Live-beleid (fail-closed)

Productiehardening B4 (2026-10-19). De sandbox **simuleert** aflevering (Delivered na de
derde poll) en verstuurt niets. Belgische B2B-e-facturatie via Peppol is sinds 2026-01-01
verplicht, dus een "Afgeleverd via Peppol" dat niet echt gebeurde is een fout bewijs van
aflevering. Daarom:

- `IPeppolProvider.SupportsLiveNetwork` (default `false`) — alleen een echte, gecertificeerde
  Access-Point-adapter zet dit op `true`. De sandbox nooit. `PeppolLivePolicy` is de enige
  beslisser; zonder zo'n provider antwoordt elk pad met **"Peppol Live-provider niet
  geconfigureerd"**:
  - **Instellingen-API** weigert `Environment = Live` (400 op veld `environment`), ook als
    Peppol uit staat, en ook voor de sandbox-key wanneer er wél een live-provider bestaat.
  - **Versturen/opnieuw proberen** weigert een factuur waarvan de juridische entiteit op Live
    staat (vóór er iets gerenderd/opgeslagen wordt); de validatiechecklist toont issue
    `company.peppol_live_unavailable`.
  - **Dispatcher** biedt een Live-transmissie nooit aan, polt haar nooit en zet haar nooit op
    Delivered: zo'n rij (bv. van vóór dit beleid) wordt `Failed` met die reden en er gaat
    een interne melding uit. **Webhook**-statusupdates voor zo'n rij worden genegeerd.
  - **Startup** (elke omgeving): elke `peppol_settings`-rij op Live zonder live-provider wordt
    als **CRITICAL** gelogd; de app start gewoon (Peppol is optioneel) en verzending blijft
    voor die entiteit uitgeschakeld. Historische Live-rijen die de sandbox op Delivered zette,
    worden ook als CRITICAL gemeld (ze zijn niet via Peppol afgeleverd; niets wordt herschreven).
- **Health check** `peppol` (tag `peppol`, bewust **niet** `ready`): Unhealthy = Live
  geconfigureerd zonder provider; Degraded = onderbroken verzendingen in afwachting van
  reconciliatie, transmissies > 7 dagen onderweg, of historische Live-"leveringen" door de
  sandbox; Healthy = "alleen Sandbox beschikbaar" (data: `liveProviderConfigured`,
  `providers`, tellers).
- **UI**: Live staat in Configuratie uitgeschakeld als "Live — Live-provider niet
  geconfigureerd"; een Live-rij toont een waarschuwing; transmissies tonen "Sandbox — niets
  verzonden naar het Peppol-netwerk" of "Live-provider niet geconfigureerd". Sandbox blijft
  volledig bruikbaar.
- **Go-live-controle**: `SELECT * FROM peppol_settings WHERE "Environment"='Live' AND NOT
  "IsDeleted";` moet 0 rijen geven zolang er geen echte provider is (anders: health
  Unhealthy + CRITICAL in de log).

## Verzendgarantie: claim, idempotentiesleutel, herstel (H3)

- **Claim vóór verzending**: de dispatcher zet een rij met één voorwaardelijke `UPDATE`
  (`Queued` + vervallen backoff → `Submitting` + `IdempotencyKey` + `SubmissionClaimedAt`).
  Alleen wie die ene rij raakt, verstuurt — ook met meerdere dispatchers of API-instanties.
  `Submitting` is niet-terminaal (bezet de "één actieve transmissie per factuur"-plaats) en
  kan niet geannuleerd worden.
- **Sleutel naar de provider**: `PeppolOutboundRequest.IdempotencyKey`
  (`tms-peppol-{transmissionId}`), stabiel per transmissie. Een provider **moet** een herhaalde
  inzending met dezelfde sleutel als hetzelfde document behandelen en
  `FindSubmissionAsync(key)` ondersteunen. Een retry na Failed/Rejected is een nieuwe
  transmissie en dus een nieuwe sleutel (bewuste nieuwe verzending).
- **Onzekere uitkomst** (time-out/verbinding weg tijdens de call, of crash na de call): de rij
  blijft `Submitting`. Na `PeppolDispatcher.SubmittingReconcileAfter` (10 min, ruim boven elke
  provider-time-out) vraagt de dispatcher de provider op sleutel: **gevonden** →
  SubmittedToProvider met het provider-id (niet opnieuw verzonden); **niet gevonden** → terug
  naar Queued (telt als poging) en opnieuw aangeboden met dezelfde sleutel; **onbekend**
  (lookup faalt) → blijft Submitting (health Degraded). Nooit blind opnieuw versturen.
- Het resultaat van een geslaagde call wordt bewaard ook als de service net stopt; meldingen
  (`invoice_peppol_*`) gaan pas uit ná de bewaakte save van de overgang die ze aankondigen.
- **Polling (EXT-03)**: `NextPollAt` per rij, minst recent gepolde eerst, met backoff op
  leeftijd (< 1 u: 1 min, < 1 dag: 15 min, daarna 1 u) — een achterstand van nooit-finale
  rijen hongert nieuwere niet meer uit.
- **Webhook (EXT-04)**: faalt het opslaan aan onze kant (database/opslag), dan antwoordt de
  webhook **503 + `Retry-After: 60`** zodat de provider opnieuw aflevert (dedupe maakt dat
  veilig); 200 blijft voor verwerkt/dubbel/genegeerd.
- Migratie `ProductionHardeningPeppolSubmission` (additief: `IdempotencyKey`,
  `SubmissionClaimedAt`, `NextPollAt` + index `IX_peppol_transmissions_status_next_poll`).

## Architectuur

```
Invoice (bevroren snapshots) ──► PeppolInvoiceService ──► UblDocumentBuilder (puur, deterministisch)
        │ validatiecatalogus (NL)          │ XML + hash
        ▼                                   ▼
InvoicePeppolController          PeppolTransmissionService ──► PeppolTransmission (Queued)
  validate / preview / xml / send          │ payload in IFileStorageService ("peppol")
                                            ▼
                     PeppolDispatcherHostedService (30s, alle tenants)
                       reconcile: Submitting > 10 min ──► FindSubmissionAsync(sleutel)
                       submit:  Queued ──claim──► Submitting ──► IPeppolProvider.SendDocumentAsync(sleutel)
                       poll:    SubmittedToProvider/AcceptedByProvider (NextPollAt) ──► GetTransmissionStatusAsync
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
  (geen existence-oracle). 200 na persist/dedupe/negeren zodat de provider niet blijft
  herafleveren; **503 + Retry-After** als het opslaan aan onze kant mislukt (EXT-04).
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
   Verplicht voor Live: `SupportsLiveNetwork => true` (alleen voor een echt, gecertificeerd
   Access Point), de `IdempotencyKey` van `PeppolOutboundRequest` als idempotentiesleutel aan
   het Access Point doorgeven, en `FindSubmissionAsync(key)` implementeren (gooi een
   exception als het antwoord onbekend is — dan blijft de rij `Submitting`).
2. **Configuratie + secrets**: options-sectie `Peppol:Providers:xyz` (API-endpoint,
   key/certificaatreferentie) volgens het `JwtOptions`-patroon; secrets via user-secrets
   of omgevingsvariabelen, nooit in entiteiten, DTO's, audits of frontend.
3. **Registreren in DI** (`Program.cs`): extra
   `builder.Services.AddSingleton<IPeppolProvider, XyzPeppolProvider>();` — de factory
   pikt hem automatisch op via zijn `Key`. Geen andere codewijzigingen.
4. **Instellingen omzetten**: per juridische entiteit in `/peppol` → Configuratie de
   provider op `xyz` en de omgeving op `Live` zetten (`peppol.configure`) — pas nu staat de
   API dat toe. Bestaande transmissierijen behouden hun oude provider/omgeving-stempel.
   Controleer daarna de health check `peppol` (Healthy, `liveProviderConfigured=true`).
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
  als `Queued`. Leveranciersfacturen
  kennen nog geen eigen module — de inkomende wachtrij is een beoordelingslijst.
