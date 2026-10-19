# Kolomversleutelingssleutels (ColumnEncryption)

Bijzondere persoonsgegevens (o.a. rijksregisternummer en identiteitskaartnummer van medewerkers)
staan versleuteld in de databank (AES-256-GCM, opgeslagen als `enc:v1:…`). Wie de sleutel kwijt
is, kan die gegevens **nooit** meer lezen: de databank-back-up alleen is dus niet genoeg.

## De sleutels

| Sleutel (`tms.env`) | Inhoud | Gebruik |
|---|---|---|
| `ColumnEncryption__Key` | 32 bytes, base64 (`openssl rand -base64 32`) | Actieve sleutel: alle nieuwe schrijfacties en de eerste leespoging |
| `ColumnEncryption__PreviousKeys__0`, `__1`, … | idem | Alleen lezen: gegevens die met een oudere sleutel zijn geschreven |

- Buiten Development weigert de API te starten zonder `ColumnEncryption__Key`.
- De sleutels staan **nooit** in de repo, de databank, de logs of een back-upset. Ze staan in
  `tms.env` (mode `0600`) en, als bron van waarheid, in de vault.

## Controle bij elke start (H5)

Bij een eerste start op een databank schrijft de API één vaste controlewaarde
(`column_encryption_canaries`). Bij **elke** start en bij elke `GET /health/ready` controleert de API
dat de geconfigureerde sleutels die controlewaarde en een kleine steekproef echte versleutelde
medewerkergegevens kunnen ontsleutelen:

- **Mislukt bij de start:** de API weigert te starten, met een melding over welke sleutel ontbreekt
  of niet past. Schrijven met een verkeerde sleutel zou de databank half onleesbaar maken.
- **Mislukt tijdens het draaien:** `/health/ready` geeft 503 (`column-encryption` Unhealthy).

Sleutelmateriaal en ontsleutelde waarden komen nooit in de melding of het log.

## Rotatie

Rotatie is een configuratiewijziging, geen datamigratie:

1. Maak een nieuwe sleutel: `openssl rand -base64 32`. Bewaar hem eerst in de vault.
2. In `tms.env`: verplaats de huidige `ColumnEncryption__Key` naar de eerstvolgende vrije
   `ColumnEncryption__PreviousKeys__N` en zet de nieuwe als `ColumnEncryption__Key`.
3. `systemctl restart transportationservice-api`; controleer `GET /health/ready` = 200.

Nieuwe en gewijzigde gegevens gebruiken dan de nieuwe sleutel. Bestaande rijen worden pas
herversleuteld wanneer ze opnieuw opgeslagen worden; er is geen bulk-herversleuteling. Een
**vorige sleutel mag je daarom nooit weggooien**: zolang er rijen (of de controlewaarde) mee
versleuteld zijn, is hij nodig, en een ontbrekende vorige sleutel laat de start weigeren.

## Back-up en herstel

- Elke back-upset (`docs/operations/backup-restore.md`) hoort bij de sleutels die golden toen de
  set gemaakt werd. Bewaar in de vault, apart van de back-ups: de actieve sleutel **en alle** vorige
  sleutels, met de datum van elke rotatie.
- Herstel = dezelfde `ColumnEncryption__Key` + alle `ColumnEncryption__PreviousKeys__N` van dat
  moment. Met een verkeerde sleutel weigert de herstelde API te starten. Dat is bewust: zo merk je
  het vóór iemand een HR-pagina opent (restore-drill 2026-10-19).
- `Jwt__SigningKey` kwijt is niet erg (iedereen moet opnieuw aanmelden); de kolomsleutel kwijt is
  onherstelbaar gegevensverlies.
