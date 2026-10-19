# Opslag (Wave 5)

> Dit document behandelt twee soorten "opslag": de **magazijnopslag** (opslagverblijven en
> pallet-dagen, direct hieronder) en de **bestandsopslag** van de applicatie
> (`Storage:RootPath`, zie [Bestandsopslag](#bestandsopslag-storagerootpath) onderaan).

## De bewegingsklok

Elke collo (handling unit) krijgt automatisch **opslagverblijven** (`storage_stays`):

- **Openen**: een Ontvangst- of Retour-inboekscan op het magazijnstation (Trace & voorraad).
  Het station geeft zijn magazijn mee; een gekozen locatie impliceert het magazijn.
- **Sluiten**: centraal, bij ELK vertrek-custody-event (laadscan, retour-/herleveringslading,
  retour naar afzender, annulering) — één interceptor vangt elk huidig én toekomstig
  vertrekpad. Correcties sluiten en heropenen; historiek wordt nooit herschreven.
- Maximaal één open verblijf per collo; opnieuw ontvangen ververst alleen de locatie.

Beperking (bewust): een ritgebonden depot-retour opent de klok pas wanneer de collo op het
magazijnstation wordt ontvangen/ingeboekt — het rit-event kent het magazijn niet.

## Pallet-dagen naar facturatie

`GET /api/customers/{id}/storage?from=&to=` (en het paneel "Opslag per klant" op Trace &
voorraad) berekent per klant en periode de **pallet-dagen**: overlap van elk verblijf met de
periode, per **begonnen dag** (dezelfde conventie als de handmatige pallet-dag-invoer); open
verblijven tellen tot het periode-einde. Uitsplitsing per order en per magazijn.

De uitkomst voedt de bestaande dienstsoorten **Per dag** en **Per pallet/dag** — de operator
(en later de Wave-10-voorstellenmotor) leest hier het echte aantal; handmatig ingevulde
dagen op een order winnen altijd.

## Handling IN/UIT automatisch verkopen

Geen nieuw mechanisme nodig: een dienst met **Automatisch toepassen** + **magazijnconditie**
(en bv. soort *Per eenheid*) prijst de handling zodra de order het magazijn raakt — inclusief
de Wave-2-verkoopcode voor de juiste grootboekpost. Recept: Diensten & toeslagen → nieuwe
dienst "Handling in" (PerUnit, auto, magazijn X) en idem "Handling uit".

---

# Bestandsopslag (`Storage:RootPath`)

Productie-readiness 2026-10-18, blocker **B1**. Alles wat de applicatie als bestand bewaart,
staat onder één duurzame opslagmap (de *storage root*):

| Pad onder de root | Inhoud |
|---|---|
| `tenant-<id>/<categorie>/<guid>-<naam>` | Uploads: POD-handtekeningen en -foto's, order-/dossierdocumenten, personeelsdocumenten (ook medische), kwalificaties, vloot-, schade-, leasing- en tachograafbestanden, taak-, factuur- en afwezigheidsbijlagen, logo's, Peppol-payloads (UBL zoals verzonden/ontvangen) |
| `backups/` | In-app back-upsets (tenzij `Backups:Directory` iets anders zegt), zie [backup-restore.md](operations/backup-restore.md) |
| `message-sink/` | Alleen Development: de mail-/sms-sink (bevat ruwe tokens; nooit in back-ups) |

Vroeger was dit vast `<ContentRoot>/App_Data`. Op de server is de content root de
werkmap van de service, in de praktijk de bin-map die het deployscript met
`rsync --delete` overschrijft: **een deploy kon alle bestanden wissen**. Daarom is de map nu
configureerbaar én verplicht buiten de releasemap.

## Configuratie

| Sleutel (env-variabele) | Default | Gedrag |
|---|---|---|
| `Storage:RootPath` (`Storage__RootPath`) | — | Absoluut pad, bv. `/var/lib/transportationservice/storage`. Verplicht buiten Development. |
| `Storage:AllowContentRootStorage` (`Storage__AllowContentRootStorage`) | `false` | Legacy-opt-in: buiten Development zonder `RootPath` tóch `<ContentRoot>/App_Data` gebruiken (met waarschuwing in het log). Alleen als tijdelijke overgang. |
| `Backups:Directory` (`Backups__Directory`) | `<root>/backups` | Optioneel; absoluut en buiten de releasemap. |
| `Backups:MaxAgeHours` (`Backups__MaxAgeHours`) | `26` | Versheidsgrens voor de `backup`-healthcheck en de waarschuwing op Systeeminformatie. |

Eén resolver (`Common/Storage/StorageRoot`) bepaalt de root; bestandsopslag, back-ups, de
dev-mailsink en de GDPR-opruiming van die sink gebruiken allemaal dezelfde waarde.

### Gedrag per omgeving

- **Development**
  - `RootPath` leeg → `<ContentRoot>/App_Data`, zoals vroeger.
  - `RootPath` gezet → moet absoluut zijn. Een relatief pad weigert te starten.
  - Geen schrijfprobe; de map wordt pas bij gebruik aangemaakt.
- **Alle andere omgevingen** (Production, Staging, …): de app **weigert te starten** tenzij
  `RootPath` gezet is (of de legacy-opt-in aan staat). Bij het opstarten
  (`StorageRootValidator`, naast `StartupSecurityValidator`) wordt gecontroleerd dat:
  1. het pad absoluut is en geen bestandssysteemroot (`/`);
  2. het **niet** binnen de content root of de applicatie-binmap ligt (de releasemap);
  3. de map bestaat of aangemaakt kan worden;
  4. een probebestand geschreven, teruggelezen en verwijderd kan worden.

  Een gezette `Backups:Directory` moet ook absoluut zijn en buiten de releasemap liggen.
  Met de legacy-opt-in vervalt alleen controle 2: het log waarschuwt dan dat een deploy de
  bestanden kan wissen.

De foutmelding noemt telkens de sleutel en wat je moet doen. Er worden **nooit automatisch
bestanden verplaatst**: dat doe je met de migratieprocedure hieronder.

Rechten: de map is van de service-gebruiker (`deploy` in de huidige unit), modus `0750`.
De map hoort niet onder de webroot en niet onder `/var/www/transportationservice/TMS`.

## Healthcheck

`storage` (tag `ready`) doet dezelfde schrijf/lees/verwijder-probe als het opstarten. Een
volle of read-only schijf haalt de API zo uit rotatie, in plaats van alleen 500-fouten op
uploads te geven. De melding noemt geen pad (het endpoint is anoniem). Het endpoint zelf
(`/health/ready`) wordt elders gemapt.

## Integriteitscontrole (`--verify-storage`)

Een offline beheerderstool die elke bestandsverwijzing in de databank vergelijkt met de
storage root. Het tool draait **nooit** bij een healthcheck. Het gaat over alle tenants,
leest alleen sleutels (geen bestandsinhoud) en streamt per kolom (begrensd geheugen).
Soft-deleted rijen telt het niet mee.

```bash
# Als de service-gebruiker, met dezelfde omgeving als de service (tms.env, werkmap):
cd <WorkingDirectory van de unit>
sudo -u deploy bash -c 'set -a; . /etc/transportationservice/tms.env; set +a;
  ASPNETCORE_ENVIRONMENT=Production dotnet <API_BIN_DIR>/TransportationService.Api.dll --verify-storage'
# optioneel: --verify-storage --list-missing
```

- De opstartcontroles lopen eerst, dus de configuratie moet geldig zijn. Daarna stopt het
  proces: de webhost, de seeders en de achtergrondtaken starten niet.
- Uitvoer: per kolom het aantal verwijzingen, aanwezig, ontbrekend en ongeldig (een sleutel
  die buiten de root zou wijzen), plus een totaal. `--list-missing` toont daarnaast tot 200
  ontbrekende sleutels per kolom. Sleutels bevatten de oorspronkelijke bestandsnaam;
  behandel die uitvoer als persoonsgegeven.
- Exitcode `0` = alles aanwezig, `2` = er ontbreekt iets of er is een ongeldige sleutel,
  `1` = de controle zelf faalde (bv. databank onbereikbaar).
- Gecontroleerde kolommen (20, uit de audit `filerefs.sql`): `absences.AttachmentPath`,
  `damage_reports.AttachmentPath`, `employee_documents.StorageKey`,
  `employee_qualifications.DocumentPath`, `execution_exception_photos.StoragePath`,
  `fleet_documents.DocumentPath`, `inspections.AttachmentPath`,
  `invoice_attachments.StorageKey`, `invoices.SellerLogoStorageKey`,
  `leasing_contracts.StorageKey`, `legal_entities.LogoStorageKey`,
  `maintenance_records.AttachmentPath`, `order_documents.DocumentPath`,
  `peppol_incoming_documents.PayloadStorageKey`, `peppol_transmissions.PayloadStorageKey`,
  `pod_photos.StoragePath`, `proofs_of_delivery.SignaturePath`, `stop_executions.PodPath`,
  `tachograph_calibrations.StorageKey`, `task_attachments.StorageKey`.
  Een nieuwe kolom met bestandsverwijzingen moet je toevoegen aan
  `StorageIntegrityVerifier.Columns`.

Gebruik het tool na elke migratie, na elke restore en in de maandelijkse restore-drill.
Bestaande, al bekende ontbrekende bestanden (de testdata had er 7) noteer je als
nulmeting, zodat een nieuw verschil opvalt.

## Migratie van `App_Data` naar `Storage:RootPath`

**Lees eerst dit.** Deze release start buiten Development niet zonder `Storage:RootPath`
(of de opt-in). Het deployscript overschrijft de bin-map met `rsync --delete`. Staat
`App_Data` in die bin-map, dan is het bij de volgende deploy weg. Dat geldt óók voor de deploy
van deze release zelf, tenzij het script `App_Data/` uitsluit. **Kopieer de data dus vóór de
deploy.** Verplaats niets: je kopieert, je controleert en je houdt het origineel.

1. **Bepaal waar de bestanden nu staan.**
   ```bash
   systemctl show -p WorkingDirectory transportationservice-api
   sudo ls -la <WorkingDirectory>/App_Data            # tenant-*, backups, (message-sink)
   ```
   Vind je daar niets, controleer dan ook de bin-map
   (`/var/www/transportationservice/TMS/TransportationService.Api/bin/Release/net10.0/App_Data`).
2. **Maak de nieuwe map aan**, buiten de releasemap, op een volume dat in de back-up zit:
   ```bash
   sudo install -d -o deploy -g deploy -m 0750 /var/lib/transportationservice/storage
   ```
3. **Eerste kopie terwijl de service draait** (let op de slashes achteraan):
   ```bash
   sudo rsync -a <WorkingDirectory>/App_Data/ /var/lib/transportationservice/storage/
   ```
4. **Zet de configuratie**: voeg aan `/etc/transportationservice/tms.env` toe:
   ```
   Storage__RootPath=/var/lib/transportationservice/storage
   ```
   Stond `Backups__Directory` op een pad onder `App_Data`, verwijder die regel (de default
   wordt `<root>/backups`) of laat hem naar de nieuwe map wijzen. De huidige release negeert
   de nieuwe sleutel; hij werkt pas na de herstart.
5. **Stop de service** en doe de laatste delta-kopie (schrijft niets terug naar de bron):
   ```bash
   sudo systemctl stop transportationservice-api
   sudo rsync -a <WorkingDirectory>/App_Data/ /var/lib/transportationservice/storage/
   sudo chown -R deploy:deploy /var/lib/transportationservice/storage
   ```
6. **Controleer aantallen en checksums** (de dev-sink mag ontbreken):
   ```bash
   SRC=<WorkingDirectory>/App_Data; DST=/var/lib/transportationservice/storage
   sudo find "$SRC" -type f -not -path '*/message-sink/*' | wc -l
   sudo find "$DST" -type f -not -path '*/message-sink/*' | wc -l      # zelfde aantal
   (cd "$SRC" && sudo find . -type f -not -path './message-sink/*' -print0 | sort -z \
      | sudo xargs -0 sha256sum) > /tmp/storage-src.sha256
   (cd "$DST" && sudo sha256sum -c --quiet /tmp/storage-src.sha256) && echo CHECKSUMS OK
   ```
7. **Bewaar het origineel buiten de releasemap**, vóór er gedeployd wordt:
   ```bash
   sudo tar -C <WorkingDirectory> -czf \
     /var/backups/transportationservice/App_Data-premigration-$(date +%Y%m%d-%H%M%S).tar.gz App_Data
   ```
   Houd die kopie minstens 30 dagen (of tot de volgende geslaagde restore-drill).
8. **Deploy/start** de nieuwe release (het deployscript start de service; anders
   `sudo systemctl start transportationservice-api`). In het log moet staan:
   `Durable storage root: /var/lib/transportationservice/storage (Configured).`
9. **Verifieer**:
   - `/health/ready` is gezond (inclusief de check `storage`);
   - `--verify-storage` (zie hierboven) op de nieuwe root geeft hetzelfde aantal ontbrekende
     bestanden als op de oude kopie. Ter vergelijking: pak het archief van stap 7 uit in een
     tijdelijke map en draai het tool met `Storage__RootPath=<die map>`;
   - in de UI: download een bestaand document, een POD-handtekening en een factuur-PDF met
     logo; upload een testdocument en controleer dat het onder
     `/var/lib/transportationservice/storage/tenant-…` verschijnt.
10. **Ruim pas op na de controle.** De oude `App_Data` in de bin-map verdwijnt bij een
    volgende deploy vanzelf; de tar van stap 7 is je terugvaloptie.

**Terugdraaien:** zet `Storage__RootPath` terug naar een uitgepakte kopie van het archief
uit stap 7 (nooit naar een map binnen de releasemap) en herstart.

Tijdelijk de oude locatie houden kan met `Storage__AllowContentRootStorage=true`. Dat is
alleen veilig zolang het deployscript `App_Data/` uitsluit of de content root niet de
bin-map is.
