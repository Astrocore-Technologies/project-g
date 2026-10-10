using Content.Server.Data;
using Content.Server.Development;
using Content.Server.Stats;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private BalanceTestBuild? _comparisonBuild;
    private string _comparisonName = "";

    private void ShowSandbox()
    {
        var tab = Tab("Персонаж и цель"); var catalog = _document.Catalog;
        var build = Card(tab, "Тестовый персонаж", "Настройки билда не меняют игровой баланс. Каждый запуск создаёт нового персонажа у арены.");
        var levels = new HBoxContainer(); build.AddChild(levels);
        levels.AddChild(Text("Уровень")); var level = Number(1, catalog.Progression.LevelCap, _build.Level); levels.AddChild(level);
        levels.AddChild(Text("Навыки · ур.")); var skillLevel = Number(1, 1000, _build.SkillLevel); levels.AddChild(skillLevel);
        level.ValueChanged += value => { _build = _build with { Level = (int)value }; UpdatePreview(); };
        skillLevel.ValueChanged += value => { _build = _build with { SkillLevel = (int)value }; UpdatePreview(); };
        var presets = new HBoxContainer(); build.AddChild(presets);
        foreach (var n in new[] { 1, 15, 30 })
        { var chosen = n; presets.AddChild(Button($"Ур. {n} · STR", () => { SetPreset(chosen, false); ShowPage(); })); }
        presets.AddChild(Button("Ур. 30 · DEX", () => { SetPreset(30, true); ShowPage(); }));
        var stats = new GridContainer { Columns = 4 }; build.AddChild(stats);
        var values = new[] { _build.Stats.Strength, _build.Stats.Agility, _build.Stats.Vitality, _build.Stats.Intelligence, _build.Stats.Dexterity, _build.Stats.Luck };
        var boxes = new SpinBox[6]; string[] names = ["STR", "AGI", "VIT", "INT", "DEX", "LUK"];
        for (var i = 0; i < 6; i++)
        {
            stats.AddChild(Text(names[i])); boxes[i] = Number(0, 100000, values[i]); stats.AddChild(boxes[i]);
            boxes[i].ValueChanged += _ => { if (boxes.Any(b => b is null)) return; _build = _build with { Stats = new(boxes[0].Value, boxes[1].Value, boxes[2].Value, boxes[3].Value, boxes[4].Value, boxes[5].Value) }; UpdatePreview(); };
        }
        var professions = new List<(ushort Id, string Name)> { (0, "Без профессии") };
        professions.AddRange(catalog.Professions.Select(p => (p.Id, p.Name)));
        var profession = Choice(build, "Профессия", professions.Select(p => p.Name).ToArray(), professions.FindIndex(p => p.Id == _build.ProfessionId));
        profession.ItemSelected += index => { _build = _build with { ProfessionId = professions[(int)index].Id, SelectedAbilityId = null }; ShowPage(); };
        var weapons = catalog.Items.Values.Where(i => i.WeaponId is { } id && catalog.Weapons[id].Kind == WeaponKind.Melee).OrderBy(i => i.Id).ToArray();
        var weapon = Choice(build, "Оружие", weapons.Select(i => EditorLabels.Name(i.Name)).ToArray(), Array.FindIndex(weapons, i => i.Id == _build.WeaponItemId));
        weapon.ItemSelected += index => { _build = _build with { WeaponItemId = weapons[(int)index].Id }; UpdatePreview(); };
        var owned = catalog.Creatures["test_adventurer"].AbilityIds.Concat(catalog.Professions.FirstOrDefault(p => p.Id == _build.ProfessionId)?.AllSkills ?? [])
            .Distinct().Where(id => catalog.Abilities[id].Kind != AbilityKind.Dash).ToArray();
        var selected = Choice(build, "Навык на Q", new[] { "Стандартная панель" }.Concat(owned.Select(EditorLabels.Name)).ToArray(), _build.SelectedAbilityId is null ? 0 : Array.IndexOf(owned, _build.SelectedAbilityId) + 1);
        selected.ItemSelected += index => { _build = _build with { SelectedAbilityId = index == 0 ? null : owned[(int)index - 1] }; UpdatePreview(); };
        var targetCard = Card(tab, "Цель и окружение", "Манекены получат характеристики выбранной цели. Специальный сценарий босса не запускается.");
        var targets = catalog.Creatures.Keys.Where(id => id != "test_adventurer").Order().ToArray();
        var target = Choice(targetCard, "Цель", targets.Select(EditorLabels.Name).ToArray(), Array.IndexOf(targets, _build.TargetId));
        target.ItemSelected += index => { _build = _build with { TargetId = targets[(int)index] }; UpdatePreview(); };
        var moving = new CheckBox { Text = "Добавить подвижного противника", ButtonPressed = _build.MovingEnemy }; targetCard.AddChild(moving);
        moving.Toggled += value => _build = _build with { MovingEnemy = value };
        ShowBuildLibrary(Tab("Сохранённые билды"));
    }

    private void ShowBuildLibrary(VBoxContainer parent)
    {
        var current = Card(parent, "Сохранить текущий билд", "Профессия, характеристики, уровни навыков, оружие и цель сохраняются отдельно от баланса.");
        var name = new LineEdit { PlaceholderText = "Например: Мечник 15 / STR" }; current.AddChild(name);
        current.AddChild(Button("Сохранить набор", () => Run(() =>
        {
            _build.Validate(_document.Catalog); _workspace.SaveBuild(name.Text, _build);
            if (!_smoke) _workspace.Save(_workspacePath); ShowPage(); Status("Тестовый билд сохранён.");
        })));
        parent.AddChild(Button("Сравнивать с исходным балансом", () => { _comparisonBuild = null; _comparisonName = ""; UpdatePreview(); }));
        if (_workspace.Builds.Count == 0) Card(parent, "Пока нет сохранённых наборов", "Настройте персонажа на соседней вкладке и сохраните его здесь.");
        foreach (var saved in _workspace.Builds)
        {
            var card = Card(parent, saved.Name, $"Уровень {saved.Build.Level} · навыки {saved.Build.SkillLevel} · STR {saved.Build.Stats.Strength:0} / DEX {saved.Build.Stats.Dexterity:0}");
            var row = new HBoxContainer(); card.AddChild(row);
            row.AddChild(Button("Загрузить", () => Run(() => { saved.Build.Validate(_document.Catalog); _build = saved.Build; ShowPage(); })));
            row.AddChild(Button("Сравнить с текущим", () => Run(() => { saved.Build.Validate(_document.Catalog); _comparisonBuild = saved.Build; _comparisonName = saved.Name; UpdatePreview(); })));
        }
    }

    private void SetPreset(int requestedLevel, bool dexterity)
    {
        var catalog = _document.Catalog; var level = Math.Min(requestedLevel, catalog.Progression.LevelCap);
        var stats = catalog.Creatures["test_adventurer"].Stats;
        var points = catalog.Progression.InitialStatPoints + (level - 1) * catalog.Progression.StatPointsPerLevel;
        stats = new(stats.Strength + (dexterity ? 0 : points), stats.Agility, stats.Vitality, stats.Intelligence, stats.Dexterity + (dexterity ? points : 0), stats.Luck);
        _build = _build with { Level = level, Stats = stats }; UpdatePreview();
    }

    private void TestSelectedSkill() => Run(() =>
    {
        var id = _document.Read(_page.RootPath)!["id"]!.GetValue<string>(); var catalog = _document.Catalog;
        if (catalog.Abilities[id].Kind == AbilityKind.Dash) { Status("Рывок уже доступен на Space."); return; }
        var profession = catalog.Professions.FirstOrDefault(p => p.Id == _build.ProfessionId && p.AllSkills.Contains(id)) ?? catalog.Professions.FirstOrDefault(p => p.AllSkills.Contains(id));
        var next = _build with { SelectedAbilityId = id, ProfessionId = profession?.Id ?? _build.ProfessionId };
        if (catalog.Abilities[id].Melee is not null) next = next with { WeaponItemId = "arena_training_sword" };
        next.Validate(catalog); _build = next; UpdatePreview(); Status("Навык назначен на Q тестового персонажа. Можно запускать игру.");
    });

    private static OptionButton Choice(VBoxContainer parent, string title, string[] options, int selected)
    {
        var row = new HBoxContainer(); parent.AddChild(row); var label = Text(title); label.CustomMinimumSize = new(100, 0); row.AddChild(label);
        var choice = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill }; foreach (var option in options) choice.AddItem(option);
        choice.Select(Math.Max(0, selected)); row.AddChild(choice); return choice;
    }
}
