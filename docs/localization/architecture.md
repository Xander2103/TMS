# Localization-architectuur

## Het canonieke principe (niet onderhandelbaar)

```
stable domain value  →  localization layer  →  NL / FR / EN display
```

en nooit omgekeerd: businesslogica, permissies, API-contracten en opgeslagen data
kennen uitsluitend stabiele codes; vertaling is presentatie. Regionale weergave
(datumpatroon, decimaalteken, tijdzone) is een APARTE as die bij de bestaande
tenant-instellingen blijft — i18n bouwt geen tweede datum-/nummerarchitectuur (§3).

## Frontend

- **Runtime**: eigen dependency-vrije laag in `src/i18n/` (bewust geen i18next — het
  project voert een minimaal runtime-dependencybeleid; het bestaande portaalsysteem is
  uitgebreid, niet vervangen). `translate(locale, key, params)`: dot-path-lookup,
  `{param}`-interpolatie, pluralisatie via `key_one`/`key_other` + `count`, fallback
  gekozen taal → nl → key-echo met dev-warning.
- **Resources**: `src/locales/{nl,fr,en}/<namespace>.json`, auto-ontdekt via
  `import.meta.glob` (nieuwe namespace = drie bestanden, geen registratie). NL = bron.
  Alles statisch gebundeld; geen lazy loading v1 (bundelmeting gedocumenteerd in README;
  herzie boven ~100 KB/taal).
- **Provider**: één `LocaleProvider` op de app-root (`RootProviders`); de context-default
  rendert nl zodat componenten/tests zonder provider exact de pre-i18n-UI geven.
  `activeLocale.ts` spiegelt de taal naar niet-React-helpers en `<html lang>`.
- **Registries** (navConfig, commands, shortcuts, lookup-nav): slaan KEYS op; de
  renderende component vertaalt; menufilter en command-palette matchen op de vertaalde
  tekst (§47) plus meertalige keywords. Routes blijven taal-onafhankelijk (§48).
- **Regionaal**: `utils/dates.ts` (tenant-datumpatroon; alleen weekdag-/maandnamen en de
  duureenheid u/h volgen de taal) en `utils/numbers.ts` (tenant-decimaalteken;
  valutalayout per taal). Portal behoudt zijn locale-gedreven `i18n/formatters.ts`
  (klantperspectief — bewuste splitsing).

## Taalresolutie (§7)

| Doel | Bron & volgorde |
|---|---|
| Interne + portaal-UI | `User.PreferredLanguageCode` (write: `PUT /api/me/language`, self-scoped/geaudit; read: `/api/auth/me`) → tenant `DefaultLanguage` (display-endpoint) → `ts.locale`-cache/browser → nl |
| Kiosk | `KioskDevice.DefaultLanguage` (ping) → handmatige NL\|FR\|EN-keuze → persoonlijke `Employee.PreferredLanguageCode` ná identificatie (§18, privacy) → reset naar device-default |
| E-mail/SMS | Messaging-keten (ongewijzigd): `MessagingProfile.PreferredLanguage` → `OverrideLanguage` → klant-default / `Employee.PreferredLanguageCode` → nl |
| Facturen (PDF) | `Invoice.LanguageCode`, bevroren bij aanmaak (`InvoicePdfStrings`) |
| Handmatige exports | Taal van de aanvrager (`AttendanceExportStrings`-patroon) |

Veldverantwoordelijkheden: `User.PreferredLanguageCode` = UI-taal (canoniek);
`Employee.PreferredLanguageCode` = HR-/communicatie-/kiosktaal; klantvelden
(`DefaultLanguageCode`, `InvoiceLanguageCode`, contact-/regeltalen) = klantcommunicatie
en zijn nooit de taal van de interne gebruiker (§10). `Accept-Language` wordt bewust
niet gebruikt (§64). Catalogus: `Common/SupportedLanguages` (nl/fr/en) — de enige
allowlist.

## Fouten (§24–§25)

`Common/ErrorCodes.cs` levert stabiele codes die als EXTRA veld meereizen (anonieme
bodies: `code`-sibling; ProblemDetails: `Extensions["code"]`) naast de bestaande
Nederlandse message — bestaande contracten ongebroken. Frontend leest `ApiError.code`,
vertaalt via `errors.*` en brancht uitsluitend op code/outcome (de vroegere twee
fouttekst-sniffs in de ritexecutie zijn vervangen). De 526 `DomainValidationException`-
sites dragen generiek `common.validation_failed`; specifiekere codes migreren
progressief per de developer-guide. Veldvalidatieteksten in de `errors`-dictionary
blijven server-Nederlands — gedocumenteerde fase-2 (codes per veld), zie README.

## Notificaties (§28 — besluit)

**Optie A**: in-app-notificaties blijven gerenderde tekst bij creatie (vandaag NL).
Reden: een historiek-item mag nooit van tekst veranderen doordat vertaalbestanden later
wijzigen, en er is geen params-kolom. Toekomstpad: params+key-kolommen toevoegen en bij
creatie in de ontvangertaal renderen; de e-mailketen is vandaag al meertalig.

## Wat bewust NIET vertaald wordt

User-created data (namen, memo's, omschrijvingen — §32); technische waarden (versies,
commit-hashes, bestandsnamen, permissiecodes — §91/§92); logging (Engels/technisch —
§59); tenant-configureerbare lookups (databankdata met eigen beheer — §31/§85);
machine-to-machine-exportheaders (pricing-round-trip — §66); seeded referentiedata
(rolnamen/lookups: databankrijen, fase-2-beslissing).
