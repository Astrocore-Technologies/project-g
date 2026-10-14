using Xunit;

namespace Content.Tests.Server.Persistence;

// Child-process startup has a free-port-to-bind interval; keep crash/restart
// scenarios away from concurrent network fixtures that can reuse that port.
[CollectionDefinition("Server processes",DisableParallelization=true)]
public sealed class ServerProcessCollection { }
