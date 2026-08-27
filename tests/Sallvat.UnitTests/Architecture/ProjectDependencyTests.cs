using System.Xml.Linq;

namespace Sallvat.UnitTests.Architecture;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void ProjectsFollowTheApprovedDependencyGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sallvat.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root containing Sallvat.sln.");
    }

    private static string[] ReadProjectReferences(
        string repositoryRoot,
        string projectPath)
    {
        var fullPath = Path.Combine(repositoryRoot, projectPath);
        var document = XDocument.Load(fullPath);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
