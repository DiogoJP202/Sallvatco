using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sallvat.UnitTests.Architecture;

public sealed partial class ContainerBuildTests
{
    [Fact]
    public void BuildSeparatesFrontendSdkAndRuntimeWithPinnedBases()
    {
        var dockerfile = Read("Dockerfile");
        Assert.Equal(3, PinnedBasePattern().Count(dockerfile));
        using var sdk = JsonDocument.Parse(Read("global.json"));
        Assert.Contains($"sdk:{sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString()}-noble@sha256:", dockerfile);
        Assert.Contains($"node:{Read(".nvmrc").Trim()}-bookworm-slim@sha256:", dockerfile);
        Assert.Contains("npm ci --ignore-scripts", dockerfile);
        Assert.Contains("dotnet restore src/Sallvat.Web/Sallvat.Web.csproj --locked-mode", dockerfile);
        Assert.Contains("COPY --from=frontend", dockerfile);
        Assert.Contains("COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./", dockerfile);
        Assert.Contains("-p:PrebuiltTailwind=true", dockerfile);
        Assert.Contains("COPY --from=build /out/ ./", dockerfile);
    }

    [Fact]
    public void RuntimeRunsNonRootWithoutBuildToolsOrEmbeddedSecrets()
    {
        var dockerfile = Read("Dockerfile");
        var runtime = dockerfile[dockerfile.LastIndexOf("FROM ", StringComparison.Ordinal)..];
        Assert.Contains("USER $APP_UID", runtime);
        Assert.Contains("EXPOSE 8080", runtime);
        Assert.Contains("chmod 700 /var/lib/sallvat/keys /var/lib/sallvat/images", runtime);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Sallvat.Web.dll\"]", runtime);
        Assert.DoesNotContain("npm", runtime);
        Assert.DoesNotContain("sdk:", runtime);
        Assert.DoesNotContain("ConnectionStrings", dockerfile);
        Assert.DoesNotContain("AccessToken", dockerfile);
        Assert.DoesNotContain("database update", dockerfile);
    }

    [Fact]
    public void BuildContextIsAllowlistedAndExcludesLocalDataAndCredentials()
    {
        var ignore = Read(".dockerignore");
        Assert.StartsWith("# Allowlist:", ignore, StringComparison.Ordinal);
        Assert.Contains("\n**\n", ignore);
        Assert.Contains("!src/\n!src/**", ignore);
        Assert.Contains("!.editorconfig\n", ignore);
        foreach (var excluded in new[]
        {
            "bin", "obj", "node_modules", ".local", ".env", ".env.*",
            "appsettings.*.local.json", "appsettings.Development.json",
            "*.pfx", "*.p12", "*.pem", "*.key", "secrets.json",
        })
        {
            Assert.Contains($"**/{excluded}\n", ignore);
        }

        Assert.DoesNotContain("!.git", ignore);
        Assert.DoesNotContain("!.local", ignore);
    }

    [Fact]
    public void PrebuiltCssIsExplicitAndStillRequiresAnAsset()
    {
        var project = XDocument.Parse(Read("src/Sallvat.Web/Sallvat.Web.csproj"));
        var targets = project.Root!.Elements("Target").ToArray();
        var build = Assert.Single(targets, item => item.Attribute("Name")?.Value == "BuildTailwindCss");
        Assert.Contains("'$(PrebuiltTailwind)' != 'true'", build.Attribute("Condition")!.Value);
        var validate = Assert.Single(targets, item => item.Attribute("Name")?.Value == "ValidatePrebuiltTailwindCss");
        Assert.Equal("'$(PrebuiltTailwind)' == 'true'", validate.Attribute("Condition")!.Value);
        Assert.Equal("PrepareForBuild", validate.Attribute("BeforeTargets")!.Value);
        Assert.Contains("!Exists(", Assert.Single(validate.Elements("Error")).Attribute("Condition")!.Value);
    }

    [Fact]
    public void ContainerSmokeIsRequiredInValidationWithoutRegistryWrite()
    {
        var ci = Read(".github/workflows/ci.yml");
        var validate = ci[..ci.IndexOf("\n  pages:\n", StringComparison.Ordinal)];
        Assert.Contains("docker build --pull", validate);
        Assert.Contains("org.opencontainers.image.revision=${GITHUB_SHA}", validate);
        Assert.Contains("bash tools/smoke-container.sh", validate);
        Assert.DoesNotContain("continue-on-error", ci);
        Assert.DoesNotContain("packages: write", ci);
        Assert.DoesNotContain("docker push", ci);
        var smoke = Read("tools/smoke-container.sh");
        Assert.Contains("--read-only --cap-drop ALL --security-opt no-new-privileges:true", smoke);
        Assert.Contains("network create --internal", smoke);
        Assert.DoesNotContain("--publish", smoke);
        Assert.Contains("docker inspect --format", smoke);
        Assert.Contains("--header 'Host: 127.0.0.1'", smoke);
        Assert.Contains("expect_status /health/ready 503", smoke);
        Assert.Contains("expect_status /health/ready 200", smoke);
        Assert.Contains("[[ \"$task_tables\" == 0 ]]", smoke);
        Assert.Contains("cmp \"$task_dir/keys-before\" \"$task_dir/keys-after\"", smoke);
    }

    private static string Read(string path) =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Find(), path)).ReplaceLineEndings("\n");

    [GeneratedRegex(@"^FROM \S+@sha256:[a-f0-9]{64} AS (frontend|build|runtime)$", RegexOptions.Multiline)]
    private static partial Regex PinnedBasePattern();
}
