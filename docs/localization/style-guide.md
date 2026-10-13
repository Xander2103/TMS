# Localization style guide

## Aanspreekvorm (§71)

| Taal | Vorm | Voorbeeld |
|---|---|---|
| NL | Informeel **je** — consistent met de bestaande interne UI | "Je hebt wijzigingen die nog niet zijn opgeslagen." |
| FR | Formeel **vous**, altijd | "Vous avez des modifications non enregistrées." |
| EN | Neutraal, direct imperatief; "you" alleen waar nodig | "You have unsaved changes." / "Save settings" |

Kiosk-uitzondering: NL-kioskschermen gebruiken **u** ("Voer uw code in") — een publiek
wandtoestel is formeler dan iemands eigen dashboard. FR/EN volgen hun normale vorm.

## Casing & interpunctie

- **Sentence case** overal: knoppen, titels, labels ("Pauze starten", niet "Pauze Starten").
- Volzinnen (meldingen, hints, empty states) eindigen op een punt; labels en knoppen niet.
- FR: spatie vóór `? ! : ;` en guillemets « … » voor citaten; apostrof `'` volstaat.
- EN: Brits Engels (colour → wij schrijven "Favourites", "synchronise"); geen Oxford-drift
  binnen één scherm.
- Ellipsis: `...` in doorlopende NL/EN-teksten mag, `…` (één teken) bij afgekorte
  bezig-status; wees per bestand consistent.

## Knoppen & acties

- Werkwoord voorop, geen lidwoord: "Opslaan / Enregistrer / Save".
- Destructieve bevestigingsknoppen herhalen de actie ("Registratie annuleren"), nooit
  alleen "OK".
- Bezig-status = zelfde werkwoord + …: "Bevestigen…" / "Confirmation…" / "Confirming…".

## Wat je NOOIT doet

- Datums/tijden/getallen/valuta met de hand in vertaalstrings uitschrijven — altijd via
  `utils/dates.ts` / `utils/numbers.ts` (tenant-instellingen) en `{param}`-interpolatie.
- Displaytekst als key, tekstconcatenatie i.p.v. interpolatie, `{count} item(s)` i.p.v.
  `_one`/`_other`-pluralen.
- Gendered formuleringen waar het systeem het geslacht niet kent (§39) — schrijf om
  ("de medewerker" i.p.v. "hij/zij").
- Afkortingen vertalen die vakjargon zijn: LDM, ADR, CMR, POD, KPI, EDI, VAT→wel "Btw/TVA/VAT".
- Endoniemen vertalen: de taalkiezer toont altijd "Nederlands / Français / English".

## Terminologie

Volg **altijd** [glossary.md](glossary.md). Nieuw kernbegrip nodig? Eerst daar toevoegen
(Key|NL|FR|EN|Context), dan pas in resources gebruiken.
