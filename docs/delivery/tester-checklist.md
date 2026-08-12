# Testerchecklist — dossiergericht TMS (eindoplevering 2026-08)

Praktische end-to-end checklist voor handmatige acceptatie. Elk scenario vermeldt de
**startconditie**, de **exacte actie** en het **verwachte zichtbare resultaat**.

Voorbereiding voor alle scenario's:

- Backend en frontend draaien lokaal; database met alle migraties toegepast.
- Inloggen kan met de dev-beheerder `admin@dev.local` / `Admin123!` (alle rechten).
- Voor portaalscenario's is een portaalgebruiker nodig die aan een klant gekoppeld is
  (aanmaken via klantdetail → portaalgebruikers, of via **Parameters → Beheer**).
- Waar een klant nodig is: gebruik een testklant met minstens één eigen locatie.

---

## 1. Onvolledig kraandossier

- **Startconditie:** ingelogd als planner/beheerder; minstens één activiteitstype "Kraan"
  (of gelijkaardig) bestaat onder **Parameters → Stamgegevens → Activiteitstypes**.
- **Actie:** open **Dossiers → Dossiers**, klik *Nieuw dossier* (snelle aanmaak), kies de
  klant en voeg een activiteit van het kraantype toe zonder verdere gegevens (geen datum,
  geen opdracht, geen prijs).
- **Verwacht resultaat:** het dossier wordt direct aangemaakt en opent als werkplek. De
  gereedheids-/opvolgindicatoren tonen zichtbaar wat nog ontbreekt (geen gekoppelde
  opdracht/prijs); het dossier is niet factuurgereed en verschijnt niet in de
  facturatiecontrole als "Klaar voor facturatie".

## 2. Direct transport

- **Startconditie:** ingelogd als planner; testklant met locatie bestaat.
- **Actie:** maak vanuit een dossier (of **Dossiers → Opdrachten (klassiek)** → *Nieuwe
  opdracht*) een transportopdracht met één laadstop en één losstop, goederenlijn
  (bv. 10 europallets) en gewenste datum. Sla op en bevestig de opdracht.
- **Verwacht resultaat:** de opdracht krijgt automatisch een opdrachtnummer, status
  wordt *Bevestigd*, de goederenlijn staat in het goederenoverzicht en de opdracht is
  zichtbaar in het dossier als activiteit/gekoppelde opdracht.

## 3. Kraan + plateau (gecombineerd dossier)

- **Startconditie:** activiteitstypes "Kraan" en "Plateau" (of vergelijkbaar) bestaan.
- **Actie:** maak één dossier en voeg twee activiteiten toe: één kraanactiviteit en één
  plateauactiviteit, elk met eigen datum; koppel aan (minstens) één transportopdracht.
- **Verwacht resultaat:** beide activiteiten staan als aparte regels in het dossier met
  eigen type, datum en status; het dossier blijft één geheel (één dossiernummer) en de
  activiteitenlaag toont de chronologie.

## 4. Enkel opslag (storage-only dossier)

- **Startconditie:** magazijn met magazijnlocaties bestaat (**Magazijn → Magazijnen
  (beheer)**); pakketten/barcodes van een klant zijn gekend of worden ter plekke geregistreerd.
- **Actie:** scan goederen binnen via **Magazijn → Laden & scannen** zonder gekoppelde
  rit (losse scan), wijs een magazijnlocatie toe en laat de goederen staan.
- **Verwacht resultaat:** in **Magazijn → Trace & voorraad** verschijnt het pakket op de
  gekozen locatie; er loopt een opslagverblijf ("Opslag per klant (pallet-dagen)" telt
  vanaf de inslag). Er is géén transportopdracht nodig.

## 5. Handmatige verkooplijn

- **Startconditie:** bevestigde opdracht uit scenario 2 met prijssnapshot.
- **Actie:** open de opdracht → sectie prijzen/verkooplijnen → voeg een handmatige lijn
  toe (bv. "Extra wachttijd", aantal 1, eenheidsprijs 50).
- **Verwacht resultaat:** de lijn verschijnt in het overzicht, telt mee in het totaal en
  is gemarkeerd als handmatig; de automatisch berekende lijnen blijven ongewijzigd.

## 6. Ontbrekende prijs

- **Startconditie:** klant zonder geldige tariefafspraak voor een bepaalde dienst.
- **Actie:** maak een opdracht voor die klant/dienst en open de prijssectie.
- **Verwacht resultaat:** het systeem toont zichtbaar dat er geen prijs gevonden werd
  (dekkingsstatus/ontbrekende prijs) in plaats van stil € 0; de opdracht is niet
  factuurgereed zolang de prijs ontbreekt en de facturatiecontrole toont een
  "needs review"-reden.

## 7. Overerving eigen bedrijf (legal entity)

- **Startconditie:** minstens twee eigen bedrijven onder **Parameters → Beheer → Eigen
  bedrijven**; testklant heeft een toegestaan/standaard eigen bedrijf ingesteld op de
  klantfiche.
- **Actie:** maak een nieuwe opdracht voor die klant; controleer daarna de conceptfactuur.
- **Verwacht resultaat:** de opdracht en de factuur nemen automatisch het eigen bedrijf
  van de klant over (nummerreeks van die entiteit). Wisselen naar een niet-toegestane
  entiteit wordt geweigerd; overschrijven kan enkel met het recht
  `dossiers.override_entity` (rol Management/Boekhouding).

## 8. Klanttaal op de factuur

- **Startconditie:** testklant met taal Frans (fr) op de klantfiche.
- **Actie:** maak voor deze klant een factuur aan vanuit een afgeronde opdracht en open
  de factuur-PDF.
- **Verwacht resultaat:** de factuur gebruikt de Franse teksten (labels/omschrijvingen
  volgens klanttaal), niet de UI-taal van de gebruiker.

## 9. Magazijnscan (ontvangst)

- **Startconditie:** opdracht met pakketten (barcodes gegenereerd) bestaat.
- **Actie:** open **Magazijn → Laden & scannen**, scan de barcode van een pakket bij
  ontvangst en wijs een locatie toe.
- **Verwacht resultaat:** het scan-event verschijnt direct in de historiek; het pakket
  staat op de gescande locatie in Trace & voorraad. Twee keer dezelfde scan geeft geen
  duplicaat (idempotent).

## 10. Locatie verplaatsen

- **Startconditie:** pakket op locatie A (scenario 9).
- **Actie:** scan het pakket opnieuw en wijs magazijnlocatie B toe (verplaatsing).
- **Verwacht resultaat:** Trace & voorraad toont het pakket nu op locatie B; de
  historiek bewaart beide bewegingen (append-only), inclusief tijdstip en gebruiker.

## 11. Planning

- **Startconditie:** meerdere bevestigde opdrachten met losadres in dezelfde regio.
- **Actie:** open **Planning → Planbord**, bekijk de ritvoorstellen per leverzone en
  neem een voorstel over (of plan handmatig een rit met voertuig + chauffeur en koppel
  de opdrachten).
- **Verwacht resultaat:** er ontstaat een rit met de gekozen opdrachten in volgorde;
  de opdrachten krijgen status *Gepland* en verschijnen op de ritlijst.

## 12. Chauffeurslevering

- **Startconditie:** geplande rit met chauffeur (scenario 11); ingelogd als die
  chauffeur (driver-app, `/driver`).
- **Actie:** start de rit, werk de stops af en meld de levering aan de losstop als
  geleverd.
- **Verwacht resultaat:** de stop- en opdrachtstatus schuiven mee (In uitvoering →
  Geleverd/Afgerond); de backoffice ziet de voortgang live in **Planning → Live
  opvolging**.

## 13. Mislukte levering

- **Startconditie:** rit in uitvoering met een nog open losstop.
- **Actie:** meld in de driver-app de levering als mislukt/geweigerd met reden
  (bv. "klant afwezig").
- **Verwacht resultaat:** de stop is zichtbaar mislukt met reden; er ontstaat een
  afwijking/incident dat in **Vandaag → Problemen** verschijnt; de opdracht is niet
  afgerond.

## 14. Retourscan

- **Startconditie:** mislukte levering (scenario 13); goederen komen terug naar het
  magazijn.
- **Actie:** scan de teruggekomen pakketten in het magazijn (retourscan) en wijs een
  locatie toe.
- **Verwacht resultaat:** de pakketten staan terug op voorraad met retour-markering in
  de historiek; de custody-keten (uit → terug) is volledig zichtbaar in de trace.

## 15. Herlevering

- **Startconditie:** incident met gekoppelde originele opdracht (scenario 13/14).
- **Actie:** open het incident en klik **Herlevering aanmaken**.
- **Verwacht resultaat:** melding "Herleveringsorder aangemaakt in hetzelfde dossier";
  het incident toont het gekoppelde herleveringsordernummer en in het dossier staat een
  nieuwe opdracht die opnieuw gepland kan worden.

## 16. Doorrekening herlevering (toeslag)

- **Startconditie:** incident met verantwoordelijkheid "klant" en een voorgestelde
  doorrekening (toeslagbedrag) in het doorrekeningspaneel.
- **Actie:** keur de doorrekening goed als Management/Boekhouding (recht
  `problems.approve_charge`).
- **Verwacht resultaat:** de goedgekeurde toeslag verschijnt als factuurlijn bij de
  facturatie van de klant; zonder het recht is de goedkeurknop niet beschikbaar. Op een
  vergrendelde snapshot verschijnt de toeslag in de facturatiecontrole als "openstaande
  goedgekeurde toeslag".

## 17. POD (leveringsbewijs)

- **Startconditie:** chauffeur levert een stop (scenario 12).
- **Actie:** rond de levering af met naam van de ontvanger en handtekening/foto in de
  driver-app.
- **Verwacht resultaat:** het POD is definitief en onveranderlijk (correctie = nieuwe
  versie met reden); de backoffice ziet het POD bij de opdracht en in het klantportaal
  verschijnt de POD-samenvatting (geleverd op, ontvangen door, uitkomst) op de
  opdrachtdetailpagina.

## 18. Factuurgereedheid

- **Startconditie:** afgeronde opdracht met volledige prijs (dekking OK), én een tweede
  opdracht met ontbrekende prijs of niet-uitgevoerde rit.
- **Actie:** open **Klanten → Facturatie → Facturatiecontrole**.
- **Verwacht resultaat:** de eerste opdracht staat onder "Klaar voor facturatie"; de
  tweede staat bij "needs review" met de concrete reden(en) (bv. prijs ontbreekt, rit
  niet uitgevoerd).

## 19. Gegroepeerde factuur

- **Startconditie:** klant met groeperingsvoorkeur *Wekelijks* (of *Maandelijks*) op de
  klantfiche; meerdere factuurgereedgemaakte opdrachten in dezelfde periode.
- **Actie:** open de facturatiecontrole en bekijk de voorstellen voor die klant; maak de
  factuur aan volgens het voorstel.
- **Verwacht resultaat:** de opdrachten van dezelfde periode zitten in één
  factuurvoorstel (één factuur met meerdere opdrachten), conform de klantvoorkeur.

## 20. Eén dossier per factuur

- **Startconditie:** klant met groeperingsvoorkeur *Per dossier*; twee dossiers met elk
  een factuurgereedgemaakte opdracht.
- **Actie:** bekijk de voorstellen in de facturatiecontrole en factureer.
- **Verwacht resultaat:** per dossier ontstaat een afzonderlijk factuurvoorstel/factuur;
  opdrachten uit verschillende dossiers worden nooit samengevoegd.

## 21. CMR / leveringsbon (per opdracht en gebundeld per rit)

- **Startconditie:** geplande rit met meerdere opdrachten (scenario 11).
- **Actie:** open eerst een opdrachtdetail en download de leveringsbon en de CMR;
  open daarna het ritdetail en download dezelfde documentsoorten voor de hele rit.
- **Verwacht resultaat:** per opdracht komt één PDF per documentsoort; op ritniveau
  bevat de PDF de documenten van álle opdrachten gebundeld in routevolgorde
  (één bestand, meerdere pagina's).

## 22. Portaaltracking

- **Startconditie:** portaalgebruiker gekoppeld aan de testklant; opdracht van die
  klant is gepland/onderweg met een ETA.
- **Actie:** log in als portaalgebruiker (`/klantportaal`), open de opdracht.
- **Verwacht resultaat:** de klant ziet status, stops, tijdlijn en de verwachte
  levertijd (ETA); geen interne prijzen of planningsdetails. Na levering verschijnt de
  POD-samenvatting. Onder **Voorkeuren** kan de klant e-mail/sms, taal en
  meldingssoorten instellen; opslaan toont een bevestiging.

---

Elke afwijking noteren met scenario­nummer, schermafdruk en de zichtbare foutmelding.
