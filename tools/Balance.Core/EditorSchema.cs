using System.Text.Json.Nodes;

namespace ProjectG.Balance;

public sealed record EditorPage(string Key, string Title, string[] Groups, string RootPath, string Section = "", string? ParentKey = null)
{
    public string Breadcrumb => string.Join(" / ", Groups.Append(Title));
}
public sealed record EditorField(string Path, string Title, string Group, string Help, bool Percent, bool Integer, double? Minimum = null, double? Maximum = null)
{
    public string RangeText => (Integer ? "Целое число" : Percent ? "Проценты" : "Число") + Bounds;
    private string Bound(double? value, string fallback) => value is { } number
        ? (Percent ? number * 100 : number).ToString(Percent ? "0.0" : "G", System.Globalization.CultureInfo.InvariantCulture) : fallback;
    private string Bounds => Minimum is null && Maximum is null ? "; проверяется сервером" : $"; от {Bound(Minimum, "−∞")} до {Bound(Maximum, "+∞")}";
}

/// <summary>Authoring metadata only: categories, units and hints. The server remains the final validator.</summary>
public static class EditorSchema
{
    public static IReadOnlyList<EditorPage> Pages(BalanceDocument document)
    {
        var pages = new List<EditorPage>();
        var entries = document.Entries();
        string PathFor(string category, string id) => entries.Single(e => e.Name == category + " / " + id).Path;
        var playerPath = PathFor("creatures", "test_adventurer");
        pages.Add(new("character/base", "Стартовые характеристики", ["Персонаж"], playerPath, "stats"));
        pages.Add(new("character/resources", "HP, мана и восстановление", ["Персонаж"], playerPath, "resources"));
        pages.Add(new("character/hurtbox", "Область получения попаданий", ["Персонаж"], playerPath, "hurtbox"));
        foreach (var (key, value) in document.Root["balance"]!.AsObject())
            if (value is JsonObject) pages.Add(new("weights/" + key, EditorLabels.Name(key), ["Персонаж", "Влияние характеристик"], "balance/" + key));
        pages.Add(new("progression/character", "Уровни персонажа и опыт", ["Уровни и опыт"], "progression", "character"));
        pages.Add(new("progression/skills", "Общие правила навыков", ["Уровни и опыт"], "progression", "skills"));
        pages.Add(new("combat/rules", "Защита и критический удар", ["Бой и защита"], "balance", "rules"));
        foreach (var (key, title) in new[] { ("stamina", "Выносливость"), ("block", "Блок"), ("parry", "Парирование") })
            pages.Add(new("combat/" + key, title, ["Бой и защита"], "defense", key));
        pages.Add(new("combat/control", "Контроль, буфер и Quick Recover", ["Бой и защита"], "defense", "control"));
        if(document.Root["control"] is JsonObject) pages.Add(new("combat/acr","Категории контроля и ACR",["Бой и защита"],"control"));
        if(document.Root["lifecycle"] is JsonObject) pages.Add(new("combat/lifecycle","Бой, disconnect и тело",["Бой и защита"],"lifecycle"));
        if(document.Root["echoSummon"] is JsonObject)pages.Add(new("echo/summon","Призыв Эхо",["Эхо"],"echoSummon"));
        if(document.Root["echoRecovery"] is JsonObject)pages.Add(new("echo/recovery","Восстановление Эхо",["Эхо"],"echoRecovery"));
        foreach(var entry in entries.Where(e=>e.Name.StartsWith("effects / ",StringComparison.Ordinal)))
        {
            var effect=document.Read(entry.Path)!;var id=effect["id"]!.GetValue<string>();
            pages.Add(new("effect/"+id,EditorLabels.Name(id),["Бой и защита","Эффекты"],entry.Path));
        }
        foreach (var profession in document.ProfessionEntries())
        {
            var entry = entries.Single(e => e.Name == "professions / " + profession.Id);
            pages.Add(new($"profession/{profession.Id}", "Обзор и получение", ["Профессии", profession.Name], entry.Path));
            if(document.Read(entry.Path)?["dodge"] is not null)
                pages.Add(new($"professions/{profession.Id}/dash","Уворот",["Профессии",profession.Name],entry.Path+"/dodge"));
            foreach (var skill in document.ProfessionSkills(profession.Id))
            {
                var id = document.Read(skill.Path)!["id"]!.GetValue<string>();
                pages.Add(new("ability/" + id, EditorLabels.Name(id), ["Профессии", profession.Name, "Активные навыки"], skill.Path));
            }
            if (document.Root["swordsman"]?["professionId"]?.GetValue<ushort>() == profession.Id)
            {
                foreach (var (key, title) in new[] { ("passives", "Пассивки") })
                    pages.Add(new($"professions/{profession.Id}/{key}", title, ["Профессии", profession.Name], "swordsman", key));
            }
        }
        foreach (var entry in document.ProfessionSkills(0))
        {
            var ability = document.Read(entry.Path)!; var id = ability["id"]!.GetValue<string>();
            var group = ability["kind"]!.GetValue<string>() == "Dash" ? "Бой и защита" : "Общие навыки";
            pages.Add(new("ability/" + id, EditorLabels.Name(id), [group], entry.Path));
        }
        foreach (var entry in entries.Where(e => e.Path.StartsWith("weapons/") || e.Path.StartsWith("items/") || e.Path.StartsWith("creatures/")))
        {
            var node = document.Read(entry.Path)!; var id = node["id"]!.GetValue<string>();
            if (id == "test_adventurer") continue;
            var kind = entry.Path.Split('/')[0];
            var title = EditorLabels.Name(node["name"]?.GetValue<string>() ?? id);
            string[] groups = kind == "weapons" ? ["Оружие и экипировка", "Боевые параметры оружия"] :
                kind == "items" ? ["Оружие и экипировка", node["slot"]?.GetValue<string>() == "Armor" ? "Броня" : "Предметы оружия"] :
                ["Существа", id.Contains("dummy") ? "Манекены" : id.Contains("boss") ? "Боссы" : "Противники"];
            pages.Add(new(kind + "/" + id, title, groups, entry.Path));
        }
        PlaceBossSkills(document, pages, playerPath);
        foreach(var entry in entries.Where(e=>e.Path.StartsWith("echoes/",StringComparison.Ordinal)))
        {
            var echo=document.Read(entry.Path)!; var id=echo["id"]!.GetValue<string>(); var name=echo["name"]!.GetValue<string>();
            pages.Add(new("echo/"+id,name,["Эхо","Боевые профили"],entry.Path));
            var profile=echo["profileId"]!.GetValue<string>();var signature=echo["signatureId"]!.GetValue<string>();
            foreach(var page in pages.Where(p=>p.Key=="creatures/"+profile || p.Key=="ability/"+signature).ToArray())
            {
                pages.Remove(page);pages.Add(page with {Groups=["Эхо",name],ParentKey="echo/"+id});
            }
        }
        pages.Add(new("sandbox", "Тестовая площадка", [], ""));
        pages.Add(new("changes", "Изменения", [], ""));
        pages.Add(new("settings", "Настройки и проект", [], ""));
        return pages;
    }

    private static void PlaceBossSkills(BalanceDocument document, List<EditorPage> pages, string playerPath)
    {
        var starterSkills = document.Read(playerPath)!["abilityIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToHashSet();
        var discoveryId = document.Root["progression"]?["discoverySkillId"]?.GetValue<ushort>();
        var bosses = pages.Where(p => p.RootPath.StartsWith("creatures/") && p.Groups.Contains("Боссы")).ToArray();
        foreach (var skill in pages.Where(p => p.RootPath.StartsWith("abilities/") && p.Groups.Length == 1).ToArray())
        {
            var ability = document.Read(skill.RootPath)!; var id = ability["id"]!.GetValue<string>();
            // Shared player abilities keep their existing home. An NPC-only ID has one editor, even with several users.
            if (starterSkills.Contains(id) || ability["networkId"]?.GetValue<ushort>() == discoveryId) continue;
            var owner = bosses.FirstOrDefault(b => CreatureSkillIds(document, b).Contains(id));
            if (owner is null) continue;
            pages.Remove(skill);
            pages.Add(skill with { Groups = [..owner.Groups, owner.Title, "Навыки"], ParentKey = owner.Key });
        }
    }

    private static IReadOnlyList<string> CreatureSkillIds(BalanceDocument document, EditorPage page) =>
        page.RootPath.StartsWith("creatures/") && document.Read(page.RootPath)?["abilityIds"] is JsonArray ids
            ? ids.Select(id => id!.GetValue<string>()).Distinct().ToArray() : [];

    public static IReadOnlyList<EditorPage> CreatureSkills(BalanceDocument document, EditorPage page, IReadOnlyList<EditorPage> pages) =>
        CreatureSkillIds(document, page).Select(id => pages.FirstOrDefault(p => p.Key == "ability/" + id)).OfType<EditorPage>().ToArray();

    public static IReadOnlyList<EditorField> Fields(BalanceDocument document, EditorPage page)
    {
        if (page.RootPath.Length == 0) return [];
        var fields = document.Fields(page.RootPath).Where(f => Include(document, page, f.Path)).Select(f => Describe(page, f.Path)).ToList();
        // Present settings at their authoring owner without duplicating or migrating server content.
        if (document.Root["swordsman"] is JsonObject training)
        {
            if (page.Key == "profession/" + training["professionId"])
                foreach (var key in new[] { "requiredDamage", "interactionRange" })
                    fields.Add(Describe(page, "swordsman/" + key) with { Group = "Обучение у тренера", Minimum = 0 });
            if (page.Key == "creatures/" + training["dummyDefinitionId"])
                fields.Insert(0, Describe(page, "swordsman/dummyResetSeconds") with { Group = "Восстановление", Minimum = 0, Maximum = 100000 });
        }
        if(page.Key=="combat/stamina")
            foreach(var key in new[]{"baseStamina","baseStaminaRecovery"})
                fields.Insert(0,Describe(page,"balance/"+key) with {Group="Общий профиль Stamina",Minimum=0});
        return fields;
    }

    private static bool Include(BalanceDocument document, EditorPage page, string path)
    {
        var key = path.Split('/')[^1];
        if(page.RootPath.StartsWith("abilities/",StringComparison.Ordinal)&&key is "healthCost" or "allowFatalHealthCost")return false;
        if(page.RootPath.StartsWith("echoes/",StringComparison.Ordinal)&&key=="stars")return false;
        if (page.RootPath == "swordsman") return page.Section switch
        {
            "dash" => key.StartsWith("dash"),
            "passives" => !(key.StartsWith("dash") || key is "requiredDamage" or "interactionRange" or "dummyResetSeconds"), _ => true
        };
        if (page.RootPath == "defense") return page.Section switch
        {
            "stamina" => key == "recoveryDelay",
            "block" => key is "blockCost" or "blockMovementMultiplier" or "guardLeaseSeconds" or "blockHoldPerSecond" or "guardBreakSeconds" or "frontalAngleDegrees",
            "parry" => key.StartsWith("parry"),
            "control" => key.StartsWith("quickRecover") || key=="inputBufferSeconds", _ => true
        };
        if (page.RootPath == "progression") return (key is "practicePerLevel" or "skillLevelCap" or "powerPerSkillLevel") == (page.Section == "skills");
        if (page.Section == "rules") return path.Count(c => c == '/') == 1;
        if (page.Section == "stats") return path.Contains("/stats/");
        if (page.Section == "hurtbox") return path.Contains("/hurtbox/");
        if (page.Section == "resources") return key.StartsWith("base") || path.Contains("/modifiers/");
        if (page.RootPath.StartsWith("abilities/"))
        {
            var node = document.Read(page.RootPath)!; var kind = node["kind"]!.GetValue<string>();
            if (kind is "Melee" or "Recovery" && path==page.RootPath+"/"+key && key is "power" or "magicAttackScale" or "speed" or "range") return false;
            if (kind == "Recovery" && key is "damageFactor" or "rangeFactor" or "radius") return false;
            if (kind == "Dash" && key is "power" or "magicAttackScale" or "speed") return false;
        }
        return true;
    }

    public static EditorField Describe(EditorPage page, string path)
    {
        var key = path.Split('/')[^1]; var title = EditorLabels.Name(key); var group = "Основное";
        if (path == "progression/experienceCurve/scale")
            return new(path, "Масштаб EXP (A)", "Степенная кривая EXP · эксперимент", "A × (уровень + B)^1.5. Числа экспериментальные; пороги рассчитывает сервер.", false, false, 0);
        if (path == "progression/experienceCurve/offset")
            return new(path, "Смещение ранних уровней (B)", "Степенная кривая EXP · эксперимент", "B > −1. Изменение параметров не переносит существующий прогресс автоматически.", false, false, -1);
        var percent = EditorLabels.IsPercent(path); var integer = false; double? min = null, max = null;
        var help = "Значение проверяется серверным каталогом перед сохранением и запуском.";
        if (page.RootPath.StartsWith("balance/"))
        { group = "За каждое очко характеристики"; help = $"Добавка к показателю «{page.Title}» за одно очко этой характеристики. Точная дробь сохраняется."; min = 0; }
        else if(path.Contains("/threat/"))
        {
            group="Угроза NPC";min=0;
            title=key=="damagePerHp" ? "Threat за снятый HP" : "Threat за восстановленный HP союзнику против NPC";
            help="Веса конкретного существа. Урон считается после защиты и щитов, лечение — без overheal и только для уже участвующей в этом бою цели. Контроль не добавляет Threat; Taunt временно меняет цель. Contribution от этих весов не зависит.";
        }
        else if (path.Contains("/hurtbox/"))
        {
            group="Область получения попаданий";min=0;
            title=key=="height" ? "Полная высота капсулы · м" : key=="radius" ? "Радиус капсулы · м" : title;
            help="Боевое тело отдельно от навигации. Высота не меньше двух радиусов. Knockdown укладывает капсулу по направлению существа; Airborne поднимает её целиком.";
        }
        else if(path.Contains("/basicVolume/"))
        {
            group="Объём обычного удара";min=0;
            title=key=="thickness"?"Полная толщина дуги · м":key=="originHeight"?"Высота начала над ногами · м":title;
            help="Дуга движения оружия с округлённой толщиной. Внешняя граница, включая толщину, равна Range. Каждый пересечённый противник получает один контакт.";
        }
        else if(path.Contains("/basicProjectile/"))
        {
            group="Снаряд обычной атаки";min=0;
            title=key=="originHeight"?"Высота выпуска над ногами · м":key=="speed"?"Скорость снаряда · м/с":key=="radius"?"Радиус снаряда · м":title;
            help="Один ручной выпуск в начале Active. Сила/Accuracy/crit фиксируются при выпуске, защита проверяется при первом контакте. Препятствия останавливают полёт; доводки нет.";
        }
        else if (path.Contains("/stats/")) { group = "Базовые характеристики"; integer = true; min = 0; help = "Исходное значение без экипировки и временных эффектов."; }
        else if (path.Contains("/modifiers/")) { group = "Бонусы к показателям"; help = "Прибавляется к рассчитанному показателю. Допустимы отрицательные значения."; }
        else if (path.Contains("/condition/")) { group = "Прочность и ремонт"; min = 0; }
        else if(path.Contains("/dodge/"))
        {
            group="Уворот";min=0;
            help="Отдельный профиль: время действия сохраняется при коротком пути, контроль прерывает защиту. Скорость вычисляется из дальности и времени; Attack Speed и уровень навыка не меняют длительность.";
            if(key=="durationSeconds")title="Длительность уворота · с";
            if(key=="protectionSeconds")title="Начальное окно защиты от урона · с";
            if(key=="protectedThreats")title="Защищаемые виды урона";
        }
        else if (key.StartsWith("base")) group = "Ресурсы и базовые показатели";
        if (page.RootPath.StartsWith("abilities/"))
        {
            group = key is "range" or "radius" or "speed" or "rangeFactor" or "arcDegrees" ? "Попадание" :
                key is "stunSeconds" or "slowFraction" or "slowSeconds" or "executeThreshold" or "executeFactor" or "restoreStamina" || key.StartsWith("knock") ? "Эффекты" :
                key.Contains("Recovery") || key is "hitCancelAfterSeconds" or "minimumWindupSeconds" ? "Фазы и продолжения" : "Основное";
            min = 0;
            if(key=="projectileLaunchHeight")
            {group="Попадание";title="Высота выпуска над ногами · м";max=128;help="Точка выпуска и начало ручного 3D-прицеливания. Клиент получает это значение от сервера.";}
            if(path.Contains("/groundVolume/"))
            {
                group="Попадание";
                title=key=="height" ? "Высота области над поверхностью · м" : key=="requiresConnectedSurface" ? "Требовать связную поверхность" : "Форма области";
                if(key=="height")max=128;
                help="Cylinder: вертикальный цилиндр, радиус берётся из Radius навыка. Видимость проверяется от центра к телу цели; путь по земле требуется только при явном включении этого поля.";
            }
            if(path.Contains("/dodge/"))
            {
                group="Уворот";help="Скорость определяется дальностью и длительностью. Окно защищает от явно перечисленного урона; сопротивления и ACR контроля остаются обычными.";
                title=key=="durationSeconds"?"Длительность уворота · с":key=="protectionSeconds"?"Начальное окно защиты · с":key=="protectedThreats"?"Защищаемые виды урона":title;
            }
            if(path.Contains("/melee/"))
            {
                if(key=="originHeight"){group="Попадание";title="Начало удара над ногами · м";max=128;}
                if(key=="thickness"){group="Попадание";title="Полная толщина дуги · м";max=128;}
                if(key=="firstTargetOnly")help="Одна цель: первый контакт по траектории. Выключено: каждый пересечённый противник, один раз за применение.";
            }
            if(path.Contains("/passive/"))
            {
                group="Пассивный навык";help="Работает после изучения без ячейки. Условия проверяются сервером при изменении состояния.";
                if(path.Contains("/modifiers/")){group="Пассивный навык / Бонусы";min=null;}
            }
            if(path.Contains("/effects/") && path.Contains("/control/"))
            {
                group="Эффекты / Контроль";
                title=key=="strength" ? "Сила контроля" : key=="height" ? "Высота полёта · м" : title;
                help="Параметр отдельного контроля. Иммунитет, сопротивления и ACR применяются общим серверным исполнителем.";
                if(path.Contains("/removal/"))
                {
                    group="Эффекты / Контроль / Снятие";
                    help="Разрешение Cleanse и теги этого экземпляра контроля. Истечение, смерть и специальные освобождения используют собственные правила.";
                }
            }
            if(path.Contains("/action/"))
            {
                var phase=path.Contains("/preparation/") ? "Подготовка" : path.Contains("/active/") ? "Активная фаза" : "Восстановление";
                group="Фазы действия / "+phase;
                title=key=="seconds" ? "Длительность · с" : key=="movementMultiplier" ? "Множитель движения" : title;
                help=key=="seconds" ? "Базовая длительность фазы. Выбранный в данных источник AttackSpeed/CastSpeed масштабирует время." :
                    key=="movementMultiplier" ? "Множитель скорости передвижения именно в этой фазе; 0 запрещает движение." : help;
            }
            if(path.Contains("/channel/"))
            {
                group="Поддержание навыка";
                help=key is "manaPerSecond" or "staminaPerSecond" ? "Дробный расход за фактически обеспеченное время поддержания, без округления до целых." :
                    key=="intervalSeconds" ? "Базовый интервал воздействий; intervalSpeed в данных явно задаёт влияние скорости." :
                    "Числовой предел поддержания. Явный null в данных разрешает поддержание до прекращения.";
            }
            if(path.Contains("/charge/"))
            {
                group="Зарядка навыка";
                help=key=="fullSeconds" ? "До полного заряда; speed явно выбирает None, AttackSpeed или CastSpeed." :
                    "Множитель силы на соответствующем конце линейной кривой зарядки. atFull явно задаёт Release или Hold.";
            }
        }
        if (page.RootPath == "swordsman")
        {
            group = key switch
            {
                "basicDamageBonus" => "Владение мечом", "rhythmHits" or "rhythmWindow" or "rhythmStamina" => "Боевой ритм",
                "blockAbsorptionBonus" => "Крепкий хват", "footworkBonus" or "footworkSeconds" => "Работа ног",
                "focusBonus" or "parryWindow" => "Собранность", _ => page.Title
            };
            min = 0;
        }
        if (page.RootPath == "defense") { group = page.Title; min = 0; }
        if (key is "levelCap" or "skillLevelCap") { integer = true; min = 2; max = 1000; }
        if (key is "initialStatPoints" or "statPointsPerLevel") { integer = true; min = key == "initialStatPoints" ? 0 : 1; max = 100; }
        if (key is "experiencePerLevel" or "discoveryExperience" or "practicePerLevel") { integer = true; min = 1; max = 1000000; }
        if (key is "rhythmHits" or "successfulUses" or "discoveryMask") { integer = true; min = 0; }
        if (key is "executeThreshold" or "slowFraction" or "powerPerSkillLevel" or "basicDamageBonus" or "blockAbsorptionBonus" or "focusBonus" or "footworkBonus" or "blockMovementMultiplier") max = 1;
        if (key == "dashRange") max = 20;
        if (key == "arcDegrees") max = 360;
        if(key=="maxCharges") { integer=true; min=1; max=255; }
        if (key is "hitRecoverySeconds" or "missRecoverySeconds" or "minimumWindupSeconds" or "knockupSeconds" or "knockdownSeconds") max=2;
        if (key is "knockbackDistance" or "knockupHeight" or "quickRecoverRange") max=3;
        if (key is "knockbackSpeed" or "quickRecoverSpeed") max=20;
        if (key == "inputBufferSeconds") max=.3;
        help = key switch
        {
            "rhythmHits" => "Сколько успешных обычных попаданий нужно для восстановления выносливости.",
            "rhythmWindow" => "Пауза между попаданиями, после которой серия сбрасывается.",
            "rhythmStamina" => "Сколько выносливости восстановится при завершении серии.",
            "basicDamageBonus" => "Усиливает обычную атаку мечом. Не увеличивает базу урона приёмов.",
            "blockAbsorptionBonus" => "Прибавляется к поглощению блока; не меняет его прочность.",
            "footworkBonus" => "Прибавка к скорости обычного бега после прямого попадания.",
            "footworkSeconds" => "Время действия ускорения после попадания.",
            "focusBonus" => "Усиление следующей попытки удара после успешного парирования.",
            "parryWindow" when page.RootPath == "swordsman" => "Сколько времени после успешного парирования доступно усиление.",
            "damageFactor" => "Урон от базового удара оружия: 120,0% означает ×1,2. Дополнительно действует сила уровня навыка.",
            "rangeFactor" => "Дальность приёма относительно дальности оружия.",
            "weaponScale" => "Доля базового удара оружия за один тик. Для Рассечения четыре тика по 15% дают суммарно 60%.",
            "inCombatSeconds" => "Время боевого статуса после подтверждённого враждебного контакта или поддержки участника боя.",
            "disconnectBodySeconds" => "При обрыве связи тело продолжает обычную серверную симуляцию до истечения этого срока.",
            "corpseSeconds" => "Срок тела после смерти по GDD 01; отдельный runtime смерти ещё находится в реализации.",
            "executeThreshold" => "Доля оставшегося HP цели, при которой применяется урон добивания.",
            "executeFactor" => "Урон от базового удара по цели ниже порога добивания.",
            "guardLeaseSeconds" => "Технический срок удержания блока без нового подтверждения от клиента.",
            "discoveryMask" => "Техническая битовая маска требуемых открытий; детали открытия остаются на сервере.",
            "experiencePerLevel" => "До следующего уровня требуется текущее значение уровня × этот коэффициент.",
            "practicePerLevel" => "Общая формула: текущий уровень навыка × это число. Свои таблицы навыков имеют приоритет.",
            "requiredDamage" => "Урон тренировочным мечом по манекенам для предложения профессии.",
            "interactionRange" when path.StartsWith("swordsman/") => "Максимальная дистанция разговора с тренером для получения меча и профессии, в метрах.",
            "dummyResetSeconds" => "Через сколько секунд после разрушения манекен арены полностью восстанавливает здоровье. Это не регенерация во время боя.",
            _ => help
        };
        if(page.RootPath=="balance/health")help="Доля базового HP за 1 характеристику: STR 0,05 = 5% базы, VIT 0,1 = 10% базы. При базовом HP=100 это +5 HP/STR и +10 HP/VIT. Формула общая для профилей существ.";
        return new(path, title, group, help, percent, integer, min, max);
    }
}
