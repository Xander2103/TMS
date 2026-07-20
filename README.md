# TransportationService

TransportationService is een modulair Transportation Management System (TMS) voor transport- en distributiebedrijven.

Het systeem centraliseert onder andere klanten, personeel, chauffeurs, voertuigen, opleggers, transportopdrachten, ritplanning, scanning, Proof of Delivery, facturatie, kostenberekening, HR en rapportering.

## Doel

Het doel van dit project is een modern en schaalbaar TMS te bouwen waarmee transportbedrijven hun volledige operationele flow kunnen beheren:

- klanten en locaties beheren;
- transportopdrachten aanmaken;
- ritten plannen;
- chauffeurs, voertuigen en opleggers toewijzen;
- pakketten en barcodes beheren;
- laden en lossen registreren;
- afwijkingen en schade melden;
- Proof of Delivery registreren;
- kosten, omzet en winst opvolgen;
- facturen genereren;
- werknemers en afwezigheden beheren;
- klant- en chauffeursportalen aanbieden.

## Technologieën

### Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- JWT authentication
- Role-based permissions
- Docker

### Frontend

- React
- TypeScript
- Vite
- React Router

## Projectstructuur

```text
TransportationService/
├── TransportationService.Api/
├── TransportationService.Application/
├── TransportationService.Domain/
├── TransportationService.Infrastructure/
├── TransportationService.Tests/
└── TransportationService.Web/

De exacte structuur kan afwijken afhankelijk van de huidige solution-opbouw.

Belangrijkste modules
Authenticatie
Gebruikers
Rollen en permissions
Personeel
Chauffeurs
Kwalificaties
Klanten
Contactpersonen
Locaties
Voertuigen
Opleggers
Vlootdocumenten
Onderhoud en inspecties
Transportopdrachten
Goederenlijnen
Ritplanning
Pakketten
Barcodes en QR-codes
Scanning
Stopuitvoering
ETA-opvolging
Afwijkingen en schade
Proof of Delivery
Facturatie
Kostenberekening
Winstgevendheid
Notificaties
Rapportering
HR en afwezigheden
EDI-integratie
Vereisten

Installeer lokaal:

.NET 10 SDK
Node.js
npm
Docker Desktop
Git
Lokaal opstarten
1. Repository clonen
git clone https://github.com/Xander2103/TransportationService.git
cd TransportationService
2. Database starten
docker compose up -d
3. Database migrations uitvoeren
dotnet ef database update --project TransportationService.Api
4. Backend starten
dotnet run --project TransportationService.Api
5. Frontend starten

Open een tweede terminal:

cd TransportationService.Web
npm install
npm run dev

Open daarna:

http://localhost:5173
Development-login

Gebruik uitsluitend lokale testaccounts.

E-mail: admin@dev.local
Wachtwoord: zie lokale developmentconfiguratie of seed-documentatie

Plaats nooit echte wachtwoorden of secrets in deze README.

Database

De applicatie gebruikt PostgreSQL.

De databaseconfiguratie staat lokaal in:

docker-compose.yml

en/of:

appsettings.Development.json

Gevoelige configuratiebestanden mogen niet naar GitHub worden gepusht.

Migrations

Een nieuwe migration maken:

dotnet ef migrations add NaamVanMigration --project TransportationService.Api

Database bijwerken:

dotnet ef database update --project TransportationService.Api

Migrations bekijken:

dotnet ef migrations list --project TransportationService.Api
Tests

Backendtests uitvoeren:

dotnet test

Frontend controleren:

cd TransportationService.Web
npm run lint
npm run build
Security

Het project gebruikt:

JWT access tokens;
refresh tokens;
wachtwoordhashing;
rollen en permissions;
tenantisolatie;
auditlogging;
server-side validatie.

Secrets, tokens, wachtwoorden en productiegegevens mogen nooit in Git worden opgeslagen.

Status

Dit project is momenteel in actieve ontwikkeling.

De bestaande basis bevat onder andere:

authenticatie;
masterdata;
vlootbeheer;
HR;
transportopdrachten;
ritplanning;
scanning;
POD;
facturatie;
kostenberekening;
notificaties;
rapportering.

Verdere functionele testen, UX-verbeteringen en integraties zijn nog bezig.

Roadmap

Geplande uitbreidingen:

uitgebreid klantenportaal;
volledige chauffeursapp;
automatische accountcreatie;
automatische barcode- en QR-generatie;
verbeterde pakketlabels;
PTV-routeplanning;
automatische ETA-notificaties;
e-mail- en sms-integraties;
EDI-import en export;
Peppol;
Outlook-integratie;
AI-ondersteunde planning;
live operational control center.
Auteur

Ontwikkeld door Xander Van Malder.


Ik zou zeker nog controleren of deze commando’s exact overeenkomen met uw echte mapnamen. Vooral:

```text
TransportationService.Api
