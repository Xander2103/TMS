# Gebruikershandleiding TMS

*Volledige handleiding voor eindgebruikers. Laatst bijgewerkt: 2026-08-12.*

Deze handleiding beschrijft het dagelijkse werk met het systeem, van dossier tot factuur.
Voor een korte samenvatting per rol zijn er aparte snelgidsen in `quick-guides/`
(dispatcher, magazijn, chauffeur, facturatie-backoffice en beheerder).

---

## 1. Inloggen & rollen

- U meldt zich aan met uw e-mailadres en wachtwoord. Bent u uw wachtwoord vergeten, dan
  gebruikt u de link **Wachtwoord vergeten** op het aanmeldscherm.
- Wat u na het aanmelden ziet, hangt af van uw rol:
  - **Kantoormedewerkers** komen in de gewone applicatie met het navigatiemenu links.
  - **Chauffeurs** krijgen de mobiele chauffeursschil (`/driver`) met een eigen tabbalk.
  - **Klantgebruikers** komen automatisch in het klantportaal (`/klantportaal`) en zien de
    interne applicatie nooit.
- Het navigatiemenu is opgebouwd in groepen: **Mijn portaal** (uw eigen dashboard,
  planning, afwezigheden, kwalificaties en profiel — alleen zichtbaar met een
  medewerkerskoppeling), **Vandaag** (Dashboard, Berichten, Meldingen en de subgroep
  Problemen), **Dossiers**, **Planning**, **Magazijn**, **Klanten** (met de subgroep
  Facturatie), **Personeel**, **Vloot**, **Rapportage** en **Parameters**.
- Menu-ingangen verschijnen alleen wanneer u de bijbehorende rechten heeft. Ziet u een
  pagina niet, vraag dan uw beheerder om de juiste rol (zie de beheerdersgids).

---

## 2. Dossiers — de centrale werkplek

Het **dossier** is de centrale werkeenheid: één klantcase die meerdere operationele
activiteiten (distributie, kraanwerk, opslag, …) en meerdere commerciële onderdelen kan
bevatten. U vindt de dossierlijst onder **Dossiers → Dossiers** (`/dossiers`).

### 2.1 Zo maakt u snel een dossier aan

1. Ga naar **Dossiers** en kies **Nieuw dossier** (`/dossiers/new`).
2. Alleen de **klant** is verplicht; de rest vult u later aan op het dossier.
3. Kies eventueel een sjabloontegel (activiteitstype met snelstart): die wordt meteen de
   eerste activiteit van het dossier.
4. De datum wordt vandaag, de titel wordt "{klant} — {datum}" en de facturerende entiteit
   wordt stil overgenomen (klantstandaard, anders de bedrijfsstandaard).

Let op: voor geblokkeerde of inactieve klanten kan geen nieuw dossier worden aangemaakt.

Opdrachten die buiten een dossier om binnenkomen (EDI, klantportaal, oudere koppelingen)
krijgen automatisch een eigen "wikkel-dossier" — er bestaat dus nooit een opdracht zonder
dossier.

### 2.2 De dossierpagina

De detailpagina van een dossier bevat:

- **Kop**: dossiernummer, klant, referentie, datum, facturerende entiteit, statuschips en
  de knop **+ Activiteit**. Via het menu kunt u de kop bewerken, het dossier sluiten of
  heropenen.
- **Aandacht-paneel**: berekende aandachtspunten met ernst (informatief / waarschuwing /
  blokkerend), bijvoorbeeld "geen activiteit", "opdracht ontbreekt", "datum ontbreekt",
  "prijs onvolledig". Elk punt springt met één klik naar de juiste sectie.
- **Activiteitenkaarten**: elke operationele activiteit met één hoofdactie. Voor een
  transportactiviteit kunt u vanuit het dossier direct een conceptopdracht aanmaken.
- **Contextuele secties Route en Goederen**: alleen zichtbaar wanneer een activiteit ze
  ondersteunt. Ze openen zijpanelen (drawers) met de bijbehorende orderformuliersecties;
  wijzigingen slaat u expliciet op.
- **Prijssamenvatting**: het totaal met details één klik dieper.

### 2.3 Samenwerken zonder elkaars werk te overschrijven

Wijzigt een collega hetzelfde dossier of dezelfde opdracht tegelijk, dan verschijnt een
conflictbanner met de knop **[Herladen]**. Herlaad eerst de actuele staat en voer uw
wijziging daarna opnieuw door — het systeem overschrijft nooit stilzwijgend andermans werk.

---

## 3. Transportopdrachten

Een transportactiviteit wordt uitgevoerd door een **transportopdracht**: daar leven de
stops, goederen, prijs, scanning en het afleverbewijs. De klassieke opdrachtenlijst blijft
bestaan onder **Dossiers → Opdrachten (klassiek)** (`/transport-orders`).

### 3.1 Het opdrachtformulier (secties)

Het formulier is opgedeeld in secties met een sectienavigatie:

1. **Algemeen** — klant, datum, referenties.
2. **Route & stops** — laad- en losstops met tijdvensters.
3. **Goederen** — goederenlijnen, hoeveelheden en maten.
4. **Services & toeslagen** — expliciet gekozen diensten.
5. **Documenten** — bijlagen bij de opdracht (uploaden, downloaden, verwijderen).
6. **Prijs** — de prijsopbouw (zie hoofdstuk 4).
7. **Samenvatting** — controle vóór het opslaan.

Optionele secties tonen zich pas wanneer ze relevant zijn (progressieve onthulling).
Kiest u een klant met intake-vereisten (bv. verplichte referentie of "getekende leverbon
(CMR) vereist"), dan toont het formulier die vereisten als hint.

### 3.2 Goederen

- Werk bij voorkeur met **goederenlijnen** met een beheerde eenheid (pallet, colli, …):
  zodra minstens één lijn een eenheidcode draagt, zijn de lijnen de bron van waarheid en
  wordt de orderkop (aantal, gewicht, volume) automatisch afgeleid.
- Naast gewicht en volume kunt u ook **afstand (km)** en **laadmeters** invullen — die
  voeden tarieven per kilometer en per laadmeter.

### 3.3 Stops en tijdvensters

Per stop registreert u het adres, contactgegevens, instructies en tijden:

- **Gevraagd / Gepland / Bevestigd / Uiterlijk** — de vensters die ook de chauffeur ziet.
- **Tijdseis** — de commerciële belofte (bv. "leveren vóór 10:00", "na 18:00" of een
  venster). Tijdseisen kunnen automatisch tijdgebonden toeslagen activeren (zie 4.4).
- **Afspraak verplicht** met eventueel een afspraakreferentie.
- **Inbegrepen laad-/lostijd** kan per stop afwijken van de order- of contractwaarde.

### 3.4 Opdrachten uit het klantportaal beoordelen

Opdrachten die een klant via het portaal indient, komen binnen met status *Ingediend* en
worden eerst door een planner beoordeeld: **accepteren** (wordt bevestigd), **afwijzen**
(reden verplicht) of **info vragen** (de vraag verschijnt als bericht op de orderthread en
de klant krijgt een e-mail).

---

## 4. Prijzen & verkooplijnen

### 4.1 Automatische prijsberekening

Bij het opslaan berekent het systeem de prijs met de tarieven die gelden op de
**orderdatum** (nooit "vandaag"): klantspecifieke tabellen winnen van gedeelde tabellen,
die winnen van bedrijfsbrede tabellen; zones (op basis van land + postcode, zowel voor
herkomst als bestemming) verfijnen de keuze. De uitkomst wordt als bevroren
**verkooplijnen** op de opdracht bewaard, met per lijn de herkomst van de prijs.

Tariefregels kunnen daarnaast aan een **activiteitstype** gebonden zijn: zo bestaan
bijvoorbeeld kraan- en plateautarieven naast elkaar binnen één dossier, en wint de
activiteitsspecifieke regel altijd van de algemene regel. Op de opdracht geeft u ook aan
of een **plateau** of een **Moffett** (meeneemheftruck) vereist is en of het om een
**retourbeweging** gaat — diensten en toeslagen kunnen op die kenmerken (net als op
kraan, ADR of activiteitstype) voorwaardelijk worden gemaakt.

Ontbrekende invoer levert nooit een stille €0: de betrokken regel wordt overgeslagen met
een leesbare toelichting (bv. "overgeslagen (geen afstand gekend)").

### 4.2 Ontbrekende prijs herkennen (dekking)

Per goederenlijn toont het systeem de **prijsdekking**: volledig geprijsd, alleen diensten,
of geen prijs — telkens met de reden ("Geen passend basistarief", "Geen staffel …").
Is niet alles gedekt, dan verschijnt de waarschuwing **"Niet alle goederen zijn geprijsd."**
met de dekkinglijst, en toont de prijs de status **Onvolledig** (rood).

### 4.3 Prijsstatus en bevestigen

De zichtbare prijsstatus is: **Nog te bevestigen** → **Bevestigd** → **Gefactureerd**,
of **Onvolledig** zolang er onbevestigde, niet-gedekte goederen zijn. Op het orderdetail:

- **Prijs bevestigen** — vergrendelt de prijs. Bevestigen terwijl er niet-geprijsde
  goederen zijn, vereist een apart recht én een reden die zichtbaar aan de prijs blijft.
- **Prijs aanpassen** — heropent een bevestigde prijs (reden verplicht); de status gaat
  terug naar *Nog te bevestigen*.
- **Herberekenen** — berekent opnieuw met de actuele tarieven; handmatig aangepaste
  lijnen blijven behouden, zuiver automatische lijnen worden herschreven.

Een bevestigde of gefactureerde prijs weigert prijsrelevante wijzigingen (goederen,
diensten, tijdseisen); notities en planningsvensters blijven gewoon bewerkbaar.

### 4.4 Handmatige lijnen, diensten en voorstellen

- U kunt vrije **handmatige lijnen** toevoegen, en automatische lijnen corrigeren — een
  correctie vereist altijd een reden en bewaart de oorspronkelijke motorwaarde.
- **Diensten & toeslagen** (bv. picking, Maut per km, weekend- of feestdagtoeslag,
  ADR-toeslag) worden automatisch toegepast wanneer ze zo geconfigureerd zijn, of kiest u
  expliciet in de sectie *Services & toeslagen*. Voor diensten *Per dag* en
  *Per pallet/dag* vult u het aantal dagen (en pallets) in.
- Diensten kunnen hun aantal ook uit de **werkelijke magazijnscans** halen (instelling
  *Bron van het aantal* op de dienst): inslag, uitslag en picking tellen dan de effectief
  gescande colli, en *Pallet-dagen* volgt de opslagklok (hoofdstuk 6). Handmatig
  ingevulde aantallen winnen altijd; zijn er nog geen scans, dan verschijnt een
  informatieve lijn — nooit een stille €0. Herberekenen werkt scan-gedreven aantallen
  veilig bij zonder handmatige correcties te verliezen.
- **Voorgestelde toeslagen** (bv. extra laad-/lostijd boven de inbegrepen tijd) tellen
  pas mee nadat u ze bevestigt.
- Voor eenmalig prijswerk kan een opdracht een **eenmalige prijsafspraak** dragen (vast
  bedrag, inbegrepen minuten, uurtarief voor extra tijd); het klantcontract wordt dan
  volledig overgeslagen.

---

## 5. Magazijn

### 5.1 Laden & scannen (ritgebonden)

**Magazijn → Laden & scannen** (`/warehouse`) toont de laadlijsten per rit en laadstop en
een zoekveld voor colli. Met de knop **Scannen** opent u het scanpaneel van een stop:
laden bij een laadstop, lossen bij een losstop, plus retourmodi voor colli in retourfase.
Elke scan is idempotent — dubbel scannen levert nooit een dubbele registratie.

### 5.2 Scannen zonder rit — Trace & voorraad

**Magazijn → Trace & voorraad** (`/warehouse/trace`) is het magazijnstation zonder rit.
Scan of typ een barcode en kies de scansoort:

| Scansoort | Gebruik |
|---|---|
| **Ontvangst** | Aankomst in het magazijn registreren (met magazijn en eventueel locatie). |
| **Verplaatsen** | Collo naar een andere locatie verplaatsen (locatie verplicht). |
| **Klaarzetten** | Collo markeren als klaargezet voor vertrek. |
| **Retour inboeken** | Een geweigerde of mislukte levering terug in depot boeken. |

Onbekende barcodes en onverwachte statussen worden als waarschuwing geregistreerd — nooit
stil weggegooid.

### 5.3 Locaties

Elk magazijn kan opslaglocaties krijgen: **zones** (bv. `A — Bulkzone`) met daaronder
**posities** (bv. `A-01`). Beheer via **Magazijn → Magazijnen (beheer)** → *Locaties
beheren*. Een locatie met posities of met colli erop kan niet worden verwijderd.

### 5.4 Waar is een collo? (trace)

Op Trace & voorraad ziet u na een scan of zoekopdracht: het collo, zijn huidige locatie,
de bijbehorende order en klant, en de laatste tien bewegingen (custody-events).

### 5.5 Voorraadoverzicht

Hetzelfde scherm toont per magazijn de colli per locatie, met twee signalen:
**"had vandaag buiten gemoeten"** (het collo staat er nog terwijl zijn order op een rit
van vandaag zit) en **"wacht op morgen"**.

### 5.6 Dockplanning

**Magazijn → Dockplanning** (`/dock-planning`) plant laad-/loskades met tijdsloten.

---

## 6. Opslag (verblijven en pallet-dagen)

- Elke collo krijgt automatisch **opslagverblijven**: een Ontvangst- of Retour-inboekscan
  op het magazijnstation opent de klok; elk vertrek (laadscan, herleveringslading, retour
  naar afzender, annulering) sluit hem. Er is maximaal één open verblijf per collo;
  historiek wordt nooit herschreven.
- Het paneel **"Opslag per klant (pallet-dagen)"** op Trace & voorraad berekent per klant
  en periode de pallet-dagen (per **begonnen dag**; open verblijven tellen tot het
  periode-einde), uitgesplitst per order en per magazijn.
- Deze uitkomst gebruikt u om de diensten **Per dag** en **Per pallet/dag** op de order
  correct in te vullen; handmatig ingevulde dagen op een order winnen altijd.

---

## 7. Planning

### 7.1 Planbord en ritlijst

- **Planning → Planbord** (`/planning-center`) — het visuele planbord met legende.
- **Planning → Ritlijst** (`/planning`) — de rittenlijst; het ritdetail toont stops,
  toegewezen orders en documentknoppen (zie hoofdstuk 11).

### 7.2 Ritvoorstellen (per leverzone)

Op de planningpagina staat het paneel **Ritvoorstellen**:

1. Kies de **voorsteldatum** (standaard vandaag).
2. Het systeem groepeert alle te plannen orders (bevestigd, nog niet op een actieve rit)
   per **leverzone** — hetzelfde zoneconcept als de prijszones. Per voorstel ziet u de
   orders met totalen (gewicht, laadmeters, pallets) en een leesbare toelichting.
3. Orders met een verstreken datum staan vooraan (achterstand eerst); orders zonder
   passende zone staan transparant in een groep "Ongezoneerd", met reden.
4. Per order verklaart het voorstel de **randvoorwaarden**: ADR, kraan, plateau, Moffett,
   het gevraagde tijdvenster en de openingsuren van de loslocatie. Per voorstel staat
   bovendien een **capaciteitssignaal**: de totalen afgezet tegen het grootste actieve
   voertuig.
5. Klik **Maak rit** om van een voorstel een rit te maken — alle bestaande toewijzings- en
   conflictcontroles blijven gelden. Nieuw en blokkerend: een **ADR-order** kan alleen op
   een rit met een chauffeur die een geldige ADR-kwalificatie heeft.

### 7.3 Live opvolging en ETA

**Planning → Live opvolging** (`/operations`) volgt de uitvoering in realtime. Bij de
**start van een rit** worden de ETA's van alle stops meteen gezet en krijgt elke klant op
de rit één "chauffeur onderweg"-bericht (nooit dubbel per rit en order). Per stop
berekent het systeem een **verwachte aankomsttijd (ETA)**, mede op basis van gemeten
stoptijden per locatie; herberekening gebeurt bij elke statuswijziging én bij aankomst,
afronden of overslaan van een stop. De dispatcher kan ETA's overschrijven. Verschuift een
ETA meer dan de ingestelde drempel (instelbaar in de bedrijfsinstellingen), dan wordt de
klant automatisch per e-mail verwittigd — ook wanneer de stop nog op tijd is.

---

## 8. Uitvoering & chauffeur

### 8.1 De chauffeursapp

Chauffeurs werken in de mobiele schil met tabbalk: **Vandaag** (`/driver`, dashboard met
huidige/volgende rit en volgende stop met ETA), **Ritten** (`/my-trips`), **Incident**
(`/driver/incidents`), **Documenten** (`/driver/documents`, uitsluitend voertuig- en
opleggerdocumenten van de eigen actieve ritten) en **Berichten** (`/inbox`).

### 8.2 Een rit uitvoeren

Op de rituitvoeringspagina werkt de chauffeur de stops in volgorde af. Per stop staat één
grote hoofdactie; de statussen verlopen als: *Gepland → Vertrokken naar stop → Aangekomen
→ Start laden / Start lossen → Laden klaar / Afronden*. Bij elke stop ziet de chauffeur
adres, contact, poort/kade, toegangscode, openingsuren, instructies en de tijdvensters
(Bevestigd / Gepland / Gevraagd / Uiterlijk / Afspraak). Wie later aankomt dan het
uiterste tijdstip, geeft een **reden late aankomst** op.

### 8.3 Leveren en POD (afleverbewijs)

- Bij **Afronden** vult de chauffeur in wie getekend heeft ("Getekend door") en eventuele
  opmerkingen.
- Met **✍ POD opnemen** legt de chauffeur het volledige afleverbewijs vast: ontvanger,
  uitkomst, schade/ontbrekend, **handtekening van de ontvanger**, **foto's van de
  levering** en **foto's van documenten** (getekende CMR, leverbon).

### 8.4 Mislukte levering, overslaan en retour

- **Mislukt** of **Overslaan** vereist altijd een reden (bv. "locatie gesloten",
  "lading geweigerd").
- Geweigerde of niet-geleverde colli komen in de retourfase; de chauffeur scant ze met de
  retour-scanmodus, en het magazijn boekt ze bij aankomst in via **Retour inboeken** op
  Trace & voorraad.
- Met **⚠ Probleem melden** registreert de chauffeur een afwijking op de stop; die
  verschijnt bij de dispatch (zie hoofdstuk 9).

### 8.5 Offline werken

Valt de verbinding weg, dan komen scans en stopacties in een lokale wachtrij ("staat in de
wachtrij…"). Bij herstel van de verbinding worden ze in volgorde verstuurd; de
offline-banner en de statusbalk tonen het aantal nog niet gesynchroniseerde acties. Een
door de server geweigerde actie blijft zichtbaar als mislukt, met de reden. Uitloggen wist
de wachtrij en lokale kopieën — er blijft niets achter voor een volgende gebruiker.

---

## 9. Problemen: incidenten, verantwoordelijkheid, doorrekening, herlevering

### 9.1 Eén overzicht, twee soorten

Onder **Vandaag → Problemen** vindt u **Incidenten** (`/incidents`) en **Afwijkingen**
(`/exceptions`). De incidentenpagina bevat bovendien de verenigde problemenlijst: alle
open incidenten én uitvoeringsafwijkingen samen, elk met een link naar het eigen detail.

### 9.2 Verantwoordelijkheid

Op het incidentdetail legt u de **verantwoordelijke partij** vast: Onbekend, Klant, Eigen
organisatie, Chauffeur of Leverancier, met toelichting. Elke wijziging wordt geauditeerd.

### 9.3 Doorrekening aan de klant (goedkeuringsplichtig)

In het paneel **Verantwoordelijkheid & doorrekening**:

1. **Doorrekening voorstellen** — kan alleen bij verantwoordelijkheid *Klant*; u geeft
   bedrag en omschrijving op.
2. **Goedkeuren / Afkeuren** — een aparte bevoegdheid (standaard management en
   boekhouding).
3. Bij goedkeuring maakt het systeem automatisch een **handmatige verkooplijn** op de
   gekoppelde order (reden "Incident: …"); die telt mee in het totaal en in de
   factuurgereedheid. Is de prijs al vergrendeld of gefactureerd, dan blijft de beslissing
   geregistreerd en voegt de backoffice de lijn handmatig toe bij facturatie — de
   facturatiecontrole toont dit expliciet.

Interne verantwoordelijkheid (Eigen organisatie / Chauffeur / Leverancier) kan nooit
worden doorgerekend; die kosten blijven intern.

Aanvullend kan de beheerder onder **Parameters → Beheer → Doorrekenbeleid** een
**doorrekenbeleid** vastleggen per klant en/of incidenttype: *Nooit*, *Voorstellen* of
*Automatisch* (met standaardbedrag). Het meest specifieke beleid wint en wordt precies
één keer toegepast zodra de verantwoordelijkheid op *Klant* landt. *Automatisch* boekt de
lijn via hetzelfde mechanisme als een handmatige goedkeuring (geauditeerd en omkeerbaar
zolang de prijs niet vergrendeld is); *Nooit* blokkeert ook het handmatig voorstellen van
een doorrekening. Eigen fouten kunnen ook met een beleid nooit worden doorgerekend.

### 9.4 Mislukte levering en herleveringsmodus

Een stop die als **Mislukt** wordt afgesloten, maakt automatisch **één incident** aan
(nooit dubbel per stop), gekoppeld aan de order, de rit, de klant en het dossier. Wat er
daarna gebeurt, bepaalt de bedrijfsinstelling **Herleveringsmodus**:

- **Handmatig** — u beslist zelf over een herlevering (zie 9.5);
- **Voorstellen** — het incident krijgt de vlag **"Herlevering aanbevolen"**;
- **Automatisch** — de herleveringsorder wordt meteen aangemaakt.

### 9.5 Herlevering

Met **Herlevering aanmaken** dupliceert u de gekoppelde order als nieuwe conceptorder in
**hetzelfde dossier** (zelfde klant, goederen en stops, referentie "HERLEVERING {origineel
nummer}"). Er is maximaal één herlevering per incident; de kost van de herlevering volgt
hetzelfde doorrekeningsproces op de nieuwe order. Een herleveringsorder (handmatig of
automatisch) krijgt als datum de **eerstvolgende werkdag** — weekends en de dagen uit de
feestdagkalender worden overgeslagen.

---

## 10. ETA & klantcommunicatie (meldingen)

- **In-app meldingen** ziet u via het belicoon rechtsboven en onder **Vandaag →
  Meldingen** (`/notifications`). Sommige meldingen vragen een expliciete bevestiging.
- **E-mails naar klanten** (o.a. ETA-updates, orderstatus, factuur- en Peppol-meldingen)
  lopen via een uitgaande wachtrij (outbox) met automatische herkansingen; mislukte
  berichten alarmeren de verantwoordelijken en kunnen handmatig opnieuw worden verstuurd.
- **Controle vóór verzending**: gevoelige klantmails (standaard: schademelding, mislukte
  levering en vertraging) worden eerst vastgehouden in een **controlewachtrij**. Een
  medewerker met berichtenbeheer geeft ze **vrij** of **wijst ze af** in het
  controle-tabblad van het meldingenbeheer. Alleen e-mail aan klanten wordt vastgehouden;
  interne meldingen nooit. Per gebeurtenis kan de beheerder de controlestap aan- of
  uitzetten.
- Welke gebeurtenissen een melding of e-mail opleveren, aan wie en in welke taal, beheert
  de beheerder onder **Parameters → Koppelingen & meldingen → Meldingen en e-mails**; per
  klant zijn afwijkingen mogelijk, en portaalklanten beheren hun eigen voorkeuren (zie
  hoofdstuk 13).
- De meldingsvoorkeuren van een klant (portaal of klantafwijkingen) gaan uitsluitend over
  de acht instelbare klantberichten (orderbevestiging, tijdvenster, onderweg, ETA,
  vertraging, geleverd, afleverbewijs, factuur). Systeemberichten — zoals
  portaaluitnodigingen of antwoorden op berichten — worden door die voorkeuren nooit
  onbedoeld uitgeschakeld.

---

## 11. Documenten: leveringsbon & CMR

### 11.1 Wie maakt het vervoersdocument? (documentstrategie)

Per klant legt u op de klantfiche de **documentstrategie** vast:

- **Zelf aanmaken** — wij drukken de leveringsbon of CMR;
- **Klantdocument** — de klant levert zijn eigen documenten mee, wij drukken niets;
- **Per opdracht kiezen** — de keuze wordt per opdracht gemaakt.

Op de opdracht kan altijd worden afgeweken (*Eigen document*, *Klantdocument* of *Geen
document nodig*). Het orderdetail toont steeds de **uiteindelijke beslissing mét de
reden**; de volgorde is: keuze op de opdracht → klantstandaard → documentregels →
ingebouwde standaard.

De **documentregels** beheert de beheerder onder **Parameters → Beheer → Documentregels**:
regels op prioriteit die op grensoverschrijdend vervoer, ADR en/of activiteitstype
matchen en het documenttype bepalen. Zonder passende regel gelden de ingebouwde
standaarden: ADR → CMR, grensoverschrijdend → CMR, anders leveringsbon. Staat een
opdracht op *Per opdracht kiezen* zonder dat de keuze gemaakt is, dan telt dat als
**ontbrekende informatie** — er wordt dan nooit automatisch gedrukt.

### 11.2 Documenten per dag (klantbatch)

Op de klantfiche staat de kaart **"Documenten per dag"**: kies een datum en u ziet per
soort de aantallen (eigen leveringsbonnen, CMR's, klantdocumenten, geen document en nog
te beslissen), met per order de reden. Met één klik downloadt u de **samengevoegde PDF**
per soort — desgewenst beperkt tot geselecteerde orders. Ook de ritbatches (zie 11.3)
slaan orders met een klantdocument of zonder documentplicht automatisch over.

### 11.3 Afdrukken per opdracht en per rit

- **Per opdracht**: op het orderdetail staan de knoppen **Leveringsbon** en **CMR**. Het
  systeem genereert de PDF uit de bevroren ordergegevens: afzender = de facturerende
  entiteit van de order, geadresseerde = klant, route, goederenlijnen, totaalgewicht en
  klantreferentie. De CMR bevat de genummerde vakken met handtekeningvelden.
- **Per rit**: op het ritdetail staan **CMR's (rit)** en **Leveringsbonnen (rit)** — één
  samengevoegde PDF, één pagina per order, in routevolgorde ("print alles voor deze rit").
- Daarnaast kunt u in de sectie **Documenten** van een opdracht eigen bestanden uploaden
  (bv. een getekende leverbon), downloaden en verwijderen.

---

## 12. Facturatie

### 12.1 Factuurgereedheid

Een order is pas factureerbaar wanneer er niets meer aan schort. De redenen die het
systeem herkent: *nog geen prijs*, *niet alle onderdelen geprijsd*, *geen onderdeel
volledig geprijsd*, *prijs verouderd — herbereken* en *afleverbewijs ontbreekt*.

### 12.2 Facturatiecontrole (de werkruimte)

**Klanten → Facturatie → Facturatiecontrole** (`/invoice-control`) is de dagelijkse
werkplek van de backoffice:

- **Factuurvoorstellen** — orders die klaar zijn, gegroepeerd volgens de
  **factuurgroepering** van de klant: *Eén factuur per dossier*, *Wekelijks verzamelen*,
  *Maandelijks verzamelen*, *Per klantreferentie* of *Handmatig*. Binnen een voorstel
  kunt u met **selectievakjes** een deel van de orders kiezen. Met **Maak factuur**
  maakt u de factuur in één klik; u komt direct op het factuurdetail.
- **Nakijken vóór facturatie** — per order de reden(en) waarom die nog niet klaar is.
- **Uitstellen** — een order die (nog) niet gefactureerd mag worden, stelt u uit tot een
  datum, met reden. Uitgestelde orders verdwijnen uit de voorstellen en de nakijklijst en
  staan in een eigen sectie **"Uitgesteld"** tot de datum verstrijkt of u het uitstel
  opheft.
- **Goedgekeurde doorrekeningen — handmatig toe te voegen** — incident-toeslagen die zijn
  goedgekeurd nadat de prijs al vergrendeld of gefactureerd was; deze voegt u handmatig
  als factuurlijn toe.

### 12.3 Facturen en creditnota's

Onder **Klanten → Facturatie → Facturen** (`/invoices`) beheert u facturen. Elke
factuurlijn draagt een verkoopcategorie (grootboekkoppeling) uit de tariefconfiguratie.
Van een verzonden of betaalde factuur maakt u een **creditnota** (maximaal één levende
creditnota per factuur); de creditnota deelt de nummerreeks met een eigen voorvoegsel en
verschijnt met badge in het klantportaal.

### 12.4 Peppol

**Klanten → Facturatie → Peppol** (`/peppol`) bevat de tabbladen **Overzicht** (checklist
per eigen bedrijf + tellers), **Uitgaand** (transmissies, opnieuw proberen, annuleren),
**Inkomend** (ontvangen documenten beoordelen), **Configuratie** (per eigen bedrijf, met
"Verbinding testen") en **Validatieproblemen**. Op het factuurdetail zelf staat het
Peppol-paneel met **Valideren**, **Voorbeeld**, **XML downloaden**, **Versturen via
Peppol** en de transmissietijdlijn. De status verloopt via de wachtrij automatisch tot
*Afgeleverd*, *Geweigerd* of *Mislukt*; mislukte verzendingen kunt u opnieuw aanbieden.

---

## 13. Klantportaal

Klantgebruikers werken uitsluitend in het portaal (`/klantportaal`), beschikbaar in het
Nederlands, Frans en Engels:

- **Dashboard** — komende leveringen, recente facturen en tellers.
- **Opdrachten** — eigen opdrachten met statustijdlijn; nieuwe opdracht indienen via
  `/klantportaal/new` (die komt bij de planners binnen ter beoordeling). Het orderdetail
  toont de **verwachte levering (ETA)** en na levering een **POD-samenvatting**
  (levermoment, uitkomst, ontvanger).
- **Documenten** — orderdocumenten, getekende afleverbewijzen en factuurbijlagen.
- **Facturen** — facturen met PDF, creditnota-badge en Peppol-status.
- **Berichten** — rechtstreeks berichtenverkeer met de binnendienst, per opdracht of
  algemeen, met ongelezen-teller.
- **Gebruikers** — klantbeheerders nodigen zelf collega's uit (activatielink per e-mail,
  72 uur geldig) en beheren per gebruiker de extra rechten Documenten / Facturen /
  Gebruikersbeheer.
- **Voorkeuren** (`/klantportaal/voorkeuren`) — de klant beheert zijn eigen
  meldingsvoorkeuren: kanalen (e-mail/sms), taal en de soorten meldingen. Geen vinkjes bij
  soorten betekent: alles ontvangen.

---

## 14. Excel-orderimport

Onder **Dossiers → Excel-import** (`/order-imports`) importeert u opdrachten in bulk uit
een Excel-bestand:

1. Kies een **importprofiel** (kolomtoewijzing). Elke omgeving heeft het standaardprofiel
   **"Generiek v1"**; de beheerder kan extra profielen aanmaken.
2. Upload het bestand en start eerst een **proefrun (dry-run)**: elke rij wordt
   gevalideerd en u ziet vooraf wat er aangemaakt, overgeslagen of geweigerd zou worden.
3. Bij de echte import wordt elke rij afzonderlijk verwerkt: een foute rij krijgt een
   leesbare foutmelding en blokkeert de rest van het bestand nooit. Rijen met een al
   bestaande klantreferentie worden overgeslagen (geen dubbels), en een identiek bestand
   dat eerder al werd geïmporteerd, wordt in zijn geheel geweigerd.
4. Geïmporteerde opdrachten doorlopen precies hetzelfde aanmaakproces als handmatige
   opdrachten — elke import-order krijgt dus gegarandeerd zijn (wikkel)dossier.

---

## 15. Rapportage per activiteitstype (KPI)

De KPI-pagina bevat de sectie **Activiteiten**: per activiteitstype het aantal
activiteiten, de gekoppelde orders, de omzet en het aantal herleveringen, met een
roll-up per KPI-categorie en de pallet-dagen voor opslag. Kraan- en plateauwerk binnen
één dossier tellen hier apart — precies waarvoor de activiteitsdimensie bedoeld is.
Opvragen kan ook via `GET /api/kpi/activities`.

---

## 16. Regionale instellingen, systeeminformatie, back-ups en de vernieuwde locaties

### 16.1 Regionale instellingen

Onder **Parameters → Instellingen → Regionale instellingen** bepaalt de beheerder hoe
datums overal in de applicatie worden weergegeven. Kies uit DD/MM/YYYY (Belgische
standaard, bv. 12/08/2026), MM/DD/YYYY (08/12/2026) of YYYY-MM-DD (2026-08-12) — elk met
een live voorbeeld. De keuze geldt direct voor élk scherm; opslag in de databank blijft
altijd genormaliseerd. Het klantportaal volgt bewust de taal van de klant, niet deze
instelling.

### 16.2 Systeeminformatie

**Parameters → Beheer → Systeeminformatie** toont voor de beheerder: omgeving, versie,
build (exacte commit), laatste deployment, API- en databankstatus, en de laatst
toegepaste databankmigratie. Zo weet u altijd exact welke versie draait.

### 16.3 Back-ups

Het tabblad **Back-ups** toont alle databaseback-ups (datum, type, grootte, schema,
status, reden) plus het retentiebeleid. Bevoegde beheerders kunnen een nieuwe back-up
maken, downloaden of verwijderen (de nieuwste back-up is beschermd). **Terugzetten** is
een destructieve actie: u ziet exact welke back-up, krijgt een waarschuwing dat de
huidige data vervangen wordt, typt de exacte bestandsnaam ter bevestiging, en het systeem
maakt éérst automatisch een veiligheidsback-up. Alles wordt geauditeerd.

### 16.4 Locaties

Het locatieoverzicht filtert op zoekterm, status, type, klant, land en postcode, sorteert
op elke kolom en kent twee weergaven: **Plat** en **Per klant** (inklapbare groepen per
klant, ongekoppelde locaties onderaan). De detailpagina toont de operationele kern in
kaarten naast elkaar (adres, contact, openingsuren, planning, operationeel, instructies,
interne informatie — die laatste alleen voor interne gebruikers), met headeracties
Bewerken / Dupliceren / Deactiveren. Het formulier werkt in secties (Algemeen t/m
Planning) met statusindicatoren per sectie (✓ vereist in orde, ● gegevens aanwezig,
! fout, ○ leeg) en springt bij een fout rechtstreeks naar het juiste veld. Vanuit een
klantfiche maakt u met één klik een nieuwe locatie voor die klant.

---

## 17. Verder lezen

- Snelgidsen per rol: `docs/delivery/quick-guides/`
  (dispatcher, magazijn, chauffeur, facturatie-backoffice, beheerder).
- De beheerdersgids beschrijft gebruikers, rollen, activiteitstypes, tarieven,
  verkoopcategorieën, eigen bedrijven en meldingsregels.
