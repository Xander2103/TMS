# Weergave: licht / donker / systeem per gebruiker (2026-10-07)

## Wat

Elke gebruiker kiest zelf **Systeem**, **Licht** of **Donker**. De keuze hoort bij de gebruiker
(`users.ThemePreference`), nooit bij tenant, bedrijf, rol of werkplek, en reist mee naar elk
toestel via `/api/auth/me`.

Kiezers in de UI:

- sidebar-footer (interne app): compacte iconknoppen naast de taalkiezer;
- `/portal/profile` (medewerkersportaal) en `/klantportaal/voorkeuren`: volledige radiogroep
  "Weergave" (`AppearanceSettings`).

## Architectuur

| Laag | Waar | Rol |
| --- | --- | --- |
| Tokens | `src/styles/global.css` | `:root` = licht, `html[data-theme='dark']` = donker (navy/blauwgrijs). Legacy-aliassen (`--bg`, `--text`, `--accent`, …) wijzen naar de semantische `--color-*`-tokens. |
| Bootstrap | `index.html` | Inline script zet `data-theme` vóór de eerste paint uit de `ts.theme`-cache (geen flash). |
| Provider | `src/theme/ThemeProvider.tsx` (gemount in `AppRoutes` boven `RequireAuth`) | Resolutie per sessie: bewaarde gebruikersvoorkeur → `system`; anoniem: cache → `system`. Volgt `prefers-color-scheme` live bij Systeem. |
| Persistentie | `PUT /api/me/theme` (`MyThemeController`) | Self-scoped, geaudit (`ThemeChanged`), in de Phase10-allowlist. `Common/ThemePreferences.cs` normaliseert. |

Regels voor nieuwe CSS:

- geen hardgecodeerde presentatiekleuren; gebruik `--color-surface`, `--color-surface-raised`
  (modals, dropdowns, toasts), `--color-input-bg`, `--color-border`, `--color-text`,
  `--color-text-muted`, `--color-hover`;
- statuskleuren: `--color-{success,warning,danger,info}` voor tekst op een vlak, `-strong` voor
  dikke lijnen/fills, `-bg` + `-text` voor getinte panelen, `-solid` voor knoppen/badges met
  witte tekst;
- `--color-primary` is tekst/outline-paars, `--color-primary-solid` de knopvulling
  (`--color-on-primary` als tekst erop);
- geen `@media (prefers-color-scheme)` in feature-CSS: de gebruikerskeuze moet altijd winnen.

Bewust ongewijzigd: de pastelkleurige statuschips van de personeelsplanning (zakelijke
statuskleuren met donkere tekst), de witte handtekeningcanvas van de POD en de zwarte
cameraweergave van het scannen.
