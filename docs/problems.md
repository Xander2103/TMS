# Problemen, verantwoordelijkheid & herlevering (Wave 6)

## Twee probleemrecords, één overzicht

- **Incident** — het beheersniveau: dossier-/ordergekoppeld, kosten, opvolging, deadline.
- **Afwijking** (uitvoeringsuitzondering) — het ritniveau: scan-/pakketafwijkingen uit de uitvoering.

De **verenigde problemenlijst** (`GET /api/problems`, blok op de incidentenpagina) toont beide
soorten open problemen samen met een Nederlands typelabel; elke rij opent zijn eigen detail
(`/incidents/{id}` of `/exceptions/{id}` — nooit een ritpagina).

## Verantwoordelijkheid

Elk incident draagt een **verantwoordelijke partij**: Onbekend, Klant, Eigen organisatie,
Chauffeur of Leverancier (+ toelichting). Elke wijziging is geauditeerd.

## Doorrekening (goedkeuringsplichtig)

| Stap | Wie | Wat |
|---|---|---|
| Voorstellen | `incidents.manage` | Alleen bij verantwoordelijkheid **Klant**; bedrag + omschrijving. |
| Goed-/afkeuren | `problems.approve_charge` (rollen v28: management + boekhouding) | Servicezijdig fail-closed afgedwongen. |
| Goedkeuring | automatisch | Maakt een **handmatige verkooplijn** op de gekoppelde order (bestaande lijnmechanieken, reden "Incident: …", telt mee in totaal en factuurgereedheid). Vergrendelde/gefactureerde prijs → beslissing blijft geregistreerd, lijn handmatig bij facturatie (Wave 10 toont dit). |

Interne verantwoordelijkheid (Eigen/Chauffeur/Leverancier) kan nooit worden doorgerekend —
die kosten blijven intern (EstimatedCost/ActualCost).

## Herlevering

`POST /api/incidents/{id}/redelivery` (`orders.create`, `orders.manage` of het gecontroleerde
commando `problems.create_redelivery` — dispatcher/planner, v39) maakt een CONCEPT-order voor wat
**niet** geleverd werd:

- **Hoeveelheid**: opgegeven, anders de hoeveelheid van de bron-afwijking, anders de gemelde
  niet-geleverde hoeveelheden (ontbrekend/onvolledig/gedeeltelijk/geweigerd), anders alles (volledig
  mislukte levering). Gewicht/volume/paletten schalen mee.
- **Stops**: bij een mislukte losstop enkel die losstop (met de laadstop); tijdsvereiste, toegang,
  contact en instructies reizen mee; gevraagde vensters schuiven naar de herleveringsdag (volgende
  werkdag), bevestigde vensters niet.
- **Koppelingen**: `RedeliveryOfOrderId` + `RedeliverySourceIncidentId` op de nieuwe order, het incident
  linkt vooruit; beide orders tonen de relatie en krijgen een historiekregel.
- **Prijs**: de herlevering start **zonder prijs** (nakijken) — nooit een stille kopie van de facturatie —
  of expliciet **gratis** via het bestaande bevestigd-gratis-mechanisme (met reden).

Eén herlevering per incident; originele colli gaan naar *Herlevering gepland* waar de levenscyclus dat toelaat.

## Uitzonderingsbeheer-remediëring (2026-10-18)

### Bewerken zonder gegevensverlies (EX-01)

`PUT /api/incidents/{id}` neemt **secties** (`report`, `links`, `impact`, `responsibility`,
`communication`); enkel meegestuurde secties worden toegepast. Rit, chauffeur en oplegger zijn
*uitvoeringskoppelingen*: invullen kan wanneer ze leeg zijn, wijzigen of wissen nooit. Een lege body
is een no-op. Het kantoorformulier stuurt per kaart enkel die kaart.

### Eén scope voor dossierproblemen (EX-04)

`ProblemScope` beslist welke problemen een dossier raken: een incident via zijn dossier, anders via
zijn opdracht, anders via zijn rit; een afwijking via haar opdracht, anders haar rit. Dossierbevestiging,
de dossierdetail en de incidentlijst-filter gebruiken dezelfde regel. Bij registratie leidt de server
klant, dossier en (bij precies één rit) rit/chauffeur/voertuig af van de opdracht en weigert hij
onmogelijke combinaties (opdracht van een andere klant/dossier, rit zonder die opdracht).

### Open afwijkingen bij bevestigen (EX-05)

| Ernst | Dialoog | Automatisch bevestigen | Handmatig bevestigen |
|---|---|---|---|
| Laag | Ter info | gaat door (afwijking blijft open, geauditeerd) | toegestaan |
| Gemiddeld | Waarschuwing | gaat door (geauditeerd als aandachtspunt) | met bewuste bevestiging |
| Hoog / Kritiek | Blokkerend | **wordt tegengehouden** | met bewuste bevestiging **én reden** |

De regel staat centraal in `ExceptionConfirmationPolicy`. Een probleem gemeld ná de bevestiging
heropent het dossier nooit; het wordt op het dossier geregistreerd (`ProblemAfterConfirmation`,
zichtbaar in de dossierhistoriek), dossierbeheerders krijgen een melding en het incident toont het.

### Wagenpark (EX-03)

Een incident beïnvloedt de inzetbaarheid nooit rechtstreeks. Voor een voertuigprobleem
(panne/ongeval vanaf Hoog, kritieke schade; afwijking *Probleem met het voertuig* vanaf Hoog) vraagt het
scherm om **Schademelding aanmaken** — één gekoppelde schademelding per incident/afwijking met voertuig,
rit en chauffeur; de bestaande schaderegels bepalen dan de inzetbaarheid. De wagenparkbeheerder krijgt
één melding (`damage_reports.edit`) met een link naar het voertuig, en ziet daar de gemelde
voertuigproblemen en de bron van elke schademelding (type, ernst, rit, opdracht, route, chauffeur) —
zonder klant-, kost- of HR-gegevens.

### Klantcommunicatie (EX-02, EX-08)

- Elk berichttype rendert met al zijn velden; een bericht met een onopgelost `{{veld}}` wordt nooit
  verzonden of vrijgegeven (onderdrukt met de ontbrekende namen; ook de dispatcher-run controleert).
- Schade en mislukte levering: de interne kopie bevat de melding van de chauffeur, de klantkopie enkel
  de klantgerichte tekst van kantoor (en blijft ter controle vastgehouden).
- Incident en afwijking hebben een apart **bericht voor de klant**; de omschrijving van de chauffeur
  komt nooit in het klantportaal.
- Het portaal toont het **leveringsresultaat** (Niet geleverd, Gedeeltelijk geleverd, Herlevering gepland,
  Herleverd) in plaats van "Afgerond" wanneer er niet (volledig) geleverd werd.

### Rechten

| Actie | Recht |
|---|---|
| Doorrekening beslissen | `problems.approve_charge` (boekhouding, management) — geen `incidents.manage` nodig |
| Verantwoordelijke kiezen | `GET /api/incidents/assignable-users` (`incidents.manage` of `exceptions.resolve`): id, naam, rol |
| Herlevering | `problems.create_redelivery` (dispatcher, planner) of orderrechten |
| Escaleren afwijking → incident | `incidents.manage` |
| Schademelding vanuit probleem | `incidents.manage` / `exceptions.resolve` |
| Domeinhistoriek | wie het probleem mag zien; financiële velden enkel met `problems.approve_charge`, `invoices.view` of `audit_logs.view` |

Foto's van een afgehandelde of geëscaleerde afwijking zijn bewijsmateriaal en kunnen niet meer
verwijderd worden.
