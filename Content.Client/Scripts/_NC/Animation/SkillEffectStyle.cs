using System.Text.Json;
using Godot;
namespace ProjectG.Animation;

/// <summary>Editable cosmetic palette; damage shapes and distances still come from server effects.</summary>
public sealed class SkillEffectStyle
{
    public string Color { get; set; } = "dae8ec";
    public float Arc { get; set; } = 70;
    public float Height { get; set; } = .9f;
    public float Width { get; set; } = .12f;
    private static Dictionary<ushort,SkillEffectStyle>? _all;
    public static SkillEffectStyle? Get(ushort id)
    {
        _all ??= JsonSerializer.Deserialize<Dictionary<ushort,SkillEffectStyle>>(
            Godot.FileAccess.GetFileAsString("res://Assets/Characters/MaleBase/skill-effects.json"),
            new JsonSerializerOptions {PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("Missing skill effect palette.");
        return _all.GetValueOrDefault(id);
    }
    public ArrayMesh Ribbon(float radius)
    {
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var inner=Math.Max(0,radius-Width);const int steps=20;
        for(var i=0;i<steps;i++)
        {
            var a=(-Arc*.5f+Arc*i/steps)*Mathf.Pi/180;var b=(-Arc*.5f+Arc*(i+1)/steps)*Mathf.Pi/180;
            var p=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));var q=new Vector3(Mathf.Sin(b),0,Mathf.Cos(b));
            surface.AddVertex(p*inner);surface.AddVertex(p*radius);surface.AddVertex(q*radius);
            surface.AddVertex(p*inner);surface.AddVertex(q*radius);surface.AddVertex(q*inner);
        }
        return surface.Commit();
    }
}
