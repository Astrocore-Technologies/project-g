using Content.Shared.Network;

namespace Content.Server.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public int Port { get; init; } = NetworkConstants.Port;
    public int TickRate { get; init; } = NetworkConstants.ServerTickRate;
    public int NetworkPollIntervalMilliseconds { get; init; } = 10;

    // Application discriminator for LiteNetLib, not an authentication secret.
    public string ConnectionKey { get; init; } = NetworkConstants.ConnectionKey;
}
