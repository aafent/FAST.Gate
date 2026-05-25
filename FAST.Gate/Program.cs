using FAST.Gate.Auth;
using FAST.Gate.Client.Abstractions;
using FAST.Gate.Extensions;
using FAST.Gate.Infrastructure;
using FAST.Gate.Management;
using FAST.Gate.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddFastGate(builder.Configuration);
builder.Services.AddHostedService<GateShutdownService>();

var app = builder.Build();

// ── Initialize identity provider ──────────────────────────────────────────────
var idp = app.Services.GetRequiredService<IIdentityProvider>();
await idp.InitializeAsync();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseMiddleware<CorrelationIdMiddleware>();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapFastGateManagement();
app.MapFastGateAuth();

app.Run();

public partial class Program { }
