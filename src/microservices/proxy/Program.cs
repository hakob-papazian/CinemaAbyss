using CinemaAbyss.ProxyService.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var port = builder.Configuration["PORT"] ?? "8000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var monolithUrl = builder.Configuration["MONOLITH_URL"] ?? "http://localhost:8080";
var moviesServiceUrl = builder.Configuration["MOVIES_SERVICE_URL"] ?? "http://localhost:8081";
var eventsServiceUrl = builder.Configuration["EVENTS_SERVICE_URL"] ?? "http://localhost:8082";

builder.Services.AddHttpClient(nameof(ProxyForwarder));
builder.Services.AddSingleton<MigrationFlag>();
builder.Services.AddSingleton<ProxyForwarder>();

var app = builder.Build();

var migrationFlag = app.Services.GetRequiredService<MigrationFlag>();
var forwarder = app.Services.GetRequiredService<ProxyForwarder>();

// Single health endpoint for the gateway itself (see api-specification.yaml: text/plain response).
app.MapGet("/health", () => Results.Text("Strangler Fig Proxy is healthy", "text/plain"));

// Strangler Fig: /api/movies is the domain being migrated. Based on the feature flag
// (GRADUAL_MIGRATION) and the rollout percentage (MOVIES_MIGRATION_PERCENT), each request is
// routed either to the new movies-service or to the legacy monolith - transparently to the caller.
app.Map("/api/movies/{**catchAll}", async context =>
{
    var toMicroservice = migrationFlag.ShouldRouteMoviesToMicroservice();
    await forwarder.ForwardAsync(context, toMicroservice ? moviesServiceUrl : monolithUrl,
        toMicroservice ? "movies-service" : "monolith");
});

app.Map("/api/movies", async context =>
{
    var toMicroservice = migrationFlag.ShouldRouteMoviesToMicroservice();
    await forwarder.ForwardAsync(context, toMicroservice ? moviesServiceUrl : monolithUrl,
        toMicroservice ? "movies-service" : "monolith");
});

// Domains not yet extracted from the monolith - always forwarded there.
foreach (var path in new[] { "/api/users", "/api/payments", "/api/subscriptions" })
{
    app.Map(path, async context => await forwarder.ForwardAsync(context, monolithUrl, "monolith"));
    app.Map($"{path}/{{**catchAll}}", async context => await forwarder.ForwardAsync(context, monolithUrl, "monolith"));
}

// Events domain - already extracted, always forwarded to events-service.
app.Map("/api/events/{**catchAll}", async context =>
    await forwarder.ForwardAsync(context, eventsServiceUrl, "events-service"));

app.Logger.LogInformation(
    "Starting Strangler Fig proxy on port {Port}. GRADUAL_MIGRATION={Enabled}, MOVIES_MIGRATION_PERCENT={Percent}",
    port, migrationFlag.GradualMigrationEnabled, migrationFlag.MoviesMigrationPercent);

app.Run();
