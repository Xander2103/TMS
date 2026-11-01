---
id: beveiliging-basis
title: Beveiliging: de basis
category: Beheer
roles: admin
keywords: beveiliging, sessies, wachtwoord, tenantscheiding, encryptiesleutel, secrets, audit, GDPR
related: gebruikers, productie-configuratie
---
## WAT DOE JE HIER?

Je leest hoe het systeem gegevens beschermt en wat jouw taak is.

## WANNEER GEBRUIK JE DIT?

Bij het opzetten van de omgeving en bij elke controle.

## STAPPEN

Wat het systeem zelf doet:

- **Scheiding:** bedrijven, klanten en chauffeurs zien alleen wat van hen is. Iets anders antwoordt "Niet gevonden".
- **Sessies:** afmelden stopt die sessie meteen. Een wachtwoordwijziging of deactivering stopt alle sessies.
- **Prijzen:** wie geen commercieel recht heeft, krijgt geen bedragen van de server.
- **Historiek:** de auditlog kan alleen worden aangevuld, nooit gewijzigd.

Wat jij doet:

1. Bewaar secrets (database, JWT, encryptiesleutel, SMTP) in `tms.env` op de server of in een kluis, nooit in de repository.
2. Bewaar de kolomencryptiesleutel apart in een kluis. Zonder die sleutel zijn versleutelde gegevens niet meer leesbaar. Zie `docs/operations/encryption-keys.md`.
3. Loop `docs/security/operational-checklist.md` door. Daar staan de organisatie- en infrastructuurpunten die nog bij jou liggen, zoals TLS, versleutelde back-ups en verwerkersovereenkomsten.

## WAT GEBEURT ER DAARNA?

De API weigert te starten bij een onveilige configuratie, zoals een ontbrekende sleutel, een te korte JWT-sleutel of onveilige opslag.

## WAAR MOET JE OP LETTEN?

Restpunten die openstaan: geen malwarescan op uploads, IBAN van medewerkers leesbaar in de database, medische bestanden onversleuteld op schijf. Beperk daarom de toegang tot de server en de database.

## VEEL VOORKOMENDE PROBLEMEN

**De API start niet na een wijziging.** Lees het logbestand: de reden staat er letterlijk in.
