using Godot;
namespace ProjectG.UI;

/// <summary>Responsive modal shell; content owns game commands and close policy.</summary>
public partial class UiWindow : Control
{
    private PanelContainer _panel=null!;
    private Button _close=null!;
    private Control? _previousFocus;
    private Vector2 _preferred=new(960,600);
    private bool _layoutQueued;
    public VBoxContainer Body { get; private set; }=null!;
    public HBoxContainer Header { get; private set; }=null!;
    public Label Title { get; private set; }=null!;
    public Key ToggleKey { get; set; }
    public event Action? CloseRequested;
    public event Action? Opened;
    public event Action? Closed;
    public override void _Ready() => Build();
    public void Build(string title="",Vector2? preferred=null)
    {
        if(_panel is not null) { if(title.Length!=0) Title.Text=title; if(preferred is {} size) _preferred=size; Layout(); return; }
        _preferred=preferred ?? _preferred; Name="UiWindow"; Visible=false; MouseFilter=MouseFilterEnum.Stop; Theme=GameUi.CreateTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shell=GD.Load<PackedScene>("res://UI/Components/WindowShell.tscn").Instantiate<Control>(); AddChild(shell);
        _panel=shell.GetNode<PanelContainer>("%Frame"); Body=shell.GetNode<VBoxContainer>("%Body");
        Header=shell.GetNode<HBoxContainer>("%Header"); Title=shell.GetNode<Label>("%Title"); Title.Text=title;
        _close=shell.GetNode<Button>("%Close"); _close.Pressed+=OnClose;
        Resized+=QueueLayout; _panel.MinimumSizeChanged+=QueueLayout; _panel.Resized+=QueueLayout; QueueLayout();
    }
    public bool Open()
    {
        if(GameUi.GameplayModalOpen && !Visible) return false;
        _previousFocus=GetViewport().GuiGetFocusOwner(); Show(); GameUi.RegisterModal(this); Layout(); _close.GrabFocus(); Opened?.Invoke(); return true;
    }
    public void RequestClose() => CloseRequested?.Invoke();
    private void OnClose() => CloseRequested?.Invoke();
    public void Close()
    {
        if(!Visible) return; Hide(); GameUi.UnregisterModal(this);
        if(GodotObject.IsInstanceValid(_previousFocus) && _previousFocus!.IsVisibleInTree()) _previousFocus.GrabFocus();
        _previousFocus=null; Closed?.Invoke();
    }
    private void QueueLayout() { if(_layoutQueued || !IsInsideTree()) return; _layoutQueued=true; CallDeferred(nameof(ApplyLayout)); }
    private void ApplyLayout() { _layoutQueued=false; Layout(); }
    public void Layout()
    {
        if(_panel is null || !IsInsideTree()) return;
        var screen=GetViewportRect().Size; var scale=Mathf.Clamp(Mathf.Min(screen.X/1280f,screen.Y/720f),1,1.4f);
        _panel.Size=new(Mathf.Min(_preferred.X*scale,screen.X-48),Mathf.Min(_preferred.Y*scale,screen.Y-48)); _panel.Position=(screen-_panel.Size)/2;
    }
    public override void _Input(InputEvent ev)
    {
        if(!Visible || ev is not InputEventKey { Pressed:true } key) return;
        if(key.PhysicalKeycode!=Key.Escape && (ToggleKey==Key.None || key.PhysicalKeycode!=ToggleKey)) return;
        if(!key.Echo) CloseRequested?.Invoke();
        GetViewport().SetInputAsHandled();
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        // Let GUI handle Tab/Enter/Space first; unused key-down must not activate gameplay.
        // Key-up still reaches held-defense cleanup.
        if(Visible && ev is InputEventKey { Pressed:true }) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() { GameUi.UnregisterModal(this); Resized-=QueueLayout; if(_close is not null) _close.Pressed-=OnClose; if(_panel is not null) { _panel.MinimumSizeChanged-=QueueLayout; _panel.Resized-=QueueLayout; } }
}
