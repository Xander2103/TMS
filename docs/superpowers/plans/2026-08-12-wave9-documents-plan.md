# Wave 9 — Document Generation + Batch Documents Implementation Note

- `TransportDocumentRenderer` (PDFsharp, huisstijlpatroon van de factuur-/labelrenderers;
  QuestPDF blijft bewust géén dependency): leveringsbon en CMR (genummerde vakken 1-2, 3-4,
  6-9 en 22-24 met handtekeningvakken) op één pagina uit bevroren ordergegevens — afzender =
  de facturerende entiteit van de order (anders tenant-standaard), geadresseerde = klant,
  route + goederen(lijnen) + totaalgewicht + klantreferentie.
- `TransportDocumentService`: per order (GET /api/orders/{id}/documents/{delivery-note|cmr},
  orders.view) en als batch per rit (GET /api/trips/{id}/documents/{kind}, planning.view) —
  één samengevoegde PDF, één pagina per order, in ROUTEVOLGORDE ("print alles voor deze rit").
- UI: Leveringsbon/CMR-knoppen op het orderdetail; "CMR's (rit)" en "Leveringsbonnen (rit)"
  op het ritdetail. Downloads via de bestaande blob-downloadaanpak met bestandsnaam uit
  Content-Disposition.
- Documentstrategie: de bestaande klantvlag `SignedDeliveryNoteRequired` blijft DE config
  (geen nieuw strategiemodel); de vlag stuurt vandaag al de POD-vereisten.
