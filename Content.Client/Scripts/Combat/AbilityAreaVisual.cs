using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;
using V2 = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;

namespace ProjectG.Combat;

/// <summary>One bounded, reusable footprint mesh. It only presents server-provided geometry.</summary>
public partial class AbilityAreaVisual : Node3D
{
    // Flat maps have thin decorative floor overlays (the sword ring is 15 cm above navigation).
    // This rendering offset never changes the foot positions used for range or collision checks.
    internal const float SurfaceLift = .18f;
    private readonly ImmediateMesh _geometry = new();
    private readonly MeshInstance3D _mesh = new();
    private readonly List<SurfaceLocation> _column = new();
    private bool _building;
    private readonly StandardMaterial3D _material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };
    public int VertexCount { get; private set; }
    public AbilityAreaShape Shape { get; private set; }
    public override void _Ready()
    {
        TopLevel = true;
        // Vertices already use world coordinates; discard the caster transform preserved by TopLevel.
        GlobalTransform = Transform3D.Identity;
        _mesh.Mesh = _geometry;
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_mesh); Visible = false;
    }

    public void Show(AbilityProfile profile, NavigationGrid grid, V3 origin, V3 aim, V2 direction,
        float directionY, V3 dashEnd, bool usable,V2? facing=null)
    {
        var area = profile.Area;
        Shape = area.Shape; VertexCount = 0; _building = false; _geometry.ClearSurfaces();
        if (area.Shape == AbilityAreaShape.None) { Visible = false; return; }
        _material.AlbedoColor = usable ? new Color(.12f, .72f, 1, .4f) : new Color(1, .22f, .14f, .4f);
        var anchor = profile.Form == AbilityForm.GroundArea ? aim : origin;
        var planar = direction.LengthSquared() > .000001f ? V2.Normalize(direction) : facing??V2.UnitY;
        var right = new V2(planar.Y, -planar.X);

        if(profile.Form==AbilityForm.GroundArea && profile.GroundVolumeHeight>0)
        {
            // A world-space cylinder keeps its selected bottom and top even on slopes or across gaps.
            // Bounded face sampling only previews occlusion; the server resolves each full target body.
            var color=_material.AlbedoColor;color.A=.16f;_material.AlbedoColor=color;
            var bottom=new Vector3(anchor.X,anchor.Y,anchor.Z);var top=bottom+Vector3.Up*profile.GroundVolumeHeight;
            var center=(bottom+top)*.5f;
            const int segments=32;
            for(var i=0;i<segments;i++)
            {
                var a=Mathf.Tau*i/segments;var b=Mathf.Tau*(i+1)/segments;
                var p=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*profile.Radius;
                var q=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*profile.Radius;
                VolumeTriangle(bottom,bottom+p,bottom+q);VolumeTriangle(top,top+q,top+p);
                VolumeTriangle(bottom+p,top+p,top+q);VolumeTriangle(bottom+p,top+q,bottom+q);
            }
            void VolumeTriangle(Vector3 a,Vector3 b,Vector3 c)
            {
                var end=(a+b+c)/3;
                if(profile.GroundRequiresSurface && !grid.TraverseSurface(anchor,new(end.X,anchor.Y,end.Z)))return;
                var distance=center.DistanceTo(end);var windows=Math.Max(1,(int)Math.Ceiling(distance/120));
                for(var step=0;step<windows;step++)
                {
                    var from=center.Lerp(end,(float)step/windows);var to=center.Lerp(end,(float)(step+1)/windows);
                    if(grid.TryVisibilityObstacle(new(from.X,from.Y,from.Z),new(to.X,to.Y,to.Z),out _))return;
                }
                Triangle(a,b,c);
            }
        }
        else if (profile.Form == AbilityForm.Projectile)
        {
            // The projectile center and its real radius use the same sweep as the server.
            origin+=V3.UnitY*profile.ProjectileLaunchHeight;
            var travel = new V3(direction.X, directionY, direction.Y) * area.Length;
            if(grid.TryProjectileObstacle(origin,origin+travel,profile.Radius,out var fraction))travel*=fraction;
            var start = new Vector3(origin.X, origin.Y, origin.Z);
            var end = start + new Vector3(travel.X, travel.Y, travel.Z);
            var width = new Vector3(right.X, 0, right.Y) * area.HalfWidth;
            Triangle(start - width, end - width, end + width);
            Triangle(start - width, end + width, start + width);
        }
        else if(profile.Form==AbilityForm.Melee)
        {
            origin+=V3.UnitY*profile.MeleeOriginHeight;
            var start=new Vector3(origin.X,origin.Y,origin.Z);var forward=new Vector3(direction.X,directionY,direction.Y).Normalized();
            var lateral=new Vector3(right.X,0,right.Y);
            if(area.Shape==AbilityAreaShape.Corridor)
            {
                var end=start+forward*Math.Max(0,area.Length-area.HalfWidth);var side=lateral*area.HalfWidth;
                Triangle(start-side,end-side,end+side);Triangle(start-side,end+side,start+side);
                // The central-plane capsule silhouette includes both rounded caps.
                const int caps=16;
                for(var cap=0;cap<2;cap++)
                    for(var step=0;step<caps;step++)
                    {
                        var center=cap==0?start:end;var from=-Mathf.Pi*.5f+Mathf.Pi*step/caps+(cap==0?Mathf.Pi:0);
                        var to=from+Mathf.Pi/caps;
                        Triangle(center,center+(forward*Mathf.Cos(from)+lateral*Mathf.Sin(from))*area.HalfWidth,
                            center+(forward*Mathf.Cos(to)+lateral*Mathf.Sin(to))*area.HalfWidth);
                    }
            }
            else
            {
                const int segments=32,bands=4;
                var half=profile.MeleeThickness*.5f;var length=Math.Max(0,area.Length-half);
                var normal=forward.Cross(lateral).Normalized();
                for(var i=0;i<segments;i++)
                {
                    var from=-area.HalfAngleRadians+2*area.HalfAngleRadians*i/segments;
                    var to=-area.HalfAngleRadians+2*area.HalfAngleRadians*(i+1)/segments;
                    var a=forward*Mathf.Cos(from)+lateral*Mathf.Sin(from);var b=forward*Mathf.Cos(to)+lateral*Mathf.Sin(to);
                    if(half==0){Triangle(start,start+a*length,start+b*length);continue;}
                    var top=start+normal*half;var bottom=start-normal*half;
                    Triangle(top,top+a*length,top+b*length);Triangle(bottom,bottom+b*length,bottom+a*length);
                    for(var band=0;band<bands;band++)
                    {
                        var low=-Mathf.Pi*.5f+Mathf.Pi*band/bands;var high=low+Mathf.Pi/bands;
                        var p=start+a*(length+half*Mathf.Cos(low))+normal*(half*Mathf.Sin(low));
                        var q=start+b*(length+half*Mathf.Cos(low))+normal*(half*Mathf.Sin(low));
                        var r=start+a*(length+half*Mathf.Cos(high))+normal*(half*Mathf.Sin(high));
                        var s=start+b*(length+half*Mathf.Cos(high))+normal*(half*Mathf.Sin(high));
                        Triangle(p,q,s);Triangle(p,s,r);
                    }
                    if(i==0){Triangle(top,bottom,bottom+a*length);Triangle(top,bottom+a*length,top+a*length);}
                    if(i==segments-1){Triangle(top,top+b*length,bottom+b*length);Triangle(top,bottom+b*length,bottom);}
                }
            }
        }
        else if (area.Shape == AbilityAreaShape.Corridor)
        {
            var length = profile.Form == AbilityForm.Dash
                ? new V2(dashEnd.X - origin.X, dashEnd.Z - origin.Z).Length() : area.Length;
            const int steps = 32;
            for (var i = 0; i < steps; i++)
            {
                var alongA = length * i / steps; var alongB = length * (i + 1) / steps;
                var a = planar * alongA; var b = planar * alongB;
                // A thrust intersects its corridor with the 3D range sphere; its far edge narrows to a tip.
                var widthA = right * WidthAt(alongA); var widthB = right * WidthAt(alongB);
                SurfaceTriangle(a - widthA, b - widthB, b + widthB);
                SurfaceTriangle(a - widthA, b + widthB, a + widthA);
            }
            float WidthAt(float along) => profile.Form == AbilityForm.Dash ? area.HalfWidth
                : Math.Min(area.HalfWidth, MathF.Sqrt(Math.Max(0, length * length - along * along)));
        }
        else
        {
            // A small marker for Self means "on yourself", not a damaging area around the caster.
            var radius = area.Shape == AbilityAreaShape.Self ? .45f : area.Length;
            var angle = area.Shape == AbilityAreaShape.Sector ? area.HalfAngleRadians : MathF.PI;
            const int segments = 48, rings = 4;
            for (var ring = 0; ring < rings; ring++)
                for (var i = 0; i < segments; i++)
                {
                    var a = Offset(-angle + 2 * angle * i / segments);
                    var b = Offset(-angle + 2 * angle * (i + 1) / segments);
                    var inner = radius * ring / rings; var outer = radius * (ring + 1) / rings;
                    SurfaceTriangle(a * inner, a * outer, b * outer);
                    if (ring > 0) SurfaceTriangle(a * inner, b * outer, b * inner);
                }
            V2 Offset(float radians) => planar * MathF.Cos(radians) + right * MathF.Sin(radians);
        }
        if (_building) _geometry.SurfaceEnd();
        Visible = VertexCount > 0;

        void SurfaceTriangle(V2 a, V2 b, V2 c)
        {
            if (Sample(a, out var va) && Sample(b, out var vb) && Sample(c, out var vc)) Triangle(va, vb, vc);
        }
        bool Sample(V2 offset, out Vector3 point)
        {
            var foot = new V3(anchor.X + offset.X, anchor.Y, anchor.Z + offset.Y);
            point = default;
            if (grid.Surface is { } surface)
            {
                // TryLocate is intentionally limited to 1 m for movement intents. Previewing a tall slope
                // samples the public column instead, keeping the closest floor to the selected height.
                surface.SampleColumn(foot.X, foot.Z, _column);
                var best = float.PositiveInfinity; var selected = foot;
                foreach (var candidate in _column)
                {
                    var difference = MathF.Abs(candidate.Position.Y - anchor.Y);
                    if (difference >= best || difference > Math.Max(.5f, area.Length + .35f)) continue;
                    best = difference; selected = candidate.Position;
                }
                if (!float.IsFinite(best)) return false;
                foot = selected;
            }
            else if (!grid.IsOnSurface(foot)) return false;
            if (area.Shape != AbilityAreaShape.Self && profile.Form != AbilityForm.Dash && !area.Contains(foot - anchor, planar)) return false;
            if ((profile.Form==AbilityForm.Dash || profile.Form==AbilityForm.GroundArea && profile.GroundRequiresSurface) && !grid.TraverseSurface(anchor, foot)) return false;
            if (area.Shape != AbilityAreaShape.Self && !grid.ClearAttack(anchor, foot)) return false;
            point = new(foot.X, foot.Y + SurfaceLift, foot.Z); return true;
        }
    }
    private void Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        // Fully occluded footprints have no vertices; Godot must not receive an empty surface.
        if (!_building) { _geometry.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material); _building = true; }
        _geometry.SurfaceAddVertex(a); _geometry.SurfaceAddVertex(b); _geometry.SurfaceAddVertex(c);
        VertexCount += 3;
    }
    public void HideArea() { Visible = false; VertexCount = 0; }
}
