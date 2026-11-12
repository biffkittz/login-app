using LoginApp.Api.Features.Auth;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Infrastructure.Configuration;
using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Infrastructure.Email;
using LoginApp.Api.Infrastructure.Hosting;
using LoginApp.Api.Infrastructure.Networking;
using LoginApp.Api.Infrastructure.Security;
using LoginApp.Api.Infrastructure.Telemetry;
using LoginApp.Api.Infrastructure.Temporal;
using LoginApp.Api.Infrastructure.Tls;

// Composition root. Each concern registers itself through an extension method, so this file reads as an ordered
// table of contents: configuration sources, then services, then the HTTP pipeline, then endpoints.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configuration sources must be complete before anything reads a secret, so Key Vault is attached first.
builder.Configuration.AddAzureKeyVaultIfConfigured(builder.Environment);
bool usesEphemeralSigningKey = builder.Configuration.AddEphemeralSigningKeyInDevelopment(builder.Environment);

builder.Services.AddServerHardening();
builder.Services.AddOriginTls();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddApplicationDatabase();
builder.Services.AddEmail();
builder.Services.AddAuthFeature();
builder.Services.AddGroceryListFeature(builder.Configuration);
builder.Services.AddInternalNetworking();
builder.Services.AddApplicationHealthChecks();
builder.Services.AddApplicationTelemetry(builder.Configuration);
builder.Services.AddTemporal(builder.Configuration);

WebApplication app = builder.Build();

if (usesEphemeralSigningKey)
{
    AuthLog.UsingEphemeralSigningKey(app.Logger);
}

app.LogMetricsExport();
app.LogTemporalConfiguration();

// Middleware order is significant:
// 1. The exception handler wraps everything so any failure becomes a ProblemDetails response without internals.
// 2. In Azure, requests without Cloudflare's client certificate are refused before any other work is done.
// 3. The internal-network check must see the *immediate* peer, so it runs before forwarded headers rewrite it.
// 4. Forwarded headers then restore the client IP and scheme, which HSTS and per-client rate limiting rely on.
// 5. Rate limiting comes after authentication, so the grocery list's limits can be counted per user.
app.UseExceptionHandler();
app.UseClientCertificateRequirement();
app.UseInternalNetworkRestriction();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapApplicationHealthChecks();
app.MapAuthEndpoints();
app.MapGroceryListEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Last, because its fallback route catches every path the endpoints above did not.
app.MapSpaFrontend();

await app.MigrateDatabaseIfEnabledAsync();
await app.RunAsync();
