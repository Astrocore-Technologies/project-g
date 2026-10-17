using Content.Server.World;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

internal static class CombatTestSteps
{
    internal static AttackEvent Contact(ServerWorld world,NetworkEntityId source)
    {
        for(var i=0;i<200;i++)
        {
            var hits=world.Combat!.Events.Where(e=>e.AttackerId==source).ToArray();
            if(hits.Length>0)return Assert.Single(hits);
            world.Simulate(.05f);
        }
        throw new Xunit.Sdk.XunitException("The started attack never reached contact.");
    }

    internal static void FinishAction(ServerWorld world,NetworkEntityId source)
    {
        for(var i=0;i<200 && world.Combat!.Get(source).Action is not null;i++)world.Simulate(.05f);
        Assert.Null(world.Combat!.Get(source).Action);
    }
}
