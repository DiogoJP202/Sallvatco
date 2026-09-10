using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sallvat.Application.Shipping;
using Sallvat.Application.Time;
using Sallvat.Infrastructure;
using Sallvat.Infrastructure.Shipping;

namespace Sallvat.IntegrationTests.Shipping;

public sealed class MelhorEnvioFreightServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendsOfficialProductPayloadAndCachesSuccessfulQuote()
    {
        using var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            [
              {
                "id": 2,
                "name": "SEDEX",
                "custom_price": "31.40",
                "custom_delivery_range": { "min": 2, "max": 4 },
                "company": { "name": "Correios" }
              },
              {
                "id": 3,
                "name": "Serviço indisponível",
                "error": "indisponível",
                "company": { "name": "Transportadora" }
              }
            ]
            """));
        using var client = Client(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Service(client, cache);

        var first = await service.QuoteAsync(Request());
        var cached = await service.QuoteAsync(Request());

        Assert.True(first.Succeeded);
        Assert.Same(first, cached);
        var option = Assert.Single(first.Options);
        Assert.Equal("melhor-envio:2", option.QuoteId);
        Assert.Equal("Correios", option.Carrier);
        Assert.Equal("SEDEX", option.Service);
        Assert.Equal(31.40m, option.Price);
        Assert.Equal(2, option.MinimumBusinessDays);
        Assert.Equal(4, option.MaximumBusinessDays);
        Assert.Equal(Now.AddMinutes(10), option.ExpiresAtUtc);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("/api/v2/me/shipment/calculate", handler.Path);
        Assert.Equal("Bearer test-access-token", handler.Authorization);

        using var payload = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var root = payload.RootElement;
        Assert.Equal(
            "01310100",
            root.GetProperty("from").GetProperty("postal_code").GetString());
        Assert.Equal(
            "30140071",
            root.GetProperty("to").GetProperty("postal_code").GetString());
        var product = Assert.Single(
            root.GetProperty("products").EnumerateArray());
        Assert.Equal("42", product.GetProperty("id").GetString());
        Assert.Equal(2, product.GetProperty("quantity").GetInt32());
        Assert.Equal(299.90m, product
            .GetProperty("insurance_value")
            .GetDecimal());
        Assert.False(root.GetProperty("options")
            .GetProperty("receipt")
            .GetBoolean());
        Assert.False(root.GetProperty("options")
            .GetProperty("own_hand")
            .GetBoolean());

        await service.QuoteAsync(Request(), forceRefresh: true);
        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, FreightQuoteStatus.AuthenticationFailure)]
    [InlineData(HttpStatusCode.UnprocessableEntity, FreightQuoteStatus.Invalid)]
    [InlineData(HttpStatusCode.TooManyRequests, FreightQuoteStatus.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, FreightQuoteStatus.Unavailable)]
    public async Task NormalizesProviderFailures(
        HttpStatusCode statusCode,
        FreightQuoteStatus expected)
    {
        using var handler = new RecordingHandler(_ =>
            JsonResponse(statusCode, "{}"));
        using var client = Client(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await Service(client, cache).QuoteAsync(Request());

        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Options);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task InvalidPostalCodeNeverCallsProvider()
    {
        using var handler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "[]"));
        using var client = Client(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await Service(client, cache).QuoteAsync(
            Request() with { DestinationPostalCode = "123" });

        Assert.Equal(FreightQuoteStatus.Invalid, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmptyProviderResultNeverBecomesFreeFreightOrCacheHit()
    {
        using var handler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "[]"));
        using var client = Client(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Service(client, cache);

        var first = await service.QuoteAsync(Request());
        var second = await service.QuoteAsync(Request());

        Assert.Equal(FreightQuoteStatus.Unavailable, first.Status);
        Assert.Equal(FreightQuoteStatus.Unavailable, second.Status);
        Assert.Empty(first.Options);
        Assert.Empty(second.Options);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task TimeoutIsNormalizedWithoutLeakingProviderDetails()
    {
        using var handler = new RecordingHandler(_ =>
            throw new TaskCanceledException("sensitive provider detail"));
        using var client = Client(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await Service(client, cache).QuoteAsync(Request());

        Assert.Equal(FreightQuoteStatus.Unavailable, result.Status);
        Assert.Empty(result.Options);
        Assert.DoesNotContain(
            "sensitive",
            Assert.Single(result.Errors),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisabledIntegrationDoesNotRequireSecrets()
    {
        var options = Options();
        options.Enabled = false;
        options.AccessToken = string.Empty;
        options.SupportEmail = string.Empty;
        options.OriginPostalCode = string.Empty;

        Assert.True(MelhorEnvioOptions.IsValid(options));
        options.Enabled = true;
        Assert.False(MelhorEnvioOptions.IsValid(options));
    }

    [Fact]
    public void RegisteredClientAppliesBaseUrlTimeoutAndRequiredHeaders()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SallvatDatabase"] =
                "Host=127.0.0.1;Database=sallvat;Username=test;Password=test",
            ["Shipping:MelhorEnvio:Enabled"] = "true",
            ["Shipping:MelhorEnvio:BaseUrl"] =
                "https://sandbox.melhorenvio.com.br/",
            ["Shipping:MelhorEnvio:OriginPostalCode"] = "01310100",
            ["Shipping:MelhorEnvio:AccessToken"] = "test-token",
            ["Shipping:MelhorEnvio:ApplicationName"] = "Sallvat",
            ["Shipping:MelhorEnvio:SupportEmail"] =
                "suporte@example.invalid",
            ["Shipping:MelhorEnvio:TimeoutSeconds"] = "7",
            ["Shipping:MelhorEnvio:CacheSeconds"] = "60",
            ["Shipping:MelhorEnvio:QuoteValiditySeconds"] = "600",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        using var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IFreightService));

        Assert.Equal(
            new Uri("https://sandbox.melhorenvio.com.br/"),
            client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
        Assert.Contains(
            "application/json",
            client.DefaultRequestHeaders.Accept.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "Sallvat (suporte@example.invalid)",
            client.DefaultRequestHeaders.UserAgent.ToString());
    }

    private static MelhorEnvioFreightService Service(
        HttpClient client,
        IMemoryCache cache) =>
        new(
            client,
            Microsoft.Extensions.Options.Options.Create(Options()),
            cache,
            new FixedClock(Now),
            NullLogger<MelhorEnvioFreightService>.Instance);

    private static HttpClient Client(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sandbox.melhorenvio.com.br/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Sallvat (suporte@example.invalid)");
        return client;
    }

    private static MelhorEnvioOptions Options() => new()
    {
        Enabled = true,
        OriginPostalCode = "01310-100",
        AccessToken = "test-access-token",
        SupportEmail = "suporte@example.invalid",
        CacheSeconds = 120,
        QuoteValiditySeconds = 600,
    };

    private static FreightQuoteRequest Request() => new(
        "30140-071",
        [
            new FreightQuoteItem(
                "42",
                2,
                299.90m,
                "BRL",
                0.4m,
                12m,
                8m,
                8m),
        ]);

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string content) => new(statusCode)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string? Path { get; private set; }

        public string? Authorization { get; private set; }

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(
                    cancellationToken));
            }

            return responseFactory(request);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
