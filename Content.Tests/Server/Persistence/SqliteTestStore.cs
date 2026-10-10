using Content.Server.Persistence;
using Microsoft.Data.Sqlite;

namespace Content.Tests.Server.Persistence;

/// <summary>Instrumentation around the real Development store: isolated files and an injectable commit gate.</summary>
internal sealed class SqliteCharacterStore : IRegionalCharacterStore
{
    internal string DatabasePath { get; }
    private readonly DatabaseCharacterStore _inner;
    private TaskCompletionSource? _saveGate;
    private string? _saveOperation;
    private int _pending;
    private int _opens;
    internal bool PendingSave => Volatile.Read(ref _pending) > 0;
    internal int OpenAttempts => Volatile.Read(ref _opens);
    internal TaskCompletionSource PauseSaves(string? operation=null)
    { _saveOperation=operation;return _saveGate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private bool ShouldPause(IEnumerable<WorldNodeSave> worlds) => _saveOperation is null || worlds.Any(w=>w.Audit.Any(a=>a.Operation==_saveOperation));
    private TaskCompletionSource? _travelGate;
    private int _pendingTravel;
    internal bool PendingTravel => Volatile.Read(ref _pendingTravel) != 0;
    internal TaskCompletionSource PauseTravel() => _travelGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal SqliteCharacterStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"), "characters.db");
        _inner = new DatabaseCharacterStore(new Content.Database.DevelopmentSqliteCharacterStore(DatabasePath));
    }
    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    internal int Count { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM characters"; return checked((int)(long)command.ExecuteScalar()!); } }
    internal Guid SingleId { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT character_id FROM characters"; return Guid.Parse((string)command.ExecuteScalar()!); } }
    internal CharacterState SingleState { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM characters"; return CharacterState.Deserialize((string)command.ExecuteScalar()!); } }
    internal bool IsHeld
    {
        get
        {
            try { using var probe = new FileStream(DatabasePath + "." + SingleId.ToString("N") + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (IOException) { return true; }
        }
    }
    public Task<SocialSession> OpenSocialAsync(CancellationToken token) => _inner.OpenSocialAsync(token);
    public async Task SaveRegionalCheckpointAsync(IReadOnlyList<CharacterSave> changes, IReadOnlyList<WorldNodeSave> worlds, SocialSave? social, CancellationToken token)
    {
        Interlocked.Increment(ref _pending);
        try
        {
            if (_saveGate is { } gate && ShouldPause(worlds)) await gate.Task.WaitAsync(token);
            if (_travelGate is { } travel && worlds.SelectMany(w => w.Audit).Any(a => a.Operation == "RegionArrival"))
            { Volatile.Write(ref _pendingTravel, 1); await travel.Task.WaitAsync(token); }
            await _inner.SaveRegionalCheckpointAsync(changes, worlds, social, token);
        }
        finally { Volatile.Write(ref _pendingTravel, 0); Interlocked.Decrement(ref _pending); }
    }
    public async Task SaveSocialCheckpointAsync(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,SocialSave social,CancellationToken token)
    {
        Interlocked.Increment(ref _pending);
        try { if(_saveGate is {} gate && ShouldPause(world is null ? [] : [world]))await gate.Task.WaitAsync(token); await _inner.SaveSocialCheckpointAsync(changes,world,social,token); }
        finally { Interlocked.Decrement(ref _pending); }
    }
    public Task InitializeAsync(CancellationToken token) => _inner.InitializeAsync(token);
    public Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken token) =>
        _inner.LoadGroundItemsAsync(seeds, token);
    public Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token)
    {
        Interlocked.Increment(ref _opens); return _inner.OpenAsync(credential, initial, token);
    }
    public Task<WorldNodeSession> OpenWorldAsync(string key,SavedWorldNode initial,CancellationToken token) => _inner.OpenWorldAsync(key,initial,token);
    public Task SaveAsync(IReadOnlyList<CharacterSave> changes,CancellationToken token) => SaveWithWorldAsync(changes,null,token);
    public async Task SaveWithWorldAsync(IReadOnlyList<CharacterSave> changes,WorldNodeSave? world,CancellationToken token)
    {
        if (changes.Count == 0 && world is null) return;
        Interlocked.Increment(ref _pending);
        try
        {
            if (_saveGate is { } gate && ShouldPause(world is null ? [] : [world])) await gate.Task.WaitAsync(token);
            await _inner.SaveWithWorldAsync(changes,world,token);
        }
        finally { Interlocked.Decrement(ref _pending); }
    }
}
