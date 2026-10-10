using Content.Server.Configuration;
using Content.Server.Data;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private void UpdatePreview()
    {
        if (_building || _comparisonBody is null) return;
        Clear(_comparisonBody);
        _source.Text = _document.Dirty ? "● Есть изменения" : "Сохранено";
        _catalogValid = false; _buildValid = false;
        try
        {
            var catalog = _document.Catalog; _catalogValid = true;
            var beforeCatalog = _comparisonBuild is null ? _document.BaselineCatalog : catalog;
            var beforeBuild = _comparisonBuild ?? _build;
            var settings = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_repository, "Content.Server", "appsettings.json")))!;
            var critical = settings["Combat"]?["CriticalMultiplier"]?.GetValue<double>() ?? new CombatOptions().CriticalMultiplier;
            var ticks = settings["Server"]?["TickRate"]?.GetValue<int>() ?? 20;
            var before = BalancePreview.Calculate(beforeCatalog, beforeBuild, critical, ticks);
            var after = BalancePreview.Calculate(catalog, _build, critical, ticks);
            _buildValid = true;
            var profession = catalog.Professions.FirstOrDefault(p => p.Id == _build.ProfessionId)?.Name ?? "Без профессии";
            _context.Text = $"Тест: {profession} · ур. {_build.Level} · навыки {_build.SkillLevel} · {EditorLabels.Name(_build.TargetId)}";
            _context.TooltipText = _context.Text + (_build.SelectedAbilityId is { } selectedId ? " · Q: " + EditorLabels.Name(selectedId) : "");
            _comparisonBody.AddChild(Wrap(_comparisonBuild is null ? "Сохранённый баланс → черновик" : _comparisonName + " → текущий билд"));
            _comparisonBody.AddChild(Button("Изменить условия теста", () => Navigate("sandbox")));
            void Pair(string name, double a, double b, string format = "0")
            {
                var changed = a != b; var box = new VBoxContainer(); _comparisonBody.AddChild(box);
                var label = Text(name, 13); label.Modulate = new("98adbc"); box.AddChild(label);
                var delta = a == 0 ? $"{b - a:+0.##;-0.##;0}" : $"{(b / a - 1) * 100:+0.0;-0.0;0.0}%";
                var value = Text(changed ? $"{a.ToString(format)} → {b.ToString(format)}  ({delta})" : b.ToString(format), 16);
                value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                value.TooltipText = $"{a:R} → {b:R}"; if (changed) value.Modulate = new("edcc8c"); box.AddChild(value);
            }
            if (_page.RootPath.StartsWith("abilities/"))
            {
                var id = _document.Read(_page.RootPath)!["id"]!.GetValue<string>();
                var ability = catalog.Abilities[id]; var old = beforeCatalog.Abilities[id];
                var curve = catalog.SkillProgressions[id]; var oldCurve = beforeCatalog.SkillProgressions[id];
                var level = Math.Min(_build.SkillLevel, curve.LevelCap); var oldLevel = Math.Min(beforeBuild.SkillLevel, oldCurve.LevelCap);
                Pair("Уровень выбранного навыка", oldLevel, level);
                if (ability.Kind == AbilityKind.Melee) Pair("Прямой урон приёма", BalancePreview.TechniqueHit(beforeCatalog, beforeBuild, id), BalancePreview.TechniqueHit(catalog, _build, id));
                Pair("Практика до следующего уровня", oldCurve.PracticeThreshold(oldLevel), curve.PracticeThreshold(level));
                Pair("Перезарядка · с", old.CooldownSeconds, ability.CooldownSeconds, "0.##");
                Pair("Стоимость выносливости", old.StaminaCost, ability.StaminaCost);
                UpdateSkillRows(catalog);
            }
            Pair("Урон автоатаки", before.Hit, after.Hit); Pair("Ожидаемый DPS", before.ExpectedDps, after.ExpectedDps);
            Pair("Интервал атаки · с", before.Interval, after.Interval, "0.00");
            Pair("HP", before.Stats.MaxHealth, after.Stats.MaxHealth); Pair("Мана", before.Stats.MaxMana, after.Stats.MaxMana);
            Pair("Реген HP / с", before.Stats.HealthRecovery, after.Stats.HealthRecovery); Pair("Реген маны / с", before.Stats.ManaRecovery, after.Stats.ManaRecovery);
            Pair("Физическая защита", before.Stats.PhysicalDefense, after.Stats.PhysicalDefense); Pair("Магическая защита", before.Stats.MagicDefense, after.Stats.MagicDefense);
            Pair("Магическая атака", before.Stats.MagicAttack, after.Stats.MagicAttack); Pair("Дальняя атака", before.Stats.RangedAttack, after.Stats.RangedAttack);
            Pair("Скорость каста · %", (before.Stats.CastSpeedMultiplier - 1) * 100, (after.Stats.CastSpeedMultiplier - 1) * 100, "0.0");
            Pair("Крит · %", before.Stats.CriticalChance * 100, after.Stats.CriticalChance * 100, "0.0");
            Pair("Блок · %", before.Stats.BlockDamage * 100, after.Stats.BlockDamage * 100, "0.0");
            Pair("HP цели", before.TargetHealth, after.TargetHealth); Pair("Защита цели", before.TargetDefense, after.TargetDefense);
            _comparisonBody.AddChild(Wrap($"Свободные очки: {after.AvailablePoints:0}" + (after.AvailablePoints < 0 ? " · тестовый сверхлимит" : "")));
            _comparisonBody.AddChild(Wrap("Расчёт по неподвижной полной цели, при выполненных требованиях навыка. Без ротации, кровотечения, блока цели и временных эффектов."));
            Status(_document.Dirty ? "Черновик изменён. Сохраните в проект или испытайте текущую версию." : "Enter — применить поле. Наведение — описание. Ctrl+S — сохранить.");
        }
        catch (Exception e) { _comparisonBody.AddChild(Wrap("Расчёт недоступен: " + e.Message)); Status(e.Message, true); }
        UpdateCommandState();
    }
}
