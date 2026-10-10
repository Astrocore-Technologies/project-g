using System.Globalization;
using System.Text.Json.Nodes;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private void ShowInspector()
    {
        var fields = EditorSchema.Fields(_document, _page);
        var skill = _page.RootPath.StartsWith("abilities/");
        var groups = fields.GroupBy(f => f.Group).ToArray();
        if (skill)
        {
            foreach (var section in new[] { "Основное", "Попадание", "Эффекты" })
            {
                var selected = fields.Where(f => f.Group == section).ToArray();
                if (selected.Length == 0) continue;
                var tab = Tab(section); DrawFieldCard(tab, section, selected);
                if (section == "Основное")
                {
                    if (_page.ParentKey is null) tab.AddChild(Button("Поставить на Q в тестовом билде", TestSelectedSkill));
                    else tab.AddChild(Wrap("Навык существа: на панель тестового персонажа не назначается."));
                }
            }
            ShowSkillCurve(Tab("Прокачка"));
        }
        else if (_page.RootPath.StartsWith("professions/"))
        {
            ShowProfession(Tab("Обзор"));
            var acquisition = Tab("Получение профессии");
            foreach (var group in groups) DrawFieldCard(acquisition, group.Key, group.ToArray());
        }
        else
        {
            var tab = Tab("Настройки");
            if (_page.RootPath == "swordsman" && _build.ProfessionId != _document.Catalog.Swordsman!.ProfessionId)
                tab.AddChild(Button("Выбрать Мечника для расчётов и теста", () =>
                { _build = _build with { ProfessionId = _document.Catalog.Swordsman!.ProfessionId, WeaponItemId = "arena_training_sword", SelectedAbilityId = null }; Changed(true); }));
            foreach (var group in groups) DrawFieldCard(tab, group.Key, group.ToArray());
        }
        var creatureSkills = EditorSchema.CreatureSkills(_document, _page, _pages);
        if (creatureSkills.Count > 0)
        {
            var skills = Card(Tab("Навыки"), "Навыки существа", "Откройте навык, чтобы настроить его урон, перезарядку, область попадания и эффекты. Один ID использует общие параметры во всех местах применения.");
            foreach (var ability in creatureSkills)
            {
                var button = Button((PageChanged(ability) ? "● " : "") + ability.Title, () => Navigate(ability.Key));
                button.TooltipText = ability.Breadcrumb; skills.AddChild(button);
            }
        }
        ShowLinks(Tab("Связи"));
    }

    private void DrawFieldCard(VBoxContainer parent, string title, EditorField[] fields)
    {
        var descriptions = new Dictionary<string, string>
        {
            ["Владение мечом"] = "Усиление обычной атаки при надетом исправном мече.",
            ["Боевой ритм"] = "Серия успешных обычных попаданий восстанавливает выносливость.",
            ["Крепкий хват"] = "Дополнительное уменьшение урона, прошедшего через блок.",
            ["Работа ног"] = "Прямое попадание ненадолго ускоряет обычный бег.",
            ["Собранность"] = "Успешное парирование усиливает следующую попытку удара."
        };
        var card = Card(parent, title, descriptions.GetValueOrDefault(title), () => Run(() => { _document.ResetPaths(fields.Select(f => f.Path)); Changed(true); }));
        foreach (var field in fields) DrawField(card, field);
    }

    private void DrawField(VBoxContainer card, EditorField field)
    {
        // A card can expose fields stored in another server section; missing optional weights remain zero.
        var value = _document.Read(field.Path)?.GetValue<double>() ?? 0;
        var row = new HBoxContainer(); card.AddChild(row);
        var label = Wrap(field.Title + (field.Percent && !field.Title.Contains('%') ? " · %" : ""), 15);
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(label);
        var accepted = FormatField(value, field.Percent, field.Integer);
        var edit = new LineEdit { Text = accepted, CustomMinimumSize = new(115, 0), TooltipText = field.Help + "\n" + field.RangeText + "\n" + field.Path + $"\nТочное значение: {value:R}" };
        row.AddChild(edit); _fieldInputs[field.Path] = edit; label.TooltipText = edit.TooltipText;
        var reset = Button("↶", () => Run(() => { _document.ResetPaths([field.Path]); Changed(true); })); row.AddChild(reset);
        var error = Wrap(""); error.Modulate = new("ffad9d"); error.Hide(); card.AddChild(error);
        void Mark() { label.Modulate = new(_document.IsChanged(field.Path) ? "f1ce8c" : "dde5ec"); reset.Disabled = !_document.IsChanged(field.Path); }
        Mark();
        void Commit()
        {
            if (_building || edit.Text == accepted) return;
            try
            {
                var parsed = ParseNumber(edit.Text); if (field.Percent) parsed /= 100;
                if (field.Integer && parsed != Math.Truncate(parsed) || field.Minimum is { } min && parsed < min || field.Maximum is { } max && parsed > max)
                    throw new ArgumentException(field.RangeText);
                _document.Set(field.Path, parsed); accepted = edit.Text; _invalidInputs.Remove(edit); error.Hide();
                Mark(); Changed();
                if (!_catalogValid) { error.Text = "Проверьте сочетание параметров: " + _status.Text; error.Show(); }
            }
            catch (Exception e) { _invalidInputs.Add(edit); error.Text = e.Message; error.Show(); UpdateCommandState(); }
        }
        edit.TextSubmitted += _ => Commit(); edit.FocusExited += Commit; _committers.Add(Commit);
    }

    private void ShowProfession(VBoxContainer parent)
    {
        var node = _document.Read(_page.RootPath)!; var id = node["id"]!.GetValue<ushort>();
        var card = Card(parent, node["name"]!.GetValue<string>(), node["requiresTrainer"]?.GetValue<bool>() == true ? "Получение через обучение и разговор с тренером." : "Открывается выполнением серверных условий.");
        foreach (var skill in _document.ProfessionSkills(id))
        {
            var abilityId = _document.Read(skill.Path)!["id"]!.GetValue<string>();
            card.AddChild(Button(EditorLabels.Name(abilityId), () => Navigate("ability/" + abilityId)));
        }
        card.AddChild(Button("Выбрать профессию для теста", () => { _build = _build with { ProfessionId = id, SelectedAbilityId = null }; UpdatePreview(); Status("Тестовая профессия выбрана."); }));
    }

    private void ShowLinks(VBoxContainer parent)
    {
        var node = _document.Read(_page.RootPath)!;
        var card = Card(parent, "Связи и требования", "Эти сведения помогают найти источник параметров. ID и тип механики здесь не изменяются.");
        if (node["id"] is { } identity) card.AddChild(Wrap("ID: " + identity.ToString()));
        if (_page.RootPath.StartsWith("abilities/"))
        {
            var id = node["id"]!.GetValue<string>();
            foreach (var profession in _document.ProfessionEntries())
                if (_document.ProfessionSkills(profession.Id).Any(s => s.Path == _page.RootPath))
                    card.AddChild(Button("Профессия: " + profession.Name, () => Navigate("profession/" + profession.Id)));
            foreach (var p in _pages.Where(p => p.RootPath.StartsWith("creatures/")))
                if (_document.Read(p.RootPath)?["abilityIds"] is JsonArray ids && ids.Any(a => a?.GetValue<string>() == id))
                    card.AddChild(Button("Использует: " + p.Title, () => Navigate(p.Key)));
            if (node["kind"]!.GetValue<string>() is "Melee" or "Recovery") card.AddChild(Wrap("Нужны активный Мечник, исправный меч и достаточная выносливость."));
            if (node["melee"] is JsonObject melee)
                foreach (var (key, title) in new[] { ("requiresParry", "Требует успешного парирования"), ("stationary", "Применение на месте"), ("firstTargetOnly", "Только первая цель"), ("narrowThrust", "Узкая направленная область") })
                    if (melee[key] is { } flag) card.AddChild(Wrap(title + ": " + (flag.GetValue<bool>() ? "да" : "нет")));
        }
        if (node["weaponId"]?.GetValue<string>() is { } weapon) card.AddChild(Button("Оружие: " + EditorLabels.Name(weapon), () => Navigate("weapons/" + weapon)));
        if (_page.RootPath == "swordsman") card.AddChild(Wrap("Тренер и размещение манекенов редактируются в сцене города. Здесь меняются числовые параметры обучения и профессии."));
        if (node["canBleed"] is { } bleed) card.AddChild(Wrap("Кровотечение: " + (bleed.GetValue<bool>() ? "да" : "нет")));
        if (node["canBeStunned"] is { } stun) card.AddChild(Wrap("Оглушение: " + (stun.GetValue<bool>() ? "да" : "нет")));
        card.AddChild(Wrap("Источник: " + _page.RootPath));
    }

    private EditorPage? PageForPath(string path) => _pages.FirstOrDefault(p => EditorSchema.Fields(_document, p).Any(f => f.Path == path)) ??
        _pages.FirstOrDefault(p => p.RootPath.StartsWith("abilities/") && path.StartsWith(p.RootPath + "/progression"));

    private void ShowChanges()
    {
        var tab = Tab("Список изменений"); var changes = _document.Changes();
        if (changes.Count == 0) { Card(tab, "Нет несохранённых изменений", "Здесь появятся только отличия от открытого / сохранённого файла."); return; }
        foreach (var group in changes.GroupBy(change => PageForPath(change.Path)?.Key ?? "catalog"))
        {
            var page = _pages.FirstOrDefault(p => p.Key == group.Key);
            var card = Card(tab, page?.Breadcrumb ?? "Каталог");
            if (page is not null) card.AddChild(Button("Открыть настройки", () => Navigate(page.Key)));
            foreach (var change in group)
            {
                var line = Wrap($"{ChangeLabel(change.Path)}: {ChangeValue(change.Path, change.Before)} → {ChangeValue(change.Path, change.After)}");
                line.TooltipText = $"{change.Path}\nТочные значения: {change.Before} → {change.After}"; card.AddChild(line);
            }
        }
    }

    private static string ChangeLabel(string path)
    {
        var parts = path.Split('/'); var index = Array.IndexOf(parts, "levels");
        return (index >= 0 && index + 1 < parts.Length ? $"Ур. {int.Parse(parts[index + 1]) + 1} · " : "") + EditorLabels.Name(parts[^1]);
    }

    private static string ChangeValue(string path, string value) => EditorLabels.IsPercent(path) &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (number * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : value;

    private void ShowSettings()
    {
        var tab = Tab("Проект"); var project = Card(tab, "Источники данных");
        project.AddChild(Wrap("Баланс: " + _document.SourcePath)); project.AddChild(Wrap("Рабочее место и билды: " + _workspacePath));
        project.AddChild(Wrap("Тестовые сессии: " + Path.Combine(_repository, ".artifacts", "balance")));
        var runtime = Card(tab, "Запуск игры", "Путь к исполняемому Godot .NET.");
        var godot = new LineEdit { Text = _godotPath }; runtime.AddChild(godot); godot.TextChanged += value => _godotPath = value;
        var help = Card(tab, "Управление");
        help.AddChild(Wrap("Ctrl+S — сохранить. Ctrl+Z / Ctrl+Y — отменить / повторить. Ctrl+F — поиск. Tab — следующее поле. Enter или выход из поля применяет ввод."));
        help.AddChild(Wrap("Проценты показываются с одним знаком. Изменённые поля отмечены цветом. Наведение показывает описание и точное значение."));
        help.AddChild(Wrap("Сохранение пишет в исходник. «Применить и играть» запускает отдельный сервер с копией черновика и новой тестовой БД."));
    }

    private static double ParseNumber(string text)
    {
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException("Введите конечное число, например 12 или 12,5.");
        return value;
    }
    private static string FormatField(double value, bool percent, bool integer) => (percent ? value * 100 : value).ToString(percent ? "0.0" : integer ? "0" : "G", CultureInfo.InvariantCulture);
}
