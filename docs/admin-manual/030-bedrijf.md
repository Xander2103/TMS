---
id: bedrijfsinstellingen
title: Bedrijfsinstellingen en een nieuw bedrijf
category: Beheer
roles: admin
keywords: tenant, bedrijf, instellingen, eigen bedrijven, juridische entiteit, nummering, branding, provisioning
related: productie-configuratie, peppol-status
---
## WAT DOE JE HIER?

Je stelt de gegevens van je bedrijf in. Elk bedrijf (technisch: een "tenant") heeft strikt gescheiden gegevens.

## WANNEER GEBRUIK JE DIT?

Bij de start, en als er iets wijzigt.

## STAPPEN

![De bedrijfsinstellingen](shot:beheer-instellingen "De instellingen")

1. Open **Parameters**, **Instellingen**. Hier staan onder andere **Bedrijfsprofiel**, **Facturatie**, **Planningsconflicten**, **Nummering**, **Branding**, **Taal** en **Regionale weergave**.

![De eigen bedrijven](shot:beheer-eigen-bedrijven "De eigen bedrijven")

2. Open **Eigen bedrijven**. Factuurnummering, IBAN, logo en de bannerfoto bovenaan dossiers en opdrachten staan per juridische entiteit. Zonder eigen bannerfoto toont de pagina de foto van het standaardbedrijf, en anders de standaard transportfoto.

### Een nieuw bedrijf aanmaken

Een productiedatabase start leeg. Een nieuw bedrijf maak je op de server met het commando `--provision-tenant` van de API. Het maakt het bedrijf, de rollen, de referentielijsten en de eerste beheerder, zonder wachtwoord en met een activatiemail. Volledige uitleg: `docs/operations/provisioning.md`.

Richt daarna in deze volgorde in: Instellingen, Eigen bedrijven, Gebruikers en rollen, referentielijsten en prijzen, berichten, Peppol, back-ups.

## WAT GEBEURT ER DAARNA?

Wijzigingen gelden voor nieuwe documenten. Uitgegeven documenten en definitieve facturen veranderen niet.

## WAAR MOET JE OP LETTEN?

Het commando is veilig om opnieuw uit te voeren: het overschrijft niets.

## VEEL VOORKOMENDE PROBLEMEN

**Een factuur blokkeert op een ontbrekende grootboekrekening.** Vul **Parameters**, **Boekhouding** aan.
