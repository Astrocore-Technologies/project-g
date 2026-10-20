using Content.Server.Activities;
using Content.Server.Configuration;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private CampServicesDefinition? _campServices;
    public bool HasActivities=>_campServices is not null;
    public void ConfigureCampServices(CampServicesDefinition definition)
    {
        if(_campServices is not null||Players.Count!=0||_progressionCatalog is null||Inventory is null||!HasWorldNode)
            throw new InvalidOperationException("Configure camp services once before login.");
        definition.Validate(_progressionCatalog);
        if(definition.RegionId==RegionId)
        {
            if(HasCrafting||_deliveryQuest is not null)throw new InvalidOperationException("Camp service placement conflicts with existing services.");
            foreach(var point in new[]{definition.Station,definition.Giver,definition.Recipient}.Concat(definition.Resources.Select(r=>r.Point)))
                if(!Navigation.IsOnSurface(point.Position)||MathF.Abs(LocateHeight(point.Ground)-point.Height)>.35f)
                    throw new InvalidDataException("Camp service is not on its authored walkable floor.");
            var crafting=_progressionCatalog.Crafting! with {StationX=definition.Station.X,StationZ=definition.Station.Z,
                Resources=_progressionCatalog.Crafting!.Resources.Select(r=>
                {var p=definition.Resources.Single(p=>p.Id==r.Id).Point;return r with {X=p.X,Z=p.Z};}).ToArray()};
            InitializeCrafting(new CraftingOptions {Enabled=true},crafting);
            var quest=_progressionCatalog.DeliveryQuest!;
            var relocatedQuest=quest with {RegionId=RegionId,Giver=quest.Giver with {X=definition.Giver.X,Z=definition.Giver.Z},
                Recipient=quest.Recipient with {X=definition.Recipient.X,Z=definition.Recipient.Z},Objective=definition.QuestObjective,
                Offer=definition.QuestOffer,Reminder=definition.QuestReminder};
            relocatedQuest.Validate(_progressionCatalog);InitializeQuests(true,relocatedQuest);
        }
        _campServices=definition;
    }
}
