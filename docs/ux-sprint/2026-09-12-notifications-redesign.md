# Meldingen — master-detail herontwerp (2026-09-12)

Referentie: `screenshots/Meldingen.png`. Visuele/structurele pass op de Meldingen-pagina; de
bestaande fetch, filters, gelezen/bevestigen/archiveren, voorkeuren en permissies blijven.
Niet gecommit, niet gedeployed.

## Root cause van de gemelde fouten

| Symptoom | Bevinding |
|---|---|
| "Pagina niet gevonden" na klik op een melding | Reproduceerbaar. Producers `TransportOrderService` (order_created) en `CustomerPortalService` (portaal-indiening) schrijven `LinkPath = /orders/{id}`; de SPA kent enkel `/transport-orders/:id`. Fix: producers schrijven nu `/transport-orders/{id}`; de frontend herschrijft bestaande rijen met `/orders/…` via `resolveNotificationLink` (ook in de bel). |
| `Failed to load resource: 500` / fout op `customer/messages` | Lokaal niet reproduceerbaar: `GET /api/customers/{id}/messages?orderId=…` geeft 200 op de opdrachtdetailpagina; de Meldingen-pagina zelf roept dit endpoint niet aan. Zelfde productie-omgeving als de open 500 op `GET /api/dossiers` → wacht op de serverlog. |
| JS-fout rond `startTime` | Niet reproduceerbaar; geen `startTime`-referentie in de frontendcode buiten planning/afwezigheden/tijdregistratie. Geen console-fouten op Meldingen of opdrachtdetail bij lokale validatie. |

## Oud

Eén kolom (`max-width: 720px`): koptekst + drie filters, daaronder alle meldingen als brede
kaarten (titel, badge, bericht, tijd) met losse archiveer-/bevestigknoppen naast elke kaart,
onderaan de voorkeuren. Klikken navigeerde meteen weg (na markeren als gelezen).

## Nieuw

```
NotificationsPage (.ntc-page, 15px paginaschaal)
├─ PageHeader  Meldingen · ondertitel · [Alles gelezen]
├─ Samenvatting (3 klikbare chips) Open · Ongelezen (n nieuw) · Waarschuwingen (n nieuw)
├─ Filterbalk  zoeken · categorie · status (alle/ongelezen/gelezen/te bevestigen)
│              · Archief tonen · Opgeloste verbergen · [Filters wissen]
└─ .ntc-body  grid  1fr | minmax(360px, 42%)
   ├─ Lijstpaneel   "{n} meldingen" + Sorteer op (nieuwste/oudste)
   │                groepen Vandaag (n) / Deze week (n) / Eerder (n), inklapbaar
   │                NotificationRow: dot · icoon · titel + categorie(+Te bevestigen/Opgelost/
   │                gearchiveerd) · één regel bericht · tijd (HH:mm vandaag, anders datum) · chevron
   │                voorkeuren per categorie (bestaand) onderaan
   └─ NotificationDetail (sticky) icoon · categorie · titel · "Vandaag om …" · Ongelezen/Gelezen
                     acties: [Open opdracht/factuur/…] [Markeer als gelezen] [Bevestigen] … [Archiveren]
                     volledige tekst · feiten (type, categorie, ernst, aangemaakt, status,
                     bevestiging, opgelost, verloopt, koppeling) · Vervolgactie (Naar planbord /
                     Bevestigen)
```

- Selectie zit in `?id=` (deeplinkbaar, terugknop werkt); klikken op een rij navigeert niet meer weg.
  De primaire actie "Open …" markeert als gelezen en navigeert (bestaand gedrag van "openen").
- `notificationView.ts` (puur): `resolveNotificationLink` (fragment strippen, legacy `/orders/` →
  `/transport-orders/`, soort koppeling → actielabel), `groupNotifications` (lokale kalenderdag /
  7 dagen / rest), `filterNotifications` (zoeken in titel/bericht/categorie, status, waarschuwingen,
  opgelost, sortering), `computeStats`, `severityTone`.
- Categorie en archief blijven serverfilters (`listNotifications`), de rest is client-side over de
  opgehaalde pagina (take 50, ongewijzigd).
- Gelezen markeren werkt lokaal (patch in state) en ververst de bel-teller via `NotificationsContext`
  (optioneel, null-veilig voor tests).
- Toetsenbord: rijen zijn knoppen (`aria-pressed`), pijl omhoog/omlaag verplaatst focus; groepen
  hebben `aria-expanded`/`aria-controls`; acties die hun eigen knop verwijderen houden focus in
  het detailpaneel.
- Responsief: < 1100px één kolom met het detail bovenaan zodra iets geselecteerd is; < 720px
  chips onder elkaar en tijd onder de titel.
- `.ntf-dot`/`.ntf-severity-*` verhuisd naar `NotificationBell.css` (de bel leunde op de pagina-CSS
  die alleen na een bezoek aan Meldingen geladen was).

## Bestanden

- Nieuw: `features/notifications/notificationView.ts`, `components/NotificationIcon.tsx`,
  `components/NotificationRow.tsx`, `components/NotificationDetail.tsx`,
  `__tests__/notificationView.test.ts`.
- Gewijzigd: `pages/NotificationsPage.tsx`, `pages/notifications.css`, `components/NotificationBell.tsx`
  (+`.css`), `locales/{nl,en,fr}/notificationCenter.json` (`page.*`, `stats`, `groups`, `detail`,
  `severity`, `links`), `__tests__/notificationsPage.test.tsx`.
- Backend (twee regels): `TransportOrderService.cs`, `CustomerPortalService.cs` → `/transport-orders/{id}`.

## Bewust uitgesteld

- "Markeer als ongelezen": geen backend-endpoint; niet toegevoegd.
- Server-side zoeken/paginering boven 50 meldingen: buiten scope (bestaand contract).
- Vervolgactie enkel voor opdrachtmeldingen (planbord) en te bevestigen meldingen; andere
  typen tonen alleen de "Open …"-actie.
- De productie-500 (`/api/dossiers`, mogelijk ook `customer/messages`) blijft wachten op de serverlog.
