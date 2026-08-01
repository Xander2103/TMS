# Meertalig klantenportaal (NL / FR / EN)

Sprintfase 14. Het klantenportaal (`/klantportaal/**`) en de auth-flowpagina's zijn
drietalig; de interne app blijft Nederlands.

## Architectuur

- **Eigen mini-i18n** (`src/i18n/`): `LocaleProvider` + `useLocale()` met `t(key, params)`,
  `formatDate/formatDateTime/formatCurrency` (locales nl-BE / fr-BE / en-GB). Geen
  i18next — bewust dependency-vrij (het project voert een minimaal runtime-dependencybeleid).
- **Vertaalbestanden per domein**: `src/locales/{nl,fr,en}/{common,navigation,auth,
  dashboard,orders,invoices,documents,notifications,messages,errors}.json`. Een testhelper
  (`translationCompleteness.test.ts`) dwingt af dat fr/en exact dezelfde sleutelsets hebben
  als nl.
- **Fallback**: gekozen taal → NL-waarde → de sleutel zelf (met dev-console-waarschuwing).

## Taalvoorkeur

- `User.PreferredLanguageCode` (nl/fr/en, nullable). Portaalcontext
  (`GET /api/customer-portal/context`) levert ze mee; `PUT /api/customer-portal/profile/language`
  slaat ze op (gevalideerd op nl|fr|en, geaudit als `User.LanguageChanged`).
- Startvolgorde in de frontend: opgeslagen voorkeur → browsertaal (eerste bezoek) → nl.
  De voorkeur overleeft logout/login (server-side opgeslagen; niets in browserstorage).
- Taalwisselaar links onderaan de portalnavigatie (Nederlands / Français / English),
  toetsenbordtoegankelijk; wisselen werkt direct en persisteert.

## Meertalige inhoud (portaalberichten)

- `PortalMessage` draagt vaste kolommen `TitleNl/Fr/En` + `BodyNl/Fr/En` (NL verplicht als
  basistaal). Kolommen-per-taal in plaats van een translations-tabel: exact drie gekende
  talen (sprintdocument A9).
- **Weergaveresolutie server-side**: gebruikersvoorkeur → `Customer.DefaultLanguageCode` →
  nl, met FR/EN → NL-terugval per veld (`PortalMessageService.Localize`). De feed retourneert
  de geresolveerde tekst + de gebruikte taal.
- **E-mail**: bij publicatie met `SendEmail` krijgt elke portalgebruiker de mail in zijn
  eigen taal via de bestaande outbox (`MessageKinds.PortalMessagePublished`;
  idempotentiesleutel `portal_message:{messageId}:{userId}`). Zelfde resolutieketen — een
  Franstalige gebruiker krijgt nooit Nederlandse tekst tenzij de FR-inhoud ontbreekt
  (expliciete NL-terugval).

## Scope-afbakening

Vertaald: portalnavigatie, login/activatie/wachtwoordflows, dashboard, opdrachten (+detail,
nieuwe opdracht), documenten, facturen (+detail), berichten, mededelingen, gebruikersbeheer,
foutmeldingen, lege toestanden, statuslabels (eigen maps in het portaal), datums.
Niet vertaald (bewust): de interne applicatie, interne notificatieteksten, e-mailsjablonen
van bestaande interne events.
