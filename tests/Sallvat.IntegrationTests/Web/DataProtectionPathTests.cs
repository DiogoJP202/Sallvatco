using Sallvat.Web.Security;

namespace Sallvat.IntegrationTests.Web;

public sealed class DataProtectionPathTests
{
    [Theory]
    [InlineData("keys")]
    [InlineData("assets/keys")]
    public void PathWithinWebRootIsRejected(string relativeCandidate)
    {
        var webRoot = Path.Combine(
            Path.GetTempPath(),
            "Sallvat.Tests.WebRoot");
        var candidate = Path.Combine(webRoot, relativeCandidate);

        Assert.False(
            DataProtectionPath.IsOutsideDirectory(candidate, webRoot));
    }

    [Fact]
    public void SiblingPathIsAccepted()
    {
        var applicationRoot = Path.Combine(
            Path.GetTempPath(),
            "Sallvat.Tests.ApplicationRoot");
        var webRoot = Path.Combine(applicationRoot, "wwwroot");
        var candidate = Path.Combine(applicationRoot, "data-protection-keys");

        Assert.True(
            DataProtectionPath.IsOutsideDirectory(candidate, webRoot));
    }
}
