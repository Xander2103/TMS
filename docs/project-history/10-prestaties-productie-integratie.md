---
id: geschiedenis-integratie-prestaties
title: Integratie van prestaties en productie
category: Projectgeschiedenis
order: 10
---
## Probleem

Beide takken vertrokken van `ad405ac2`. De eerdere testclusters in `/private/tmp` waren intussen door macOS leeggemaakt.

## Risico

Een verkeerde volgorde van migraties, Designer-bestanden die verhardingskolommen terugdraaien, en te ruime claims over races.

## Oplossing

De testset is opnieuw opgebouwd vanuit een echte `ad405ac2`-database en 23 prestatiecommits zijn overgezet. Inhoudelijke herstellingen:

- `3b4a994b`: Designer-bestanden beschrijven het geïntegreerde model.
- `09b987c5`: CMR op één opdracht. Het eerdere "12×200" was gemeten op 12 verschillende opdrachten.
- `abead311`: de tweede uitgifte van een factuur is altijd 409.
- `5837ea11`: een race op serienummers antwoordt 409.
- `191433e8`: de meldingstaak loopt onder vergrendeling, met heartbeat.
- `32136c59`: activiteitstypes worden aangemaakt bij de provisioning (7 van de 8 parallelle eerste opdrachten liepen vast op een deadlock).

## Belangrijke architectuurbeslissing

Een overgenomen claim is pas waar na een meting op de geïntegreerde code. Daarom is de CMR-race opnieuw gemeten, met een negatieve controle op de oude code.

## Uitgevoerde tests

- Migraties: nieuw 195; upgrade 191 + 4; tweemaal herstarten identiek.
- CMR, 5 rondes: 12 uitgiftes geven 1×200 en 11×409; 12 kopieën van één verzoek geven 12×200 en één document.
- CMR negatieve controle: 3 mislukkingen op de oude code, daarna 18/18 in 8 runs.
- Factuur: acht keer dezelfde geeft 1×200 en 7×409; acht verschillende zijn aansluitend genummerd.
- Tenant A/B 37/37. Herstel 12/12. CMR, laadbrief, PDF en UBL byte-identiek na upgrade.
- 8 weigeringen bij het starten (verkeerde sleutel, korte JWT, opslag, CORS, SMTP).
- `/health/ready`: 1000 keer 200 met 40 parallel.
- Technische poorten: dotnet 3482/3482, vitest 2745/2745 (twee keer), secret scan schoon, 195 migraties.

## Resultaat

Eén tak `a18e4d8c` met beide lijnen. De map bevat geen expliciet oordeel, alleen de poorten en het README-bestand.
