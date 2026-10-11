using Content.Shared.Network;
using Godot;

namespace ProjectG.Actors;

/// <summary>Editable client scenes chosen only from already public roles and entity kinds.</summary>
public static class ActorScenes
{
    private const string Folder="res://Scenes/Actors/Primitives/";
    private static readonly Dictionary<string,PackedScene> Scenes=new();
    public static PackedScene Load(string name)
    {
        if(!Scenes.TryGetValue(name,out var scene)) Scenes[name]=scene=GD.Load<PackedScene>(Folder+name+".tscn");
        return scene;
    }
    public static PackedScene ForRole(string role) => Load(role=="Наставник мечников"?"TrainerFigure":"ResidentFigure");
    public static PackedScene ForKind(CombatEntityKind kind) => Load(kind switch
        { CombatEntityKind.Boss=>"BossFigure", CombatEntityKind.Monster=>"MonsterFigure", _=>"ResidentFigure" });
}
