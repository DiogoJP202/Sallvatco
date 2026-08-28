namespace Sallvat.UnitTests.Architecture;

public sealed class DevelopmentComposeTests
{
    [Fact]
    public void PostgreSqlServiceKeepsDevelopmentDataAndPortPrivate()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var compose = File.ReadAllText(
            Path.Combine(repositoryRoot, "compose.yaml"));

        Assert.Contains("image: postgres:18.6-alpine3.24", compose);
        Assert.Contains("127.0.0.1:${SALLVAT_POSTGRES_PORT:-5432}:5432", compose);
        Assert.Contains("POSTGRES_PASSWORD: ${SALLVAT_POSTGRES_PASSWORD:?", compose);
        Assert.Contains("postgres-data:/var/lib/postgresql", compose);
        Assert.Contains("PGDATA: /var/lib/postgresql/18/docker", compose);
        Assert.Contains("pg_isready", compose);
        Assert.DoesNotContain("/var/lib/postgresql/data", compose);
    }
}
