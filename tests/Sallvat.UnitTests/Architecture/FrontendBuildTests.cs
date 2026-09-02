using System.Text.Json;

namespace Sallvat.UnitTests.Architecture;

public sealed class FrontendBuildTests
{
    [Fact]
    public void TailwindDependenciesSourcesAndAssetAreVersioned()
    {
        var repositoryRoot = RepositoryRoot.Find();
        using var packageJson = ReadJson(
            Path.Combine(repositoryRoot, "package.json"));
        var developmentDependencies = packageJson.RootElement
            .GetProperty("devDependencies");
        var inputCss = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Sallvat.Web",
            "Styles",
            "app.css"));
        var outputCss = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Sallvat.Web",
            "wwwroot",
            "css",
            "app.css"));

        Assert.Equal(
            "4.3.3",
            developmentDependencies.GetProperty("tailwindcss").GetString());
        Assert.Equal(
            "4.3.3",
            developmentDependencies.GetProperty("@tailwindcss/cli").GetString());
        Assert.Contains("source(none)", inputCss, StringComparison.Ordinal);
        Assert.Contains("../Views/**/*.cshtml", inputCss, StringComparison.Ordinal);
        Assert.Contains("tailwindcss v4.3.3", outputCss, StringComparison.Ordinal);
        Assert.Contains(".sr-only", outputCss, StringComparison.Ordinal);
    }

    private static JsonDocument ReadJson(string path)
    {
        using var stream = File.OpenRead(path);

        return JsonDocument.Parse(stream);
    }
}
