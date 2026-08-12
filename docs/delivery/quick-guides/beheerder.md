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
  rondgang met preview mogelijk.
- **Parameters → Prijzen → Prijsinstellingen** (`/settings/pricing`): **zones** (land +
  postcodereeksen — hetzelfde zoneconcept dat ook de ritvoorstellen gebruikt), **diensten
  & toeslagen** (incl. automatisch toepassen, ADR-, magazijn-, tijd-, weekend- en
  feestdagvoorwaarden, en het soort *Per km* voor bv. een Maut-toeslag), **eenheden** en
  de **feestdagkalender** die de feestdagtoeslagen stuurt.
- **Parameters → Prijzen → Kostentarieven** (`/cost-rates`) voor de kostzijde van ritten.

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
  (in-app / e-mail) en de ontvangers.
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

## Overige instellingen

- **Parameters → Instellingen** (`/settings`): bedrijfsgegevens en algemene instellingen.
- **Personeel-configuratie**: Verlof (types & saldi) (`/settings/leave`),
  HR-herinneringen (`/settings/hr-reminders`), Bedrijfsmiddelen-sjablonen
  (`/settings/issued-item-templates`), Taaksjablonen (`/settings/task-templates`).
- **Stamgegevens**: eenheden, services & toeslagen en de registry-gedreven lijsten
  (organisatie, referentie, categorieën) onder **Parameters → Stamgegevens**.
