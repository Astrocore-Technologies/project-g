using Content.Server.Data;
namespace Content.Server.Economy;
public sealed record EconomyDefinition
{
    public required string BaseWeapon {get;init;}
    public required string EvolvedWeapon {get;init;}
    public required int[] EnhancementChances {get;init;}
    public required double AttackPerLevel {get;init;}
    public required int FailureDurabilityLoss {get;init;}
    public required ushort IngotId {get;init;}
    public required ushort WoodId {get;init;}
    public required ushort OreId {get;init;}
    public required int EvolutionIngots {get;init;}
    public required int EvolutionWood {get;init;}
    public required int AttemptIngots {get;init;}
    public required int OrePrice {get;init;}
    public required int CoinLimit {get;init;}
    public required int ListingsPerSeller {get;init;}
    public required int ListingLimit {get;init;}
    public required int RefillSeconds {get;init;}
    public void Validate(ContentCatalog c)
    {
        if(BaseWeapon==EvolvedWeapon || !c.Items.TryGetValue(BaseWeapon,out var a) || !c.Items.TryGetValue(EvolvedWeapon,out var b) || a.Name.Length>21 || b.Name.Length>21 || System.Text.Encoding.UTF8.GetByteCount(a.Name)>45 || System.Text.Encoding.UTF8.GetByteCount(b.Name)>45 || b.Modifiers.MeleeAttack<a.Modifiers.MeleeAttack || b.Modifiers.MeleeAttack-a.Modifiers.MeleeAttack>1000 || a.Condition is null || b.Condition is null || a.Condition.Maximum!=b.Condition.Maximum || a.Slot!=b.Slot || EnhancementChances is not {Length:>0 and <=5} || EnhancementChances.Any(v=>v is <0 or >100) || !double.IsFinite(AttackPerLevel) || AttackPerLevel is <=0 or >100 || FailureDurabilityLoss is <1 or >100 || EvolutionIngots is <1 or >999 || EvolutionWood is <1 or >999 || AttemptIngots is <1 or >999 || OrePrice is <1 or >1000 || CoinLimit is <1 or >1000000 || ListingLimit is <1 or >8 || ListingsPerSeller is <1 or >2 || RefillSeconds is <1 or >86400 || c.Crafting is null || new[]{IngotId,WoodId,OreId}.Distinct().Count()!=3 || new[]{IngotId,WoodId,OreId}.Any(id=>!c.Crafting.Materials.Any(m=>m.Id==id)))throw new ArgumentException("Invalid economy balance/references.");
    }
}
