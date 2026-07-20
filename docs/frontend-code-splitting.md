# Frontend code splitting

Datum: 2026-07-20.

## Analyse

De productiebundel was één chunk van **936 kB** (238 kB gzip). Oorzaak: geen zware
third-party libraries (dependencies zijn enkel react, react-dom en react-router-dom;
scanning gebruikt native browser-API's en grafieken zijn handgemaakt), maar
`src/routes/AppRoutes.tsx` importeerde alle ±80 pagina's statisch, waardoor Rollup niets
kon splitsen. `manualChunks` was daarom niet het juiste middel.

## Oplossing: route-based lazy loading

- Elke pagina in `AppRoutes.tsx` laadt via `React.lazy` + dynamic import; de helper
  `lazyPage(loader, exportName)` overbrugt de named exports van de paginabestanden.
- Twee `Suspense`-grenzen: in `AppLayout` rond de `<Outlet/>` (de schil blijft zichtbaar
  terwijl een paginachunk laadt) en in `RootProviders` voor de routes buiten de schil
  (wachtwoordflows).
- Statisch blijven: `LoginPage` (eerste scherm, geen laadflits), `AppLayout`,
  `NotFoundPage` en `lookupRegistry` (ook door de sidebar gebruikt).
- Gedeelde featurecode (bv. `TransportOrderForm`, mutation-hooks) wordt door Rollup
  automatisch als gedeelde chunk afgesplitst — geen handmatige chunkconfiguratie.

## Resultaat

| | vóór | na |
|---|---|---|
| Initiële JS | 936 kB (238 kB gzip) | 334 kB (105 kB gzip) |
| Route-chunks | 0 | ±80, elk 1–31 kB, on-demand |
| Vite-waarschuwing >500 kB | ja | nee |

## Richtlijn voor nieuwe pagina's

Registreer nieuwe pagina's altijd via `lazyPage(...)` in `AppRoutes.tsx`. Importeer een
pagina nooit statisch vanuit de schil (layout/sidebar/palette), anders belandt hij weer in
de hoofdchunk.
