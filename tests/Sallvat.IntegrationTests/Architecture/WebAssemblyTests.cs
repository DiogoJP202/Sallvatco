namespace Sallvat.IntegrationTests.Architecture;

public sealed class WebAssemblyTests
{
    [Fact]
    public void WebApplicationAssemblyIsAvailable()
    {
        var assemblyName = typeof(global::Program).Assembly.GetName().Name;

        Assert.Equal("Sallvat.Web", assemblyName);
    }
}
