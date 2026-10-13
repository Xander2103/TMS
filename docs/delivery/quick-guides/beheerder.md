# Snelgids — Beheerder

*Gebruikers, rollen, stamgegevens, tarieven en instellingen. Alles staat in het menu onder
**Parameters**.*

## Gebruikers

- **Parameters → Beheer → Gebruikers** (`/users`): accounts aanmaken, bewerken en
  (de)activeren, en rollen toekennen.
- Een **klantgebruiker** (portaaltoegang) is een gewoon account met een gekoppelde klant;
  zulke gebruikers komen automatisch en uitsluitend in het klantportaal terecht.

## Rollen & rechten

- **Parameters → Beheer → Rollen & rechten** (`/roles`): rollen aanmaken en per rol de
  rechten (permissies) aan- of uitzetten op de roldetailpagina.
- **Roltemplates**: het systeem levert standaardrollen mee (planner, dispatcher,
  management, boekhouding, HR, chauffeur, magazijn, klantportaal en de
  portaal-add-ons). Deze sjablonen worden automatisch aangevuld bij nieuwe versies; eigen
  aanpassingen aan rollen blijven mogelijk.
- **Parameters → Beheer → Functie → rol** (`/job-function-mappings`): koppel een
  personeelsfunctie aan één of meer rollen — bij het aanmaken van een account uit een
  medewerker worden die rollen voorgesteld; de beheerder bevestigt altijd.

## Activiteitstypes

**Parameters → Stamgegevens → Activiteitstypes** (`/settings/activity-types`): de soorten
dossierwerk (distributie, kraanwerk, plateau, opslag, …). Gedrag stuurt u volledig met de
capability-vlaggen: heeft stops, ondersteunt goederen, planningsrelevant,
magazijnrelevant, duur toegestaan, snelstart-tegel. Precies één actief type is het
standaard-transporttype. Verwijderde types komen nooit vanzelf terug.

## Verkoopcodes (verkoopcategorieën)

**Parameters → Beheer → Boekhouding** (`/settings/accounting`): beheer de
**verkoopcategorieën** en hun koppeling aan grootboekrekeningen. Factuurlijnen bevriezen
de verkoopcode op het moment van facturatie. De code wordt bepaald door:

- de **Verkoopcategorie**-kolom op een tariefregel (regel wint van tabel);
- het veld *Verkoopcategorie* op de tarieventabel zelf;
- het veld *Verkoopcategorie* op een dienst/toeslag (leeg = standaardrol Supplementen);
- leeg overal = de standaardrol Transport.

## Tarieventabellen en prijsinstellingen

- **Parameters → Prijzen → Tarieventabellen** (`/pricing/tables`): per tabel de tabbladen
  **Regels** (raster met staffels, klantafwijkingen per rij, minimum aantal en
  afrondingsstap), **Klanten** (koppelingen met eigen % of vast bedrag), **Afleiding**
  (bv. "NL = BE +30%"), **Toeslagen**, **Kortingen**, **Prijsaanpassing** (geplande
  verhogingen met ingangsdatum), **Versies** en de **Controle**-sectie die
  configuratiefouten en -waarschuwingen rapporteert. Excel-export/-import maakt een
  rondgang met preview mogelijk. Een tariefregel kan aan een **activiteitstype** gebonden
  worden (bv. aparte kraan- en plateautarieven binnen één dossier); zo'n regel wint
  altijd van een algemene regel.
- **Parameters → Prijzen → Prijsinstellingen** (`/settings/pricing`): **zones** (land +
  postcodereeksen — hetzelfde zoneconcept dat ook de ritvoorstellen gebruikt), **diensten
  & toeslagen** (incl. automatisch toepassen, ADR-, kraan-, plateau-, Moffett-, retour-,
  activiteitstype-, magazijn-, tijd-, weekend- en feestdagvoorwaarden, en het soort
  *Per km* voor bv. een Maut-toeslag), **eenheden** en de **feestdagkalender** die de
  feestdagtoeslagen stuurt.
- Per dienst kiest u de **Bron van het aantal**: *Besteld* (standaard), *Ingescand in*,
  *Ingescand uit*, *Picking* of *Pallet-dagen*. Scan-gedreven diensten tellen de
  werkelijke magazijnscans (unieke colli); *Pallet-dagen* volgt de opslagklok. Handmatig
  ingevulde aantallen op een order winnen altijd; zonder scans verschijnt een
  informatieve lijn, nooit €0.
- **Parameters → Prijzen → Kostentarieven** (`/cost-rates`) voor de kostzijde van ritten.

## Documentregels

**Parameters → Beheer → Documentregels** (`/settings/document-rules`): regels op
prioriteit die bepalen welk vervoersdocument een opdracht krijgt, op basis van
grensoverschrijdend vervoer, ADR en/of activiteitstype. De volgorde van beslissen is
altijd: keuze op de opdracht → **documentstrategie van de klant** (op de klantfiche:
*Zelf aanmaken*, *Klantdocument* of *Per opdracht kiezen*) → documentregels → ingebouwde
standaard (ADR → CMR, grensoverschrijdend → CMR, anders leveringsbon). Een opdracht op
*Per opdracht kiezen* zonder gemaakte keuze telt als ontbrekende informatie en wordt
nooit automatisch gedrukt.

## Doorrekenbeleid (incidenten)

**Parameters → Beheer → Doorrekenbeleid** (`/settings/charge-policies`, recht
*problems.approve_charge*): per klant en/of incidenttype de modus **Nooit**,
**Voorstellen** of **Automatisch**, met optioneel standaardbedrag. Het meest specifieke
beleid wint en vuurt één keer zodra de verantwoordelijkheid van een incident op *Klant*
landt. *Automatisch* boekt via hetzelfde geauditeerde mechanisme als een handmatige
goedkeuring; *Nooit* blokkeert ook handmatig voorstellen.

## Eigen bedrijven (facturerende entiteiten)

**Parameters → Beheer → Eigen bedrijven** (`/settings/legal-entities`): de juridische
entiteiten waaruit u factureert, met nummerreeksen (factuur- en creditnotavoorvoegsel),
identiteit en Peppol-gegevens. Per klant stelt u op de klantfiche de **standaard
facturerende entiteit** en de **toegestane entiteiten** in; dossiers erven de entiteit
stil (klantstandaard → bedrijfsstandaard).

## Meldingsregels

**Parameters → Koppelingen & meldingen → Meldingen en e-mails**
(`/settings/notifications`), met tabbladen:

- **Gebeurtenissen** — de volledige cataloog per groep; per gebeurtenis aan/uit, kanalen
  (in-app / e-mail), de ontvangers en de schakelaar **Controle vóór verzending** (leeg =
  catalogusstandaard; schade, mislukte levering en vertraging staan standaard op
  controle). Alleen klantmail wordt vastgehouden.
- **Te controleren** — de controlewachtrij: vastgehouden klantmails **vrijgeven** of
  **afwijzen** (recht *messaging.manage*).
- **Sjablonen** — e-mailsjablonen per tenant en per klant, met placeholder-controle en
  voorbeeldweergave.
- **Ontvangers** — uitleg van de ontvangertypes (klantcontact, communicatieregel,
  interne permissie/rol, vast adres, chauffeur).
- **Klantafwijkingen** — per klant opengestelde gebeurtenissen aan/uit.
- **Verzonden berichten / Mislukte berichten** — de uitgaande wachtrij met detail en
  opnieuw versturen.

Aanvullend: **Escalatieregels** (`/settings/escalations`), **EDI** (`/edi`) en
**Integraties** (`/integrations`).

## Portaalgebruikers uitnodigen

- Zelfbediening (aanbevolen): een klantbeheerder met het add-on-recht *Gebruikersbeheer*
  nodigt in het portaal onder **Gebruikers** zelf collega's uit. De genodigde ontvangt een
  activatiemail (link 72 uur geldig) en kiest een eigen wachtwoord; per gebruiker zet de
  klantbeheerder de extra rechten **Documenten**, **Facturen** en **Gebruikersbeheer**
  aan of uit. Deactiveren, heractiveren en uitnodiging opnieuw versturen kan daar ook.
- Mededelingen voor alle portaalklanten beheert u via **Parameters → Beheer →
  Klantportaal mededelingen** (`/settings/portal-announcements`); gerichte
  (meertalige) portaalberichten via **Portaalberichten** (`/settings/portal-messages`).

## Excel-importprofielen

**Dossiers → Excel-import** (`/order-imports`): elke omgeving krijgt automatisch het
importprofiel **"Generiek v1"**; extra profielen definiëren per profiel de
kolomtoewijzing (JSON) van het Excel-bestand naar de ordervelden. Importeurs draaien
eerst een proefrun; identieke bestanden worden geweigerd en dubbele klantreferenties
overgeslagen.

## Overige instellingen

- **Parameters → Instellingen** (`/settings`): bedrijfsgegevens en algemene
  instellingen, waaronder nu ook de **herleveringsmodus** (*Handmatig* / *Voorstellen* /
  *Automatisch* — wat er gebeurt na een mislukte stop) en de **ETA-drempel**
  (minuten verschuiving waarboven de klant automatisch gemaild wordt).
- **Personeel-configuratie**: Verlof (types & saldi) (`/settings/leave`),
  HR-herinneringen (`/settings/hr-reminders`), Bedrijfsmiddelen-sjablonen
  (`/settings/issued-item-templates`), Taaksjablonen (`/settings/task-templates`).
- **Stamgegevens**: eenheden, services & toeslagen en de registry-gedreven lijsten
  (organisatie, referentie, categorieën) onder **Parameters → Stamgegevens**.

## Regionale instellingen, systeeminformatie & back-ups (2026-08)

- **Datumnotatie**: Instellingen → Regionale instellingen — gesloten lijst met live
  voorbeeld; geldt meteen voor de hele applicatie (weergave, nooit opslag).
- **Systeeminformatie** (Parameters → Beheer, `system_info.view`): omgeving, versie,
  build-commit, laatste deployment, API-/databankstatus, laatst toegepaste migratie.
- **Back-ups** (`backups.view` voor het overzicht; create/download/delete/restore zijn
  bewuste per-beheerder-rechten): nieuwe back-up, downloaden, verwijderen (nieuwste is
  beschermd) en **terugzetten** met getypte bestandsnaam-bevestiging en automatische
  veiligheidsback-up vooraf. Retentie: automatisch/pre-restore 30 dagen; handmatig nooit
  automatisch verwijderd. Herstart na een restore de API-service gecontroleerd.
