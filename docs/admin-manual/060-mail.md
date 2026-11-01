---
id: mail-configuratie
title: Mail
category: Beheer
roles: admin
keywords: mail, SMTP, e-mail, wachtrij, outbox, TLS, activatiemail
related: storingen, productie-configuratie
---
## WAT DOE JE HIER?

Je stelt in hoe het systeem mails verstuurt.

## WANNEER GEBRUIK JE DIT?

Bij de installatie en als mails niet aankomen.

## STAPPEN

1. Vul in `tms.env` de instellingen `Email__Smtp__Host`, `Port`, `Username`, `Password`, `FromAddress` en `UseTls` in. Buiten Development is TLS verplicht.
2. Stel `Frontend__BaseUrl` in. Daarmee maakt het systeem de links in mails.
3. Stuur een test door een activatielink te maken.

## WAT GEBEURT ER DAARNA?

Mails gaan via een wachtrij die elke 30 seconden loopt. Mislukt het, dan probeert het systeem opnieuw na 5, 10, 20 en 40 minuten. Na 5 pogingen staat de mail op Mislukt.

## WAAR MOET JE OP LETTEN?

- Een onderbroken verzending wordt niet vanzelf herhaald, om dubbele mails te vermijden. Ze wacht op jouw **Vrijgeven** of **Afwijzen**.
- Sms is niet aangesloten buiten Development.
- Mails bevatten geen factuur als bijlage, maar verwijzen naar het portaal.
- Activatielinks zijn leesbaar in de wachtrij in de database tot ze worden gewist. Beperk die toegang.

## VEEL VOORKOMENDE PROBLEMEN

**Mails blijven hangen.** Kijk op `/health/details` naar de wachtrij. Zie [Storingen](help:storingen).
