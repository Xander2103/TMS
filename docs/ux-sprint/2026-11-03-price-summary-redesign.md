# Verkoop & prijs — herontwerp onder de dossiernavigatie (2026-11-03)

Scope: uitsluitend het blok ONDER de dossiernavigatie op de tab Verkoop & prijs. Het prijsmodel, de
rechten en de API zijn ongewijzigd. Dit vervangt de eerste versie van vandaag (drie tegels boven de
lijst met chips per open onderdeel).

## Opbouw

**Links** (`ActivityPricePicker`, `DossierPriceSummary`)

- "Prijsbare onderdelen (n)": één kaart per onderdeel met icoon van het activiteitstype, nummer, naam,
  soort (Opdracht / Activiteit), bedrag en statusbadge. Geprijsd groen, Niet geprijsd oranje.
- Daaronder de dossiersamenvatting: "Dossiertotaal" of "Voorlopig totaal" zolang een onderdeel open
  staat, "x van y onderdelen afgehandeld" met een segment per onderdeel, prijswaarschuwingen die bij
  geen open onderdeel horen, en legacy-opdrachten zonder activiteit.

**Rechts, voor het geselecteerde onderdeel**

- `PriceUnitSummary`: naam, soort en status; totaalprijs, prijsstatus, aandacht en acties.
- De aandachtstegel telt hetzelfde als links (`priceAttention`). Is er een ander open onderdeel, dan
  selecteert een klik dat onderdeel. Niet-opgeslagen wijzigingen blokkeren dat, zoals in de lijst.
- Opdracht: de verkooplijnentabel met Bewerken en Verwijderen, daarna de uitklapbare secties
  Berekeningsdetails en Prijsdekking goederen (standaard open), de kaart Vaste prijs en een rij naar
  de volledige opdrachtprijs.
- Losse activiteit: dezelfde samenvatting; de verkooplijnen blijven inline bewerkbaar en de kaart
  Vaste prijs staat eronder.

## Regels die blijven

- Een ontbrekende prijs leest "Nog geen prijs", nooit € 0,00. Een onbevestigde € 0 telt niet als
  afgehandeld.
- Een nog niet geprijsd onderdeel stelt eerst de vraag "Prijs bepalen via"; "+ Verkooplijn toevoegen"
  verschijnt pas wanneer verkooplijnen gekozen zijn.
- De schakelaar "Vaste prijs gebruiken" staat er alleen zolang verkooplijnen de prijs dragen. Is de
  vaste prijs de huidige prijs, dan toont de kaart meteen het formulier.

## Bewust anders dan de mockup

- "Volledige opdrachtprijs openen" klapt niet uit maar opent de opdrachtpagina, zoals voorheen.
- "Prijs bevestigen" staat alleen bij opdrachten: een losse activiteit kent die stap niet.
- Bewerken van de verkooplijnen van een losse activiteit blijft inline in de rijen.
- De dossiersamenvatting links staat niet in de mockup. Ze blijft, omdat het dossiertotaal en de
  voortgang anders nergens meer op dit scherm staan.

## Techniek

- De order-variant rendert niet meer `OrderPricingPanel` (de opdrachtpagina gebruikt die nog wel),
  maar werkt op dezelfde `useOrderPricingEditor` en `OrderPricingDialogs`. `calculationLabel` en de
  regels voor de prijsacties staan daardoor op twee plaatsen.
- Vertaalsleutels: nieuw `dossierPricing.panel.*` (nl/en/fr). Vervallen in `summary`: `todo`,
  `progress`, `toHandle`, `needsReview`, `toPrice`, `partial`.
- Tokens uit `styles/global.css`, dus licht en donker thema zonder extra regels.
