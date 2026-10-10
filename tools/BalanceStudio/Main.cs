using Content.Server.Development;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

/// <summary>Editor lifecycle. Navigation, forms, previews and test fixtures live in separate views.</summary>
public partial class Main : Control
{
    private readonly BalanceSession _session = new();
    private BalanceDocument _document = null!;
    private BalanceWorkspace _workspace = new();
    private BalanceTestBuild _build = new();
    private IReadOnlyList<EditorPage> _pages = [];
    private EditorPage _page = null!;
    private string _repository = "", _workspacePath = "", _godotPath = "";
    private Tree _tree = null!;
    private LineEdit _search = null!;
    private CheckBox _changedOnly = null!, _favoritesOnly = null!;
    private Label _title = null!, _breadcrumb = null!, _status = null!, _source = null!, _context = null!;
    private TabContainer _tabs = null!;
    private HSplitContainer _layout = null!;
    private VBoxContainer _comparison = null!, _comparisonBody = null!;
    private Button _launch = null!, _save = null!, _star = null!, _undo = null!, _redo = null!;
    private TextEdit _log = null!;
    private FileDialog _open = null!, _copy = null!;
    private ConfirmationDialog _close = null!;
    private readonly Dictionary<string, TreeItem> _treePages = [];
    private readonly List<Action> _committers = [];
    private readonly HashSet<LineEdit> _invalidInputs = [];
    private readonly Dictionary<string, LineEdit> _fieldInputs = [];
    private bool _building, _busy, _smoke, _catalogValid = true, _buildValid = true;
    private double _logTimer;
    private int _curvePage;

    public override async void _Ready()
    {
        try
        {
            _repository = FindRepository();
            _workspacePath = Path.Combine(_repository, ".artifacts", "balance-studio", "workspace.json");
            _smoke = OS.GetCmdlineUserArgs().Any(a => a.EndsWith("smoke", StringComparison.Ordinal));
            string? preferenceError = null;
            if (!_smoke) try { _workspace = BalanceWorkspace.Load(_workspacePath); } catch (Exception e) { preferenceError = e.Message; }
            _document = new(Path.Combine(_repository, "Content.Server", "Data", "prototype.json"));
            _pages = EditorSchema.Pages(_document); _build = _workspace.CurrentBuild;
            _godotPath = File.Exists(_workspace.GodotPath) ? _workspace.GodotPath : OS.GetExecutablePath();
            BuildUi(); RebuildTree(); Navigate(_workspace.Selection);
            if (preferenceError is not null) Status("Настройки мастерской не прочитаны: " + preferenceError, true);
            GetTree().AutoAcceptQuit = false;
            if (_smoke) await RunSmoke();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Changed(bool rebuild = false)
    { UpdatePreview(); RebuildTree(); if (rebuild) ShowPage(); }

    private bool CommitInputs()
    {
        foreach (var commit in _committers.ToArray()) commit();
        if (_invalidInputs.Count == 0) return true;
        Status("Исправьте выделенные поля перед сохранением или запуском.", true); return false;
    }
    private void SaveSource() => Run(() =>
    { if (!CommitInputs()) return; _document.SaveSource(); Changed(true); Status("Изменения сохранены в проект."); });
    private void Undo(bool redo = false)
    { if (redo) _document.Redo(); else _document.Undo(); ShowPage(); Changed(); }

    private async void StartSession()
    {
        if (_busy || !CommitInputs()) return;
        _busy = true; UpdateCommandState(); Status("Подготовка тестовой площадки…");
        try { await _session.StartAsync(_repository, _godotPath, _document, _build); Status("Тест запущен. Следующий запуск использует новый снимок черновика."); }
        catch (Exception error) { if (IsInsideTree()) { _log.Show(); Status(error.Message, true); } }
        finally { _busy = false; if (IsInsideTree()) UpdateCommandState(); }
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.S: SaveSource(); break;
            case Key.Z: Undo(key.ShiftPressed); break;
            case Key.Y: Undo(true); break;
            case Key.F: _search.GrabFocus(); _search.SelectAll(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta)
    {
        if (_log is null || (_logTimer += delta) < .2) return;
        _logTimer = 0;
        foreach (var line in _session.DrainLog())
        { _log.Text += line + "\n"; if (_smoke) GD.Print(line); if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) _log.Show(); }
        if (_log.Text.Length > 20000) _log.Text = _log.Text[^16000..];
    }
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        CommitInputs();
        if (_document.Dirty || _invalidInputs.Count > 0) _close.PopupCentered(); else GetTree().Quit();
    }
    public override void _ExitTree()
    {
        _session.Dispose();
        if (_smoke || _layout is null) return;
        try
        {
            RememberView(); _workspace.CurrentBuild = _build; _workspace.GodotPath = _godotPath;
            _workspace.SidebarWidth = _layout.SplitOffsets[0]; _workspace.ShowComparison = _comparison.Visible;
            _workspace.Save(_workspacePath);
        }
        catch (Exception e) { GD.PushWarning("Настройки мастерской не сохранены: " + e.Message); }
    }
    private void UpdateCommandState()
    {
        _save.Disabled = !_catalogValid || _invalidInputs.Count > 0;
        _launch.Disabled = _busy || !_catalogValid || !_buildValid || _invalidInputs.Count > 0;
        _undo.Disabled = !_document.CanUndo; _redo.Disabled = !_document.CanRedo;
    }
    private void Run(Action action) { try { action(); } catch (Exception error) { Status(error.Message, true); } }
    private void Status(string text, bool error = false)
    { _status.Text = text; _status.Modulate = new Color(error ? "ffad9d" : "98b0c0"); }
    private static string FindRepository()
    {
        var argument = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--repo=", StringComparison.Ordinal));
        if (argument is not null) return Path.GetFullPath(argument[7..]);
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "Game.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("Не найден Project G. Укажите -- --repo=путь.");
    }
}
