# GDPR — retentie, datasubjectrechten en anonimisering (H13)

Conceptdocumentatie bij de technische H13-implementatie. Juridische validatie (grondslagen,
definitieve bewaartermijnen, DSR-doorlooptijden) hoort bij Legal/DPO — zie de open vragen onderaan
en operationele checklist #20/#27.

## 1. Retentiebeleid (technisch afdwingbaar)

Configuratie: sectie `Retention` in appsettings/vault. `Retention:LegalHold=true` bevriest **alle**
geautomatiseerde purges (litigation hold).

| Gegeven | Mechanisme | Default | Opmerking |
|---|---|---|---|
| Refresh tokens (verlopen/ingetrokken) | `TokenRetentionHostedService` (elke 6 u) | 30 d na intrekking | lineage kort bewaard voor reuse-detectie |
| Activatie-/resettokens (gebruikt/verlopen) | `GdprRetentionHostedService` (dagelijks) | 30 d | rijen bevatten enkel hashes |
| Outbox-mails (Sent/Failed/Suppressed) | `GdprRetentionHostedService` | 365 d | credential-dragende bodies worden al bij bezorging gescrubd (C3) |
| Dev message-sink-bestanden | `GdprRetentionHostedService` | 14 d | alleen Development |
| Audit-logs | **NIET** in-app | maintenance-runbook | append-only trigger blokkeert DELETE; zie checklist #29 |
| Facturen/financiële records | geen purge | wettelijk (7 j BE) | bewuste uitzondering |
| Personeelsdocumenten/attesten | via anonimisering/DSR | n.v.t. | verwijdering is een bewuste HR-actie, geen timer |

## 2. Data-subject-export (art. 15/20)

`GET /api/employees/{id}/gdpr-export` → JSON-download met profiel, afwezigheden (incl.
gezondheidsvelden), kwalificaties, documentmetadata, notities en accountgegevens.

- Permissie: `employees.view_confidential`.
- Elke export wordt read-geaudit als `DataExported` met classificatie `Health`.
- Het bestand zelf heeft een korte houdbaarheid bij de aanvrager — het systeem bewaart geen kopie.

## 3. Anonimisering (art. 17, gebalanceerd met wettelijke bewaarplicht)

`POST /api/employees/{id}/anonymize` — permissie `employees.anonymize` (zit bewust in **geen enkel**
default-roltemplate; alleen expliciete toekenning/systeembeheerder).

Guard: alleen een **gedeactiveerde** medewerker.

### Anonimiseringsmatrix

| Categorie | Actie |
|---|---|
| Naam | overschreven met marker + personeelsnummer |
| Geboortedatum/-plaats, nationaliteit, taal | gewist (datum → 1900-01-01) |
| NRN, identiteitskaartnummer, IBAN/BIC, DIMONA | gewist |
| Contact/adres/noodcontacten, burgerlijke staat, kinderen ten laste | gewist |
| Vrije notities (profiel + notitierijen) | verwijderd |
| Afwezigheden: reden, HR-notitie, beslissingsnota, attest (bestand) | gewist/verwijderd; **rijen blijven** (capaciteitshistoriek) |
| Personeelsdocumenten (ID/medisch/contract/…) | bestand + rij verwijderd |
| Kwalificaties: documentnummer, notities, certificaatbestand | gewist/verwijderd; **rijen blijven** (compliance-historiek) |
| Gebruikersaccount | gedeactiveerd, e-mail vervangen, security-stamp geroteerd, alle sessies ingetrokken |
| Personeelsnummer, dienstverband, ritten, kosten | **behouden** (referentiële integriteit + wettelijke records) |

De auditrij van de anonimisering bevat **geen** oude waarden.

## 4. Databronnenlijst (persoonsgegevens)

`employees` (+ noodcontacten, jobfuncties), `users`, `absences` (+ attesten in storage),
`employee_documents` (+ storage), `employee_notes`, `employee_qualifications` (+ storage),
`drivers`, `audit_logs` (actor/IP), `outbox_messages` (adres + gerenderde mail), dev message-sink,
`refresh_tokens`/`user_security_tokens` (hashes), klantcontacten (`customers` e.a.), Peppol-/EDI-
payloads (facturatie, wettelijke bewaring).

## 5. Open juridische vragen (Legal/DPO)

1. Definitieve bewaartermijnen per categorie (defaults hierboven zijn technische aannames).
2. Grondslag en termijn voor gezondheidsgegevens bij ziekteverzuim (art. 9(2)(b)).
3. DSR-proces: identiteitsverificatie van de aanvrager, doorlooptijd, weigeringsgronden.
4. Peppol-/EDI-payloadretentie versus factuurbewaarplicht.
5. Moet anonimisering ook `audit_logs`-oldvalues van vóór de anonimisering raken? (Vereist de
   maintenance-rol uit checklist #29; nu bewust niet in-app.)
