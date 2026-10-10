using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Development;

namespace ProjectG.Balance;

/// <summary>Owns only processes and fresh artifacts launched by this editor. Never attaches to the user's server.</summary>
public sealed class BalanceSession : IDisposable
{
    private readonly List<Process> _owned = [];
    private readonly ConcurrentQueue<string> _log = new();
    private readonly SemaphoreSlim _launch = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    public string? DirectoryPath { get; private set; }
    public int Port { get; private set; }
    public bool Running => _owned.Any(p => !p.HasExited);
    public IEnumerable<string> DrainLog() { while (_log.TryDequeue(out var line)) yield return line; }

    public async Task StartAsync(string repository, string godot, BalanceDocument document, BalanceTestBuild build, bool client = true)
    {
        if (!await _launch.WaitAsync(0)) throw new InvalidOperationException("Запуск уже выполняется.");
        try
        {
            var catalog = document.Catalog; build.Validate(catalog);
            var content = (JsonObject)document.Root.DeepClone();
            if (!File.Exists(godot)) throw new FileNotFoundException("Укажите путь к Godot .NET в поле редактора.", godot);
            Stop();
            DirectoryPath = Path.Combine(repository, ".artifacts", "balance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            // Build outputs are isolated too: a running game/server cannot lock this session's assemblies.
            var output = Path.Combine(DirectoryPath, "server");
            await RunAsync("dotnet", repository, ["build", "Content.Server/Content.Server.csproj", "--no-restore", "--disable-build-servers", "-m:1", "-o", output]);
            // Stationary targets use the selected creature's stats while retaining dummy behavior.
            var creatures = content["creatures"]!.AsArray();
            var selected = creatures.Single(n => n!["id"]!.GetValue<string>() == build.TargetId)!;
            var dummyIndex = creatures.Select((n, i) => (n, i)).Single(v => v.n!["id"]!.GetValue<string>() == "arena_dummy").i;
            var dummy = selected.DeepClone(); dummy["id"] = "arena_dummy"; dummy["canBleed"] = false; dummy["canBeStunned"] = false;
            creatures[dummyIndex] = dummy;
            var contentPath = Path.Combine(DirectoryPath, "content.json");
            BalanceDocument.WriteAtomic(contentPath, Encoding.UTF8.GetBytes(content.ToJsonString()));
            var buildPath = Path.Combine(DirectoryPath, "build.json");
            BalanceDocument.WriteAtomic(buildPath, JsonSerializer.SerializeToUtf8Bytes(build));
            var regions = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "Data", "regions.json")))!.AsArray();
            var city = regions.Single(n => n!["Id"]!.GetValue<string>() == "river_city")!;
            city["Npc"] = JsonSerializer.SerializeToNode(new
            {
                Enabled = build.MovingEnemy, DefinitionId = build.TargetId, SpawnId = "balance_enemy",
                X = 9, Z = -14, RespawnSeconds = 5, AggroRadius = 6, LeashRadius = 12
            });
            var regionsPath = Path.Combine(DirectoryPath, "regions.json");
            BalanceDocument.WriteAtomic(regionsPath, Encoding.UTF8.GetBytes(regions.ToJsonString()));
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            { socket.Bind(new IPEndPoint(IPAddress.Loopback, 0)); Port = ((IPEndPoint)socket.LocalEndPoint!).Port; }
            var serverDll = Path.Combine(output, "Content.Server.dll");
            string[] settings = ["--environment", "Development", "--Server:StartingRegion=river_city", $"--Server:Port={Port}",
                $"--Persistence:SqlitePath={Path.Combine(DirectoryPath, "sandbox.db")}",
                $"--Balance:ContentPath={contentPath}", $"--Balance:BuildPath={buildPath}", $"--Balance:RegionsPath={regionsPath}",
                "--Logging:LogLevel:Default=Warning", "--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information"];
            await RunAsync("dotnet", repository, [serverDll, "--validate-content", ..settings]);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = Start("dotnet", repository, [serverDll, ..settings], line =>
            { if (line.Contains("Application started", StringComparison.Ordinal)) ready.TrySetResult(); });
            server.EnableRaisingEvents = true;
            server.Exited += (_, _) => ready.TrySetException(new IOException("Тестовый сервер завершился. Подробности в журнале запуска."));
            if (server.HasExited) throw new IOException("Тестовый сервер не запустился.");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(25), _lifetime.Token);
            if (client)
                Start(godot, repository, ["--path", Path.Combine(repository, "Content.Client"), "--windowed", "--resolution", "1280x720",
                    "res://Scenes/World.tscn", "--", $"--server-port={Port}", "--identity=balance-" + Path.GetFileName(DirectoryPath)[..16]]);
            Log($"Готово. Тестовая площадка: {Port}. Новая сессия применяет текущий черновик и выбранный билд.");
        }
        catch { Stop(); throw; }
        finally { _launch.Release(); }
    }

    private Process Start(string executable, string cwd, IEnumerable<string> arguments, Action<string>? output = null)
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        void OnLine(object sender, DataReceivedEventArgs e) { if (e.Data is { } line) { Log(line); output?.Invoke(line); } }
        process.OutputDataReceived += OnLine; process.ErrorDataReceived += OnLine;
        process.Start(); _owned.Add(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }
    private async Task RunAsync(string exe, string cwd, string[] args)
    {
        var process = Start(exe, cwd, args);
        await process.WaitForExitAsync(_lifetime.Token);
        if (process.ExitCode != 0) throw new IOException($"Команда завершилась с кодом {process.ExitCode}. Смотрите журнал запуска.");
    }
    public async Task RunClientCheckAsync(string repository, string godot)
    {
        var client = Start(godot, repository, ["--headless", "--path", Path.Combine(repository, "Content.Client"),
            "res://Tests/UI/BalanceSandboxSmoke.tscn", "--", $"--server-port={Port}", "--identity=balance-smoke-" + Path.GetFileName(DirectoryPath!)[..12]]);
        await client.WaitForExitAsync(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(45), _lifetime.Token);
        if (client.ExitCode != 0) throw new IOException("Проверка тестового клиента не прошла.");
    }
    private void Log(string line) { _log.Enqueue(line); while (_log.Count > 200) _log.TryDequeue(out _); }
    public void Stop()
    {
        // These are disposable test databases. Only processes created by this instance may be stopped.
        foreach (var process in _owned.AsEnumerable().Reverse())
        { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } process.Dispose(); }
        _owned.Clear();
    }
    public void Dispose() { _lifetime.Cancel(); Stop(); }
}
