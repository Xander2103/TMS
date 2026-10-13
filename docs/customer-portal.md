# Klantportaal (2026-07-30)

Zelfbedieningsportaal voor klantgebruikers: eigen opdrachten bekijken en indienen,
documenten, facturen (incl. PDF en Peppol-status), berichten met de binnendienst en
mededelingen. Backend in `Modules/CustomerPortal`, frontend onder
`TransportationService.Web/src/features/customer-portal` (routes `/klantportaal/*`).

## Architectuur & beveiligingsmodel

```
User.CustomerId (enige bron van klantcontext)
        │
        ▼
CustomerPortalController (api/customer-portal/…)
        │  elk endpoint: PortalCustomerResolver (één definitie: eigen user-rij + klant moet ACTIEF
        │  zijn) → geen koppeling of gedeactiveerde klant? PortalOutcomeKind.NoCustomerLink → 403
        ▼
CustomerPortalService / PortalDashboardService / PortalDocumentService /
PortalInvoiceService / CustomerMessageService / PortalAnnouncementService
        │  elke query + referentievalidatie gescoped op die ene klant
        ▼
Bestaande interne services (TransportOrderService, IFileStorageService, …)
```

- **De klantcontext komt uitsluitend van `User.CustomerId`** van de aangemelde
  gebruiker; een door de client meegegeven klant-id wordt nergens geaccepteerd
  (doccomment `CustomerPortalController`). Geen koppeling ⇒ `NoCustomerLink` ⇒ HTTP 403;
  een resource van een andere klant valt buiten de gescopete query ⇒ 404, nooit data.
- **Orderintake hergebruikt de ene interne `TransportOrderService`-use-case** (zelfde
  validatoren, vrachtregels, nummering, audit, statushistoriek); portaalorders komen
  binnen als `Submitted` zodat planners ze eerst beoordelen.
- **Rate limiting**: de anonieme auth-endpoints (`/api/auth/login`, `forgot-password`,
  `reset-password`) dragen de fixed-window-policy `auth` — 10 requests/minuut per
  client-IP, geen queueing, 429 met Nederlands probleemdocument
  (`RateLimitingServiceCollectionExtensions`).

## Gebruikersmodel: rol + add-ons, uitnodigen & activeren

- Basisrolsjabloon **`klantportaal`** (view, submit_orders, manage_locations, messages)
  plus drie optionele add-on-sjablonen die per gebruiker gecomponeerd worden (nooit los
  toegekend): **`klantportaal_documenten`**, **`klantportaal_facturen`**,
  **`klantportaal_gebruikersbeheer`**. Bewust géén interne `orders.view`.
- `CustomerPortalUserService` beheert uitsluitend deze vier sjablonen (resolutie via
  `Role.TemplateCode`); een eventueel foutief toegekende niet-portaalrol wordt met rust
  gelaten maar is nooit toekenbaar via dit pad. Grants (`Documents`/`Invoices`/
  `ManageUsers`) togglen enkel de drie add-ons; de basisrol blijft altijd staan.
- **Uitnodigen** (`POST api/customer-portal/users`, self-service met
  `customer_portal.manage_users`): maakt de gebruiker aan met `CustomerId`, start via
  `UserAccountFlowService.StartActivationAsync` een single-use activatietoken (72 uur,
  SHA-256-gehashed opgeslagen, oude tokens eerst ingetrokken, `MustChangePassword`) en
  queuet de uitnodigingsmail (`MessageKinds.PortalUserInvited`, activatielink
  `{Frontend:BaseUrl}/activeren?token=…`) rechtstreeks in de outbox. Verder:
  deactivate/reactivate, resend-invite, `PUT …/grants`.

## Modules & endpoints (alle onder `api/customer-portal`)

| Gebied | Endpoints | Permissie |
|---|---|---|
| Context/dashboard | `GET context`, `GET dashboard` (komende leveringen, recente facturen, tellers) | `customer_portal.view` |
| Opdrachten | `GET orders`, `GET orders/{id}` (status-tijdlijn + excepties), `POST orders` (→ Submitted) | view / submit_orders |
| Locaties | `GET locations`, `POST locations` | view / manage_locations |
| Documenten | `GET documents`, `GET documents/{source}/{id}/content` | view_documents |
| Facturen | `GET invoices`, `GET invoices/{id}`, `GET …/pdf`, `GET …/attachments/{attachmentId}/content` | view_invoices |
| Berichten | `GET/POST messages`, `POST messages/read`, `GET messages/unread-count` | messages |
| Mededelingen | `GET announcements` (actieve) | view |
| Gebruikers | `api/customer-portal/users` (zie hierboven) | manage_users |

- **Beoordelingsflow** (intern): `POST api/transport-orders/{id}/portal-review`
  (`orders.change_status`/`orders.manage`) op een `Submitted`-order — `Accept` →
  Confirmed, `Reject` → geannuleerd met verplichte reden, `RequestInfo` → blijft
  Submitted en post een staff-bericht op de orderthread (géén aparte info-request-
  entiteit; het generieke reply-event wordt onderdrukt zodat de klant precies één
  `order_info_requested`-mail krijgt). Alles geauditeerd + notificatie-events.
- **Documenten** (`PortalDocumentService`): één lijst over drie brontabellen —
  orderdocumenten, afleverbewijzen (enkel `IsCurrent && CustomerVisible` met
  handtekening) en factuurbijlagen met `IncludeWhenSending` op niet-Draft-facturen.
  Geen nieuwe opslag: alles blijft achter `IFileStorageService` van de eigen module;
  het content-endpoint hercontroleert dezelfde voorwaarden.
- **Facturen** (`PortalInvoiceService`): enkel niet-Draft; detail + PDF; sinds
  2026-07-30 ook `Invoice.Kind` (creditnota-badge) en het Nederlandse label van de
  nieuwste niet-geannuleerde Peppol-transmissie per factuur — interne foutdetails
  worden niet aan klanten getoond.
- **Berichten** (`CustomerMessage`): threads per (CustomerId, TransportOrderId) —
  `TransportOrderId` null = algemene thread. `CustomerMessageRead` is de per-gebruiker
  leesmarkering die de ongelezen-tellers aan beide kanten voedt. Interne kant:
  `api/customers/{customerId}/messages` (+ read/unread-count) met expliciete klant-id.
  Events: klant → staff in-app (`customer_message_received`), staff → klant e-mail
  (`customer_message_reply`).
- **Mededelingen**: admin-CRUD op `api/portal-announcements`
  (`portal_announcements.manage`); portaal ziet enkel actieve. Sinds fix wave B loopt
  `GET announcements` via `PortalAnnouncementService.ListForPortalAsync`, dat eerst de
  `PortalCustomerResolver` raadpleegt — het was het enige portaalendpoint zonder
  klantresolutie en bleef dus antwoorden voor een gedeactiveerde klant. `ListActiveAsync`
  blijft de tenantbrede query voor callers die de klant al hebben opgelost (dashboard).

## Vrije tekst per stop: wat de klant ziet en wat intern blijft

`TransportOrderStop` heeft vier vrijetekstvelden. Ze zijn **niet** uitwisselbaar: twee
zijn van de klant, twee zijn van de binnendienst. De grens ligt in de projectie
(`PortalStopDto`), niet in de frontend.

| Veld | Klantportaal | Waarom |
|---|---|---|
| `Instructions` ("Instructies") | **zichtbaar** | Klantgegeven leverinstructie; de portaalintake schrijft dit veld. |
| `Reference` ("Referentie") | **zichtbaar** | Klanteigen dossier-/container-/boekingsnummer. |
| `AccessInstructions`, `LoadingInstructions`, `UnloadingInstructions` | **nooit** | Interne behandelinstructies; niet geprojecteerd door `PortalStopDto`. |
| `AccessCode`, `Gate`, `Dock`, `RouteDescription`, contacten, openingsuren, geplande/bevestigde vensters | **nooit** | Intern; niet geprojecteerd. |

**Regel voor planners:** `Instructions` en `Reference` zijn *gedeelde* schrijfvelden — de
klant vult ze in bij het indienen, een planner past ze daarna aan, en de klant leest het
resultaat terug op zijn eigen orderpagina. Een interne opmerking ("alleen laden na
bevestiging boekhouding") hoort daarom **niet** in `Instructions` maar in de toegangs-,
laad- of losinstructies. Het interne orderformulier zegt dit ter plaatse:
`transportOrders.route.instructionsHint` (nl/fr/en) staat als hint onder het veld, en
`routeSectionInstructionsHint.test.tsx` bewaakt dat de tekst er blijft staan.

`TransportOrder.Notes` en `CancellationReason` zitten in de andere categorie: die zijn
sinds H-14 volledig uit de portaal-DTO verwijderd (staff-velden zonder klantvariant). De
reden van een afgewezen portaalorder bereikt de klant via de berichtenthread, niet via het
orderveld.

## Permissies (rollen v19)

| Code | Omschrijving | Standaard |
|---|---|---|
| `customer_portal.view` | Eigen opdrachten, dashboard, context, mededelingen | klantportaal |
| `customer_portal.submit_orders` | Opdrachten indienen | klantportaal |
| `customer_portal.manage_locations` | Eigen locaties beheren | klantportaal |
| `customer_portal.messages` | Berichten bekijken en versturen | klantportaal |
| `customer_portal.view_documents` | Documenten bekijken | add-on klantportaal_documenten |
| `customer_portal.view_invoices` | Facturen bekijken | add-on klantportaal_facturen |
| `customer_portal.manage_users` | Klantgebruikers beheren | add-on klantportaal_gebruikersbeheer |
| `customer_messages.view` / `.send` | Intern: klantberichten lezen/beantwoorden | planner, dispatcher, management |
| `portal_announcements.manage` | Mededelingen beheren | management |

## Frontend-shell

`CustomerPortalLayout` is een eigen shell — portaalgebruikers zien nooit de interne
`AppLayout`-navigatie; `portalRouting` stuurt een portaalgebruiker (CustomerId +
`customer_portal.view`) altijd naar `/klantportaal` en bounct hem weg van interne
routes. Navigatie per permissie: Dashboard, Opdrachten (`/klantportaal`), Documenten
(`/documenten`), Facturen (`/facturen`, detail `/facturen/:id`), Berichten
(`/berichten`, met unread-badge), Gebruikers (`/gebruikers`); nieuw indienen via
`/klantportaal/new`.

## Bekende beperkingen

- POD-**foto's** zitten bewust niet in het documentenoverzicht — enkel de handtekening
  (uitgesteld, zie `PortalDocumentService`). Berichten kennen in v1 geen bijlagen
  (`CustomerMessage`-doccomment). De opdrachtenlijst haalt maximaal 200 orders op
  (`PageRequest.Of(1, 200)`) zonder paginering of zoekveld in het portaal. De basisrol
  bevat `manage_locations` standaard — tenants die het striktere portaal willen,
  trekken dat handmatig in (upgrade-notitie rollen v9). De uitnodigingsmail omzeilt de
  ontvanger-resolutie van het notificatiesysteem (het adres is intrinsiek aan de
  uitnodiging); enkel het uitschakelen van het event via de meldingsregels wordt
  gerespecteerd.
