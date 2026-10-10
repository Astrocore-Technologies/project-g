using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private void BuildUi()
    {
        Theme = StudioTheme.Create();
        var background = new ColorRect { Color = new("101921"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        var root = new VBoxContainer(); margin.AddChild(root); root.AddThemeConstantOverride("separation", 10);
        var heading = new HBoxContainer(); root.AddChild(heading);
        var brand = Text("PROJECT G  /  Мастерская баланса", 22); brand.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(brand);
        _source = Text(""); heading.AddChild(_source);
        var actions = new HBoxContainer(); root.AddChild(actions);
        actions.AddChild(Button("Открыть…", () => _open.PopupCenteredRatio(.7f)));
        _save = Button("Сохранить", SaveSource); _save.TooltipText = "Ctrl+S · сохранить черновик в открытый исходник"; actions.AddChild(_save);
        actions.AddChild(Button("Копия…", () => { if (CommitInputs()) _copy.PopupCenteredRatio(.7f); }));
        _undo = Button("↶", () => Undo()); _undo.TooltipText = "Ctrl+Z · отменить действие"; actions.AddChild(_undo);
        _redo = Button("↷", () => Undo(true)); _redo.TooltipText = "Ctrl+Y · повторить"; actions.AddChild(_redo);
        actions.AddChild(Button("Изменения", () => Navigate("changes")));
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        actions.AddChild(Button("Тестовый билд", () => Navigate("sandbox")));
        _launch = Button("▶ Применить и играть", StartSession); _launch.Modulate = new Color("91e0c6"); actions.AddChild(_launch);
        actions.AddChild(Button("■", () => { if (!_busy) { _session.Stop(); Status("Тестовая сессия остановлена."); } }));
        actions.AddChild(Button("Сравнение", () => _comparison.Visible = !_comparison.Visible));
        _layout = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [_workspace.SidebarWidth] }; root.AddChild(_layout);
        var sidebar = new VBoxContainer { CustomMinimumSize = new(220, 0) }; _layout.AddChild(sidebar);
        _search = new LineEdit { PlaceholderText = "Поиск объекта или параметра…", TooltipText = "Ctrl+F · названия, ID, параметры и эффекты" }; sidebar.AddChild(_search);
        _search.TextChanged += _ => RebuildTree();
        var filters = new HBoxContainer(); sidebar.AddChild(filters);
        _changedOnly = new CheckBox { Text = "Изменённые" }; _favoritesOnly = new CheckBox { Text = "★", TooltipText = "Только избранное" };
        filters.AddChild(_changedOnly); filters.AddChild(_favoritesOnly);
        _changedOnly.Toggled += _ => RebuildTree(); _favoritesOnly.Toggled += _ => RebuildTree();
        _tree = new Tree { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill }; sidebar.AddChild(_tree);
        _tree.ItemSelected += () => { if (!_building && _tree.GetSelected()?.GetMetadata(0).AsString() is { } key && !key.StartsWith('@')) Navigate(key); };
        _tree.ItemCollapsed += item =>
        {
            if (_building) return; var key = item.GetMetadata(0).AsString();
            if (item.Collapsed) _workspace.Collapsed.Add(key); else _workspace.Collapsed.Remove(key);
        };
        var workspace = new HBoxContainer(); _layout.AddChild(workspace); workspace.AddThemeConstantOverride("separation", 12);
        var editor = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(460, 0) }; workspace.AddChild(editor);
        _breadcrumb = Text("", 13); _breadcrumb.Modulate = new Color("91a5b7"); _breadcrumb.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; editor.AddChild(_breadcrumb);
        var titleRow = new HBoxContainer(); editor.AddChild(titleRow);
        _title = Text("", 24); _title.SizeFlagsHorizontal = SizeFlags.ExpandFill; _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; titleRow.AddChild(_title);
        _star = Button("☆", () => { if (!_workspace.Favorites.Add(_page.Key)) _workspace.Favorites.Remove(_page.Key); UpdateStar(); RebuildTree(); }); titleRow.AddChild(_star);
        _context = Text("", 13); _context.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; editor.AddChild(_context);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; editor.AddChild(_tabs);
        _comparison = new VBoxContainer { CustomMinimumSize = new(270, 0), Visible = _workspace.ShowComparison }; workspace.AddChild(_comparison);
        _comparison.AddChild(Text("Результат расчёта", 19));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _comparison.AddChild(scroll);
        _comparisonBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_comparisonBody);
        var footer = new HBoxContainer(); root.AddChild(footer);
        _status = Text("Готово", 13); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; footer.AddChild(_status);
        footer.AddChild(Button("Журнал запуска", () => _log.Visible = !_log.Visible));
        _log = new TextEdit { Editable = false, CustomMinimumSize = new(0, 100), Visible = false, WrapMode = TextEdit.LineWrappingMode.Boundary }; root.AddChild(_log);
        BuildDialogs();
    }

    private void BuildDialogs()
    {
        _open = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile, Filters = ["*.json ; Каталог баланса"], CurrentDir = Path.Combine(_repository, "Content.Server", "Data") }; AddChild(_open);
        _open.FileSelected += path =>
        {
            void Open() => Run(() => { _document = new(path); _pages = EditorSchema.Pages(_document); RebuildTree(); Navigate(_page.Key); });
            if (!_document.Dirty) { Open(); return; }
            var dialog = new ConfirmationDialog { DialogText = "Открыть другой файл и отказаться от несохранённых правок?" }; AddChild(dialog);
            dialog.Confirmed += () => { Open(); dialog.QueueFree(); }; dialog.Canceled += dialog.QueueFree; dialog.PopupCentered();
        };
        _copy = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.SaveFile, Filters = ["*.json ; Черновик баланса"], CurrentFile = "balance-draft.json" }; AddChild(_copy);
        _copy.FileSelected += path => Run(() => { _document.SaveDraft(path); Changed(true); Status("Копия сохранена: " + path); });
        _close = new ConfirmationDialog { DialogText = "Есть несохранённые изменения или незавершённый ввод. Закрыть мастерскую?", OkButtonText = "Закрыть", CancelButtonText = "Продолжить" }; AddChild(_close); _close.Confirmed += () => GetTree().Quit();
    }

    private static Label Text(string text, int size = 15) { var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size); return label; }
    private static Label Wrap(string text, int size = 14) { var label = Text(text, size); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.SizeFlagsHorizontal = SizeFlags.ExpandFill; return label; }
    private static Button Button(string text, Action action) { var button = new Button { Text = text }; button.Pressed += action; return button; }
    private static SpinBox Number(double min, double max, double value) => new() { MinValue = min, MaxValue = max, Value = value, Step = 1, CustomMinimumSize = new(95, 0) };
    private static void Clear(Node parent) { foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    private VBoxContainer Tab(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _tabs.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 10); scroll.AddChild(box); return box;
    }
    private static VBoxContainer Card(VBoxContainer parent, string title, string? description = null, Action? reset = null)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", StudioTheme.Panel()); parent.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        if (title.Length > 0)
        {
            var heading = new HBoxContainer(); box.AddChild(heading);
            var label = Text(title, 18); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.TooltipText = title; heading.AddChild(label);
            if (reset is not null)
            {
                var button = Button("↶ Вернуть", reset);
                button.TooltipText = "Вернуть карточку к значениям на момент открытия / последнего сохранения. Можно отменить."; heading.AddChild(button);
            }
        }
        if (!string.IsNullOrEmpty(description)) { var label = Wrap(description); label.Modulate = new Color("91a5b7"); box.AddChild(label); }
        return box;
    }
}
