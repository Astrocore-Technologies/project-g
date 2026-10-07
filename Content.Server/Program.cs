using Content.Server.Configuration;
using Content.Server.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
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

builder.Services.AddSingleton<HandshakeCoordinator>();
builder.Services.AddHostedService<GameServerService>();

await builder.Build().RunAsync();
