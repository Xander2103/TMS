# Localization testing

## Automatische gates (draaien in de gewone suites)

| Test | Bewijst |
|---|---|
| `src/i18n/__tests__/translationCompleteness.test.ts` | FR/EN hebben per namespace exact de NL-keyset (beide richtingen); geen lege strings. Nieuwe namespaces worden automatisch meegenomen (glob). |
| `src/i18n/__tests__/missingKeys.test.ts` | Elke letterlijke `t('…')` in `src` resolvet in de NL-bundel (typfouten falen de build); alle nav-/command-/shortcut-keys resolven; `{param}`-sets zijn identiek over de talen. |
| `src/i18n/__tests__/translate.test.ts` | Lookup, interpolatie, pluralisatie (`_one`/`_other`), nl-fallback, key-echo, browserdetectie. |
| `TransportationService.Api.Tests/Localization/LanguageFoundationTests.cs` | Taalcatalogus-normalisatie; `PUT /api/me/language` persist+audit, weigert onbekende talen met stabiele code, tenant-veilig; outcome→errorcode-mapping. |
| `KioskSecurityTests.KioskLanguage_…` | Device-default in ping; persoonlijke taal alleen ná geldige identificatie (privacy §18). |
| Attendance-exporttest | FR-koppen bij FR-gebruiker; kolomvolgorde/data identiek (§66). |

## Conventies voor componenttests

- De context-default rendert **nl** zonder provider: bestaande Nederlandse
  `getByText(...)`-asserties blijven geldig — dat is bewust (minimale churn).
- Taalwissel test je door de component in `<LocaleProvider>` te mounten en `setLocale`
  te triggeren, of door `translate('fr', key)` te asserten (zie
  `attendanceI18nSmoke.test.tsx` als model).
- **Businessflow-taalinvariantie (§74)**: de smoke-test voert dezelfde punchflow in
  nl/fr/en uit en assert dat de API-payloads identiek zijn — alleen de weergave
  verschilt. Nieuw taalafhankelijk gedrag = bug.
- Dubbelzinnige datum (§76): `dates.test.ts` bewijst dat 03/04 door de
  TENANT-datumnotatie wordt bepaald, niet door de UI-taal. Getallen (§77):
  `numbers.test.ts` bewijst display-only-formatting op het tenant-decimaalteken.
- Security (§79): Phase-suites + `LanguageFoundationTests` — taal wijzigt nooit
  permissies; cross-tenant preference-writes geweigerd; kiosk-taalwissel geeft geen
  extra capabilities (alleen presentatie, zelfde device-auth).

## Handmatige smoke per release (NL → FR → EN)

Wissel de taal via de sidebar-kiezer en loop na: navigatie, dashboardcard, Mijn uren,
Aanwezigheid, medewerkerfiche-tab, instellingen, kiosk (device-default + persoonlijke
taal + reset), klantportaal (regressie). Refresh en herlogin behouden de taal.
