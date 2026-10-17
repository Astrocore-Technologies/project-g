using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Uses the same resource, effect, defense and cooldown restoration as character combat.</summary>
internal static class CompanionRuntimeState
{
    internal static SavedEchoCombat Capture(CombatSimulation combat, AbilitySimulation abilities,
        NetworkEntityId id, string definitionId, BaseStats stats, byte? activeSlot, long now, SavedSurface? surface = null)
    {
        var actor = combat.Get(id);
        if (actor.Kind != CombatEntityKind.Echo || !actor.CompanionOwner.IsValid)
            throw new ArgumentException("Only a bound Echo has companion state.");
        var saved = new SavedEchoCombat
        {
            InstanceId = actor.SourceIdentity.Id, DefinitionId = definitionId, ProfileId = actor.DefinitionId,
            Availability = actor.Health <= 0 ? SavedEchoAvailability.RequiresRestoration :
                activeSlot.HasValue ? SavedEchoAvailability.Active : SavedEchoAvailability.Ready,
            ActiveSlot = actor.Health <= 0 ? null : activeSlot,
            Stats = stats, X = actor.Position.X, Z = actor.Position.Y, Surface = surface,
            Facing = new(1, actor.Facing.X, actor.Facing.Y), Health = actor.Health, Mana = abilities.Mana(id),
            Defense = combat.CaptureDefense(id), CombatControl = combat.CaptureControl(id),
            DeathId = actor.DeathId, AttackCooldownSeconds = Math.Max(0, actor.ReadyAt - combat.Time),
            InCombatSeconds = combat.CombatRemaining(id), ActivePassiveModifiers = combat.CapturePassiveModifiers(id),
            Cooldowns = abilities.CaptureCooldowns(id), SavedAtUnixMilliseconds = now
        };
        saved.Validate(); return saved;
    }

    internal static IReadOnlyList<SavedCombatEffect> CaptureEffects(CombatSimulation combat, NetworkEntityId id)
    {
        var target = combat.Get(id).SourceIdentity.Id;
        return combat.CaptureEffects(id).Select(effect => effect with { TargetEcho = target }).ToArray();
    }

    internal static void Restore(CombatSimulation combat, AbilitySimulation abilities, NetworkEntityId id,
        SavedEchoCombat saved, IReadOnlyList<SavedCombatEffect> effects, double offline, bool loggingIn = true)
    {
        saved.Validate();
        if (!double.IsFinite(offline) || offline < 0) throw new ArgumentOutOfRangeException(nameof(offline));
        var actor = combat.Get(id);
        if (actor.Kind != CombatEntityKind.Echo || actor.SourceIdentity != new CombatSourceId(CombatSourceKind.Echo, saved.InstanceId) ||
            actor.DefinitionId != saved.ProfileId || saved.Availability == SavedEchoAvailability.RequiresRestoration ||
            effects.Any(effect => effect.TargetEcho != saved.InstanceId))
            throw new InvalidDataException("Saved Echo does not match this combat body.");
        var previousStats = combat.StatsAtSave(id, effects, saved.ActivePassiveModifiers);
        if (saved.Health > previousStats.MaxHealth || saved.Mana > Math.Max(0, previousStats.MaxMana) ||
            saved.Defense.Stamina > Math.Max(0, previousStats.MaxStamina))
            throw new InvalidDataException("Saved Echo resources exceed the previous maximum; content migration is required.");
        actor.Health = saved.Health;
        combat.RestoreFacing(id, saved.Facing);
        combat.RestoreCombatPresence(id, saved.InCombatSeconds, offline);
        combat.RestoreEffects(id, effects, offline, loggingIn, saved.CombatControl.Phase);
        actor.Health = Math.Min(saved.Health, actor.Stats.MaxHealth);
        combat.RefreshHealthRecovery(id);
        actor.ReadyAt = combat.Time + Math.Max(0, saved.AttackCooldownSeconds - offline);
        abilities.Restore(id, saved.ActorState() with { Mana = Math.Min(saved.Mana, Math.Max(0, actor.Stats.MaxMana)) }, offline);
        combat.RestoreDefense(id, saved.Defense with { Stamina = Math.Min(saved.Defense.Stamina, Math.Max(0, actor.Stats.MaxStamina)) }, offline);
        combat.RestoreControl(id, saved.CombatControl, offline, finishForcedMotion: loggingIn);
        combat.RestoreLife(id, new(1, saved.DeathId, false, 0, 0), offline);
    }
}
