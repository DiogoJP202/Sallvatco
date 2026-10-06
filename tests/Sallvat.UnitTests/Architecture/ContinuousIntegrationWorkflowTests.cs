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
        Assert.Contains("npm audit --audit-level=high", workflow);
        Assert.Contains("npm run lint:markdown", workflow);
        Assert.Contains("npm run test:tooling", workflow);
        Assert.Contains("dotnet restore Sallvat.sln --locked-mode", workflow);
        Assert.Contains("dotnet build Sallvat.sln --configuration Release", workflow);
        Assert.Contains("dotnet test Sallvat.sln --configuration Release", workflow);
        Assert.Contains("dotnet format Sallvat.sln --verify-no-changes", workflow);
        Assert.DoesNotMatch(FloatingActionVersionPattern(), workflow);
    }

    [Fact]
    public void PagesWorkflowExportsAndDeploysWithPinnedActions()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "pages.yml"));

        Assert.Contains(
            "actions/configure-pages@45bfe0192ca1faeb007ade9deae92b16b8254a0d",
            workflow);
        Assert.Contains(
            "actions/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9",
            workflow);
        Assert.Contains(
            "actions/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346",
            workflow);
        Assert.Contains("tools/Sallvat.Showcase/Sallvat.Showcase.csproj", workflow);
        Assert.Contains("steps.pages.outputs.base_url", workflow);
        Assert.DoesNotMatch(FloatingActionVersionPattern(), workflow);
    }

    [Fact]
    public void PagesCanOnlyBeCalledAfterSuccessfulValidationOfMain()
    {
        var ci = ReadWorkflow("ci.yml");
        var pages = ReadWorkflow("pages.yml");
        var publish = Job(ci, "pages");

        Assert.Contains("    needs: validate\n", publish);
        Assert.Contains("    uses: ./.github/workflows/pages.yml\n", publish);
        Assert.Contains(MainPublicationCondition, publish);
        Assert.DoesNotContain("always()", publish);
        Assert.DoesNotContain("!cancelled()", publish);
        Assert.Contains("on:\n  workflow_call:\n", pages);
        Assert.DoesNotContain("  push:", pages);
        Assert.DoesNotContain("  workflow_dispatch:", pages);
        Assert.DoesNotContain("  workflow_run:", pages);
        Assert.DoesNotContain("pull_request_target", ci + pages);
        Assert.DoesNotContain("continue-on-error", ci + pages);
        Assert.Contains(MainPublicationCondition, Job(pages, "build"));
        Assert.Contains("    needs: build\n", Job(pages, "deploy"));
    }

    [Fact]
    public void ValidationAndPublicationCheckoutTheSameImmutableSha()
    {
        foreach (var file in new[] { "ci.yml", "pages.yml" })
        {
            var workflow = ReadWorkflow(file);
            Assert.Contains(
                "with:\n          ref: ${{ github.sha }}\n          persist-credentials: false",
                workflow);
            Assert.DoesNotContain("ref: main", workflow);
            Assert.DoesNotContain("secrets: inherit", workflow);
        }
    }

    [Fact]
    public void OnlyPublicationReceivesPagesWriteAndOidcPermissions()
    {
        var ci = ReadWorkflow("ci.yml");
        var pages = ReadWorkflow("pages.yml");

        Assert.Contains("permissions:\n  contents: read\n", ci);
        Assert.Contains("permissions:\n  contents: read\n", pages);
        Assert.DoesNotContain("pages: write", Job(ci, "validate"));
        Assert.DoesNotContain("id-token: write", Job(ci, "validate"));
        Assert.Contains("pages: write", Job(ci, "pages"));
        Assert.Contains("id-token: write", Job(ci, "pages"));
        Assert.Contains("pages: read", Job(pages, "build"));
        Assert.DoesNotContain("pages: write", Job(pages, "build"));
        Assert.DoesNotContain("id-token: write", Job(pages, "build"));
        Assert.Contains("pages: write", Job(pages, "deploy"));
        Assert.Contains("id-token: write", Job(pages, "deploy"));
        Assert.DoesNotContain("contents: write", ci + pages);
    }

    [Fact]
    public void ManualPublicationRunsValidationAndDoesNotCancelMainDeployments()
    {
        var ci = ReadWorkflow("ci.yml");
        var pages = ReadWorkflow("pages.yml");

        Assert.Contains("  workflow_dispatch:\n", ci);
        Assert.Contains("group: ci-${{ github.ref }}", ci);
        Assert.Contains("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", ci);
        Assert.Contains("group: pages\n  cancel-in-progress: false", pages);
        Assert.Contains("name: github-pages", Job(pages, "deploy"));
    }

    private const string MainPublicationCondition =
        "github.ref == 'refs/heads/main' &&\n" +
        "      (github.event_name == 'push' || github.event_name == 'workflow_dispatch')";

    private static string ReadWorkflow(string file) =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Find(), ".github", "workflows", file))
            .ReplaceLineEndings("\n");

    private static string Job(string workflow, string id)
    {
        var marker = $"\n  {id}:\n";
        var start = workflow.IndexOf(marker, workflow.IndexOf("\njobs:\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing workflow job: {id}");
        var remainder = workflow[(start + marker.Length)..];
        var nextJob = NextJobPattern().Match(remainder);
        return nextJob.Success ? remainder[..nextJob.Index] : remainder;
    }

    [GeneratedRegex(@"^  [a-zA-Z][a-zA-Z0-9_-]*:\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NextJobPattern();

    [GeneratedRegex(@"uses:\s+[^@\s]+@v\d", RegexOptions.CultureInvariant)]
    private static partial Regex FloatingActionVersionPattern();
}
