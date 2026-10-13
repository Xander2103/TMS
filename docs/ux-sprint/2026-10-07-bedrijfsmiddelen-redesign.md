# Bedrijfsmiddelen — redesign van het beheer (2026-10-07)

Scope: Personeel → Bedrijfsmiddelen (overzicht, aanmaak-/bewerkdialoog, categorieën). Alleen
frontend; het domeinmodel, de API en de permissies zijn ongewijzigd. Niet gecommit, niet gedeployed.

## 1. Waarom

Het formulier toonde ~20 velden tegelijk, sprak over "sjablonen", vroeg komma-gescheiden
functiecodes, en combineerde twee technische vinkjes (Voorraadbeheer / Varianten gebruiken) met
een dubbel voorraadveld zodra varianten aan stonden. Een HR-medewerker moest het interne datamodel
kennen om een T-shirt aan te maken.

## 2. Mentaal model in de UI

| Begrip | Betekenis | Backend |
|---|---|---|
| Categorie | Waar hoort het bij? | `IssuedItemCategory` (lookup) |
| Bedrijfsmiddel | Wat is het? | `IssuedItemTemplate` (naam blijft intern) |
| Beheerwijze | Hoe volgen we het op? | `StockTrackingEnabled` + `VariantsEnabled` |
| Variant | Welke maat/uitvoering? | `IssuedItemVariant` |

Beheerwijze is puur presentatie (`features/issued-items/managementMode.ts`):

- **Individueel item** = geen voorraadbeheer (laptop, telefoon, tankkaart, badge)
- **Voorraadartikel** = voorraadbeheer zonder varianten (helm, handschoenen)
- **Voorraad met varianten** = voorraadbeheer + varianten (kleding, schoenen)

Elke bestaande combinatie van de twee booleans valt op precies één beheerwijze; bestaande rijen
openen dus ongewijzigd.

## 3. Het nieuwe formulier (`TemplateFormModal`)

1. **Basisgegevens** — naam, categorie (dropdown met "+ Nieuwe categorie maken": alleen een naam,
   code wordt gegenereerd), omschrijving.
2. **Beheerwijze** — drie kaarten (één radiogroep). De keuze stuurt wat volgt:
   - Voorraadartikel: beginvoorraad (bewerken: huidige voorraad + verplichte correctiereden),
     minimumvoorraad, opslaglocatie. Wordt voorraadbeheer opnieuw aangezet op een item dat het
     vroeger al had, dan wordt de reden aangeboden maar niet afgedwongen (de backend weet of er
     historiek is).
   - Voorraad met varianten: inline variantentabel (maat/uitvoering, voorraad, minimum) met
     "+ Variant toevoegen" en snel-toevoegen "S, M, L, XL". Géén voorraad- of minimumveld op
     niveau van het bedrijfsmiddel. Eén gedeelde opslaglocatie.
   - Bewerken van een item dat al varianten heeft: kaarten vergrendeld (backendregel), varianten
     worden live bewerkt (`TemplateVariantsEditor`).
3. **Uitgifte** — uitgiftedatum registreren, moet worden teruggebracht, serienummer verplicht.
   Bij varianten verhuist het serienummervinkje naar "Meer instellingen" (met een verwijzing als
   het aan staat).
4. **Voor wie is dit bedoeld?** — Iedereen (standaard) of een doorzoekbare keuze van functies als
   chips. Intern blijft het de CSV van functiecodes (`applicableJobFunctionCodes`); onbekende
   codes blijven zichtbaar als chip en gaan nooit verloren.
5. **Meer instellingen** (ingeklapt) — eenheid, standaardaantal, negatieve voorraad, standaard
   minimum voor varianten (= de bestaande templatedrempel, die de backend al als fallback per
   variant gebruikt), volgorde, interne notities, en bij bewerken "Actief".

Slimme standaardwaarden: bij het kiezen van een categorie (aanmaken) stelt het formulier de
beheerwijze en uitgifte-instellingen voor die de bestaande bedrijfsmiddelen in die categorie
gebruiken (`suggestDefaultsFromSiblings`). Geen categorienamen in code; een expliciete keuze van de
gebruiker wordt nooit overschreven.

## 4. Overzicht en navigatie

- Tabs: **Bedrijfsmiddelen** (standaard) · Categorieën. URL-slugs (`/issued-items/templates`,
  `/issued-items/categories`, `/issued-items/templates/:id`) blijven voor deep links, meldingen en
  de bestaande redirects.
- Kolommen: Naam, Categorie, Beheerwijze (+ aantal varianten), Beschikbaar, Uitgifte (badges
  Serienummer / Terugbrengen), Status. Filter op beheerwijze (incl. lage voorraad).
- Na het aanmaken blijft de gebruiker op de lijst (varianten zijn al ingevuld). Alleen wanneer een
  variant na het aanmaken toch faalt, gaat de gebruiker met de mislukte labels naar het tabblad
  Varianten van het item.
- Categoriebeheer: `simpleForm` in `lookupRegistry` → naam, omschrijving, actief vooraan; code en
  volgorde onder "Meer instellingen"; geen codekolom in de lijst. Andere lookups ongewijzigd.
- Terminologie: "Sjabloon" is overal in nl/fr/en vervangen door Bedrijfsmiddel / Matériel /
  Company asset. Sleutels zijn behouden, alleen waarden gewijzigd.

## 5. Backend / API

Geen wijziging. Inline varianten gebruiken de bestaande volgorde (POST template → POST variant per
rij, elk met een auditeerbare beginvoorraadbeweging). Een backendwijziging zou enkel atomiciteit
toevoegen; de UI vangt het niet-atomische geval op zoals hierboven beschreven.

Voorstel voor een latere, configureerbare uitbreiding (vergt backend + migratie, hier bewust niet
gedaan omdat de .NET SDK op deze machine ontbreekt): `DefaultTrackingMode` + standaard
uitgiftevlaggen op `IssuedItemCategory`, bewerkbaar onder "Meer instellingen" van de categorie.
Een opslaglocatie per variant zou een kolom op `IssuedItemVariant` vragen.

## 6. Compatibiliteit

- Geen schema- of datamigratie. Rijen zonder `categoryId` tonen hun snapshotcategorie en blijven
  opslaanbaar. Legacy eenheden tonen als "Overige". Bestaande functiecode-CSV's worden teruggelezen
  als chips en genormaliseerd (`CHAUF, PLAN` → `CHAUF,PLAN`) weggeschreven.
- `minimumStock` wordt ongewijzigd meegestuurd; de kaart "Voorraadregels" op de detailpagina blijft
  de plek voor de volledige drempelset.

## 7. Gewijzigde bestanden

- Nieuw: `features/issued-items/managementMode.ts`, `components/ManagementModeCards.tsx`,
  `components/VariantRowsEditor.tsx`, `components/EligibilitySelect.tsx`.
- Herschreven: `features/issued-items/TemplateFormModal.tsx`,
  `components/IssuedItemTemplatesPanel.tsx`.
- Aangepast: `pages/IssuedItemsAdminPage.tsx`, `pages/IssuedItemTemplateDetailPage.tsx`,
  `TemplateVariantsEditor.tsx`, `issued-items.css`.
- Designsysteem (additief): `components/ui/Modal.tsx` (+css, `size="wide"`),
  `master-data/components/LookupSelect.tsx` (`createFields`, `createTitle`),
  `LookupFormDialog.tsx` + `LookupManager.tsx` (+css) en `lookupRegistry.ts` (`simpleForm`).
- Vertalingen: `locales/{nl,en,fr}/issuedItems.json`, `masterData.json`.
- Tests: `templateFormModal.test.tsx` (herschreven), `issuedItemTemplatesPanel.test.tsx`,
  `issuedItemsTab.test.tsx`, `issuedItemTemplateDetailPage.test.tsx`; nieuw
  `managementMode.test.ts`, `issuedItemsAdminPage.test.tsx`, `master-data/__tests__/lookupSimpleForm.test.tsx`.

## 8. Niet gedaan / aandachtspunten

- Geen browservalidatie in deze ronde (geen draaiende API op deze machine); donkere modus en
  responsiviteit steunen op de bestaande tokens (`--bg`, `--border`, `--accent*`, `--warning*`)
  en op breakpoints bij 720px.
- De gebruikershandleiding (`docs/delivery/gebruikershandleiding-thalie.*`) spreekt nog over
  "sjablonen" bij bedrijfsmiddelen.
