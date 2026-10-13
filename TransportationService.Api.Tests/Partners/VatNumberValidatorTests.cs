using TransportationService.Api.Common;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Tests.Partners;

public class VatNumberValidatorTests
{
    [Fact]
    public void Null_And_Whitespace_ReturnNull()
    {
        Assert.Null(VatNumberValidator.NormalizeAndValidate(null));
        Assert.Null(VatNumberValidator.NormalizeAndValidate("   "));
    }

    [Fact]
    public void ValidBelgianNumber_IsNormalized()
    {
        // Checksum: 01234567 % 97 = 48 → check digits 97 - 48 = 49.
        Assert.Equal("BE0123456749", VatNumberValidator.NormalizeAndValidate("be 0123.456.749"));
        // Real-world style: 04174971 % 97 = 91 → check 06.
        Assert.Equal("BE0417497106", VatNumberValidator.NormalizeAndValidate("BE 0417.497.106"));
    }

    [Fact]
    public void BelgianNumber_WithWrongChecksum_IsRejected()
    {
        Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("BE0123456750"));
    }

    [Fact]
    public void BelgianNumber_WithWrongLength_IsRejected()
    {
        Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("BE123456749"));
    }

    [Fact]
    public void BelgianNumber_MustStartWithZeroOrOne()
    {
        Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("BE9123456749"));
    }

    [Fact]
    public void ForeignNumbers_PassLooseValidation()
    {
        Assert.Equal("NL123456789B01", VatNumberValidator.NormalizeAndValidate("NL123456789B01"));
        Assert.Equal("DE129273398", VatNumberValidator.NormalizeAndValidate("de 129 273 398"));
        Assert.Equal("FRXX999999999", VatNumberValidator.NormalizeAndValidate("FRXX999999999"));
    }

    [Fact]
    public void InvalidNumber_CarriesFieldError_ForFieldLevelDisplay()
    {
        var ex = Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("BE0123456750"));
        Assert.NotNull(ex.FieldErrors);
        var (field, messages) = Assert.Single(ex.FieldErrors!);
        Assert.Equal("vatNumber", field);
        Assert.Equal(ex.Message, Assert.Single(messages));

        var custom = Assert.Throws<DomainValidationException>(
            () => VatNumberValidator.NormalizeAndValidate("BE0123456750", "billing.vatNumber"));
        Assert.Equal("billing.vatNumber", Assert.Single(custom.FieldErrors!).Key);
    }

    [Fact]
    public void Garbage_IsRejected()
    {
        Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("!!"));
        Assert.Throws<DomainValidationException>(() => VatNumberValidator.NormalizeAndValidate("A/B"));
    }
}
