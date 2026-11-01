---
id: productie-configuratie
title: Productieconfiguratie
category: Beheer
roles: admin
keywords: productie, configuratie, tms.env, nginx, systemd, omgeving, secrets, CORS
related: deployment, opslag, mail-configuratie, beveiliging-basis
---
## WAT DOE JE HIER?

Je stelt de server in voor echt gebruik.

## WANNEER GEBRUIK JE DIT?

Bij de installatie en bij een wijziging van de omgeving.

## STAPPEN

De instellingen staan in `tms.env` op de server. De volledige lijst staat in `docs/delivery/operations.md` sectie 4.

| Instelling | Waarvoor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | De PostgreSQL-database |
| `Jwt__SigningKey` | Sleutel voor aanmeldtokens |
| `ColumnEncryption__Key` | Sleutel voor versleutelde kolommen |
| `Storage__RootPath` | De [opslagmap](help:opslag) |
| `Cors__AllowedOrigins__0` en `Frontend__BaseUrl` | Het publieke adres, met https |
| `Email__Smtp__*` | De [mail](help:mail-configuratie) |

Vul de waarden nooit in een document of in de repository in.

De opzet is één domein met nginx en TLS, en de API op `127.0.0.1:5019`. De voorbeelden staan in `deploy/nginx/` en `deploy/systemd/`.

## WAT GEBEURT ER DAARNA?

De API controleert bij het starten de instellingen en weigert te starten als iets onveilig is.

## WAAR MOET JE OP LETTEN?

Een productiebuild van de frontend vereist `VITE_API_BASE_URL`, anders stopt de build.

## VEEL VOORKOMENDE PROBLEMEN

**De browser krijgt CORS-fouten.** Controleer of het adres exact overeenkomt met `Cors__AllowedOrigins__0`.
