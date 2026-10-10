using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private bool PageChanged(EditorPage page) => page.RootPath.Length > 0 &&
        (EditorSchema.Fields(_document, page).Any(f => _document.IsChanged(f.Path)) || page.RootPath.StartsWith("abilities/") && _document.IsChanged(page.RootPath + "/progression"));

    private void RebuildTree()
    {
        if (_tree is null) return;
        var previousBuilding = _building; _building = true;
        var scroll = _tree.GetScroll().Y;
        _tree.Clear(); _treePages.Clear(); var root = _tree.CreateItem(); var groups = new Dictionary<string, TreeItem>();
        var query = _search.Text.Trim();
        foreach (var page in _pages)
        {
            var changed = PageChanged(page);
            if (_changedOnly.ButtonPressed && !changed || _favoritesOnly.ButtonPressed && !_workspace.Favorites.Contains(page.Key)) continue;
            var searchable = page.Breadcrumb + " " + page.Key + " " + string.Join(' ', EditorSchema.Fields(_document, page).Select(f => f.Title + " " + f.Group + " " + f.Path));
            if (query.Length > 0 && !searchable.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var parent = root; var groupPath = ""; var skipGroups = 0;
            if (page.ParentKey is { } parentKey && _treePages.TryGetValue(parentKey, out var owner))
            {
                // Use the actual editable boss item as the parent, not a duplicate folder with the same title.
                parent = owner; skipGroups = _pages.First(p => p.Key == parentKey).Groups.Length + 1;
                groupPath = "/" + string.Join('/', page.Groups.Take(skipGroups));
            }
            foreach (var group in page.Groups.Skip(skipGroups))
            {
                groupPath += "/" + group;
                if (!groups.TryGetValue(groupPath, out var item))
                {
                    item = _tree.CreateItem(parent); item.SetText(0, group); item.SetMetadata(0, "@" + groupPath); item.SetSelectable(0, false);
                    item.SetCustomColor(0, new("94b5c5")); item.Collapsed = query.Length == 0 && _workspace.Collapsed.Contains("@" + groupPath);
                    groups.Add(groupPath, item);
                }
                parent = item;
            }
            var leaf = _tree.CreateItem(parent); leaf.SetText(0, (changed ? "● " : "") + (_workspace.Favorites.Contains(page.Key) ? "★ " : "") + page.Title);
            leaf.SetMetadata(0, page.Key); leaf.SetTooltipText(0, page.Breadcrumb + "\n" + page.Key); _treePages[page.Key] = leaf;
            leaf.Collapsed = query.Length == 0 && _workspace.Collapsed.Contains(page.Key);
            if (_page?.Key == page.Key) leaf.Select(0);
        }
        _building = previousBuilding;
        _tree.SetDeferred("scroll_vertical", (int)scroll);
    }

    private void Navigate(string key)
    {
        RememberView();
        _page = _pages.FirstOrDefault(p => p.Key == key) ?? _pages.First();
        _workspace.Selection = _page.Key; _curvePage = 0;
        ShowPage(remember: false);
        if (_treePages.TryGetValue(_page.Key, out var item))
        {
            _building = true;
            for (var parent = item.GetParent(); parent is not null; parent = parent.GetParent())
            { parent.Collapsed = false; _workspace.Collapsed.Remove(parent.GetMetadata(0).AsString()); }
            item.Select(0); _building = false; _tree.ScrollToItem(item);
        }
    }

    private void UpdateStar() { _star.Text = _workspace.Favorites.Contains(_page.Key) ? "★" : "☆"; _star.TooltipText = "Избранное"; }
    private void RememberView()
    {
        if (_page is null || _tabs is null) return;
        var active = _tabs.GetCurrentTabControl();
        foreach (var scroll in _tabs.GetChildren().OfType<ScrollContainer>())
            _workspace.ScrollOffsets[_page.Key + "/" + scroll.Name] = scroll.ScrollVertical;
        if (active is not null) _workspace.Tab = active.Name;
    }

    private void ShowPage(bool remember = true)
    {
        if (remember) RememberView();
        _building = true; _committers.Clear(); _invalidInputs.Clear(); _fieldInputs.Clear(); _levelResults.Clear(); Clear(_tabs);
        _title.Text = _page.Title; _breadcrumb.Text = string.Join("  /  ", _page.Groups); UpdateStar();
        try
        {
            switch (_page.Key)
            {
                case "sandbox": ShowSandbox(); break;
                case "changes": ShowChanges(); break;
                case "settings": ShowSettings(); break;
                default: ShowInspector(); break;
            }
            for (var i = 0; i < _tabs.GetTabCount(); i++)
                if (_tabs.GetTabTitle(i) == _workspace.Tab) { _tabs.CurrentTab = i; break; }
            if (_tabs.GetCurrentTabControl() is ScrollContainer scroll)
                scroll.SetDeferred("scroll_vertical", _workspace.ScrollOffsets.GetValueOrDefault(_page.Key + "/" + scroll.Name));
        }
        catch (Exception e) { Tab("Ошибка").AddChild(Wrap(e.Message)); Status(e.Message, true); }
        finally { _building = false; UpdatePreview(); }
    }
}
