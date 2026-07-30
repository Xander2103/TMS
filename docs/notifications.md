# Meldingen & e-mails (Messaging + Notifications)

Twee lagen, twee modules. `Modules/Notifications` levert **in-app meldingen**
(`Notification` per gebruiker, met categorie/ernst uit `NotificationTypeCatalog`);
`Modules/Messaging` levert **uitgaande berichten** via een provider-neutrale
**outbox** (`OutboxMessage`). Producenten queuen alleen; de dispatcher bezit de
aflevering. Er is geen SMTP-integratie: in ontwikkeling schrijft
`DevelopmentSinkProvider` elk "verzonden" bericht naar `App_Data/message-sink`.

## Architectuur

```
Businesscode ──► INotificationEventService.PublishAsync(eventKey, context)
                   │  NotificationEventCatalog (statisch) + NotificationRule (tenantoverride)
                   │  + CustomerNotificationOverride (klant-opt-out)
                   ├─ per e-mailontvanger ──► IMessageOutboxService.QueueAsync ──► OutboxMessage (Pending)
                   └─ per in-app-ontvanger ──► INotificationService ──► Notification (per gebruiker)

OutboxDispatcherHostedService (30s) ──► MessageDispatcher.DispatchPendingAsync
    succes → Sent │ fout → backoff 5/10/20/40 min, na 5 pogingen → Failed
    Failed → in-app alarm naar orders.manage-houders + éénmalige kanaal-fallback (nooit geketend)
```

- **Idempotentie**: `OutboxMessage.IdempotencyKey` is uniek per tenant; het
  event-pad bouwt hem als `{eventKey}:{EntityType}:{EntityId}:{adres}` zodat een
  dubbele publish nooit een dubbele mail wordt. `Suppressed`-rijen bewaren het spoor
  van wat bewust níét verstuurd is (voorkeur/opt-out). Mislukte/onderdrukte rijen
  kunnen handmatig opnieuw (`POST api/messaging/outbox/{id}/retry`, vers pogingbudget).
- **PublishAsync hoort ná de business-save** te lopen, nooit in dezelfde transactie;
  call sites vangen publicatiefouten af zodat de businessoperatie nooit terugrolt.

## Eventcatalogus, regels & ontvangers

- **`NotificationEventCatalog`** (statische code) beschrijft elk event: Nederlandse
  label, groep (Orders/Facturatie/Personeel/Vloot/Portaal), toegestane placeholders,
  standaardkanalen en -ontvangers, ernst. Eventkey == `MessageKinds`-waarde (1:1).
  De drie factuur-Peppol-events (`invoice_peppol_queued` / `_delivered` / `_failed`)
  worden sinds 2026-07-30 effectief gepubliceerd door `PeppolTransmissionService` en
  `PeppolDispatcher`.
- **`NotificationRule`** (per tenant, uniek per eventkey) overschrijft de catalogus:
  aan/uit, kanalen (`InAppEnabled`/`EmailEnabled`; `SmsEnabled` gereserveerd, vandaag
  altijd false), ontvangerslijst (`RecipientsJson`) en `AllowCustomerOverride`. Geen
  rij = catalogusdefaults. **`CustomerNotificationOverride`** kan een opengesteld
  event per klant onderdrukken (alleen versmallen, nooit een tenant-uit weer aan).
- **Ontvangertypes** (`NotificationRecipientType`): `CustomerPrimaryContact`
  (primair actief contact, terugval `Customer.Email`), `CustomerCommunicationRule`
  (via de bestaande communicatieregels, bv. "Invoice"), `InternalPermission`,
  `InternalRole` (TemplateCode), `ExplicitEmail`, `Driver` (= de subject-medewerker
  van het event: chauffeur bij orderevents, de betrokkene zelf bij HR-events).

## Sjablonen, rendering & sanering

- **Resolutieketen** (`MessageOutboxService.ResolveTemplateAsync`): (klant, kind,
  kanaal, taal) → (klant, "nl") → (tenant, taal) → (tenant, "nl") → ingebouwd
  (`BuiltInMessageTemplates`). Een klantrij wordt alleen geraadpleegd bij
  klantgerichte berichten. Taal: voorkeurstaal ontvanger → klanttaal → tenanttaal →
  "nl". Token `companyName` is overal beschikbaar (globaal token).
- **Placeholder-validatie** bij het opslaan van een sjabloon
  (`MessagingController.ValidatePlaceholders`): tokens buiten de allowlist van het
  event (∪ globale tokens) worden geweigerd ("Onbekende placeholder"); legacy
  pre-catalogus-kinds worden niet gevalideerd. `GET api/message-templates/placeholders`
  voedt de editor; `POST api/message-templates/preview` rendert zonder te queuen.
- **HTML-sanering** (`HtmlSanitizer`, op `MessageTemplate.BodyHtml` bij opslaan):
  allowlist `p, br, strong, em, ul, ol, li, a (alleen http/https-href), h1-h3`;
  script/style verdwijnen mét inhoud, elk ander tag verliest zijn tags, elk attribuut
  behalve een geldige `a[href]` wordt gedropt.

## In-app laag

`Notification` (type, categorie, ernst, titel, bericht, `LinkPath`, gelezen/
gearchiveerd) + `NotificationPreference` (opt-out per categorie; `Critical` levert
altijd). `NotificationTypeCatalog` mapt typecode → categorie/ernst. Fan-out via
`NotificationService` (`NotifyPermissionHoldersAsync`/`NotifyRoleAsync`/per gebruiker).

## Beheerscherm `/settings/notifications` (nav "Meldingen en e-mails")

Gate `notification_rules.view`; tabs verbergen zich zonder de vereiste permissie:

- **Gebeurtenissen** — hele catalogus gegroepeerd, kanaal/actief-toggles en
  ontvangers-editor (`notification_rules.manage` om te wijzigen).
- **Sjablonen** (`message_templates.manage`) — tenant-sjablonen + per klant het
  effectief-vs-override-overzicht.
- **Ontvangers** — uitleg van de ontvangertypes (informatief).
- **Klantafwijkingen** — per klant de opengestelde events aan/uit.
- **Verzonden berichten** / **Mislukte berichten** (`messaging.manage`) — de outbox
  gefilterd op status, met detail en retry.

## Permissies (rollen v18)

| Code | Omschrijving | Standaard |
|---|---|---|
| `notification_rules.view` | Meldingsregels en klantafwijkingen bekijken | management, hr, boekhouding |
| `notification_rules.manage` | Regels, ontvangers en klantafwijkingen beheren | management |
| `messaging.manage` | Outbox inzien/retry, messagingprofielen | planner, management |
| `message_templates.manage` | Berichtsjablonen beheren | planner, management |

## Bekende beperkingen

- **SMS bestaat alleen als seam**: `SmsEnabled` is gereserveerd (altijd false), de
  enige geregistreerde provider is de development-sink voor beide kanalen; een echte
  provider implementeert `IEmailProvider`/`ISmsProvider`. `BodyHtml` wordt gesaneerd
  opgeslagen maar nog niet door de uitgaande rendering gebruikt (platte `Body` stuurt
  de outbox). De drie Peppol-events zijn sinds 2026-07-30 live en in de admin-UI
  bewerkbaar (`PeppolPending` staat uit). Het
  preview-endpoint zoekt één actieve sjabloonrij op exacte (kind, kanaal, taal) zonder
  klant-scoping of taal-terugval — het volgt dus niet de volledige resolutieketen van
  echte verzending. De uitnodigingsmail van het klantportaal passeert de
  ontvanger-resolutie niet (direct gequeued); alleen event-uit wordt gerespecteerd.
