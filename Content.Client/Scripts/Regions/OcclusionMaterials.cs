using Godot;
using System.Text.RegularExpressions;

namespace ProjectG.Regions;

/// <summary>Region-local fade variants. Never mutates an authored/shared material or shader.</summary>
internal sealed class OcclusionMaterials(float opacity)
{
    private readonly Dictionary<Material, Material> _materials = new();
    private readonly Dictionary<Shader, Shader> _shaders = new();
    private readonly StandardMaterial3D _default = new();
    public int UnsupportedCount { get; private set; }

    public Material Faded(Material? source)
    {
        source ??= _default;
        if (_materials.TryGetValue(source, out var cached)) return cached;
        Material faded;
        if (source is BaseMaterial3D standard)
        {
            var copy = (BaseMaterial3D)standard.Duplicate();
            copy.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            var color = copy.AlbedoColor;
            color.A *= opacity;
            copy.AlbedoColor = color;
            faded = copy;
        }
        else if (source is ShaderMaterial painted && painted.Shader is { } shader && IsPainted(painted))
        {
            if (!_shaders.TryGetValue(shader, out var variant))
            {
                // These known opaque art shaders do not write ALPHA. Derive the fade
                // variant once so texture/light fixes remain shared with the authored shader.
                // GeometryInstance3D.Transparency is ignored by Godot's Mobile renderer.
                var fragment = new Regex(@"void\s+fragment\s*\(\s*\)\s*\{");
                if (fragment.Matches(shader.Code).Count != 1 || Regex.IsMatch(shader.Code, @"\bALPHA\s*="))
                    throw new InvalidDataException($"Unsupported painted shader: {shader.ResourcePath}");
                variant = new Shader { Code = fragment.Replace(shader.Code,
                    "void fragment() {\n ALPHA = camera_opacity;", 1).Replace("shader_type spatial;",
                    "shader_type spatial;\nuniform float camera_opacity = 1.0;", StringComparison.Ordinal) };
                _shaders.Add(shader, variant);
            }
            var copy = (ShaderMaterial)painted.Duplicate();
            copy.Shader = variant;
            copy.SetShaderParameter("camera_opacity", opacity);
            // Retire the former global foliage cutout; this instance gets one uniform opacity.
            copy.SetShaderParameter("focus_world", Vector3.One * -10000);
            faded = copy;
        }
        else if (source is ShaderMaterial custom && custom.Shader is { } customShader &&
                 customShader.GetShaderUniformList().Any(uniform => uniform.AsGodotDictionary()["name"].AsString() == "camera_opacity"))
        {
            var copy = (ShaderMaterial)custom.Duplicate();
            copy.SetShaderParameter("camera_opacity", opacity);
            faded = copy;
        }
        else
        {
            UnsupportedCount++;
            GD.PushWarning($"Camera fade needs Standard/ORM material or a shader with camera_opacity: {source.ResourcePath}");
            faded = source;
        }
        _materials.Add(source, faded);
        return faded;
    }

    private static bool IsPainted(ShaderMaterial material) =>
        material.ResourcePath.EndsWith("/painted_forest.tres", StringComparison.Ordinal) ||
        material.ResourcePath.EndsWith("/painted_gardens.tres", StringComparison.Ordinal) ||
        material.ResourcePath.EndsWith("/painted_hills.tres", StringComparison.Ordinal) ||
        material.Shader?.ResourcePath is
            "res://Assets/World/Linvel/Beech/Revision2/painted_vertex.gdshader" or
            "res://Assets/ArtDirection/ForestReference/Shaders/painted_props.gdshader" or
            "res://Assets/ArtDirection/ForestReference/Shaders/painted_surface.gdshader" or
            "res://Assets/ArtDirection/ForestReference/Shaders/beech_foliage.gdshader" or
            "res://Assets/World/Linvel/Beech/Revision2/VisualStyle/Shaders/beech_foliage.gdshader" or
            "res://Assets/World/Linvel/Beech/Revision2/VisualStyle/Shaders/painted_bark.gdshader";
}
