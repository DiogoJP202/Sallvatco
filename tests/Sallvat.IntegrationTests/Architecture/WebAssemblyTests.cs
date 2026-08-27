namespace Sallvat.IntegrationTests.Architecture;

public sealed class WebAssemblyTests
{
    [Fact]
    public void Web_application_assembly_is_available()
    {
        var assemblyName = typeof(global::Program).Assembly.GetName().Name;

        Assert.Equal("Sallvat.Web", assemblyName);
    }
}
