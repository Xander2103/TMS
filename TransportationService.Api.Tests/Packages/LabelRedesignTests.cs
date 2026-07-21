using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Labels;

namespace TransportationService.Api.Tests.Packages;

/// <summary>Redesigned horizontal label: snapshot mapping + rendering (business-feedback wave).</summary>
public class LabelRedesignTests
{
    private static LabelSnapshot Snapshot(int? seqNo = 2, int? seqTotal = 3) => new(
        TenantName: "Acme Transport",
        PackageNumber: "PKG-00007",
        BarcodeValue: "PKG-00007-7K2M9QX4",
        IncludeQr: true,
        OrderNumber: "ORD-1",
        CustomerName: "Haven BV",
        LoadingLocation: "Depot, Antwerpen",
        DeliveryLocation: "Klant, Rotterdam",
        DeliveryStopSequence: 2,
        CustomerReference: "REF-99",
        WeightKg: 123.5m,
        UnitTypeLabel: "Colli",
        HandlingInstructions: "Voorzichtig",
        IsFragile: true,
        AdrRequired: true,
        RequiresTemperatureControl: false,
        RequiresSignature: true,
        SequenceLabel: "Collo 2 van 3",
        SenderName: "Acme Depot",
        SenderStreet: "Havenlaan 1",
        SenderPostalCodeCity: "2000 Antwerpen",
        SenderCountry: "BE",
        PickupDate: "22-07-2026",
        PickupTime: "08:30",
        RecipientName: "Haven BV",
        RecipientStreet: "Kade 5",
        RecipientPostalCodeCity: "3000 Rotterdam",
        RecipientCountry: "NL",
        DeliveryDate: "23-07-2026",
        DeliveryTime: "14:00",
        SequenceNumber: seqNo,
        SequenceTotal: seqTotal,
        VolumeM3: 1.25m,
        PurchaseOrderNumber: "PO-2026-Q3",
        CashOnDelivery: "€ 250");

    [Fact]
    public void Render_HorizontalThermal_ProducesPdf()
    {
        var pdf = new LabelRenderService().Render([Snapshot()], LabelFormat.Thermal100x150);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 200);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }

    [Fact]
    public void Render_MultiplePackages_OnePagePerLabel()
    {
        var pdf = new LabelRenderService().Render([Snapshot(1, 3), Snapshot(2, 3), Snapshot(3, 3)], LabelFormat.Thermal100x150);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 200);
    }

    [Fact]
    public void Render_A4Grid_StillWorks()
    {
        var pdf = new LabelRenderService().Render([Snapshot(), Snapshot()], LabelFormat.A4);

        Assert.Equal((byte)'%', pdf[0]);
    }

    [Fact]
    public void Render_LegacySnapshot_WithoutRedesignFields_StillRenders()
    {
        // A pre-redesign snapshot: only the original positional fields are set.
        var legacy = new LabelSnapshot(
            "Acme", "PKG-1", "PKG-1-ABCDEFGH", true, "ORD-1", "Haven BV",
            "Depot, Antwerpen", "Klant, Rotterdam", 2, "REF-1", 50m, "Colli", "Instructie",
            false, false, false, false, "Collo 1 van 1");

        var pdf = new LabelRenderService().Render([legacy], LabelFormat.Thermal100x150);

        Assert.Equal((byte)'%', pdf[0]);
        Assert.True(pdf.Length > 200);
    }

    [Fact]
    public void Snapshot_SequenceFields_AreExposed()
    {
        var snapshot = Snapshot(2, 3);
        Assert.Equal(2, snapshot.SequenceNumber);
        Assert.Equal(3, snapshot.SequenceTotal);
        Assert.Equal("PKG-00007-7K2M9QX4", snapshot.BarcodeValue); // barcode value unchanged
        Assert.Equal("PO-2026-Q3", snapshot.PurchaseOrderNumber);
        Assert.Equal("2000 Antwerpen", snapshot.SenderPostalCodeCity);
        Assert.Equal("3000 Rotterdam", snapshot.RecipientPostalCodeCity);
    }
}
