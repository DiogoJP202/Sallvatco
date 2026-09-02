using System.Xml.Linq;

namespace Sallvat.UnitTests.Architecture;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void ProjectsFollowTheApprovedDependencyGraph()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var expectedReferences = new Dictionary<string, string[]>
        {
            ["src/Sallvat.Domain/Sallvat.Domain.csproj"] = [],
            ["src/Sallvat.Application/Sallvat.Application.csproj"] =
                ["Sallvat.Domain"],
            ["src/Sallvat.Infrastructure/Sallvat.Infrastructure.csproj"] =
                ["Sallvat.Application", "Sallvat.Domain"],
            ["src/Sallvat.Web/Sallvat.Web.csproj"] =
                ["Sallvat.Application", "Sallvat.Infrastructure"],
            ["tests/Sallvat.UnitTests/Sallvat.UnitTests.csproj"] =
                ["Sallvat.Application", "Sallvat.Domain"],
            ["tests/Sallvat.IntegrationTests/Sallvat.IntegrationTests.csproj"] =
                ["Sallvat.Infrastructure", "Sallvat.Web"]
        };

        foreach (var (projectPath, expected) in expectedReferences)
        {
            var actual = ReadProjectReferences(repositoryRoot, projectPath);

            Assert.Equal(expected, actual);
        }
    }

    private static string[] ReadProjectReferences(
        string repositoryRoot,
        string projectPath)
    {
        var fullPath = Path.Combine(repositoryRoot, projectPath);
        var document = XDocument.Load(fullPath);
        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!);

        Assert.All(
            projectReferences,
            path => Assert.DoesNotContain('\\', path));

        return projectReferences
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
