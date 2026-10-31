# Een nieuw bedrijf (tenant) inrichten — provisioning

Een productiedatabase start **leeg**: 0 tenants, 0 gebruikers. De Development-seeders
(`MasterDataSeeder`, `DevAdminSeeder`, `DevDemoDataSeeder`, …) draaien buiten Development nooit,
en dat blijft zo: er bestaat in productie geen `admin@dev.local`, geen demowachtwoord en geen
demodata. Een nieuw bedrijf richt je in met het expliciete, idempotente provisioningcommando van
de API-binary zelf. Het draait op de volledig opgebouwde host (zelfde configuratie, database,
mailprovider, kolomversleuteling en startupvalidatie als de service), maar start **geen**
webserver en **geen** achtergrondjobs; het proces stopt zodra het klaar is.

## 1. Wat het commando aanmaakt

| Onderdeel | Inhoud | Bij een tweede run |
|---|---|---|
| Tenant | naam + slug (uniek) | bestaat al → gerapporteerd, niets overschreven |
| Bedrijfsinstellingen | tijdzone `Europe/Brussels`, taal `nl`, standaardnummering (`KL-`, `CH-`, `ORD-`, `RIT-`, `FAC-`, `MED-`, …), bedrijfsnaam/btw/land uit de opties | bestaat al |
| Systeemrol `Administrator` | volledige permissiecatalogus (`PermissionCatalogSeeder`) | bestaat al; catalogus wordt bijgewerkt |
| 12 standaardrollen | `DefaultRoleSeeder`, actuele sjabloonversie (`DefaultRoleUpgrades.CurrentVersion`, nu v41) | ontbrekende aangevuld |
| Referentielijsten | afdelingen, eenheden, contracttypes, talen, nationaliteiten, voertuig-/opleggercategorieën, functies, klantcategorieën, takencategorieën, uitgiftecategorieën, startlijst diensten/toeslagen | alleen ontbrekende codes |
| Kwalificatietypes | globale catalogus (rijbewijzen, Code 95, ADR, …) | alleen ontbrekende |
| Eigen bedrijf (juridische entiteit) | standaardentiteit uit `--legal-entity-name`/`--name`, `--vat`, `--company-number`, `--country` | alleen als de tenant er nog geen heeft |
| Vervaltermijn-herinneringen | 8 standaardbeleiden | alleen ontbrekende |
| Uitgiftesjablonen | startcatalogus (20) | nooit opnieuw na eigen beheer |
| Eerste beheerder | gebruiker **zonder wachtwoord** met rol Administrator + eenmalige activatielink per e-mail (72 uur geldig) | bestaat al → gerapporteerd; geen nieuwe link zonder `--resend-activation` |

Nooit: `admin@dev.local`, een (demo)wachtwoord, test- of demorecords (klanten, medewerkers,
voertuigen, orders, facturen), of een **tweede** beheerder voor een bestaande tenant (extra
beheerders voeg je toe in de applicatie onder Instellingen > Gebruikers). Een e-mailadres dat al
bij een gebruiker van een andere tenant hoort, wordt geweigerd. Alles loopt in één
databasetransactie: een weigering halverwege laat niets achter.

## 2. Voorwaarden

1. **Omgeving** — exact dezelfde variabelen als de service (`/etc/transportationservice/tms.env`):
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `ConnectionStrings__DefaultConnection`
   - `Jwt__SigningKey`, `ColumnEncryption__Key` (zonder deze weigert de startupvalidator)
   - `Email__Smtp__Host`, `…__Port`, `…__Username`, `…__Password`, `…__FromAddress`,
     `…__UseTls=true` — de activatiemail gaat via de outbox naar SMTP
   - **`Frontend__BaseUrl`** — de absolute publieke URL van de webapp (bv.
     `https://tms.bedrijf.be`). Zonder absolute URL weigert het commando buiten Development: een
     activatielink zonder domein is onbruikbaar.
2. **Migraties toegepast** — het commando migreert nooit zelf en weigert (exitcode 3) zolang er
   openstaande migraties zijn:

   ```bash
   dotnet ef database update --project TransportationService.Api \
       --startup-project TransportationService.Api --no-build --configuration Release
   ```

   (of gewoon de deploy: `deploy-transportationservice.sh` doet deze stap.)
3. **Werkmap = de binary-map** (daar staan `appsettings*.json`), bv.
   `/var/www/transportationservice/TMS/TransportationService.Api/bin/Release/net10.0`.

## 3. Het commando

Aanbevolen: via `systemd-run`, zodat het env-bestand exact zo geladen wordt als door de service
(zelfde gebruiker, zelfde EnvironmentFile-parser) en geen secret in je shell belandt:

```bash
sudo systemd-run --pty --wait --collect --uid=deploy \
  -p EnvironmentFile=/etc/transportationservice/tms.env \
  -p WorkingDirectory=/var/www/transportationservice/TMS/TransportationService.Api/bin/Release/net10.0 \
  /usr/bin/dotnet TransportationService.Api.dll --provision-tenant \
    --name "Transport Peeters BV" \
    --slug transport-peeters \
    --admin-email jan.peeters@peeters.be \
    --admin-first-name Jan --admin-last-name Peeters \
    --legal-entity-name "Transport Peeters BV" --vat BE0123456749 --company-number 0123456749
```

| Optie | Verplicht | Betekenis |
|---|---|---|
| `--provision-tenant` | ja | activeert het commando |
| `--name` | ja | bedrijfsnaam (max. 200) |
| `--slug` | ja | unieke sleutel: kleine letters, cijfers, koppeltekens (max. 100) |
| `--admin-email` | ja | e-mailadres van de eerste beheerder (ontvangt de activatiemail) |
| `--admin-first-name` / `--admin-last-name` | nee | default `Beheerder` / bedrijfsnaam |
| `--legal-entity-name` | nee | juridische naam van het eigen bedrijf (default `--name`) |
| `--vat` | nee | btw-nummer; Belgische nummers worden strikt gevalideerd (mod-97) |
| `--company-number` | nee | ondernemingsnummer |
| `--country` | nee | ISO-landcode, default `BE` |
| `--timezone` | nee | IANA-tijdzone, default `Europe/Brussels` |
| `--language` | nee | `nl` (default), `fr` of `en` — taal van tenant, beheerder en activatiemail |
| `--resend-activation` | nee | beheerder bestaat al maar activeerde nog niet: oude link intrekken, nieuwe mail versturen |
| `--print-activation-link` | nee | **zie §5** — toont de link op de terminal |

De provisioningopties komen nooit in de `IConfiguration` van de host terecht.

**Exitcodes:** `0` klaar · `1` geweigerd (reden op stderr, bv. tweede beheerder, adres van een
andere tenant, ontbrekende `Frontend:BaseUrl`, ongeldig btw-nummer) · `2` ongeldige/ontbrekende
opties · `3` openstaande migraties. Een startupvalidatiefout (geen SMTP, geen kolomsleutel, …)
breekt het proces af zoals bij een gewone start.

**Uitvoer** (niet-geheim): wat aangemaakt werd en wat al bestond, tenant-id, beheerder-id, het
outbox-bericht-id van de activatiemail en de vervaldatum van de link. De link of het token zelf
verschijnt nooit in de uitvoer of in de logs.

## 4. Activatie en eerste aanmelding

1. De activatiemail staat in de outbox (`outbox_messages`, soort `user_activation`). De **draaiende
   TMS-service** verstuurt hem binnen ~30 s via SMTP. Draait de service nog niet, dan gaat de
   mail uit bij de eerste start. Controle: `SELECT "Status","AttemptCount","FailureReason" FROM
   outbox_messages WHERE "Kind"='user_activation' ORDER BY "CreatedAt" DESC LIMIT 1;`
   (`Sent` = verstuurd; de body is na verzending gewist).
2. De beheerder opent `https://<Frontend:BaseUrl>/activeren?token=…`, kiest een wachtwoord
   (wachtwoordbeleid geldt) en meldt zich aan op `/login`. De link is eenmalig en 72 uur geldig.
3. Verlopen of kwijt? Opnieuw met dezelfde opties plus `--resend-activation` (de vorige link
   wordt ingetrokken). Is het account al geactiveerd, dan weigert dit; gebruik dan
   "Wachtwoord vergeten" op het aanmeldscherm (die mail loopt nu ook via de outbox).

## 5. Air-gapped installaties: `--print-activation-link`

Alleen als er **geen werkende uitgaande mail** is. De link is een credential: wie hem ziet, kan
tot hij gebruikt of verlopen is het beheerdersaccount overnemen. Daarom:

- gebruik de vlag alleen interactief (`systemd-run --pty`), nooit in een script, CI-job of
  logbestand; `journalctl`/scrollback bevat de link anders;
- geef de link rechtstreeks aan de beheerder en laat hem meteen activeren;
- wis daarna de terminalhistoriek; bij twijfel: `--resend-activation` (trekt de oude link in).

De mail wordt ook dan in de outbox gezet (en mislukt zonder SMTP-verbinding; na verzending of
definitieve mislukking wordt de body gewist).

## 6. Daarna configureren (in de applicatie, als beheerder)

1. **Instellingen > Bedrijf**: adres, IBAN, contactgegevens, logo; controleer nummering en
   tijdzone.
2. **Eigen bedrijven** (juridische entiteiten): adres, IBAN, factuurprefix/-formaat, en voor
   Peppol het Peppol-ID/-schema.
3. **Gebruikers en rollen**: extra beheerders en medewerkers aanmaken (elk krijgt een eigen
   activatiemail), rollen controleren.
4. **Referentielijsten** (afdelingen, eenheden, diensten/toeslagen met **voorbeeldprijzen** —
   pas die aan of deactiveer ze vóór gebruik).
5. **Berichten/notificaties**: afzender, sjablonen en regels.
6. **Peppol**: alleen Sandbox is beschikbaar zolang er geen echte Access-Point-provider is
   geconfigureerd — zie [`docs/peppol.md`](../peppol.md#live-beleid-fail-closed).
7. Back-ups en monitoring volgens `docs/delivery/operations.md` §6.

## 7. Bestaande tenants

Bij **elke** start (alle omgevingen) vult `TenantReferenceDataSync` voor bestaande tenants de
ontbrekende referentiedata aan: kwalificatietypes, referentielijsten (alleen codelijsten; geen
geprijsde startlijsten), vervaltermijn-herinneringen en — alleen voor een tenant zonder — een
standaard juridische entiteit uit de eigen bedrijfsinstellingen. Wat een tenant verwijderde
(soft delete) wordt nooit opnieuw aangemaakt. Er worden nooit gebruikers, rollen of demorecords
aangemaakt; een fout in deze stap wordt gelogd en blokkeert de start niet.
