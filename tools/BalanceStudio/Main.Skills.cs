using Content.Server.Data;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private readonly List<(int Level, Label Total, Label Damage)> _levelResults = [];

    private void ShowSkillCurve(VBoxContainer parent)
    {
        _levelResults.Clear(); var path = _page.RootPath; var curve = _document.SkillCurve(path);
        var card = Card(parent, "Прокачка навыка", _document.HasSkillCurve(path) ? "Собственная таблица. Правки действуют только на этот ID навыка." : "Наследует общую формулу. Первая правка создаст собственную таблицу.");
        var top = new HBoxContainer(); card.AddChild(top);
        top.AddChild(Text("Максимум")); var cap = Number(1, 1000, curve.LevelCap); top.AddChild(cap);
        top.AddChild(Button("Задать", () => Run(() => { _document.ResizeSkillCurve(path, (int)cap.Value); Changed(true); })));
        top.AddChild(Text("Практика за применение"));
        top.AddChild(SkillInput(curve.PracticePerUse, false, value => _document.SetSkillPractice(path, Whole(value)), "practicePerUse"));
        var actions = new HBoxContainer(); card.AddChild(actions);
        actions.AddChild(Button("Общая формула", () => Run(() => { _document.ResetSkillCurve(path); Changed(true); })));
        actions.AddChild(Button("Вернуть таблицу", () => Run(() => { _document.ResetPaths([path + "/progression"]); Changed(true); })));
        var bulk = new VBoxContainer { Visible = false }; card.AddChild(bulk);
        actions.AddChild(Button("Диапазон / вставка…", () => bulk.Visible = !bulk.Visible));
        BuildRangeTools(bulk, path, curve.LevelCap);
        card.AddChild(Wrap("«До след.» — цена перехода с этого уровня. «Всего» — практика от первого уровня. Сила 100,0% = базовый урон."));
        const int pageSize = 15;
        _curvePage = Math.Clamp(_curvePage, 0, (curve.LevelCap - 1) / pageSize);
        if (curve.LevelCap > pageSize)
        {
            var pages = new HBoxContainer(); card.AddChild(pages);
            var back = Button("←", () => { _curvePage--; ShowPage(); }); back.Disabled = _curvePage == 0; pages.AddChild(back);
            pages.AddChild(Text($"Страница {_curvePage + 1} из {(curve.LevelCap + pageSize - 1) / pageSize}"));
            var next = Button("→", () => { _curvePage++; ShowPage(); }); next.Disabled = (_curvePage + 1) * pageSize >= curve.LevelCap; pages.AddChild(next);
        }
        var grid = new GridContainer { Columns = 5 }; card.AddChild(grid);
        foreach (var title in new[] { "Ур.", "До след.", "Всего", "Сила · %", "Урон" }) grid.AddChild(Text(title, 13));
        for (var i = _curvePage * pageSize; i < Math.Min(curve.LevelCap, (_curvePage + 1) * pageSize); i++)
        {
            var level = i + 1; var row = curve.Levels[i]; grid.AddChild(Text(level.ToString()));
            if (level == curve.LevelCap) grid.AddChild(Text("Макс.", 13));
            else grid.AddChild(SkillInput(row.PracticeToNext, false, value => _document.SetSkillLevel(path, level, Whole(value), _document.SkillCurve(path).PowerFactor(level)), "practice/" + level));
            var total = Text("", 13); total.CustomMinimumSize = new(65, 0); grid.AddChild(total);
            grid.AddChild(SkillInput(row.PowerMultiplier, true, value => _document.SetSkillLevel(path, level, _document.SkillCurve(path).PracticeThreshold(level), value), "power/" + level));
            var damage = Text("", 13); damage.CustomMinimumSize = new(55, 0); grid.AddChild(damage);
            _levelResults.Add((level, total, damage));
        }
        var kind = _document.Read(path)!["kind"]!.GetValue<string>();
        card.AddChild(Wrap(kind switch
        {
            "Recovery" => "Сила уровня не меняет восстановление выносливости у «Перевести дух». Меняйте параметр восстановления на вкладке эффектов.",
            "Dash" => "Сила уровня меняет скорость рывка, не его дистанцию.",
            "Melee" => "Урон показан для текущего тестового билда по полной цели, без кровотечения и временных бонусов.",
            _ => "Сила масштабирует базовую мощность и вклад магической атаки. Колонка урона пока рассчитана только для ближних приёмов."
        }));
    }

    private LineEdit SkillInput(double value, bool percent, Action<double> apply, string key)
    {
        var accepted = FormatField(value, percent, !percent);
        var edit = new LineEdit { Text = accepted, CustomMinimumSize = new(80, 0), TooltipText = $"Точное значение: {value:R}" };
        _fieldInputs["skill/" + key] = edit;
        void Commit()
        {
            if (_building || edit.Text == accepted) return;
            try
            {
                var parsed = ParseNumber(edit.Text); apply(percent ? parsed / 100 : parsed);
                accepted = edit.Text; edit.Modulate = Colors.White; _invalidInputs.Remove(edit); Changed();
            }
            catch (Exception error) { edit.Modulate = new("ffad9d"); edit.TooltipText = error.Message; _invalidInputs.Add(edit); Status(error.Message, true); UpdateCommandState(); }
        }
        edit.TextSubmitted += _ => Commit(); edit.FocusExited += Commit; _committers.Add(Commit); return edit;
    }

    private void BuildRangeTools(VBoxContainer parent, string path, int cap)
    {
        var row = new HBoxContainer(); parent.AddChild(row);
        row.AddChild(Text("С")); var from = Number(1, cap, 1); row.AddChild(from);
        row.AddChild(Text("по")); var to = Number(1, cap, cap); row.AddChild(to);
        var column = new OptionButton(); column.AddItem("Сила · %"); column.AddItem("Практика"); row.AddChild(column);
        var value = new LineEdit { Text = "100", CustomMinimumSize = new(85, 0) }; row.AddChild(value);
        var actions = new HBoxContainer(); parent.AddChild(actions);
        void Apply(bool multiply) => Run(() =>
        {
            if (!CommitInputs()) return;
            var amount = ParseNumber(value.Text);
            _document.EditSkillRange(path, (int)from.Value, (int)to.Value, column.Selected == 0,
                multiply ? 1 + amount / 100 : column.Selected == 0 ? amount / 100 : amount, multiply); Changed(true);
        });
        actions.AddChild(Button("Заполнить", () => Apply(false)));
        var increase = Button("Изменить на %", () => Apply(true)); increase.TooltipText = "Например 10 = +10%, −10 = −10%. Практика округляется до целого."; actions.AddChild(increase);
        actions.AddChild(Button("Копировать", () => Run(() => { DisplayServer.ClipboardSet(_document.CopySkillRows(path, (int)from.Value, (int)to.Value)); Status("Скопированы практика и сила; две колонки с Tab."); })));
        actions.AddChild(Button("Вставить", () => Run(() => { _document.PasteSkillRows(path, (int)from.Value, DisplayServer.ClipboardGet()); Changed(true); })));
        parent.AddChild(Wrap("Изменение диапазона отменяется целиком. Вставка двух колонок: практика, сила в %. Последняя строка требует 0 практики."));
    }

    private void UpdateSkillRows(ContentCatalog catalog)
    {
        if (!_page.RootPath.StartsWith("abilities/")) return;
        var id = _document.Read(_page.RootPath)!["id"]!.GetValue<string>(); var curve = catalog.SkillProgressions[id];
        foreach (var (level, total, damage) in _levelResults)
        {
            if (!GodotObject.IsInstanceValid(total) || level > curve.LevelCap) continue;
            long sum = 0; for (var i = 1; i < level; i++) sum += curve.PracticeThreshold(i);
            total.Text = sum.ToString();
            damage.Text = catalog.Abilities[id].Kind == AbilityKind.Melee ? BalancePreview.TechniqueHit(catalog, _build with { SkillLevel = level }, id).ToString("0") : "—";
        }
    }
    private static int Whole(double value) => value == Math.Truncate(value) && value >= 0 && value <= int.MaxValue
        ? (int)value : throw new ArgumentException("Практика должна быть целым неотрицательным числом.");
}
