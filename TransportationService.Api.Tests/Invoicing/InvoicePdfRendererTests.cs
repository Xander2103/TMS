using TransportationService.Api.Modules.Invoicing.Services;

namespace TransportationService.Api.Tests.Invoicing;

public class InvoicePdfRendererTests
{
    private static InvoicePdfSnapshot Snapshot(IReadOnlyList<InvoicePdfLine>? lines = null) => new(
        SellerName: "Acme Transport BV",
        SellerAddressLine: "Havenlaan 1, 2000 Antwerpen, BE",
        SellerVatNumber: "BE0123456789",
        SellerIban: "BE68539007547034",
        SellerBic: "GKCCBEBB",
        InvoiceFooter: "Algemene voorwaarden op aanvraag beschikbaar.",
        VatLegalText: "Btw verlegd naar de medecontractant.",
        LogoBytes: null,
        InvoiceNumber: "2026070001",
        InvoiceDate: new DateOnly(2026, 7, 30),
        DueDate: new DateOnly(2026, 8, 29),
        CustomerName: "Haven BV",
        CustomerAddressLine: "Kaai 12, 9000 Gent, BE",
        CustomerVatNumber: "BE0987654321",
        PurchaseOrderNumber: "PO-2026-100",
        Lines: lines ?? [new InvoicePdfLine("Transport Antwerpen-Gent", 1, 450.00m, 21m, 450.00m)],
        Subtotal: 450.00m,
        VatAmount: 94.50m,
        Total: 544.50m,
        Currency: "EUR",
        Notes: "Bedankt voor uw opdracht.");

    [Fact]
    public void Render_ProducesNonTrivialPdfBytes_NoException()
    {
        var bytes = InvoicePdfRenderer.Render(Snapshot());

        Assert.True(bytes.Length > 1000, $"Expected a non-trivial PDF, got {bytes.Length} bytes.");
        // A real PDF always starts with the %PDF- magic header.
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void Render_WithManyLines_OverflowsToASecondPage_NoException()
    {
        var lines = Enumerable.Range(1, 80)
            .Select(i => new InvoicePdfLine($"Regel {i}", 1, 10m, 21m, 10m))
            .ToList();

        var bytes = InvoicePdfRenderer.Render(Snapshot(lines));

        Assert.True(bytes.Length > 1000);
        Assert.True(PageCount(bytes) > 1, "80 line items must overflow the line-item table onto a second page.");
    }

    /// <summary>
    /// Fix round 1: with too few lines to ever trip the per-line table page-break (threshold
    /// ~page height - 220pt) but enough to leave insufficient room for the VAT breakdown +
    /// totals + payment + notes block above the fixed-position footer, the renderer must still
    /// start a second page for that block rather than let it overlap the footer.
    /// </summary>
    [Fact]
    public void Render_WithLinesJustUnderTableThreshold_StillBreaksBeforeOverlappingTheFooter()
    {
        // 25 lines never triggers the per-line loop's own break (y stays well under the ~622pt
        // threshold throughout), but leaves y ~598pt — too little room for the VAT/totals/
        // payment/notes block (~159pt) before the footer reserve.
        var lines = Enumerable.Range(1, 25)
            .Select(i => new InvoicePdfLine($"Regel {i}", 1, 10m, 21m, 10m))
            .ToList();

        var bytes = InvoicePdfRenderer.Render(Snapshot(lines));

        Assert.Equal(2, PageCount(bytes));
    }

    private static int PageCount(byte[] pdfBytes)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public void Render_WithNoLines_StillProducesAValidPdf()
    {
        var bytes = InvoicePdfRenderer.Render(Snapshot([]) with { Subtotal = 0, VatAmount = 0, Total = 0 });

        Assert.True(bytes.Length > 500);
    }
}
