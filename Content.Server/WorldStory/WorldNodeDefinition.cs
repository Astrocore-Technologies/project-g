using System.Numerics;
using System.Text;
namespace Content.Server.WorldStory;
public sealed record WorldNodeDefinition
{
    public string Key { get; init; } = "prototype-crossing";
    // Quiet regions still own durable loot/economy/audit state without a repair event.
    public bool Interactive { get; init; } = true;
    public string KeeperName { get; init; } = "Хранитель переправы";
    public float X { get; init; } = -3;
    public float Z { get; init; } = -2;
    public float InteractionRange { get; init; } = 2;
    public int ContributionsRequired { get; init; } = 2;
    public int PatrolHitsRequired { get; init; } = 1;
    public float PatrolAggroRadius { get; init; } = 2;
    public int[] OpeningCells { get; init; } = [404,405,434,435,464,465];
    public string[] KeeperLines { get; init; } = ["Нужна помощь с ремонтом и разведкой восточной угрозы.","Переправа открыта. Восточная угроза пока рядом.","Дозор собран. Переправа всё ещё повреждена.","Переправа открыта и под защитой дозора."];
    public string[] Rumors { get; init; } = ["У переправы ждут добровольцев.","Путники восстановили короткий путь.","На восточном берегу появился дозор.","Дорога у переправы стала спокойнее.","Хранитель предупреждает: надвигается буря."];
    public Vector2 Position => new(X,Z);
    public void Validate()
    {
        static bool Text(string s,int length,int bytes) => !string.IsNullOrWhiteSpace(s) && s.Length<=length && Encoding.UTF8.GetByteCount(s)<=bytes && !s.Any(char.IsControl);
        if (Key.Length is < 1 or > 64 || Key.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_') || !Text(KeeperName,32,96) || !float.IsFinite(X) || !float.IsFinite(Z) || !float.IsFinite(InteractionRange) || InteractionRange is < 0.5f or > 5 || ContributionsRequired is < 1 or > 64 || PatrolHitsRequired is < 1 or > 100 || !float.IsFinite(PatrolAggroRadius) || PatrolAggroRadius is < 0.5f or > 10 || OpeningCells is null || OpeningCells.Length > 16 || Interactive && OpeningCells.Length == 0 || !Interactive && OpeningCells.Length != 0 || OpeningCells.Distinct().Count()!=OpeningCells.Length || OpeningCells.Any(i=>i<0) || KeeperLines is null || KeeperLines.Length!=4 || Rumors is null || Rumors.Length!=5 || KeeperLines.Concat(Rumors).Any(s=>!Text(s,100,300))) throw new InvalidDataException("Invalid world node definition.");
    }
}
