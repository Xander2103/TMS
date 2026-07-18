using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Tests.Authentication;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var sut = new PasswordHasher();
        var hash = sut.Hash("Sup3r$ecret");

        Assert.NotEqual("Sup3r$ecret", hash);
        Assert.NotEqual(PasswordVerificationResult.Failed, sut.Verify(hash, "Sup3r$ecret"));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var sut = new PasswordHasher();
        var hash = sut.Hash("correct-horse");

        Assert.Equal(PasswordVerificationResult.Failed, sut.Verify(hash, "wrong-horse"));
    }

    [Fact]
    public void Verify_NullOrEmptyHash_Fails()
    {
        var sut = new PasswordHasher();

        Assert.Equal(PasswordVerificationResult.Failed, sut.Verify(null, "anything"));
        Assert.Equal(PasswordVerificationResult.Failed, sut.Verify("", "anything"));
    }

    [Fact]
    public void Hash_ProducesDistinctSaltsForSamePassword()
    {
        var sut = new PasswordHasher();

        Assert.NotEqual(sut.Hash("same"), sut.Hash("same"));
    }
}
