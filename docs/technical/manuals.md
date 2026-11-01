# Hoe de handleidingen werken

Eén bron: de Markdown-bestanden in `docs/user-manual/` en `docs/admin-manual/` voeden het helpcentrum en de PDF's. `docs/project-history/` voedt alleen de PDF.

```text
docs/*/NNN-naam.md
   |  TransportationService.Web/src/features/help/manualParser.ts
   |-- helpcentrum: Vite laadt de Markdown in de aparte chunk van de helppagina's
   '-- PDF: scripts/generate-manuals.mjs importeert dezelfde parser
```

## Een artikel schrijven

```markdown
---
id: uniek-id
title: Titel
category: Planning
roles: planner, dispatcher
keywords: woorden, waarop, je, wilt, vinden
related: ander-artikel
---
## WAT DOE JE HIER?
...
```

- `category` komt uit `CATEGORY_ORDER` in `manualParser.ts`. Het nummer in de bestandsnaam bepaalt de volgorde.
- `roles`: `all`, `internal`, of een lijst uit `admin, management, planner, dispatcher, hr, fleet, accounting, warehouse, driver, customer`.
- Elk werkinstructie-artikel gebruikt de zes vaste koppen. `manual.test.ts` controleert dat, net als dode links en afbeeldingen zonder alt-tekst.
- Ondersteund: `##`, `###`, lijsten, tabellen, `**vet**`, `` `code` ``, `[tekst](help:id)` en `![alt](shot:id "bijschrift")`.
- Rolbewustzijn is alleen presentatie. De API blijft de beveiliging. De handleidingen bevatten geen geheimen; de PDF's staan onder `/manual-assets/` zonder aanmelding.

## PDF's maken

```bash
npm --prefix scripts/manuals ci
npx --prefix scripts/manuals playwright-core install chromium-headless-shell
node scripts/generate-manuals.mjs
```

Dit werkt alleen vanuit de repository en heeft geen draaiende applicatie nodig. Chromium drukt twee keer af: de eerste keer om de paginanummers voor de inhoudstafel te vinden. De tekst blijft doorzoekbaar.

## Schermafbeeldingen vernieuwen

Dit vraagt een demo-omgeving die de actuele `main` draait, met testgegevens. De database van de eerste opname staat als `.local/demo-stack/tms_docs-seeded.dump`; die map wordt niet gecommit.

1. Start PostgreSQL, de API in productiemodus en `vite preview`.
2. Zet de omgevingsvariabelen: `MANUAL_APP`, `MANUAL_API`, `MANUAL_DOMAIN_PASSWORD`, `MANUAL_ADMIN_PASSWORD`, `MANUAL_IDS`, `MANUAL_PSQL` en eventueel `MANUAL_CHROME`.
3. Vul zo nodig de testgegevens aan met `seed-demo.mjs` en daarna `seed-demo-2.mjs` uit `scripts/manuals/`.
4. Voer uit: `node scripts/manuals/capture.mjs`, of `--only=id,id` voor enkele opnames.
5. Maak daarna de PDF's opnieuw.

`shots.mjs` beschrijft elke opname. De positie van elke pijl komt uit de echte pagina: staat een element niet meer op het scherm, dan mislukt die opname met een duidelijke melding. Verouderde woorden, zoals "Dossier bevestigen", laten de opname ook mislukken. `manifest.json` houdt per opname bij welke pagina, schermgrootte en labels gebruikt zijn.

De applicatie wordt nooit aangepast voor een opname: de pijlen staan op een aparte pagina boven een kopie van de afbeelding.

## Publiceren

De plug-in `manualAssets` in `vite.config.ts` kopieert de afbeeldingen en PDF's bij de build naar `dist/manual-assets/`. De map heet bewust niet `help`: nginx zou de route `/help` dan als map behandelen.
