using System.Text.RegularExpressions;

namespace Sallvat.UnitTests.Architecture;

public sealed partial class ContinuousIntegrationWorkflowTests
{
    [Fact]
    public void WorkflowPinsActionsAndRunsRequiredValidationSteps()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains(
            "actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd",
            workflow);
        Assert.Contains(
            "actions/setup-node@395ad3262231945c25e8478fd5baf05154b1d79f",
            workflow);
        Assert.Contains(
            "actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7",
            workflow);
        Assert.Contains("npm ci --ignore-scripts", workflow);
        Assert.Contains("npm run css:build", workflow);
        Assert.Contains("dotnet restore Sallvat.sln --locked-mode", workflow);
        Assert.Contains("dotnet build Sallvat.sln --configuration Release", workflow);
        Assert.Contains("dotnet test Sallvat.sln --configuration Release", workflow);
        Assert.Contains("dotnet format Sallvat.sln --verify-no-changes", workflow);
        Assert.DoesNotMatch(FloatingActionVersionPattern(), workflow);
    }

    [GeneratedRegex(@"uses:\s+[^@\s]+@v\d", RegexOptions.CultureInvariant)]
    private static partial Regex FloatingActionVersionPattern();
}
