using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private async Task RunSmoke()
    {
        if (OS.GetCmdlineUserArgs().Contains("--session-smoke"))
        {
            _document.Set("abilities/6/cooldownSeconds", 3);
            _document.Set("abilities/6/melee/damageFactor", 2.4);
            _document.SetSkillCurve("abilities/6", new() { PracticePerUse = 2, Levels = [new(7, 1), new(11, 1.6), new(0, 2)] });
            await _session.StartAsync(_repository, _godotPath, _document,
                new() { Level = 15, SkillLevel = 2, Stats = new(25, 1, 10, 1, 10, 1), ProfessionId = 2, TargetId = "test_creature", MovingEnemy = true, SelectedAbilityId = "sword_thrust" }, client: false);
            await _session.RunClientCheckAsync(_repository, _godotPath);
            foreach (var line in _session.DrainLog()) GD.Print(line);
            GD.Print("BALANCE_SESSION_OK: categorized editor -> isolated server -> chosen skill on Q -> authoritative hit/practice.");
            GetTree().Quit(); return;
        }
        // Assigning LineEdit.Text does not emit the user's TextChanged signal.
        void Search(string query) { _search.Text = query; _search.EmitSignal(LineEdit.SignalName.TextChanged, query); }
        var original = _document.Text;
        foreach (var page in _pages) { Navigate(page.Key); if (!_catalogValid) throw new Exception("Invalid page: " + page.Key); }
        Navigate("creatures/test_boss");
        _tabs.CurrentTab = Enumerable.Range(0, _tabs.GetTabCount()).Single(i => _tabs.GetTabTitle(i) == "Навыки");
        _tabs.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Область босса").EmitSignal(Godot.Button.SignalName.Pressed);
        if (_page.Key != "ability/boss_ground_area" || _page.ParentKey != "creatures/test_boss" ||
            _treePages[_page.Key].GetParent().GetParent() != _treePages["creatures/test_boss"] ||
            _tabs.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text == "Поставить на Q в тестовом билде"))
            throw new Exception("Boss skill navigation failed.");
        var bossPowerPath = _page.RootPath + "/power";
        var bossPower = _fieldInputs[bossPowerPath]; bossPower.Text = "60"; bossPower.EmitSignal(LineEdit.SignalName.TextSubmitted, bossPower.Text);
        if (!_catalogValid || _document.Catalog.Abilities["boss_ground_area"].Power != 60 || PageForPath(bossPowerPath)?.Key != _page.Key)
            throw new Exception("Boss skill editing failed.");
        Undo();
        Search("Область босса");
        if (_treePages.Count != 1 || !_treePages.ContainsKey("ability/boss_ground_area")) throw new Exception("Boss skill search failed.");
        Search("");
        Navigate("profession/2");
        _tabs.CurrentTab = Enumerable.Range(0, _tabs.GetTabCount()).Single(i => _tabs.GetTabTitle(i) == "Получение профессии");
        if (!_fieldInputs.ContainsKey("swordsman/requiredDamage") || !_fieldInputs["swordsman/requiredDamage"].IsVisibleInTree() ||
            !_fieldInputs.ContainsKey("swordsman/interactionRange") || _fieldInputs.ContainsKey("swordsman/dummyResetSeconds"))
            throw new Exception("Profession acquisition grouping failed.");
        var training = _fieldInputs["swordsman/requiredDamage"]; training.Text = "150"; training.EmitSignal(LineEdit.SignalName.TextSubmitted, training.Text);
        if (_document.Catalog.Swordsman!.RequiredDamage != 150 || PageForPath("swordsman/requiredDamage")?.Key != "profession/2")
            throw new Exception("Relocated training edit failed.");
        Undo();
        Navigate("creatures/arena_dummy");
        var reset = _fieldInputs["swordsman/dummyResetSeconds"]; reset.Text = "7"; reset.EmitSignal(LineEdit.SignalName.TextSubmitted, reset.Text);
        if (_document.Catalog.Swordsman!.DummyResetSeconds != 7 || !PageChanged(_page) || PageForPath("swordsman/dummyResetSeconds")?.Key != "creatures/arena_dummy")
            throw new Exception("Relocated dummy recovery edit failed.");
        Undo();
        Search("Восстановление манекена");
        if (_treePages.Count != 1 || !_treePages.ContainsKey("creatures/arena_dummy")) throw new Exception("Dummy recovery search failed.");
        Search("");
        Navigate("professions/2/passives");
        if (!_fieldInputs.ContainsKey("swordsman/rhythmHits") || _fieldInputs.ContainsKey("swordsman/dashRange")) throw new Exception("Passive grouping failed.");
        var hits = _fieldInputs["swordsman/rhythmHits"]; hits.Text = "-1"; hits.EmitSignal(LineEdit.SignalName.TextSubmitted, hits.Text);
        if (_invalidInputs.Count != 1 || !_save.Disabled) throw new Exception("Inline validation failed.");
        hits.Text = "4"; hits.EmitSignal(LineEdit.SignalName.TextSubmitted, hits.Text);
        if (_invalidInputs.Count != 0 || !_document.Dirty) throw new Exception("Field edit failed.");
        Undo(); if (_document.Text != original) throw new Exception("Undo failed.");
        Navigate("ability/sword_thrust"); TestSelectedSkill();
        if (_build.SelectedAbilityId != "sword_thrust" || _build.ProfessionId != 2) throw new Exception("Quick skill fixture failed.");
        var power = _fieldInputs["skill/power/2"]; power.Text = "165,5"; power.EmitSignal(LineEdit.SignalName.TextSubmitted, power.Text);
        if (_document.Catalog.SkillProgressions["sword_thrust"].PowerFactor(2) != 1.655) throw new Exception("Level table input failed.");
        Undo();
        _tabs.CurrentTab = Enumerable.Range(0, _tabs.GetTabCount()).Single(i => _tabs.GetTabTitle(i) == "Прокачка");
        _document.EditSkillRange("abilities/6", 1, 3, true, 1.1, true); Changed(true);
        if (_tabs.GetTabTitle(_tabs.CurrentTab) != "Прокачка") throw new Exception("Table edit lost the selected tab.");
        Undo();
        Search("кровотечение");
        if (!_treePages.ContainsKey("ability/sword_rend")) throw new Exception("Parameter search failed.");
        Search("");
        _workspace.Favorites.Add("professions/2/passives"); _favoritesOnly.ButtonPressed = true;
        if (_treePages.Count != 1) throw new Exception("Favorites failed.");
        _favoritesOnly.ButtonPressed = false;
        _workspace.SaveBuild("Проверка", _build); Navigate("sandbox"); Navigate("changes");
        if (_document.Text != original) throw new Exception("Smoke changed source content.");
        if (DisplayServer.GetName() != "headless")
        {
            foreach (var size in new[] { new Vector2I(1366, 768), new Vector2I(1920, 1080) })
            {
                _workspace.Collapsed = new BalanceWorkspace().Collapsed; RebuildTree();
                GetWindow().Size = size; Navigate("professions/2/passives");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_repository, ".artifacts", $"balance-categories-{size.X}.png"));
                Navigate("ability/sword_thrust"); _tabs.CurrentTab = Enumerable.Range(0, _tabs.GetTabCount()).Single(i => _tabs.GetTabTitle(i) == "Прокачка");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_repository, ".artifacts", $"balance-levels-{size.X}.png"));
                _tabs.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Диапазон / вставка…").EmitSignal(Godot.Button.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_repository, ".artifacts", $"balance-range-{size.X}.png"));
                foreach (var (key, title, file) in new[] { ("profession/2", "Получение профессии", "acquisition"), ("creatures/arena_dummy", "Настройки", "dummy"),
                    ("creatures/test_boss", "Навыки", "boss-skills"), ("ability/boss_ground_area", "Основное", "boss-ability") })
                {
                    Navigate(key); _tabs.CurrentTab = Enumerable.Range(0, _tabs.GetTabCount()).Single(i => _tabs.GetTabTitle(i) == title);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_repository, ".artifacts", $"balance-{file}-{size.X}.png"));
                }
            }
        }
        GD.Print("BALANCE_STUDIO_OK: all category pages, inline errors, skill fixture, undo, table, search, favorites, builds, two resolutions; source unchanged.");
        GetTree().Quit();
    }
}
