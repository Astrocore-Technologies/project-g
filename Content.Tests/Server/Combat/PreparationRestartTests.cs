using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class PreparationRestartTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CrashBeforeCastOrChargedActivationEndsPreparationWithoutCostChargeCooldownOrPayload(bool charged)
    {
        var data=ChargedAbilityTests.Document();var skill=data["abilities"]![1]!;
        if(!charged)
        {
            skill.AsObject().Remove("charge");skill["action"]!["form"]="Cast";
            skill["action"]!["preparation"]!["seconds"]=.5;
        }
        var f=new ChannelingTests.Fight(data);f.Cast();f.Step();
        Assert.Equal(charged?AbilityPhase.Charging:AbilityPhase.Telegraph,Assert.Single(f.Abilities.ActiveStates).Phase);
        var initialMana=f.Mana;Assert.Equal(1,f.Skill.Charges);Assert.Empty(f.Hits);
        var saved=f.World.CaptureCharacter(42);Assert.Equal(1u,f.Abilities.Loadout(f.Id,f.World.Tick).LastInputSequence);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var session=await store.OpenAsync("",saved,default);var token=session.IssuedToken;await store.SaveAsync([new(session,saved)],default);await session.DisposeAsync();
        await using var reopened=await store.OpenAsync(token,saved,default);
        var next=new ChannelingTests.Fight(data,saved:reopened.State with {SavedAtUnixMilliseconds=saved.SavedAtUnixMilliseconds-60000});
        Assert.Null(next.Actor.Action);Assert.Empty(next.Abilities.ActiveStates);Assert.Equal(initialMana,next.Mana);
        Assert.Equal(1,next.Skill.Charges);Assert.Equal(0,next.Skill.ReadyInSeconds);Assert.Equal(0,next.Skill.PendingEndCharges);
        Assert.Equal(0u,next.Abilities.Loadout(next.Id,0).LastInputSequence);
        for(var i=0;i<15;i++)next.Step();Assert.Empty(next.Hits);Assert.Empty(next.Abilities.ActiveStates);
        Assert.Equal(initialMana,next.Mana);Assert.Equal(1,next.Skill.Charges);
    }
}
