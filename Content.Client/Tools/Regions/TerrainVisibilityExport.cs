using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Conservative solid volumes below a native terrain, built offline for attack visibility.</summary>
internal static class TerrainVisibilityExport
{
    private const float Cell = 8;
    private const float Safety = .75f;

    public static SurfaceBox[] Bake(Node terrain, int budget)
    {
        if (budget <= 0) throw new InvalidDataException("No budget for terrain visibility.");
        var data = terrain.Get("data").AsGodotObject();
        var native = terrain.Call("bake_mesh",1,0).AsGodotObject() as Mesh
            ?? throw new InvalidDataException("Missing native terrain visibility source.");
        var bounds = native.GetAabb();
        var minX = MathF.Floor(bounds.Position.X/Cell)*Cell;
        var minZ = MathF.Floor(bounds.Position.Z/Cell)*Cell;
        var width = (int)MathF.Ceiling((bounds.End.X-minX)/Cell);
        var depth = (int)MathF.Ceiling((bounds.End.Z-minZ)/Cell);
        if (width is < 1 or > 128 || depth is < 1 or > 128)
            throw new InvalidDataException("Terrain visibility sampling exceeds local-region budget.");

        // Read every native vertex covered by a cell. A volume never reaches above
        // the lowest actual terrain sample, including the bottoms of ramps.
        var tops = new float[width*depth];
        var floor = float.PositiveInfinity;
        for(var z=0;z<depth;z++)
        for(var x=0;x<width;x++)
        {
            var lowest=float.PositiveInfinity;
            for(var dz=0;dz<=8;dz+=2)
            for(var dx=0;dx<=8;dx+=2)
            {
                var y=data.Call("get_height",new Vector3(minX+x*Cell+dx,0,minZ+z*Cell+dz)).AsSingle();
                if(!float.IsFinite(y)) { lowest=float.NaN; break; }
                if(float.IsNaN(lowest)) break;
                lowest=MathF.Min(lowest,y);
            }
            tops[z*width+x]=lowest-Safety;
            if(float.IsFinite(lowest)) floor=MathF.Min(floor,lowest);
        }
        if(!float.IsFinite(floor)) throw new InvalidDataException("No finite terrain visibility data.");
        floor=MathF.Floor(floor/8)*8-8;

        // Ordinary low ground is already represented by walkable faces. Keep
        // extra solids only where native relief rises above its surroundings;
        // omitting deep flat-ground boxes leaves room for precise cliff volumes.
        var relief=new float[tops.Length];
        Array.Fill(relief,float.NaN);
        for(var z=0;z<depth;z++)
        for(var x=0;x<width;x++)
        {
            var top=tops[z*width+x];
            if(!float.IsFinite(top)) continue;
            var surrounding=top;
            for(var dz=Math.Max(0,z-12);dz<=Math.Min(depth-1,z+12);dz++)
            for(var dx=Math.Max(0,x-12);dx<=Math.Min(width-1,x+12);dx++)
            {
                var neighbour=tops[dz*width+dx];
                if(float.IsFinite(neighbour)) surrounding=MathF.Min(surrounding,neighbour);
            }
            if(top>surrounding+6) relief[z*width+x]=top;
        }
        // Larger height bands reduce fragmentation, never inflate a solid upward.
        // Keep the existing 256-box transfer cap and fail rather than widening it.
        foreach(var band in new[] {4f,8f,12f,16f,24f,32f})
        {
            SurfaceBox[]? best=null;
            var chosenOffset=0f;
            foreach(var offset in new[] {0f,band*.25f,band*.5f,band*.75f})
            {
                var candidate=Pack(relief,width,depth,minX,minZ,floor,band,offset);
                if(best is not null && candidate.Length>=best.Length) continue;
                best=candidate; chosenOffset=offset;
            }
            if(best!.Length>budget) continue;
            GD.Print($"TERRAIN_VISIBILITY boxes={best.Length} cell={Cell} height_band={band} offset={chosenOffset}");
            return best;
        }
        throw new InvalidDataException("Native terrain visibility exceeds the bounded box budget.");
    }

    private static SurfaceBox[] Pack(float[] tops,int width,int depth,float minX,float minZ,float floor,float band,float offset)
    {
        var levels=tops.Select(v=>float.IsFinite(v) ? MathF.Floor((v-offset)/band)*band+offset : float.NaN).ToArray();
        var used=new bool[levels.Length];
        var boxes=new List<SurfaceBox>();
        var maxSpan=(int)(512/Cell);
        for(var z=0;z<depth;z++)
        for(var x=0;x<width;x++)
        {
            var index=z*width+x; var top=levels[index];
            if(used[index] || !float.IsFinite(top) || top<=floor) continue;
            var columns=1;
            while(columns<maxSpan && x+columns<width && !used[index+columns] && levels[index+columns]==top)
                columns++;
            var rows=1;
            while(rows<maxSpan && z+rows<depth)
            {
                var matches=true;
                for(var dx=0;dx<columns;dx++)
                    if(used[(z+rows)*width+x+dx] || levels[(z+rows)*width+x+dx]!=top) { matches=false; break; }
                if(!matches) break;
                rows++;
            }
            for(var dz=0;dz<rows;dz++)
            for(var dx=0;dx<columns;dx++) used[(z+dz)*width+x+dx]=true;
            boxes.Add(new(new(minX+(x+columns*.5f)*Cell,(floor+top)*.5f,minZ+(z+rows*.5f)*Cell),
                new(columns*Cell,top-floor,rows*Cell),new(0,0,0)));
        }
        return boxes.ToArray();
    }
}
