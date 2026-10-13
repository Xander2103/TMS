# Testerchecklist — dossiergericht TMS (eindoplevering 2026-08, incl. afrondingsgolf)

Praktische end-to-end checklist voor handmatige acceptatie. Elk scenario vermeldt de
**startconditie**, de **exacte actie** en het **verwachte zichtbare resultaat**.
Scenario's 1–22 dekken de basisoplevering; 23–38 de reeds bestaande functies die
eerder buiten de checklist vielen; 39–52 de afrondingsgolf (P0–P13).

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
  Open daarna de plateauactiviteit (bewerken) en kies bij **Begeleidt activiteit** de
  kraanactiviteit.
- **Verwacht resultaat:** beide activiteiten staan als aparte regels in het dossier met
  eigen type, datum en status; het dossier blijft één geheel (één dossiernummer) en de
  activiteitenlaag toont de chronologie. De plateaukaart toont welke activiteit ze
  begeleidt; het type van elke activiteit blijft onveranderd.

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
- **Actie:** meld in de driver-app de levering als mislukt met reden (bv. "klant
  gesloten").
- **Verwacht resultaat:** de stop is zichtbaar mislukt met reden; er wordt AUTOMATISCH
  één incident "Mislukte levering …" aangemaakt (gekoppeld aan opdracht, rit, klant en
  dossier) dat in **Vandaag → Problemen** verschijnt — ook wanneer de melding door een
  offline-replay dubbel binnenkomt blijft het één incident. Een geweigerde levering op
  PAKKETNIVEAU (scan) maakt daarnaast een afwijking aan. De opdracht is niet afgerond.

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
  versie met reden); de backoffice bekijkt het POD via het RITDETAIL ("POD bekijken"
  per stop) en in het klantportaal verschijnt de POD-samenvatting (geleverd op,
  ontvangen door, uitkomst) op de opdrachtdetailpagina.

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

## Deel 2 — bestaande functies, nieuw in acceptatie (23–38)

## 23. Commerciële zone-uitzondering (BE-postcode → Luxemburgtarief)

- **Startconditie:** onder **Parameters → Prijzen** bestaat een zone "LUX" met gebieden
  (LU, 0000–9999) én (BE, 6700–6700); een tariefregel is aan die zone gebonden.
- **Actie:** maak een opdracht met losadres in 6700 Aarlen (België) en bereken de prijs.
- **Verwacht resultaat:** de prijsuitleg toont "(zone LUX)" en het Luxemburgtarief;
  het fysieke land van het adres blijft BE.

## 24. Eigen activiteitstype zonder programmeerwerk

- **Actie:** maak onder **Parameters → Stamgegevens → Activiteitstypes** het type
  "Koeltransport" (met stops, sneltegel aan). Maak er een dossier mee; probeer het
  type daarna te verwijderen terwijl het in gebruik is.
- **Verwacht resultaat:** het type verschijnt direct als sneltegel bij Nieuw dossier
  en gedraagt zich volgens zijn vlaggen; verwijderen wordt geweigerd zolang een
  dossieractiviteit het gebruikt.

## 25. "Had vandaag buiten gemoeten" (magazijn)

- **Startconditie:** pakket ontvangen en op een locatie gescand; zijn opdracht staat op
  een rit met ritdatum vandaag; het pakket wordt NIET geladen.
- **Actie:** open **Magazijn → Trace & voorraad** en kies het magazijn.
- **Verwacht resultaat:** rode melding "⚠ Had vandaag buiten gemoeten" met het pakket;
  verzet de rit naar morgen → het pakket verhuist naar "Wacht op morgen".

## 26. Partiële uitslag stopt de opslagklok per pallet

- **Startconditie:** 5 pallets van één opdracht in het magazijn gescand (scenario 4).
- **Actie:** laad na enkele dagen 2 van de 5 pallets uit (laadscan); open daarna
  "Opslag per klant (pallet-dagen)".
- **Verwacht resultaat:** 2 verblijven zijn gesloten (tellen niet verder), 3 lopen
  door; "nog aanwezig" toont 3.

## 27. Automatische magazijndienst (contract)

- **Startconditie:** dienst "Picking" (per eenheid, €1,25/colli) met **Automatisch
  toepassen** aan.
- **Actie:** maak een opdracht met 3 colli en bereken de prijs.
- **Verwacht resultaat:** automatische lijn "Picking (3 colli) €3,75" met bron
  "Automatisch (contract)".

## 28. Staffels en klantafwijking

- **Startconditie:** gewichtsstaffelregel (bv. 0–500 kg €80, 500–1000 kg €120) en één
  klantafwijking op de tweede staffelrij (€100 voor testklant).
- **Actie:** bereken een opdracht van 700 kg voor de testklant en voor een andere klant.
- **Verwacht resultaat:** testklant €100 (lijn toont "— klantafwijking"), andere
  klant €120.

## 29. Tijd- en weekendtoeslagen

- **Startconditie:** toeslag "Levering vóór 10u" en weekendtoeslag geconfigureerd.
- **Actie:** maak een opdracht met tijdseis "vóór 09:00" op de losstop; plan een tweede
  opdracht op een zaterdag.
- **Verwacht resultaat:** de vóór-10u-toeslag past automatisch toe op de eerste
  (09:00 ≤ 10:00); de weekendtoeslag op de tweede.

## 30. Doorrekening geweigerd bij eigen fout (negatief)

- **Startconditie:** incident met verantwoordelijkheid **Eigen fout**.
- **Actie:** probeer een doorrekening voor te stellen.
- **Verwacht resultaat:** het voorstelformulier verschijnt niet ("interne kosten
  blijven intern"); ook rechtstreeks opslaan wordt door de server geweigerd.

## 31. Factuurvoorstel ververst na correctie

- **Startconditie:** opdracht in "Nakijken vóór facturatie" wegens ontbrekende prijs.
- **Actie:** open de opdracht via de werkruimte, herstel de prijs, keer terug naar
  **Facturatiecontrole** en ververs.
- **Verwacht resultaat:** de opdracht is uit de nakijklijst verdwenen en staat in een
  factuurvoorstel — zonder verdere handelingen.

## 32. Groepering op referentie + handmatige factuurlijnen

- **Startconditie:** klant met groeperingsvoorkeur *ByReference*; twee gereedgemaakte
  opdrachten met referentie "PO-1" en één met "PO-2".
- **Actie:** bekijk de voorstellen; maak de PO-1-factuur; voeg op het factuurconcept
  een handmatige lijn toe en pas een bestaande lijn aan.
- **Verwacht resultaat:** PO-1 en PO-2 zijn gescheiden voorstellen; het concept
  aanvaardt handmatige lijnen en bewerkingen zonder de opdracht te wijzigen; na
  verzenden is de factuur vergrendeld.

## 33. EDI-instroom eindigt in een dossier

- **Actie:** dien via **Parameters → Koppelingen → EDI** (tab Testen) een geldige
  testorder in.
- **Verwacht resultaat:** er ontstaat een opdracht mét omhullend dossier (dossierchip
  op het orderdetail); een tweede identieke indiening wordt gededupliceerd.

## 34. Portaalorder eindigt in een dossier

- **Actie:** dien als portaalgebruiker een nieuwe opdracht in.
- **Verwacht resultaat:** intern verschijnt de opdracht als *Ingediend* binnen een
  eigen dossier; de planner accepteert via de normale flow (klant ontvangt de
  acceptatiemail).

## 35. Communicatietaal van de klant

- **Startconditie:** testklant met taal **fr**; dev-mailsink actief (App_Data/message-sink).
- **Actie:** accepteer een portaalorder van die klant en bekijk de gegenereerde mail.
- **Verwacht resultaat:** de orderacceptatiemail is in het Frans ("Votre ordre … est
  accepté"). Factuur-PDF's volgen scenario 8.

## 36. Meldingsregel uitschakelen → onderdrukt met reden

- **Actie:** zet in **Meldingen en e-mails → Gebeurtenissen** een klantgerichte
  gebeurtenis uit; veroorzaak de gebeurtenis; controleer het tabblad Verzonden/Mislukt.
- **Verwacht resultaat:** geen mail; waar van toepassing een onderdrukte rij met
  duidelijke reden — nooit een stille verdwijning.

## 37. Planningsconflict + vrijgave met reden

- **Startconditie:** ADR-opdracht en een voertuig zonder ADR-geschiktheid (of te klein
  laadvermogen).
- **Actie:** wijs de opdracht aan een rit met dat voertuig toe; probeer te vertrekken;
  geef vervolgens vrij met het vrijgaverecht en een reden.
- **Verwacht resultaat:** blokkerend conflict met Nederlandse uitleg en voorgestelde
  actie; vrijgave vereist recht + verplichte reden en wordt geauditeerd.

## 38. Locatieprojectie na uitladen (P0-fix)

- **Startconditie:** pakket ontvangen op locatie A en daarna geladen op een rit.
- **Actie:** open Trace & voorraad vóór enige retourscan; weiger daarna de levering en
  boek retour in.
- **Verwacht resultaat:** na het laden staat het pakket NIET meer op locatie A (geen
  magazijnlocatie tot een echte scan); pas de retourscan zet het opnieuw op een locatie.

---

## Deel 3 — afrondingsgolf P0–P13 (39–52)

## 39. Klantdocumentstrategie

- **Actie:** zet op de klantfiche **Transportdocumenten** op "Klant levert het document
  aan"; open daarna een opdracht van die klant.
- **Verwacht resultaat:** het orderdetail toont "Geen eigen document nodig: klant
  levert het transportdocument aan"; de rit- en dagbatches slaan de opdracht over;
  handmatig downloaden blijft mogelijk (bewuste keuze).

## 40. Documentkeuze per opdracht + documentregels

- **Startconditie:** onder **Parameters → Beheer → Documentregels** de standaardregels
  (of eigen regels).
- **Actie:** maak een binnenlandse opdracht (→ leveringsbon voorgesteld), een
  ADR-opdracht en een grensoverschrijdende opdracht (→ CMR voorgesteld); overschrijf
  op één opdracht de keuze via "Documentkeuze voor deze opdracht".
- **Verwacht resultaat:** de beslissing + Nederlandse reden staan op elk orderdetail;
  de orderkeuze wint van klantinstelling en regels; alles geauditeerd.

## 41. Dagbatch documenten per klant

- **Startconditie:** klant met meerdere leveringen op dezelfde datum, waarvan één met
  klantdocument en één onbeslist (PerOrder).
- **Actie:** open op de klantfiche **Documenten per dag**, kies de datum, bekijk de
  voorvertoning en download de leveringsbonnen.
- **Verwacht resultaat:** de telling toont eigen bonnen / CMR's / klantdocumenten /
  nog te beslissen; de PDF bevat alléén de eigen leveringsbonnen; onbesliste orders
  worden nooit stil meegeprint.

## 42. Herleveringsmodus Voorstellen

- **Startconditie:** **Instellingen → Herlevering bij mislukte stop** = "Voorstellen
  aan dispatch".
- **Actie:** laat een chauffeur een stop laten mislukken; open het incident.
- **Verwacht resultaat:** het incident toont "⚠ Herlevering aanbevolen…"; één klik op
  "Herlevering aanmaken" maakt de order in hetzelfde dossier met datum = eerstvolgende
  WERKDAG (weekend/feestdag overgeslagen).

## 43. Herleveringsmodus Automatisch

- **Startconditie:** modus "Automatisch aanmaken".
- **Actie:** laat een stop mislukken (eventueel vrijdag, met een feestdag op maandag).
- **Verwacht resultaat:** de herleveringsorder bestaat direct (Draft, zelfde dossier,
  datum dinsdag in het feestdagvoorbeeld); een herhaalde melding maakt géén tweede
  incident of order.

## 44. Doorrekenbeleid Automatisch en Nooit

- **Startconditie:** **Parameters → Beheer → Doorrekenbeleid**: algemeen beleid
  "Automatisch, €120"; voor één klant "Nooit doorrekenen".
- **Actie:** zet op twee incidenten (algemene klant / uitzonderingsklant) de
  verantwoordelijkheid op **Klant**.
- **Verwacht resultaat:** bij de algemene klant is de doorrekening direct goedgekeurd
  en staat de €120-lijn op de order (omkeerbaar zolang de prijs niet vergrendeld is;
  audit "ChargeAutoApprovedByPolicy"); bij de uitzonderingsklant gebeurt niets en wordt
  ook handmatig voorstellen geweigerd. Eigen fout blijft altijd onmogelijk.

## 45. Activiteitsgebonden tarieven (kraan vs plateau)

- **Startconditie:** kilometerregels: "Kraantarief €3/km" (activiteitstype Kraanwerk),
  "Plateautarief €2/km" (Plateau), algemeen €1/km.
- **Actie:** bereken een order in een kraandossier-activiteit en een order gekoppeld
  aan een plateauactiviteit (zelfde route, 100 km).
- **Verwacht resultaat:** €300 respectievelijk €200 — het activiteitstarief wint van
  het algemene; orders zonder activiteitsmatch krijgen €100 (legacy onveranderd).

## 46. Moffett- en retourtoeslag

- **Startconditie:** dienst "Moffett-toeslag €45" met voorwaarde Moffett; dienst
  "Retourtoeslag €30" met voorwaarde Retourrit.
- **Actie:** vink op een opdracht "Moffett vereist" aan; maak een tweede opdracht met
  "Retourrit"; bereken beide en een derde zonder vlaggen.
- **Verwacht resultaat:** €45 resp. €30 automatisch toegepast; de derde opdracht
  krijgt geen van beide.

## 47. Diensten uit echte scans (handling/picking)

- **Startconditie:** dienst "Picking €1,25" per eenheid met **Hoeveelheid uit** =
  "Gepickt"; opdracht met 5 pakketten waarvan er 3 klaargezet (Staged) zijn gescand.
- **Actie:** herbereken de prijs (opdracht opslaan).
- **Verwacht resultaat:** lijn "Picking (3 …) €3,75" — de GESCANDE aantallen, niet de
  bestelde; nogmaals herberekenen verandert niets (geen dubbele lijnen); zonder scans
  een informatieve lijn "nog geen scans geregistreerd", nooit €0.

## 48. ETA-drempelmelding + ritstartmail

- **Startconditie:** **Instellingen → Klantmelding bij ETA-verschuiving** = 30 min.
- **Actie:** start een rit (chauffeur vertrekt); geef daarna een ritvertraging van
  45 minuten in.
- **Verwacht resultaat:** bij de start krijgt de klant per order één
  "chauffeur onderweg"-mail (een herstart stuurt géén tweede); de 45-minuten-
  verschuiving levert een ETA-updatemail; kleine verschuivingen (< 30 min) niet.

## 49. Controlewachtrij gevoelige berichten

- **Startconditie:** schade-incident met gekoppelde order (of mislukte levering).
- **Actie:** open **Meldingen en e-mails → Wacht op controle**; geef één bericht vrij
  en wijs een ander af met reden.
- **Verwacht resultaat:** de klantmail stond vastgehouden (niet verzonden); na
  vrijgave vertrekt hij bij de volgende verzendronde; het afgewezen bericht is
  onderdrukt met de reden; beide beslissingen staan in de audit. Interne mails werden
  nooit vastgehouden.

## 50. Voorstellen verklaren randvoorwaarden

- **Startconditie:** onvertoonde ADR-opdracht met Moffett-vlag en een tour die zwaarder
  is dan het grootste voertuig.
- **Actie:** open het Planbord → ritvoorstellen.
- **Verwacht resultaat:** de orderregel toont de voorwaarden (ADR, Moffett, venster,
  openingsuren waar gekend); de tour toont "overschrijdt het grootste voertuig —
  splits deze tour"; niets wordt verborgen.

## 51. Factuur uitstellen en gedeeltelijk factureren

- **Actie:** stel in **Facturatiecontrole** één order uit ("Uitstellen", datum +
  reden); vink in een voorstel één van drie orders uit en klik "Maak factuur".
- **Verwacht resultaat:** de uitgestelde order staat onder "Uitgesteld" (met datum en
  reden, opheffen mogelijk) en verdwijnt uit voorstellen tot de datum verstrijkt; de
  factuur bevat enkel de aangevinkte orders; de uitgevinkte order blijft beschikbaar
  voor het volgende voorstel.

## 52. Excel-orderimport

- **Startconditie:** xlsx volgens profiel "Generiek v1" (kolommen A–O; één rij met
  ontbrekende losplaats, één duplicaatreferentie).
- **Actie:** importeer via **Dossiers → Excel-import** eerst met "Enkel valideren";
  daarna echt; probeer hetzelfde bestand daarna nogmaals echt te importeren.
- **Verwacht resultaat:** de proefronde toont per rij Geldig/Fout (Nederlandse fout,
  geen orders aangemaakt); de echte run maakt per geldige rij een opdracht MET eigen
  dossier, slaat de duplicaatreferentie over ("Bestaat al") en isoleert de foutrij;
  dezelfde file nogmaals echt importeren wordt geweigerd ("al verwerkt").

---

Elke afwijking noteren met scenario­nummer, schermafdruk en de zichtbare foutmelding.
