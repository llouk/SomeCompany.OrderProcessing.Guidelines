using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SomeCompany.OrderProcessing.Inventory;
using SomeCompany.OrderProcessing.Ordering;
using SomeCompany.OrderProcessing.Payments;
using SomeCompany.OrderProcessing.Pricing;
using SomeCompany.OrderProcessing.Shipping;

// Composition root: the only place that knows about every module.
var builder = WebApplication.CreateBuilder(args);

// Cross-cutting concerns
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// Security: JWT bearer tokens from the company identity provider (configured under Authentication:Schemes:Bearer).
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

// Observability: traces, metrics and logs via OpenTelemetry. Exported over OTLP when an endpoint is configured.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("order-processing"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithLogging();

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    otel.UseOtlpExporter();

// Modules
builder.Services
    .AddOrderingModule(builder.Configuration)
    .AddPricingModule()
    .AddInventoryModule(builder.Configuration)
    .AddPaymentsModule()
    .AddShippingModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Liveness: the process responds. Readiness: dependencies (Redis) are reachable, so the instance may take traffic.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapOrderingModule();

app.Run();
