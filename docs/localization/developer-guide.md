# Localization developer guide

## De twee niet-onderhandelbare regels (§82)

1. **No new user-facing hardcoded strings.** Elke tekst die een gebruiker kan zien
   (labels, knoppen, toasts, aria-labels, placeholders, empty states, documenttitels)
   gaat via `t('namespace.key')`.
2. **Never branch business logic on translated labels.** Vergelijk/switch/filter altijd
   op stabiele codes/enums (`status === 'Working'`), nooit op displaytekst
   (`label === 'Aan het werk'`). Dit geldt óók server-side (§61: nooit rechten of
   gedrag aan een taal of vertaald label koppelen).

## Nieuwe tekst toevoegen (frontend)

```tsx
import { useLocale } from '../../i18n/localeContext'

const { t } = useLocale()
<Button>{t('attendance.actions.clockIn')}</Button>
t('attendance.card.clockedInAt', { time: formatTime(iso) })   // interpolatie {param}
t('employees.count', { count })                                // pluralisatie: keys count_one/count_other
```

- Keys: `namespace.groep.naam` in camelCase, semantisch en stabiel — nooit de tekst zelf.
- Resources: `src/locales/{nl,fr,en}/<namespace>.json`. **Nieuwe namespace = drie
  JSON-bestanden neerzetten — klaar** (auto-discovery via glob in `translations.ts`).
- NL is bron; FR/EN verplicht identieke keysets én identieke `{param}`-sets
  (`translationCompleteness.test.ts` + `missingKeys.test.ts` bewaken dit; een letterlijke
  `t('typfout.key')` faalt de suite).
- Buiten React (registries zoals navConfig/commands/shortcuts): sla de KEY op en laat de
  renderende component vertalen.
- Datums/getallen/valuta: `utils/dates.ts` / `utils/numbers.ts` (tenant-instellingen) —
  nooit `toLocaleDateString` of handmatige € -opmaak in componenten.

## Nieuwe status/enum

Backend serialiseert de stabiele (Engelse) enumwaarde; frontend houdt per feature een
key-map en vertaalt bij render:

```ts
export const X_STATUS_LABELS: Record<XStatus, string> = {
  Active: 'domein.status.Active', ...
}
<Badge>{t(X_STATUS_LABELS[row.status])}</Badge>
```

Tone-maps (BadgeTone) blijven op de code gekeyd. Databankwaarden veranderen nooit voor
vertaling (§86).

## Nieuwe backend-fout

1. Voeg een constante toe in `Common/ErrorCodes.cs` (`domein.snake_case`; alleen
   toevoegen, nooit hernoemen).
2. Geef de code mee naast de bestaande Nederlandse message: anonieme bodies
   `new { message, code = ErrorCodes.X }`, ProblemDetails via `Extensions["code"]`.
3. Frontend: vertaal via `errors.<code>` (namespace errors) met de servermessage als
   fallback; branch uitsluitend op `code`/outcome, nooit op de tekst.

## Nieuwe e-mail/SMS

Gebruik de bestaande Messaging-keten: `MessageKinds`-template in
`BuiltInMessageTemplates` **in de drie talen** (Email + evt. Sms), tokens via
`{{token}}`. Taalresolutie is al centraal: `MessagingProfile.PreferredLanguage` →
`OverrideLanguage` → eigenaarstaal (klant-default of `Employee.PreferredLanguageCode`) →
nl. Nooit drie codepaden (`SendDutchEmail()`…): één kind, drie templates (§68).

## Nieuwe export/PDF

Handmatige exports: koptekstcatalogus per het `AttendanceExportStrings`/
`InvoicePdfStrings`-patroon, taal = aanvrager (User.PreferredLanguageCode). Machine-
to-machine-formaten (bv. pricing-round-trip-Excel) behouden hun bestaande headers als
contract — documenteer dat expliciet bij de service (§66).

## Nieuwe module

1. Namespace-JSON ×3 aanmaken; keys volgens [style-guide](style-guide.md) en
   [glossary](glossary.md).
2. Navigatie-item: key in `navigation.menu.*` + entry in navConfig (label = key).
3. Command-palette-entry: key in `commands.json` ×3 + meertalige `keywords`.
4. Pagina's gebruiken `PageHeader` (regelt de vertaalde documenttitel).
5. Fouten: codes per hierboven.

## Taalresolutie (referentie)

UI: `User.PreferredLanguageCode` (PUT `/api/me/language`) → tenant `DefaultLanguage` →
browser → nl; cache `ts.locale` voorkomt flash. Kiosk: device-default → persoonlijke
`Employee.PreferredLanguageCode` ná identificatie → reset naar device-default.
Communicatie (e-mail/SMS): zie Messaging-keten hierboven. `Accept-Language` wordt bewust
NIET gebruikt: persisted voorkeuren winnen altijd (§64).
