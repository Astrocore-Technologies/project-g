namespace ProjectG.UI;

/// <summary>Public labels for explicitly learned local QA abilities; contains no hidden content.</summary>
internal static class CombatTestUi
{
    public static string Name(ushort id) => id switch
    {
        600=>"Тест: Channel · лечение",601=>"Тест: Charged · снаряд",602=>"Тест: Hold · лечение",
        603=>"Тест: Toggle · лечение",604=>"Тест: Passive · маг. защита",605=>"Тест: лечение",
        606=>"Тест: щит",607=>"Тест: HoT",608=>"Тест: DoT",609=>"Тест: очищение",
        610=>"Тест: рассеивание",611=>"Тест: воскрешение",612=>"Тест: Steal и отражение",
        613=>"Тест: сброс зарядов снаряда",614=>"Тест: Cast с ценой HP",615=>"Тест: восстановление ресурсов",
        620=>"Тест: Stun",621=>"Тест: Root",622=>"Тест: Slow",623=>"Тест: Silence",624=>"Тест: Disarm",
        625=>"Тест: Knockback",626=>"Тест: Knockdown",627=>"Тест: Fear",628=>"Тест: Sleep",
        629=>"Тест: Blind",630=>"Тест: Taunt",631=>"Тест: Pull",632=>"Тест: Freeze",633=>"Тест: Knockup",
        _=>"Тестовый навык"
    };
}
