using Godot;

namespace ProjectG.UI;

/// <summary>Local presentation preferences, independent of login and authoritative game state.</summary>
public partial class ClientSettingsPanel : PanelContainer
{
    public event Action? CloseRequested;
    [Export(PropertyHint.File, "*.cfg")] public string SettingsPath { get; set; } = "user://client-settings.cfg";
    private CheckButton _fullscreen = null!;
    private HSlider _volume = null!;
    private Label _volumeValue = null!, _feedback = null!;
    private Button _back = null!;

    public override void _Ready()
    {
        _fullscreen = GetNode<CheckButton>("%Fullscreen");
        _volume = GetNode<HSlider>("%Volume");
        _volumeValue = GetNode<Label>("%VolumeValue");
        _feedback = GetNode<Label>("%SettingsFeedback");
        _back = GetNode<Button>("%Back");
        LoadPreferences();
        _fullscreen.Toggled += OnFullscreen;
        _volume.ValueChanged += OnVolume;
        _back.Pressed += OnBack;
    }

    public void FocusFirst() => _fullscreen.GrabFocus();
    public void Commit() => SavePreferences();

    private void LoadPreferences()
    {
        var config = new ConfigFile();
        var result = config.Load(SettingsPath);
        var fullscreen = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        var volume = (double)AudioServer.GetBusVolumeLinear(0) * 100;
        if (result == Error.Ok)
        {
            var storedMode = config.GetValue("display", "fullscreen", fullscreen);
            var storedVolume = config.GetValue("audio", "master_volume", volume);
            if (storedMode.VariantType == Variant.Type.Bool) fullscreen = storedMode.AsBool();
            if (storedVolume.VariantType is Variant.Type.Float or Variant.Type.Int)
            {
                var number = storedVolume.AsDouble();
                if (double.IsFinite(number)) volume = Math.Clamp(number, 0, 100);
            }
        }
        _fullscreen.SetPressedNoSignal(fullscreen);
        _volume.SetValueNoSignal(volume);
        _volumeValue.Text = $"{volume:0}%";
        AudioServer.SetBusVolumeLinear(0, (float)(volume / 100));
        // Headless smoke must not change a desktop display mode.
        if (DisplayServer.GetName() != "headless") ApplyWindowMode(fullscreen);
        if (result is not (Error.Ok or Error.FileNotFound)) _feedback.Text = "Не удалось прочитать настройки. Используются текущие значения.";
    }

    private static void ApplyWindowMode(bool enabled) => DisplayServer.WindowSetMode(enabled
        ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);

    private void OnFullscreen(bool enabled)
    {
        if (DisplayServer.GetName() != "headless") ApplyWindowMode(enabled);
        SavePreferences();
    }

    private void OnVolume(double value)
    {
        AudioServer.SetBusVolumeLinear(0, (float)(value / 100));
        _volumeValue.Text = $"{value:0}%";
    }

    private void SavePreferences()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", _fullscreen.ButtonPressed);
        config.SetValue("audio", "master_volume", _volume.Value);
        _feedback.Text = config.Save(SettingsPath) == Error.Ok ? "Настройки сохранены" : "Не удалось сохранить настройки.";
    }

    private void OnBack() => CloseRequested?.Invoke();

    public override void _ExitTree()
    {
        _fullscreen.Toggled -= OnFullscreen;
        _volume.ValueChanged -= OnVolume;
        _back.Pressed -= OnBack;
        SavePreferences();
    }
}
