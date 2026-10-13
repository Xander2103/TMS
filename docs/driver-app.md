# Chauffeursapp (`/driver`)

Mobile-first schil BINNEN de bestaande SPA (bewust de minst ingrijpende architectuur):
eigen `DriverLayout` (statusbalk + tabbalk), terwijl rituitvoering de bestaande
`/my-trips`-pagina's hergebruikt — één workflow, geen gedupliceerde uitvoeringslogica of
apart chauffeursbackend.

## Navigatie

Vandaag (`/driver`) · Ritten (`/my-trips`) · Incident (`/driver/incidents`) · Documenten
(`/driver/documents`) · Berichten (`/inbox`, bestaande interne inbox). Profiel loopt via
het bestaande portaal.

## Self-scoped backend

Alles resolveert via het chauffeursprofiel van de aangemelde gebruiker
(User.EmployeeId → Driver.EmployeeId); zonder profiel: 404.

- `GET /api/my/dashboard` — huidige/volgende rit, volgende stop + eerlijke ETA (bron
  gelabeld), open stops, onopgeloste afwijkingen, actieve incidenten (`driver_workflow.view`).
- `GET /api/my/documents` (+ `{id}/download`) — UITSLUITEND papieren van de voertuigen/
  opleggers op de eigen actieve ritten; alles daarbuiten is 404, nooit een 403-lek. Geen
  HR- of financiële documenten.
- `POST /api/my/incidents` — meldingen op het GEDEELDE incidentenregister
  (`driver_workflow.execute`): links gevalideerd tegen eigen ritten, chauffeur gestempeld,
  `ClientRequestId`-idempotent, dispatch genotificeerd (`operations.view`-houders).
- Rituitvoering: bestaande endpoints; de stopstatusmachine verhindert elke sprong.

## Offline-architectuur

- **Scans**: bestaande `scanQueue` (localStorage, idempotent via `ClientEventId`).
- **Acties**: `ActionQueue` (`src/features/driver/actionQueue.ts`) voor stopovergangen,
  stop afronden/overslaan en incidentmeldingen. Per gebruiker genamespaced
  (`ts.actionQueue.v1.<userId>`), geordende replay (een netwerkfout stopt de run zodat
  volgorde-afhankelijke acties nooit door elkaar lopen), serverafwijzingen blijven
  ZICHTBAAR als 'failed' met de reden (nooit stille dataverlies), retry per item.
- De my-trips-API's zijn offline-bewust: bij een netwerkfout wordt de actie met haar
  `clientRequestId` gequeued en krijgt de gebruiker `OfflineQueuedError` ("staat in de
  wachtrij…"); serveroordelen (4xx/5xx) propageren onveranderd.
- `useActionQueueSync` (gemount in beide schillen) bindt de queue aan de gebruiker,
  replayt beide wachtrijen bij herstel van de verbinding en toont het aantal
  ongesynchroniseerde acties in de offline-banner en de chauffeursstatusbalk.
- **Leescache**: alleen het eigen dashboard-snapshot per gebruiker
  (`ts.driverSnapshot.v1.<userId>`), duidelijk gemarkeerd als "offline kopie". Nooit
  tenant-brede data; documenten worden niet gecachet.

## Beveiliging

- Uitloggen wist wachtrij(en) én snapshots (`clearDriverOfflineState`) — niets overleeft
  voor de volgende gebruiker op het toestel.
- Idempotentie is server-side afgedwongen (unieke gefilterde indexen), dus een gestolen
  replay kan hooguit de bestaande staat teruglezen, nooit dubbel muteren.
- Tokenopslag volgt het bestaande authmodel (localStorage; de bestaande aanbeveling om
  refresh-tokens naar een httpOnly-cookie te verhuizen blijft staan, zie authStorage.ts).
