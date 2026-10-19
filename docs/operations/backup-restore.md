# Back-up en herstel (runbook)

Productie-readiness 2026-10-18: **H4** (back-ups waren alleen de databank) en **H8**
(terugzetten via een HTTP-request). Dit runbook beschrijft wat een in-app back-up bevat, hoe je
die bewaakt en buiten de server brengt, en hoe je **offline** herstelt. De opslagmap zelf staat
beschreven in [../storage.md](../storage.md) (sectie *Bestandsopslag*).

## 1. Wat een back-up is

Een in-app back-up (handmatig, automatisch om `Backups:AutomaticHourUtc`, of een
veiligheidsback-up) is een **set van drie bestanden** in `Backups:Directory` (default
`<Storage:RootPath>/backups`):

| Bestand | Inhoud |
|---|---|
| `backup-<db>-<ts>-<bron>-<id>.dump` | `pg_dump -Fc` van de hele databank (alle tenants) |
| `backup-…-<id>.files.tar.gz` | De storage root (`tenant-*/…`), **zonder** `backups/` en `message-sink/`. De paden zijn relatief aan de root. |
| `backup-…-<id>.sha256` | Manifest in `sha256sum`-formaat voor beide bestanden |

De set telt pas als **Voltooid** wanneer alle stappen slagen:

1. `pg_dump` met exitcode 0 en een niet-leeg bestand;
2. het archief is geschreven;
3. het manifest is geschreven;
4. `pg_restore --list` kan de dump lezen;
5. het archief is volledig teruggelezen, met hetzelfde aantal bestanden en een geldige gzip-CRC;
6. beide checksums kloppen met het manifest.

Faalt een stap, dan verwijdert de app de halve set. Er komt een rij met status **Mislukt** en de
reden (bv. `bestandsarchief (opslagmap) mislukt (UnauthorizedAccessException)` of
`pg_dump exitcode 1`), een auditregel `BackupFailed` en een error-log. De stderr van
`pg_dump`/`pg_restore` staat in het log. Een mislukte run telt nooit als verse back-up.

**Niet in de back-up, met opzet:**

- `ColumnEncryption:Key` en `ColumnEncryption:PreviousKeys`. Die staan in
  `/etc/transportationservice/tms.env`, niet in de storage root. Bewaar ze in een **vault**,
  apart van de back-ups. Zonder dezelfde sleutels zijn rijksregister- en
  identiteitskaartnummers in een herstelde databank onleesbaar: de app start "gezond", maar het
  personeelsdetail geeft een 500 (audit H5).
- De rest van `tms.env` (databankwachtwoord, JWT-sleutel, SMTP), de systemd-unit en de
  nginx-config. Die bewaar je apart (vault of configuratiebeheer).
- De dev-mailsink (ruwe tokens) en de back-upmap zelf (geen dumps in dumps).

### Bewaking

- De healthcheck `backup` (tag `backup`, bewust **niet** `ready`) is *Unhealthy* als er geen
  geslaagde back-up is van minder dan `Backups:MaxAgeHours` (default 26 u). Hij is
  *Degraded* als de laatste poging na de laatste geslaagde back-up mislukte. De data van de
  check bevat alleen tijdstippen. Laat de externe monitoring hierop alarmeren. Een oude
  back-up haalt de API niet uit rotatie.
- Instellingen → Systeeminformatie → Back-ups toont de laatste geslaagde back-up en
  waarschuwt als die te oud is.
- SQL-controle:
  ```sql
  SELECT "FileName","Source","Status","Note","SizeBytes","CreatedAtUtc"
  FROM database_backups ORDER BY "CreatedAtUtc" DESC LIMIT 5;
  ```

### Off-host kopie (verplicht)

De in-app back-up staat op dezelfde server als de databank en de bestanden. Kopieer elke
nacht, na de automatische back-up, de **volledige sets** (dump + archief + manifest) en de
dumps van het deployscript (`/var/www/transportationservice/backups`) **versleuteld** naar
een andere locatie, bv. met restic of borg. Alarmeer als een kopie ontbreekt. Controleer op
de bestemming met `sha256sum -c <set>.sha256`.

### Downloaden vanuit de app

`GET /api/system-info/backups/{id}/download` vraagt `backups.download` **én** lidmaatschap
van de systeemrol **Administrator**. Een tenant-rol met dezelfde permissie krijgt 403. Je
downloadt alleen de `.dump`: het bestandsarchief en het manifest blijven op de server.

**Risico:** een dump bevat de gegevens van **alle** tenants, onversleuteld, inclusief
persoonsgegevens. Gebruik downloaden alleen als het echt nodig is, bewaar het bestand
versleuteld en verwijder het daarna. Elke download wordt geauditeerd
(`BackupDownloaded`). De off-host kopie hierboven is de normale route.

## 2. Terugzetten vanuit de app: uitgeschakeld

`POST /api/system-info/backups/{id}/restore` antwoordt buiten Development met **409** en
verwijst naar dit runbook. De knop *Terugzetten* is dan verborgen. Waarom: `pg_restore
--clean` op de live databank vanuit een HTTP-request kan alles wissen, ook nieuwere
auditlogs. Een afgebroken request stopt het proces niet, en de app blijft ondertussen
schrijven. In Development blijft de oude flow bestaan (getypte bevestiging en
veiligheidsback-up), maar die zet alleen de databank terug, niet de bestanden.

## 3. Offline herstel

Voer dit uit als beheerder op de server (of op een nieuwe server). Noteer elke stap en de
tijden in het incidentlog. Herstel bij voorkeur eerst naar een **nieuwe databanknaam**, zodat
de huidige staat blijft bestaan tot het herstel geverifieerd is.

Voorbeeldwaarden: service `transportationservice-api`, service-gebruiker `deploy`, env-bestand
`/etc/transportationservice/tms.env`, databank `tms`, rol `tms`, storage root
`/var/lib/transportationservice/storage`.

0. **Kies de set** en noteer de `SchemaVersion` (Systeeminformatie, of de kolom in
   `database_backups`). Alles wat na het tijdstip van de set geschreven is, gaat verloren
   (RPO = back-upinterval). De release die je straks start, moet hetzelfde of een **nieuwer**
   schema hebben: deze releases zijn forward-only. Start nooit oudere binaries op een nieuwer
   schema.
1. **Onderhoud: stop de service** (en zet eventueel een onderhoudspagina in nginx):
   ```bash
   sudo systemctl stop transportationservice-api
   systemctl is-active transportationservice-api      # moet 'inactive' zijn
   ```
   Zo kunnen er geen schrijfacties, achtergrondtaken of automatische back-ups meer tussen
   komen.
2. **Controleer de checksums** in de map met de set:
   ```bash
   cd <backupmap> && sha256sum -c backup-…-<id>.sha256     # beide regels: OK
   ```
   Een set zonder `.sha256` of `.files.tar.gz` (van vóór deze release) bevat **alleen de
   databank**. Neem de bestanden dan uit de off-host bestandsback-up van hetzelfde tijdstip.
3. **Bewaar de huidige staat** (als die nog bestaat), buiten de releasemap:
   ```bash
   TS=$(date +%Y%m%d-%H%M%S)
   pg_dump -h <host> -U tms -w -d tms -Fc -f /var/backups/transportationservice/tms-before-restore-$TS.dump
   sudo tar -C /var/lib/transportationservice -czf \
     /var/backups/transportationservice/storage-before-restore-$TS.tar.gz storage
   ```
4. **Maak de rol en de databank aan** (op een nieuwe server eerst de rol):
   ```bash
   sudo -u postgres createuser --pwprompt tms                 # alleen op een nieuwe server
   sudo -u postgres createdb -O tms tms_restore_$TS
   ```
5. **Zet de databank terug.** De `pg_restore`-versie moet minstens de major-versie van de
   server hebben. Het wachtwoord gaat via `PGPASSFILE` (0600), nooit op de commandoregel.
   ```bash
   pg_restore -h <host> -U tms -w -d tms_restore_$TS --no-owner --role=tms \
     --exit-on-error -j 4 <backupmap>/backup-…-<id>.dump
   ```
   Wissel daarna de namen om (er mogen geen verbindingen open zijn):
   ```sql
   ALTER DATABASE tms RENAME TO tms_old_<TS>;
   ALTER DATABASE tms_restore_<TS> RENAME TO tms;
   ```
   (Of laat `ConnectionStrings__DefaultConnection` naar de nieuwe naam wijzen.)
6. **Zet de bestanden terug in `Storage:RootPath`.** Het archief bevat geen `backups/`; die map
   zet je terug uit de oude root.
   ```bash
   ROOT=/var/lib/transportationservice/storage
   sudo mv "$ROOT" "$ROOT.pre-restore-$TS"
   sudo install -d -o deploy -g deploy -m 0750 "$ROOT"
   sudo tar -C "$ROOT" -xzf <backupmap>/backup-…-<id>.files.tar.gz
   [ -d "$ROOT.pre-restore-$TS/backups" ] && sudo mv "$ROOT.pre-restore-$TS/backups" "$ROOT/backups"
   sudo chown -R deploy:deploy "$ROOT"
   ```
   Zet je de set van een andere server terug, kopieer dan eerst de drie bestanden naar die
   server en controleer de checksums opnieuw (stap 2).
7. **Secrets:** zet in `tms.env` **dezelfde** `ColumnEncryption__Key` en alle
   `ColumnEncryption__PreviousKeys__N` uit de vault, zoals ze golden toen de back-up gemaakt
   werd. `Storage__RootPath` wijst naar de root uit stap 6. De JWT-sleutel mag anders zijn
   (iedereen moet dan opnieuw inloggen). Controleer ook de SMTP-instellingen.
8. **DR-voorzichtigheid:** een herstelde databank bevat ook haar outbox. Op een test- of
   stagingserver verzend je anders echte mails. Zet daar eerst de wachtende berichten stil:
   ```sql
   UPDATE outbox_messages SET "Status" = 'Suppressed' WHERE "Status" = 'Pending';
   ```
9. **Start** de service: `sudo systemctl start transportationservice-api`. Controleer in het
   log of de opstartcontroles slaagden (`Durable storage root: … (Configured)`) en of er geen
   migratiefouten zijn.
10. **Verifieer**:
    - de bestanden:
      ```bash
      sudo -u deploy bash -c 'set -a; . /etc/transportationservice/tms.env; set +a;
        ASPNETCORE_ENVIRONMENT=Production dotnet <API_BIN_DIR>/TransportationService.Api.dll --verify-storage'
      ```
      Verwacht exitcode 0, of exact de bekende nulmeting aan ontbrekende bestanden
      (zie [../storage.md](../storage.md));
    - `/health/ready` is gezond;
    - smoketest in de UI:
      - inloggen;
      - een order met document en POD openen en het document en de handtekening downloaden;
      - een factuur-PDF met logo openen;
      - het detail van een werknemer met rijksregisternummer openen: een 500 hier betekent de
        verkeerde encryptiesleutel;
    - `SELECT count(*) FROM audit_logs;` en andere steekproeven komen overeen met wat je van
      de set verwacht.
11. **Afronden:** maak meteen een nieuwe back-up (Systeeminformatie → *Nieuwe back-up maken*)
    en controleer de status *Voltooid*. Houd `tms_old_<TS>`, `storage.pre-restore-<TS>` en de
    tarballs van stap 3 minstens 7 dagen. Ruim ze daarna bewust op (`DROP DATABASE`,
    `rm -r`).

**Terugdraaien tijdens de procedure:** stop de service, wissel de databanknamen terug,
zet `storage.pre-restore-<TS>` terug op zijn plaats en start opnieuw.

## 4. Maandelijkse restore-drill

Doe stap 2 tot en met 10 elke maand met de nieuwste off-host set, op een **aparte** host of
cluster en nooit op productie. Gebruik een niet-bestaande SMTP-host of stap 8. Noteer de
duur (RTO), de grootte van de set en de uitkomst van `--verify-storage`. Een drill die faalt,
is een incident.
