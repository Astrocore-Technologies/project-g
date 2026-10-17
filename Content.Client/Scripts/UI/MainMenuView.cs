using Godot;

namespace ProjectG.UI;

/// <summary>Scene-owned menu presentation; the app controller owns the network session.</summary>
public partial class MainMenuView : Control
{
    public event Action? EnterRequested;
    public event Action? CancelRequested;
    private VBoxContainer _actions = null!;
    private ClientSettingsPanel _settings = null!;
    private Button _enter = null!, _settingsButton = null!, _quit = null!, _cancel = null!;
    private Label _status = null!;
    private Control _tagline = null!, _gap = null!;

    public string StatusText => _status.Text;
    public bool SettingsOpen => _settings.Visible;

    public override void _Ready()
    {
        _actions = GetNode<VBoxContainer>("%MenuActions");
        _settings = GetNode<ClientSettingsPanel>("%Settings");
        _enter = GetNode<Button>("%EnterWorld");
        _settingsButton = GetNode<Button>("%OpenSettings");
        _quit = GetNode<Button>("%Quit");
        _cancel = GetNode<Button>("%CancelConnection");
        _status = GetNode<Label>("%ConnectionStatus");
        _tagline = GetNode<Control>("%Tagline");
        _gap = GetNode<Control>("%Gap");
        _enter.Pressed += OnEnter;
        _settingsButton.Pressed += OpenSettings;
        _quit.Pressed += OnQuit;
        _cancel.Pressed += OnCancel;
        _settings.CloseRequested += CloseSettings;
        GetNode<Label>("%BuildVersion").Text = $"Development · {typeof(MainMenuView).Assembly.GetName().Version}";
        ShowMenu();
    }

    public void SetStatus(string text) => _status.Text = text;
    public void DisableEntry() => _enter.Disabled = true;
    public void ShowMenu()
    {
        Show();
        _settings.Hide();
        _actions.Show();
        _status.Show();
        _tagline.Show();
        _gap.Show();
        _cancel.Hide();
        _enter.Disabled = false;
        _enter.GrabFocus();
    }

    public void ShowConnecting()
    {
        _settings.Hide();
        _actions.Hide();
        _status.Show();
        _tagline.Show();
        _gap.Show();
        _enter.Disabled = true;
        _cancel.Show();
        _cancel.GrabFocus();
    }

    public void OpenSettings()
    {
        if (!Visible || !_actions.Visible) return;
        _actions.Hide();
        _settings.Show();
        _status.Hide();
        _tagline.Hide();
        _gap.Hide();
        _settings.FocusFirst();
    }

    public void CloseSettings()
    {
        _settings.Commit();
        _settings.Hide();
        _actions.Show();
        _status.Show();
        _tagline.Show();
        _gap.Show();
        _settingsButton.GrabFocus();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || !@event.IsActionPressed("ui_cancel")) return;
        if (_settings.Visible) CloseSettings();
        else if (_cancel.Visible) OnCancel();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void OnEnter() => EnterRequested?.Invoke();
    private void OnCancel() => CancelRequested?.Invoke();
    private void OnQuit() => GetTree().Quit();

    public override void _ExitTree()
    {
        _enter.Pressed -= OnEnter;
        _settingsButton.Pressed -= OpenSettings;
        _quit.Pressed -= OnQuit;
        _cancel.Pressed -= OnCancel;
        _settings.CloseRequested -= CloseSettings;
    }
}
