using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>Validated server-only snapshot; never send the catalog to clients.</summary>
public sealed class ContentCatalog
{
    public Content.Server.Combat.DefenseBalance Defense { get; }
    public Content.Server.Professions.SwordsmanDefinition? Swordsman { get; }
    public Content.Server.WorldStory.WorldNodeDefinition? WorldNode { get; }
    public Content.Server.StarterZone.StarterZoneDefinition? StarterZone { get; }
    public Content.Server.Crafting.CraftingDefinition? Crafting { get; }
    public Content.Server.Quests.DeliveryQuestDefinition? DeliveryQuest {get;}
    public Content.Server.Pvp.PvpDefinition? Pvp {get;}
    public Content.Server.Economy.EconomyDefinition? Economy { get; }
    public const int SchemaVersion = 3;
    public const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxDefinitions = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    private ContentCatalog(ContentDocument document)
    {
        if (document.SchemaVersion != SchemaVersion || document.BalanceVersion <= 0)
            throw new ArgumentException("Unsupported schemaVersion or invalid balanceVersion.");
        ArgumentNullException.ThrowIfNull(document.Balance);
        document.Balance.Validate();
        ArgumentNullException.ThrowIfNull(document.Defense); document.Defense.Validate(); Defense=document.Defense;
        document.StarterZone?.Validate(); StarterZone=document.StarterZone;
        document.WorldNode?.Validate(); WorldNode = document.WorldNode;
        Balance = document.Balance;
        BalanceVersion = document.BalanceVersion;
        ArgumentNullException.ThrowIfNull(document.Progression); document.Progression.Validate();
        Progression = document.Progression;
        Weapons = Index(document.Weapons, item => item.Id, "weapons");
        Abilities = Index(document.Abilities, item => item.Id, "abilities");
        Creatures = Index(document.Creatures, item => item.Id, "creatures");
        Items = Index(document.Items, item => item.Id, "items");
        foreach (var item in Items.Values)
        {
            Check(item.Name is { Length: > 0 and <= 24 } && System.Text.Encoding.UTF8.GetByteCount(item.Name) <= 48,
                $"item {item.Id}: invalid public name");
            Check(item.Slot is Content.Shared.Network.EquipmentSlot.Weapon or Content.Shared.Network.EquipmentSlot.Armor,
                $"item {item.Id}: unknown equipment slot");
            Check(item.Modifiers is not null && item.Modifiers.IsValid(), $"item {item.Id}: invalid modifiers");
            Check(item.Slot == Content.Shared.Network.EquipmentSlot.Weapon
                ? item.WeaponId is not null && Weapons.TryGetValue(item.WeaponId, out var weapon) && weapon.Kind == WeaponKind.Melee
                : item.WeaponId is null, $"item {item.Id}: invalid weapon reference/kind");
        }

        document.Crafting?.Validate(this); Crafting=document.Crafting;
        document.DeliveryQuest?.Validate(this); DeliveryQuest=document.DeliveryQuest;
        foreach(var item in Items.Values) if(item.Condition is { } condition)
        { condition.Validate(); Check(item.Slot==Content.Shared.Network.EquipmentSlot.Weapon && Crafting is not null && Crafting.Materials.Any(m=>m.Id==condition.RepairMaterialId), "Invalid durability material/slot reference."); }
        document.Economy?.Validate(this); Economy=document.Economy; document.Pvp?.Validate(); Pvp=document.Pvp;
        foreach (var weapon in Weapons.Values)
        {
            Check(Enum.IsDefined(weapon.Kind), $"weapon {weapon.Id}: unknown kind");
            NonNegative(weapon.Attack, $"weapon {weapon.Id}.attack");
            Positive(weapon.Range, $"weapon {weapon.Id}.range");
            Positive(weapon.AttackIntervalSeconds, $"weapon {weapon.Id}.attackIntervalSeconds");
        }
        var abilityIds = new HashSet<ushort>();
        foreach (var ability in Abilities.Values)
        {
            Check(ability.NetworkId != 0 && abilityIds.Add(ability.NetworkId), $"ability {ability.Id}: duplicate/zero networkId");
            Check(Enum.IsDefined(ability.Kind), $"ability {ability.Id}: unknown kind");
            NonNegative(ability.Power, $"ability {ability.Id}.power");
            NonNegative(ability.ManaCost, $"ability {ability.Id}.manaCost");
            NonNegative(ability.CastSeconds, $"ability {ability.Id}.castSeconds");
            Positive(ability.CooldownSeconds, $"ability {ability.Id}.cooldownSeconds");
            Positive(ability.Range, $"ability {ability.Id}.range");
            Positive(ability.Radius, $"ability {ability.Id}.radius");
            NonNegative(ability.Speed, $"ability {ability.Id}.speed");
            Check(ability.Kind is not (AbilityKind.Projectile or AbilityKind.Dash) || ability.Speed > 0, $"ability {ability.Id}: speed must be positive");
            NonNegative(ability.StaminaCost, $"ability {ability.Id}.staminaCost");
            Check(ability.StaminaCost <= Defense.MaxStamina, "Ability stamina exceeds capacity.");
            Check((ability.Kind is AbilityKind.Melee or AbilityKind.Recovery) == (ability.Melee is not null), "Missing/unexpected melee technique.");
            ability.Melee?.Validate();
            NonNegative(ability.MagicAttackScale, $"ability {ability.Id}.magicAttackScale");
        }
        Check(Abilities.Values.Any(a => a.NetworkId == Progression.DiscoverySkillId && a.Kind != AbilityKind.Dash), "progression: unknown discovery skill");
        if (document.Professions is null || document.Professions.Length > 8) throw new ArgumentException("Invalid profession budget.");
        var professionIds = new HashSet<ushort>();
        foreach (var profession in document.Professions)
            Check(profession is not null && profession.Id != 0 && professionIds.Add(profession.Id) &&
                profession.Name is { Length: > 0 and <= 24 } && System.Text.Encoding.UTF8.GetByteCount(profession.Name) <= 72 &&
                (profession.RequiresTrainer || profession.DiscoveryMask is > 0 and <= 3 && profession.SuccessfulUses is > 0 and <= 10000) &&
                profession.SkillId is not null && Abilities.TryGetValue(profession.SkillId,out var skill) && skill.Kind != AbilityKind.Dash,
                "Invalid profession definition/reference.");
        Professions = Array.AsReadOnly((Content.Server.Professions.ProfessionDefinition[])document.Professions.Clone());
        foreach (var profession in Professions)
            Check(profession.AdditionalSkills is { Length: <= 9 } && profession.AllSkills.Distinct().Count() == profession.AdditionalSkills.Length + 1 &&
                profession.AllSkills.All(id => Abilities.TryGetValue(id, out var a) && a.Kind != AbilityKind.Dash), "Invalid profession skill list.");
        document.Swordsman?.Validate(this); Swordsman = document.Swordsman;
        foreach (var creature in Creatures.Values)
        {
            Check(creature.Modifiers is not null && creature.Modifiers.IsValid(),
                $"creature {creature.Id}: derived modifiers must be finite");
            Positive(creature.BaseHealth, $"creature {creature.Id}.baseHealth");
            Positive(creature.BaseMana, $"creature {creature.Id}.baseMana");
            NonNegative(creature.BaseHealthRecovery, $"creature {creature.Id}.baseHealthRecovery");
            NonNegative(creature.BaseManaRecovery, $"creature {creature.Id}.baseManaRecovery");
            Check(creature.WeaponId is not null && Weapons.ContainsKey(creature.WeaponId),
                $"creature {creature.Id}: unknown weaponId {creature.WeaponId}");
            Check(!creature.AbilityIds.IsDefault && creature.AbilityIds.Length <= MaxDefinitions,
                $"creature {creature.Id}: invalid abilityIds count");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in creature.AbilityIds)
                Check(id is not null && Abilities.ContainsKey(id) && seen.Add(id),
                    $"creature {creature.Id}: unknown or duplicate abilityId {id}");
            Check(!creature.StarterItemIds.IsDefault && creature.StarterItemIds.Length <= Content.Shared.Network.NetworkConstants.MaxInventoryItems,
                $"creature {creature.Id}: invalid starter inventory");
            foreach (var id in creature.StarterItemIds)
                Check(id is not null && Items.ContainsKey(id), $"creature {creature.Id}: unknown starter item {id}");
        }
    }

    public Content.Server.Progression.ProgressionBalance Progression { get; }
    public IReadOnlyList<Content.Server.Professions.ProfessionDefinition> Professions { get; }
    public int BalanceVersion { get; }
    public StatBalance Balance { get; }
    public FrozenDictionary<string, WeaponDefinition> Weapons { get; }
    public FrozenDictionary<string, AbilityDefinition> Abilities { get; }
    public FrozenDictionary<string, CreatureDefinition> Creatures { get; }
    public FrozenDictionary<string, ItemDefinition> Items { get; }

    public static ContentCatalog LoadFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Check(file.Length is > 0 and <= MaxFileBytes, "content file exceeds size budget or is empty");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return Parse(bytes);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Cannot read content '{path}': {exception.Message}", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"Invalid content '{path}': {exception.Message}", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Invalid content '{path}': {exception.Message}", exception);
        }
    }

    public static ContentCatalog Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            Check(utf8.Length is > 0 and <= MaxFileBytes, "content file exceeds size budget or is empty");
            // Duplicate JSON keys otherwise silently overwrite earlier values.
            using var json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(json.RootElement);
            var document = JsonSerializer.Deserialize<ContentDocument>(utf8, JsonOptions)
                ?? throw new ArgumentException("Content document must not be null.");
            return new ContentCatalog(document);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException($"Invalid content: {exception.Message}", exception);
        }
    }

    private static FrozenDictionary<string, T> Index<T>(T[]? values, Func<T, string> getId, string kind)
        where T : class
    {
        Check(values is { Length: > 0 and <= MaxDefinitions }, $"{kind}: invalid definition count");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in values!)
        {
            Check(item is not null, $"{kind}: null definition");
            var id = getId(item!);
            Check(IsValidId(id), $"{kind}: invalid ID '{id}'");
            Check(result.TryAdd(id, item!), $"{kind}: duplicate ID '{id}'");
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static bool IsValidId(string? id)
    {
        if (id is not { Length: > 0 and <= 64 })
            return false;
        foreach (var value in id)
            if (!(value is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.'))
                return false;
        return true;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Check(names.Add(property.Name), $"duplicate JSON property '{property.Name}'");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                RejectDuplicateProperties(child);
    }

    private static void Positive(double value, string name) =>
        Check(double.IsFinite(value) && value > 0, $"{name}: expected finite positive value");

    private static void NonNegative(double value, string name) =>
        Check(double.IsFinite(value) && value >= 0, $"{name}: expected finite non-negative value");

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }
}
