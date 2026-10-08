using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.Stats;
using Content.Server.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var validateContentOnly = args.Contains("--validate-content", StringComparer.Ordinal);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(argument => argument != "--validate-content").ToArray(),
    // appsettings files are copied beside the executable, independent of shell cwd.
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services
    .AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= ushort.MaxValue,
        "Server port must be between 1 and 65535.")
    .Validate(options => options.TickRate is >= 1 and <= 120,
        "Server tick rate must be between 1 and 120.")
    .Validate(options => options.NetworkPollIntervalMilliseconds is >= 1 and <= 100,
        "Network poll interval must be between 1 and 100 milliseconds.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionKey),
        "Connection key must not be empty.")
    .ValidateOnStart();

builder.Services
    .AddOptions<MovementOptions>()
    .Bind(builder.Configuration.GetSection(MovementOptions.SectionName))
    .Validate(options => options.Speed > 0f && float.IsFinite(options.Speed),
        "Movement speed must be finite and positive.")
    .Validate(options => options.StopDistance >= 0f && float.IsFinite(options.StopDistance),
        "Stop distance must be finite and non-negative.")
    .Validate(options => options.MinX < options.MaxX && options.MinZ < options.MaxZ,
        "Movement bounds must be ordered.")
    .ValidateOnStart();

builder.Services.AddSingleton<HandshakeCoordinator>();
builder.Services.AddOptions<InterestOptions>()
    .Bind(builder.Configuration.GetSection(InterestOptions.SectionName))
    .Validate(options => options.IsValid(),
        "AOI radii must be finite, positive and ordered; cell query must be bounded.")
    .ValidateOnStart();
builder.Services.AddSingleton<ServerWorld>();
builder.Services.AddOptions<NavigationOptions>()
    .Bind(builder.Configuration.GetSection(NavigationOptions.SectionName));
builder.Services.AddOptions<CombatOptions>()
    .Bind(builder.Configuration.GetSection(CombatOptions.SectionName));
builder.Services.AddOptions<NpcOptions>()
    .Bind(builder.Configuration.GetSection(NpcOptions.SectionName));
builder.Services.AddOptions<BossOptions>()
    .Bind(builder.Configuration.GetSection(BossOptions.SectionName));
builder.Services.AddHostedService<GameServerService>();

// Parse once before opening the UDP port. Definitions remain server-only and immutable.
var catalog = ContentCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "Data", "prototype.json"));
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(new StatCalculator(catalog.Balance));
using var host = builder.Build();
// Resolve the world during validation too: combat profile references/ranges must fail before UDP startup.
host.Services.GetRequiredService<ServerWorld>();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Content").LogInformation(
    "Content loaded. Schema={Schema}, Balance={Balance}, Weapons={Weapons}, Abilities={Abilities}, Creatures={Creatures}",
    ContentCatalog.SchemaVersion, catalog.BalanceVersion, catalog.Weapons.Count,
    catalog.Abilities.Count, catalog.Creatures.Count);
if (validateContentOnly)
    return;
await host.RunAsync();
