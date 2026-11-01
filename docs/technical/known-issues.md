# Bekende problemen

## Verwijderde conceptfacturen blijven in de factuurlijst

Gevonden op 2026-11-01 bij het maken van de handleiding. Niet opgelost, omdat de geaccepteerde facturatiecode niet gewijzigd moest worden tijdens de consolidatie.

**Wat je ziet:** een verwijderd concept staat in **Facturen** met € 0,00. Openen geeft "Niet gevonden".

**Oorzaak:** `InvoiceService.SearchAsync` gebruikt `_dbContext.Customers.IgnoreQueryFilters()` in dezelfde query. In EF Core schakelt dat de queryfilters van de hele query uit, dus ook `!IsDeleted` op `Invoice`. De expliciete `TenantId`-voorwaarde blijft gelden, dus er is geen lek tussen bedrijven.

**Impact:** alleen een spookregel in de lijst. Geen nummer, geen bedrag, geen boeking.

**Oplossing:** voeg `.Where(i => !i.IsDeleted)` toe aan de query in `SearchAsync`, met een regressietest.

## Restpunten uit de acceptatie

- Geen echte Peppol Access Point: Live staat uit.
- Eén API-instantie: geen schaalvergroting.
- Geen malwarescan op uploads.
- IBAN van medewerkers leesbaar in de database; medische bestanden onversleuteld op schijf.
- Activatie- en resetlinks leesbaar in de mailwachtrij tot ze gewist worden.
- Geen idempotentiesleutel op aanmaken via de ruwe API.
- Echte mailaflevering is niet getest op de acceptatiestack.
- `invoices.css` bevat een niet-afgesloten regel `.inv-list-muted {`: de stijlen eronder zijn slapend. Bewust niet aangepast.
- De chauffeurstabs **Ritten** en **Berichten** openen in de gewone lay-out.
