using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Sallvat.Application.Accounts;
using Sallvat.Application.Authorization;
using Sallvat.Infrastructure;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Storage;
using Sallvat.Web.Configuration;
using Sallvat.Web.Email;
using Sallvat.Web.Observability;
using Sallvat.Web.Security;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var culture = CultureInfo.GetCultureInfo("pt-BR");
    options.DefaultRequestCulture = new(culture);
    options.SupportedCultures = [culture];
    options.SupportedUICultures = [culture];
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] =
            context.HttpContext.TraceIdentifier;
});
builder.Services
    .AddOptions<OperationalOptions>()
    .Bind(builder.Configuration.GetSection(OperationalOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ServiceName),
        $"{OperationalOptions.SectionName}:ServiceName is required.")
    .Validate(
        options => options.CorrelationIdMaxLength is >= 16 and <= 128,
        $"{OperationalOptions.SectionName}:CorrelationIdMaxLength must be between 16 and 128.")
    .ValidateOnStart();
builder.Services
    .AddOptions<DataProtectionStorageOptions>()
    .Bind(builder.Configuration.GetSection(
        DataProtectionStorageOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrWhiteSpace(options.KeysPath)
            && !Path.IsPathFullyQualified(options.KeysPath))
        {
            options.KeysPath = Path.GetFullPath(
                options.KeysPath,
                builder.Environment.ContentRootPath);
        }
    })
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.KeysPath),
        $"{DataProtectionStorageOptions.SectionName}:KeysPath is required.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.KeysPath)
            || Path.IsPathFullyQualified(options.KeysPath),
        $"{DataProtectionStorageOptions.SectionName}:KeysPath must resolve to an absolute path.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.KeysPath)
            || DataProtectionPath.IsOutsideDirectory(
                options.KeysPath,
                builder.Environment.WebRootPath
                    ?? Path.Combine(
                        builder.Environment.ContentRootPath,
                        "wwwroot")),
        $"{DataProtectionStorageOptions.SectionName}:KeysPath must be outside the web root.")
    .ValidateOnStart();
builder.Services
    .AddOptions<AccountLinkOptions>()
    .Bind(builder.Configuration.GetSection(AccountLinkOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(
            options.PublicOrigin,
            UriKind.Absolute,
            out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps
                || ((builder.Environment.IsDevelopment()
                        || builder.Environment.IsEnvironment("Testing"))
                    && uri.Scheme == Uri.UriSchemeHttp))
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment),
        $"{AccountLinkOptions.SectionName}:PublicOrigin must be an absolute HTTPS URL outside Development.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ImageStorageOptions>()
    .Bind(builder.Configuration.GetSection(ImageStorageOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrWhiteSpace(options.RootPath)
            && !Path.IsPathFullyQualified(options.RootPath))
        {
            options.RootPath = Path.GetFullPath(
                options.RootPath,
                builder.Environment.ContentRootPath);
        }
    })
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.RootPath)
            && Path.IsPathFullyQualified(options.RootPath),
        $"{ImageStorageOptions.SectionName}:RootPath must resolve to an absolute path.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.RootPath)
            || DataProtectionPath.IsOutsideDirectory(
                options.RootPath,
                builder.Environment.WebRootPath
                    ?? Path.Combine(
                        builder.Environment.ContentRootPath,
                        "wwwroot")),
        $"{ImageStorageOptions.SectionName}:RootPath must be outside the web root.")
    .Validate(
        options => options.PublicPath.StartsWith('/')
            && options.PublicPath.Length > 1
            && !options.PublicPath.EndsWith('/'),
        $"{ImageStorageOptions.SectionName}:PublicPath must be an absolute request path without a trailing slash.")
    .Validate(
        options => options.MaximumUploadBytes is >= 1_048_576 and <= 20_971_520
            && options.MaximumPixelCount is >= 1_000_000 and <= 50_000_000
            && options.MaximumDimension is >= 1_000 and <= 20_000
            && options.MaximumImagesPerProduct is >= 1 and <= 20,
        $"{ImageStorageOptions.SectionName}:configured limits are invalid.")
    .ValidateOnStart();
builder.Services.Configure<FormOptions>(options =>
{
    options.MemoryBufferThreshold = 64 * 1024;
    options.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});
builder.Services
    .AddDataProtection()
    .SetApplicationName(
        $"Sallvat.Web:{builder.Environment.EnvironmentName}");
builder.Services.AddSingleton<
    IConfigureOptions<KeyManagementOptions>,
    DataProtectionKeyRepositoryConfigurator>();
builder.Services.AddHostedService<DataProtectionKeyRingInitializer>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
    })
    .AddEntityFrameworkStores<SallvatDbContext>()
    .AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(3));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        RoleNames.Admin,
        policy => policy.RequireRole(RoleNames.Admin));
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name =
        $"Sallvat.Auth.{builder.Environment.EnvironmentName}";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/conta/entrar";
    options.AccessDeniedPath = "/conta/acesso-negado";
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRecoveryRequestLimiter, RecoveryRequestLimiter>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy<string>(
        RateLimitPolicyNames.Login,
        context => CreateIpFixedWindowLimiter(
            context,
            permitLimit: 10,
            TimeSpan.FromMinutes(10)));
    options.AddPolicy<string>(
        RateLimitPolicyNames.Registration,
        context => CreateIpFixedWindowLimiter(
            context,
            permitLimit: 5,
            TimeSpan.FromHours(1)));
    options.AddPolicy<string>(
        RateLimitPolicyNames.Recovery,
        context => CreateIpFixedWindowLimiter(
            context,
            permitLimit: 3,
            TimeSpan.FromHours(1)));
});
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<
        IEmailSender,
        DevelopmentFileAccountEmailSender>();
}
else
{
    builder.Services.AddSingleton<
        IEmailSender,
        UnavailableAccountEmailSender>();
}
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [HealthCheckTags.Live])
    .AddDbContextCheck<SallvatDbContext>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: [HealthCheckTags.Ready]);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
builder.Services.AddSingleton<
    IHttpMessageHandlerBuilderFilter,
    CorrelationIdHttpMessageHandlerBuilderFilter>();
builder.Services.AddSerilog((services, configuration) =>
{
    var operationalOptions = services
        .GetRequiredService<IOptions<OperationalOptions>>()
        .Value;

    configuration
        .MinimumLevel.Is(
            builder.Environment.IsDevelopment()
                ? LogEventLevel.Debug
                : LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.With(new SensitiveDataEnricher())
        .Enrich.WithProperty("Service", operationalOptions.ServiceName)
        .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
        .Enrich.WithProperty(
            "Version",
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown")
        .WriteTo.Console(new CompactJsonFormatter());
});

var app = builder.Build();

app.UseCorrelationId();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RouteTemplate} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, exception) =>
    {
        if (exception is not null
            || httpContext.Response.StatusCode >=
                StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (httpContext.Response.StatusCode >=
                StatusCodes.Status400BadRequest
            || elapsed > 30_000)
        {
            return LogEventLevel.Warning;
        }

        return LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var routeTemplate = (httpContext.GetEndpoint() as RouteEndpoint)?
            .RoutePattern.RawText ?? "unmatched";

        diagnosticContext.Set("RouteTemplate", routeTemplate);
        diagnosticContext.Set(
            "TraceId",
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
    };
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/erro");
}

app.UseStaticFiles();
var imageStorageOptions = app.Services
    .GetRequiredService<IOptions<ImageStorageOptions>>()
    .Value;
Directory.CreateDirectory(imageStorageOptions.RootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageStorageOptions.RootPath),
    RequestPath = imageStorageOptions.PublicPath,
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            "public,max-age=31536000,immutable";
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});
app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration =>
            registration.Tags.Contains(HealthCheckTags.Live),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    })
    .AllowAnonymous();
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration =>
            registration.Tags.Contains(HealthCheckTags.Ready),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    })
    .AllowAnonymous();

app.Run();

static RateLimitPartition<string> CreateIpFixedWindowLimiter(
    HttpContext context,
    int permitLimit,
    TimeSpan window)
{
    var partitionKey = context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = permitLimit,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = window,
        });
}

public partial class Program
{
}
