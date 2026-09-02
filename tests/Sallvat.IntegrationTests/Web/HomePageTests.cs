using System.Net;

namespace Sallvat.IntegrationTests.Web;

public sealed class HomePageTests
{
    [Fact]
    public async Task HomeRendersAccessibleTechnicalPlaceholder()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"pt-BR\"", content, StringComparison.Ordinal);
        Assert.Contains("href=\"#conteudo\"", content, StringComparison.Ordinal);
        Assert.Contains("<main id=\"conteudo\"", content, StringComparison.Ordinal);
        Assert.Contains("/css/app.css?v=", content, StringComparison.Ordinal);
        Assert.Contains("Nenhuma compra pode ser realizada", content, StringComparison.Ordinal);
    }
}
