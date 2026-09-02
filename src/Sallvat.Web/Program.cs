using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Sallvat.Application.Authorization;
using Sallvat.Infrastructure;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Web.Configuration;
using Sallvat.Web.Observability;
using Sallvat.Web.Security;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
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
app.UseRouting();
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

public partial class Program
{
}
