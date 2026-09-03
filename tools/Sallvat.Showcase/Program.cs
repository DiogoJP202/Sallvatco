using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sallvat.Application.Catalog;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Showcase;

internal static partial class Program
{
    private static readonly IReadOnlyList<ShowcaseProduct> Products =
    [
        new(
            new ProductEditorInput(
                "Sea Salt",
                "sea-salt",
                "Um frescor mineral inspirado pelo encontro entre sal, pele e brisa.",
                "Bergamota luminosa e um acorde marinho abrem caminho para folhas verdes, madeiras claras e um fundo confortável de almíscar.",
                "Fresco aquático",
                "Bergamota e acorde marinho",
                "Sálvia e folhas verdes",
                "Madeiras claras e almíscar",
                "Eau de parfum",
                "Moderada",
                "8 horas",
                "Dias leves e encontros ao ar livre",
                "Primavera e verão",
                "Diurno"),
            true,
            [
                new("SAL-SEA-050", 50, 299.90m, 12),
                new("SAL-SEA-100", 100, 449.90m, 7),
            ],
            [
                new("sea-salt.png", "Frasco Sea Salt da Sallvat & Co. em composição editorial"),
                new("sea-salt-atmosphere.png", "Cristais de sal sobre pedra clara diante do mar"),
            ]),
        new(
            new ProductEditorInput(
                "Hibernum",
                "hibernum",
                "Calor envolvente, texturas gourmand e uma presença que permanece.",
                "Especiarias luminosas encontram baunilha, caramelo e fava-tonka em uma composição âmbar de evolução macia.",
                "Âmbar gourmand",
                "Laranja amarga e cardamomo",
                "Baunilha e caramelo",
                "Fava-tonka e âmbar",
                "Eau de parfum",
                "Intensa",
                "10 horas",
                "Noites e ocasiões especiais",
                "Outono e inverno",
                "Noturno"),
            true,
            [
                new("SAL-HIB-050", 50, 299.90m, 6),
                new("SAL-HIB-100", 100, 449.90m, 0),
            ],
            [
                new("hibernum.png", "Frasco Hibernum da Sallvat & Co. entre notas quentes"),
                new("hibernum-atmosphere.png", "Baunilha, fava-tonka e resina âmbar sob luz quente"),
            ]),
        new(
            new ProductEditorInput(
                "Corium",
                "corium",
                "Madeiras, couro e luz em uma assinatura elegante e segura.",
                "Uma abertura aromática revela couro macio, cedro e madeiras secas, equilibrados por um fundo de âmbar discreto.",
                "Amadeirado",
                "Bergamota e ervas aromáticas",
                "Couro macio e cedro",
                "Madeiras secas e âmbar",
                "Eau de parfum",
                "Moderada",
                "9 horas",
                "Encontros e momentos de presença",
                "Todas as estações",
                "Versátil"),
            true,
            [
                new("SAL-COR-050", 50, 299.90m, 8),
                new("SAL-COR-100", 100, 449.90m, 3),
            ],
            [
                new("corium.png", "Frasco Corium da Sallvat & Co. diante de uma paisagem costeira"),
                new("corium-atmosphere.png", "Couro, cedro e pedra em composição de luz lateral"),
            ]),
        new(
            new ProductEditorInput(
                "Cumiere",
                "cumiere",
                "Claridade cítrica e madeiras suaves para uma presença luminosa.",
                "Bergamota e petitgrain conduzem a uma textura verde, apoiada por cedro claro e um acorde âmbar delicado.",
                "Cítrico amadeirado",
                "Bergamota e petitgrain",
                "Folhas verdes e néroli",
                "Cedro claro e âmbar",
                "Eau de parfum",
                "Moderada",
                "8 horas",
                "Rotina, viagens e encontros casuais",
                "Primavera e verão",
                "Diurno"),
            false,
            [
                new("SAL-CUM-050", 50, 299.90m, 4),
                new("SAL-CUM-100", 100, 449.90m, 2),
            ],
            [
                new("cumiere.png", "Frasco Cumiere da Sallvat & Co. sob luz natural"),
                new("cumiere-atmosphere.png", "Bergamota, folhas verdes e madeira sob luz dourada"),
            ]),
    ];

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ExportOptions.Parse(args);
            await ExportAsync(options);
            Console.WriteLine(
                $"Demonstração exportada para {options.OutputDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task ExportAsync(ExportOptions options)
    {
        EnsureEmptyOutput(options.OutputDirectory);
        await using var application = new ShowcaseApplicationFactory();
        await InitializeDatabaseAsync(application);
        await SeedCatalogAsync(application, options.RepositoryRoot);

        CopyDirectory(
            Path.Combine(
                options.RepositoryRoot,
                "src",
                "Sallvat.Web",
                "wwwroot"),
            options.OutputDirectory);
        CopyDirectory(
            application.ImageStoragePath,
            Path.Combine(options.OutputDirectory, "media"));

        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://showcase.sallvat.invalid"),
            });
        await ExportPageAsync(client, "/", "index.html", options);
        await ExportPageAsync(
            client,
            "/perfumes",
            Path.Combine("perfumes", "index.html"),
            options);
        foreach (var product in Products)
        {
            await ExportPageAsync(
                client,
                $"/perfumes/{product.Product.Slug}",
                Path.Combine(
                    "perfumes",
                    product.Product.Slug,
                    "index.html"),
                options);
        }

        var notice = BuildDemonstrationNotice(options.BasePath);
        await WriteTextAsync(
            Path.Combine(
                options.OutputDirectory,
                "demonstracao",
                "index.html"),
            notice);
        await WriteTextAsync(
            Path.Combine(options.OutputDirectory, "404.html"),
            notice);
        await WriteTextAsync(
            Path.Combine(options.OutputDirectory, ".nojekyll"),
            string.Empty);
        await WriteTextAsync(
            Path.Combine(options.OutputDirectory, "robots.txt"),
            "User-agent: *\nDisallow: /\n");
    }

    private static async Task InitializeDatabaseAsync(
        ShowcaseApplicationFactory application)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    private static async Task SeedCatalogAsync(
        ShowcaseApplicationFactory application,
        string repositoryRoot)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var actorId = Guid.NewGuid();
        var email = $"showcase-{actorId:N}@example.invalid";
        context.Users.Add(new ApplicationUser
        {
            Id = actorId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
        });
        await context.SaveChangesAsync();

        var operation = new AdminOperationContext(actorId, "pages-showcase");
        var assetRoot = Path.Combine(
            repositoryRoot,
            "tools",
            "Sallvat.Showcase",
            "Assets",
            "products");

        foreach (var showcaseProduct in Products)
        {
            var created = await service.CreateProductAsync(
                showcaseProduct.Product,
                operation);
            var productId = RequireEntity(
                created,
                $"criar o produto {showcaseProduct.Product.Name}");

            foreach (var showcaseVariant in showcaseProduct.Variants)
            {
                var variant = await service.AddVariantAsync(
                    productId,
                    new VariantEditorInput(
                        showcaseVariant.Sku,
                        showcaseVariant.VolumeMl,
                        showcaseVariant.Price,
                        showcaseVariant.VolumeMl == 50 ? 0.4m : 0.7m,
                        showcaseVariant.VolumeMl == 50 ? 12m : 15m,
                        showcaseVariant.VolumeMl == 50 ? 8m : 10m,
                        showcaseVariant.VolumeMl == 50 ? 8m : 10m,
                        true),
                    operation);
                var variantId = RequireEntity(
                    variant,
                    $"adicionar a variante {showcaseVariant.Sku}");

                if (showcaseVariant.Stock > 0)
                {
                    var product = await RequireProductAsync(service, productId);
                    var adminVariant = product.Variants.Single(
                        item => item.Id == variantId);
                    RequireSuccess(
                        await service.AdjustStockAsync(
                            productId,
                            variantId,
                            adminVariant.ConcurrencyVersion,
                            showcaseVariant.Stock,
                            "Estoque da demonstração estática",
                            operation),
                        $"registrar o estoque de {showcaseVariant.Sku}");
                }
            }

            foreach (var image in showcaseProduct.Images)
            {
                await AddImageAsync(
                    service,
                    productId,
                    Path.Combine(assetRoot, image.FileName),
                    image.AltText,
                    operation);
            }

            var current = await RequireProductAsync(service, productId);
            RequireSuccess(
                await service.PublishAsync(
                    productId,
                    current.ConcurrencyVersion,
                    operation),
                $"publicar o produto {showcaseProduct.Product.Name}");

            if (showcaseProduct.Featured)
            {
                current = await RequireProductAsync(service, productId);
                RequireSuccess(
                    await service.SetFeaturedAsync(
                        productId,
                        current.ConcurrencyVersion,
                        true,
                        operation),
                    $"destacar o produto {showcaseProduct.Product.Name}");
            }
        }
    }

    private static async Task AddImageAsync(
        ICatalogService service,
        long productId,
        string path,
        string altText,
        AdminOperationContext operation)
    {
        var product = await RequireProductAsync(service, productId);
        await using var content = File.OpenRead(path);
        RequireSuccess(
            await service.AddImageAsync(
                productId,
                product.ConcurrencyVersion,
                new ProductImageUpload(
                    content,
                    content.Length,
                    Path.GetFileName(path)),
                altText,
                operation),
            $"adicionar a imagem {Path.GetFileName(path)}");
    }

    private static async Task<AdminProductDetails> RequireProductAsync(
        ICatalogService service,
        long productId) =>
        await service.GetAdminAsync(productId)
        ?? throw new InvalidOperationException(
            "O produto demonstrativo não foi encontrado.");

    private static long RequireEntity(
        CatalogMutationResult result,
        string operation)
    {
        RequireSuccess(result, operation);
        return result.EntityId
            ?? throw new InvalidOperationException(
                $"A operação não retornou uma entidade ao {operation}.");
    }

    private static void RequireSuccess(
        CatalogMutationResult result,
        string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Não foi possível {operation}: {string.Join(", ", result.Errors)}");
        }
    }

    private static async Task ExportPageAsync(
        HttpClient client,
        string requestPath,
        string outputPath,
        ExportOptions options)
    {
        using var response = await client.GetAsync(requestPath);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"A rota {requestPath} respondeu {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync();
        html = RewriteForPages(html, options);
        await WriteTextAsync(
            Path.Combine(options.OutputDirectory, outputPath),
            html);
    }

    private static string RewriteForPages(
        string html,
        ExportOptions options)
    {
        html = html.Replace(
            "<body class=\"",
            "<body data-showcase=\"true\" class=\"",
            StringComparison.Ordinal);
        html = html.Replace(
            "https://showcase.sallvat.invalid",
            options.SiteUrl,
            StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(options.BasePath))
        {
            html = RootRelativeAttributePattern().Replace(
                html,
                $"=\"{options.BasePath}/");
            html = SrcSetContinuationPattern().Replace(
                html,
                $", {options.BasePath}/");
        }

        var noticePath = $"{options.BasePath}/demonstracao/";
        html = html.Replace(
            $"href=\"{options.BasePath}/conta/entrar\"",
            $"href=\"{noticePath}\"",
            StringComparison.Ordinal);
        html = html.Replace(
            $"href=\"{options.BasePath}/conta/criar\"",
            $"href=\"{noticePath}\"",
            StringComparison.Ordinal);
        return html.Replace(
            "A coleção Sallvat &amp; Co. está ganhando forma",
            "Apresentação visual · produtos, preços e textos sujeitos à validação",
            StringComparison.Ordinal);
    }

    private static string BuildDemonstrationNotice(string basePath)
    {
        var rootPath = $"{basePath}/";
        return $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <meta name="robots" content="noindex,nofollow" />
                <title>Demonstração | Sallvat &amp; Co.</title>
                <link rel="icon" href="{{rootPath}}favicon.svg" type="image/svg+xml" />
                <link rel="stylesheet" href="{{rootPath}}css/app.css" />
            </head>
            <body class="min-h-screen bg-paper text-ink antialiased">
                <main class="flex min-h-screen items-center justify-center px-5 py-16">
                    <section class="w-full max-w-2xl rounded-[2.5rem] border border-stone-300 bg-white p-8 text-center shadow-xl shadow-stone-900/10 sm:p-14">
                        <p class="eyebrow">Demonstração visual</p>
                        <h1 class="mt-5 font-serif text-5xl leading-none tracking-tight sm:text-6xl">A experiência completa está a caminho.</h1>
                        <p class="mx-auto mt-7 max-w-lg text-base leading-8 text-stone-600">Esta publicação apresenta a identidade visual, o catálogo e os produtos. Cadastro, login, carrinho e compras serão habilitados somente no ambiente seguro da aplicação.</p>
                        <a class="btn-primary mt-9" href="{{rootPath}}">Voltar para a coleção</a>
                    </section>
                </main>
            </body>
            </html>
            """;
    }

    private static void EnsureEmptyOutput(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory)
            && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new InvalidOperationException(
                "O diretório de saída precisa estar vazio.");
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(source, sourcePath);
            var destinationPath = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static async Task WriteTextAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    [GeneratedRegex("=\"/(?!/)", RegexOptions.CultureInvariant)]
    private static partial Regex RootRelativeAttributePattern();

    [GeneratedRegex(@", /(?!/)", RegexOptions.CultureInvariant)]
    private static partial Regex SrcSetContinuationPattern();
}

internal sealed record ShowcaseProduct(
    ProductEditorInput Product,
    bool Featured,
    IReadOnlyList<ShowcaseVariant> Variants,
    IReadOnlyList<ShowcaseImage> Images);

internal sealed record ShowcaseVariant(
    string Sku,
    int VolumeMl,
    decimal Price,
    int Stock);

internal sealed record ShowcaseImage(string FileName, string AltText);

internal sealed record ExportOptions(
    string RepositoryRoot,
    string OutputDirectory,
    string SiteUrl,
    string BasePath)
{
    public static ExportOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length
                || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Use --output <diretório> --site-url <URL>.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        if (!values.TryGetValue("output", out var output)
            || string.IsNullOrWhiteSpace(output)
            || !values.TryGetValue("site-url", out var siteUrl)
            || !Uri.TryCreate(siteUrl, UriKind.Absolute, out var siteUri)
            || siteUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(siteUri.Query)
            || !string.IsNullOrEmpty(siteUri.Fragment))
        {
            throw new ArgumentException(
                "Use --output <diretório> --site-url <URL HTTP ou HTTPS>.");
        }

        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.GetFullPath(output);
        if (string.Equals(
                outputDirectory.TrimEnd(Path.DirectorySeparatorChar),
                repositoryRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "O diretório de saída não pode ser a raiz do repositório.");
        }

        var normalizedSiteUrl = siteUri.GetLeftPart(UriPartial.Path)
            .TrimEnd('/');
        var basePath = siteUri.AbsolutePath.TrimEnd('/');
        if (basePath == "/")
        {
            basePath = string.Empty;
        }

        return new ExportOptions(
            repositoryRoot,
            outputDirectory,
            normalizedSiteUrl,
            basePath);
    }

    internal static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Sallvat.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Execute o exportador dentro do repositório Sallvatco.");
    }
}

internal sealed class ShowcaseApplicationFactory :
    WebApplicationFactory<global::Program>
{
    private readonly string databaseName = $"sallvat-showcase-{Guid.NewGuid():N}";

    public ShowcaseApplicationFactory()
    {
        DataProtectionKeysPath = Path.Combine(
            Path.GetTempPath(),
            "Sallvat.Showcase.Keys",
            Guid.NewGuid().ToString("N"));
        ImageStoragePath = Path.Combine(
            Path.GetTempPath(),
            "Sallvat.Showcase.Images",
            Guid.NewGuid().ToString("N"));
    }

    public string DataProtectionKeysPath { get; }

    public string ImageStoragePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(
            ExportOptions.FindRepositoryRoot(),
            "src",
            "Sallvat.Web"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SallvatDatabase"] =
                        "Host=127.0.0.1;Port=1;Database=sallvat;" +
                        "Username=sallvat;Password=showcase;Timeout=1",
                    ["Operational:ServiceName"] = "Sallvat.Showcase",
                    ["Operational:CorrelationIdMaxLength"] = "64",
                    ["DataProtection:KeysPath"] = DataProtectionKeysPath,
                    ["ImageStorage:RootPath"] = ImageStoragePath,
                    ["AccountLinks:PublicOrigin"] =
                        "https://showcase.sallvat.invalid",
                });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<SallvatDbContext>>();
            services.RemoveAll<
                IDbContextOptionsConfiguration<SallvatDbContext>>();
            services.RemoveAll<SallvatDbContext>();
            services.AddDbContext<SallvatDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteOwnedTemporaryDirectory(DataProtectionKeysPath);
        DeleteOwnedTemporaryDirectory(ImageStoragePath);
        GC.SuppressFinalize(this);
    }

    private static void DeleteOwnedTemporaryDirectory(string path)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(
                temporaryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "O diretório temporário está fora da raiz esperada.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
