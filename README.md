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
