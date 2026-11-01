# TransportationService

TransportationService is een modulair Transportation Management System (TMS) voor transport- en distributiebedrijven: klanten, dossiers en opdrachten, ritplanning, live opvolging, chauffeursapp, klantportaal, incidenten, wagenpark, personeel, magazijn en facturatie.

`main` is de enige canonieke tak.

## Technologie

| Onderdeel | Stack |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL 16 |
| Frontend | React 19, TypeScript, Vite, React Router |
| Tests | xUnit, Vitest, Testing Library |

## Mappen

```text
TransportationService.Api/        de API, per module onder Modules/
TransportationService.Api.Tests/  backendtests
TransportationService.Web/        de webapplicatie
docs/                             alle documentatie, zie docs/README.md
scripts/                          deploy, secret scan en de generatie van de handleidingen
deploy/                           voorbeelden voor nginx en systemd
.local/                           lokaal bewijs en archieven, nooit gecommit
```

## Lokaal starten

Vereist: .NET 10 SDK, Node.js en Docker. De volledige uitleg, met de lokale secrets, staat in `docs/security/dev-setup.md`.

```bash
docker compose up -d
dotnet ef database update --project TransportationService.Api
dotnet run --project TransportationService.Api

cd TransportationService.Web
npm ci
npm run dev
```

## Testen

```bash
dotnet build TransportationService.slnx
dotnet test TransportationService.slnx
dotnet ef migrations has-pending-model-changes --project TransportationService.Api

cd TransportationService.Web
npx tsc -b
npx eslint .
npx vitest run
VITE_API_BASE_URL=https://example.invalid npm run build
```

## Documentatie

Begin bij [docs/README.md](docs/README.md).

| Document | Bron | PDF |
|---|---|---|
| Gebruikershandleiding | `docs/user-manual/` | `docs/generated/TMS-Complete-Gebruikershandleiding.pdf` |
| Handleiding klantportaal | uittreksel van de gebruikershandleiding | `docs/generated/TMS-Klantportaal-Handleiding.pdf` |
| Beheerdershandleiding | `docs/admin-manual/` | `docs/generated/TMS-Beheerdershandleiding.pdf` |
| Projectoverzicht en historiek | `docs/project-history/` | `docs/generated/TMS-Projectoverzicht-En-Wijzigingshistoriek.pdf` |

### Helpcentrum in de applicatie

Aangemeld vind je de handleidingen op `/help`, via de knop **Help** rechtsboven. Op belangrijke schermen opent die knop meteen het juiste artikel. Klanten gebruiken `/klantportaal/help`, chauffeurs `/driver/help`.

### Handleidingen opnieuw maken

```bash
npm --prefix scripts/manuals ci
npx --prefix scripts/manuals playwright-core install chromium-headless-shell
node scripts/generate-manuals.mjs
```

Schermafbeeldingen vernieuwen: zie [docs/technical/manuals.md](docs/technical/manuals.md).
