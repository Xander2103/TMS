# Security Policy

## Kwetsbaarheid melden

Meld vermoedelijke kwetsbaarheden **niet** via een publieke issue. Mail de beheerder(s) van deze
repository rechtstreeks met:

- een beschrijving en de impact zoals jij die inschat;
- reproductiestappen of een proof-of-concept;
- versie/commit waarop je testte.

Je krijgt binnen 5 werkdagen een eerste reactie. Coordinated disclosure: publiceer niets tot een
fix beschikbaar is en dat is afgestemd.

## Scope

- `TransportationService.Api` (backend, incl. auth/tenancy/uploads/Peppol)
- `TransportationService.Web` (frontend)

## Wat wij zelf draaien

- CI: backend- en frontendbuilds + volledige testsuites op elke PR.
- Security-workflow: gitleaks (volledige historie), CodeQL (C# + TS, wekelijks + per PR),
  dependency-review (blokkeert high/critical bij PR's), SBOM-generatie, `dotnet list package
  --vulnerable` en `npm audit` als release-gates.
- Dependabot voor NuGet, npm en GitHub Actions (wekelijks).

## Bekende bewuste beperkingen

Operationele/infrastructurele punten (TLS-terminatie, vault, WAF, virusscanner-engine, …) staan in
`docs/security/operational-checklist.md` en worden daar beheerd — niet in code afgehandeld.
