using System.Numerics;
namespace Content.Server.Pvp;
public sealed record PvpDefinition
{
    public required float SafeMinX {get;init;}
    public required float SafeMaxX {get;init;}
    public required float SafeMinZ {get;init;}
    public required float SafeMaxZ {get;init;}
    public required float RespawnX {get;init;}
    public required float RespawnZ {get;init;}
    public int CombatSeconds {get;init;}=120;
    public int AggressorSeconds {get;init;}=600;
    public int MixedSeconds {get;init;}=30;
    public int PveRespawnSeconds {get;init;}=15;
    public int PvpRespawnSeconds {get;init;}=60;
    public int ProtectionSeconds {get;init;}=10;
    public int LootSeconds {get;init;}=1800;
    public int PickupSeconds {get;init;}=5;
    public int PveExperiencePercent {get;init;}=5;
    public int PvpExperiencePercent {get;init;}=10;
    public int AttackPenalty {get;init;}=10;
    public int KillPenalty {get;init;}=100;
    public Vector2 Respawn=>new(RespawnX,RespawnZ);
    public bool IsSafe(Vector2 p)=>p.X>=SafeMinX&&p.X<=SafeMaxX&&p.Y>=SafeMinZ&&p.Y<=SafeMaxZ;
    public void Validate(){if(!float.IsFinite(SafeMinX)||!float.IsFinite(SafeMaxX)||!float.IsFinite(SafeMinZ)||!float.IsFinite(SafeMaxZ)||SafeMinX>=SafeMaxX||SafeMinZ>=SafeMaxZ||!float.IsFinite(RespawnX)||!float.IsFinite(RespawnZ)||!IsSafe(Respawn)||CombatSeconds is <1 or >120||AggressorSeconds is <1 or >600||MixedSeconds is <1 or >30||PveRespawnSeconds is <1 or >60||PvpRespawnSeconds is <1 or >60||ProtectionSeconds is <1 or >10||LootSeconds is <1 or >1800||PickupSeconds is <1 or >5||PveExperiencePercent is <0 or >100||PvpExperiencePercent is <0 or >100||AttackPenalty is <0 or >1000||KillPenalty is <0 or >1000)throw new ArgumentException("Invalid PvP rules.");}
}
