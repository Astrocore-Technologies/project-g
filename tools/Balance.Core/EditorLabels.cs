namespace ProjectG.Balance;

/// <summary>Authoring labels and units only. Validation and gameplay calculations remain in the server.</summary>
public static class EditorLabels
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["sword_rising"]="Восходящий удар", ["hitRecoverySeconds"]="Восстановление после попадания · с",
        ["missRecoverySeconds"]="Восстановление после промаха / защиты · с", ["hitCancelAfterSeconds"]="Продолжение после попадания · с",
        ["knockupSeconds"]="Полёт · с", ["knockdownSeconds"]="Падение · с", ["knockupHeight"]="Высота полёта · м",
        ["knockbackDistance"]="Отбрасывание · м", ["knockbackSpeed"]="Скорость отбрасывания · м/с", ["minimumWindupSeconds"]="Минимальный замах · с",
        ["inputBufferSeconds"]="Буфер действия · с", ["controlResetSeconds"]="Сброс повторного контроля · с",
        ["repeatedControlFactor"]="Сила второго контроля", ["quickRecoverCost"]="Стоимость Quick Recover",
        ["quickRecoverCooldown"]="Перезарядка Quick Recover · с", ["quickRecoverRange"]="Перекат Quick Recover · м",
        ["quickRecoverSpeed"]="Скорость переката · м/с",
        ["balance"]="Влияние характеристик", ["progression"]="Уровни и опыт", ["defense"]="Защита и выносливость", ["swordsman"]="Мечник · пассивки", ["melee"]="Приём", ["stats"]="Характеристики", ["modifiers"]="Добавки",
        ["weapons"]="Оружие", ["abilities"]="Навыки", ["creatures"]="Существа", ["items"]="Предметы", ["professions"]="Профессии",
        ["strength"]="STR · Сила", ["agility"]="AGI · Ловкость", ["vitality"]="VIT · Живучесть", ["intelligence"]="INT · Интеллект", ["dexterity"]="DEX · Точность", ["luck"]="LUK · Удача",
        ["meleeAttack"]="Ближняя атака", ["rangedAttack"]="Дальняя атака", ["magicAttack"]="Магическая атака", ["physicalDefense"]="Физическая защита", ["magicDefense"]="Магическая защита",
        ["health"]="Рост HP", ["mana"]="Рост маны", ["healthRecovery"]="Регенерация HP", ["manaRecovery"]="Регенерация маны", ["attackSpeed"]="Скорость атаки", ["castSpeed"]="Скорость каста",
        ["baseHealth"]="Базовое HP", ["baseMana"]="Базовая мана", ["baseHealthRecovery"]="Базовый реген HP / с", ["baseManaRecovery"]="Базовый реген маны / с",
        ["attack"]="Атака оружия", ["attackIntervalSeconds"]="Интервал атаки · с", ["range"]="Дальность · м", ["radius"]="Радиус · м", ["speed"]="Скорость · м/с",
        ["power"]="Базовая сила", ["magicAttackScale"]="Коэффициент магической атаки", ["manaCost"]="Стоимость маны", ["staminaCost"]="Стоимость выносливости", ["cooldownSeconds"]="Перезарядка · с", ["castSeconds"]="Применение · с",
        ["damageFactor"]="Урон от базового удара", ["rangeFactor"]="Дальность от базовой", ["armorIgnore"]="Игнорирование брони", ["bleedFactor"]="Урон кровотечения", ["bleedSeconds"]="Кровотечение · с",
        ["stunSeconds"]="Оглушение · с", ["slowFraction"]="Замедление", ["slowSeconds"]="Замедление · с", ["executeThreshold"]="Порог HP цели", ["executeFactor"]="Урон добивания", ["restoreStamina"]="Восстановление выносливости",
        ["baseBlockDamage"]="Блокируемый урон", ["baseCriticalChance"]="Базовый шанс крита", ["basicDamageBonus"]="Бонус автоатаки", ["blockRemainderReduction"]="Доп. снижение остатка урона", ["focusBonus"]="Бонус концентрации", ["footworkBonus"]="Бонус скорости передвижения",
        ["dashRange"]="Рывок · м", ["dashCost"]="Стоимость рывка", ["dashCooldown"]="Перезарядка рывка · с", ["requiredDamage"]="Урон для получения профессии", ["dummyResetSeconds"]="Восстановление манекена · с",
        ["maxStamina"]="Максимум выносливости", ["recoveryPerSecond"]="Реген выносливости / с", ["recoveryDelay"]="Задержка регена · с", ["parryWindow"]="Окно парирования / ответа · с", ["parryCooldown"]="Перезарядка парирования · с",
        ["blockCost"]="Стоимость блока", ["parryCost"]="Стоимость парирования", ["dodgeCost"]="Стоимость уклонения", ["blockMovementMultiplier"]="Скорость при блоке",
        ["levelCap"]="Максимальный уровень", ["initialStatPoints"]="Очки на старте", ["statPointsPerLevel"]="Очки за уровень", ["experiencePerLevel"]="Коэффициент опыта", ["skillLevelCap"]="Общий максимум навыков без своей таблицы", ["powerPerSkillLevel"]="Общее усиление за уровень (без своей таблицы)", ["practicePerLevel"]="Общая практика × текущий уровень (без своей таблицы)",
        ["rhythmHits"]="Ударов в серии", ["rhythmWindow"]="Окно серии · с", ["rhythmStamina"]="Выносливость за серию", ["footworkSeconds"]="Длительность подвижности · с",
        ["sword_thrust"]="Выпад", ["sword_sweep"]="Размашистый удар", ["sword_rend"]="Рассечение", ["sword_breaker"]="Проламывающий удар", ["sword_whirl"]="Круговой удар", ["sword_pommel"]="Удар навершием", ["sword_hamstring"]="Подрез", ["sword_riposte"]="Ответный удар", ["sword_breath"]="Перевести дух", ["sword_finisher"]="Решающий удар",
        ["trail_impulse"]="Импульс троп",
        ["training_sword"]="Тренировочный меч", ["arena_training_sword"]="Тренировочный меч арены", ["arena_dummy"]="Манекен", ["test_creature"]="Обычный моб", ["test_boss"]="Босс", ["test_adventurer"]="Базовый персонаж",
        ["training_bow"]="Тренировочный лук", ["training_staff"]="Тренировочный посох", ["boss_claw"]="Когти босса",
        ["Training sword"]="Тренировочный меч", ["Training armor"]="Тренировочная броня",
        ["test_projectile"]="Магический снаряд", ["test_ground_area"]="Область поражения", ["test_dash"]="Обычный рывок", ["boss_ground_area"]="Область босса", ["discovery_bolt"]="Открытый магический болт",
        ["meleeWeapon"]="Усиление ближнего оружия", ["rangedWeapon"]="Усиление дальнего оружия", ["healthItem"]="Эффект предметов HP", ["manaItem"]="Эффект предметов маны", ["criticalOdds"]="Рост шанса крита", ["defenseScale"]="Масштаб защиты",
        ["discoveryExperience"]="Опыт за открытие", ["interactionRange"]="Дистанция разговора · м", ["guardLeaseSeconds"]="Срок подтверждения блока · с", ["arcDegrees"]="Угол удара · °", ["successfulUses"]="Успешных применений для открытия", ["discoveryMask"]="Маска открытий",
        ["maxHealth"]="Максимум HP", ["maxMana"]="Максимум маны", ["maximum"]="Максимальная прочность", ["wearPerHit"]="Износ за попадание", ["durabilityPerMaterial"]="Ремонт за материал", ["condition"]="Прочность",
        ["meleeWeaponMultiplier"]="Усиление ближнего оружия", ["rangedWeaponMultiplier"]="Усиление дальнего оружия", ["healthItemMultiplier"]="Усиление предметов HP", ["manaItemMultiplier"]="Усиление предметов маны", ["attackSpeedMultiplier"]="Бонус скорости атаки", ["castSpeedMultiplier"]="Бонус скорости каста", ["criticalChance"]="Добавка к шансу крита", ["blockDamage"]="Добавка к блокированию",
        ["practicePerUse"]="Практика за применение", ["practiceToNext"]="Практика до следующего уровня", ["powerMultiplier"]="Сила навыка"
    };
    private static readonly HashSet<string> Percent = ["damageFactor","rangeFactor","armorIgnore","bleedFactor","slowFraction","executeThreshold","executeFactor","baseBlockDamage","baseCriticalChance","basicDamageBonus","blockRemainderReduction","focusBonus","footworkBonus","blockMovementMultiplier","powerPerSkillLevel","repeatedControlFactor"];
    public static string Name(string key) => Names.GetValueOrDefault(key, key);
    public static bool IsPercent(string path) => Percent.Contains(path.Split('/')[^1]) || path.Split('/')[^1] is "powerMultiplier" or "magicAttackScale" or "meleeWeaponMultiplier" or "rangedWeaponMultiplier" or "healthItemMultiplier" or "manaItemMultiplier" or "attackSpeedMultiplier" or "castSpeedMultiplier" or "criticalChance" or "blockDamage";
    public static string Label(string path) => string.Join(" / ", path.Split('/').Skip(2).Select(Name)) is { Length: > 0 } text ? text : Name(path.Split('/')[^1]);
}
