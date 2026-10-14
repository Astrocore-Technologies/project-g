using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Conservative solid volumes below a native terrain, built offline for attack visibility.</summary>
internal static class TerrainVisibilityExport
{
    private const float Cell = 8;
    private const float Safety = .75f;

    public static SurfaceBox[] Bake(Node terrain, int budget, bool steepOnly = false)
    {
        if (budget <= 0) throw new InvalidDataException("No budget for terrain visibility.");
        var data = terrain.Get("data").AsGodotObject();
        var native = terrain.Call("bake_mesh",steepOnly ? 0 : 1,0).AsGodotObject() as Mesh
            ?? throw new InvalidDataException("Missing native terrain visibility source.");
        var bounds = native.GetAabb();
        var minX = MathF.Floor(bounds.Position.X/Cell)*Cell;
        var minZ = MathF.Floor(bounds.Position.Z/Cell)*Cell;
        var width = (int)MathF.Ceiling((bounds.End.X-minX)/Cell);
        var depth = (int)MathF.Ceiling((bounds.End.Z-minZ)/Cell);
        if (width is < 1 or > 128 || depth is < 1 or > 128)
            throw new InvalidDataException("Terrain visibility sampling exceeds local-region budget.");

        // Walkable V2 faces already block rays entering ordinary slopes. For
        // this mode, reserve solid volumes for omitted steep faces and their
        // high-side neighbours; do not duplicate the entire rolling landscape.
        var steepCells = steepOnly ? SteepCells(native.GetFaces(),width,depth,minX,minZ) : null;

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
            if (steepCells is not null)
            {
                if (steepCells[z*width+x]) relief[z*width+x]=top;
                continue;
            }
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
                if (steepOnly)
                {
                    // Layering merges adjacent cells of different heights without
                    // changing their occupied volume: each layer is a subset of
                    // the SAME conservative quantized columns.
                    var layered=PackLayered(relief,width,depth,minX,minZ,floor,band,offset);
                    if (layered.Length<candidate.Length) candidate=layered;
                    var extended=PackExtended(relief,tops,width,depth,minX,minZ,floor,band,offset);
                    if (extended.Length<candidate.Length) candidate=extended;
                }
                if(best is not null && candidate.Length>=best.Length) continue;
                best=candidate; chosenOffset=offset;
            }
            if(best!.Length>budget)
            {
                GD.Print($"TERRAIN_VISIBILITY_TRY boxes={best.Length} budget={budget} band={band}");
                continue;
            }
            GD.Print($"TERRAIN_VISIBILITY boxes={best.Length} cell={Cell} height_band={band} offset={chosenOffset}");
            return best;
        }
        throw new InvalidDataException("Native terrain visibility exceeds the bounded box budget.");
    }

    private static bool[] SteepCells(Vector3[] faces,int width,int depth,float minX,float minZ)
    {
        var cells=new bool[width*depth];
        for(var i=0;i+2<faces.Length;i+=3)
        {
            var a=faces[i]; var b=faces[i+1]; var c=faces[i+2];
            var normal=(b-a).Cross(c-a);
            if(normal.LengthSquared()<.000001f || normal.Y*normal.Y>=normal.LengthSquared()*.5f) continue;
            // A one-cell border captures the upper bank as well as the steep
            // triangle. Every resulting volume still stays BELOW real samples.
            var x0=Math.Clamp((int)MathF.Floor((MathF.Min(a.X,MathF.Min(b.X,c.X))-minX)/Cell)-1,0,width-1);
            var x1=Math.Clamp((int)MathF.Floor((MathF.Max(a.X,MathF.Max(b.X,c.X))-minX)/Cell)+1,0,width-1);
            var z0=Math.Clamp((int)MathF.Floor((MathF.Min(a.Z,MathF.Min(b.Z,c.Z))-minZ)/Cell)-1,0,depth-1);
            var z1=Math.Clamp((int)MathF.Floor((MathF.Max(a.Z,MathF.Max(b.Z,c.Z))-minZ)/Cell)+1,0,depth-1);
            for(var z=z0;z<=z1;z++)
            for(var x=x0;x<=x1;x++) cells[z*width+x]=true;
        }
        return cells;
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

    private static SurfaceBox[] PackLayered(float[] tops,int width,int depth,float minX,float minZ,float floor,float band,float offset)
    {
        var quantized=tops.Select(v=>float.IsFinite(v) ? MathF.Floor((v-offset)/band)*band+offset : float.NaN).ToArray();
        var levels=quantized.Where(v=>float.IsFinite(v) && v>floor).Distinct().Order().ToArray();
        var boxes=new List<SurfaceBox>();
        var bottom=floor;
        foreach(var level in levels)
        {
            var slice=quantized.Select(v=>v>=level ? level : float.NaN).ToArray();
            boxes.AddRange(Pack(slice,width,depth,minX,minZ,bottom,1,0));
            bottom=level;
        }
        return boxes.ToArray();
    }

    private static SurfaceBox[] PackExtended(float[] required,float[] actual,int width,int depth,float minX,float minZ,float floor,float band,float offset)
    {
        var heights=required.Select(v=>float.IsFinite(v) ? MathF.Floor((v-offset)/band)*band+offset : float.NaN).ToArray();
        var levels=heights.Where(v=>float.IsFinite(v) && v>floor).Distinct().OrderDescending().ToArray();
        var covered=new bool[heights.Length];
        var result=new List<SurfaceBox>();
        const int maxSpan=64;
        foreach(var top in levels)
        {
            // Expanding across non-steep terrain is safe ONLY if every native
            // sample there is above this box. This fills no air, road or bridge.
            var invalid=new int[depth,width+1];
            for(var z=0;z<depth;z++)
            for(var x=0;x<width;x++)
                invalid[z,x+1]=invalid[z,x]+(float.IsFinite(actual[z*width+x]) && actual[z*width+x]>=top ? 0 : 1);
            for(var z=0;z<depth;z++)
            for(var x=0;x<width;x++)
            {
                var index=z*width+x;
                if(covered[index] || heights[index]!=top) continue;
                var counts=new int[depth+1,width+1];
                for(var row=0;row<depth;row++)
                for(var col=0;col<width;col++)
                {
                    var cell=row*width+col;
                    var needed=!covered[cell] && float.IsFinite(heights[cell]) && heights[cell]>floor && heights[cell]<=top ? 1 : 0;
                    counts[row+1,col+1]=needed+counts[row,col+1]+counts[row+1,col]-counts[row,col];
                }
                var leftMin=x;
                while(leftMin>0 && x-leftMin+1<maxSpan && invalid[z,leftMin]-invalid[z,leftMin-1]==0) leftMin--;
                var rightMax=x;
                while(rightMax+1<width && rightMax-x+1<maxSpan && invalid[z,rightMax+2]-invalid[z,rightMax+1]==0) rightMax++;
                var best=(Left:x,Right:x,First:z,Last:z,Count:1,Area:1);
                for(var left=leftMin;left<=x;left++)
                for(var right=x;right<=rightMax && right-left+1<=maxSpan;right++)
                {
                    var first=z; var last=z;
                    while(first>0 && z-first+1<maxSpan && invalid[first-1,right+1]-invalid[first-1,left]==0) first--;
                    while(last+1<depth && last-first+1<maxSpan && invalid[last+1,right+1]-invalid[last+1,left]==0) last++;
                    var count=counts[last+1,right+1]-counts[first,right+1]-counts[last+1,left]+counts[first,left];
                    var area=(right-left+1)*(last-first+1);
                    if(count>best.Count || count==best.Count && area>best.Area) best=(left,right,first,last,count,area);
                }
                for(var row=best.First;row<=best.Last;row++)
                for(var col=best.Left;col<=best.Right;col++)
                    if(heights[row*width+col]<=top) covered[row*width+col]=true;
                var columns=best.Right-best.Left+1; var rows=best.Last-best.First+1;
                result.Add(new(new(minX+(best.Left+columns*.5f)*Cell,(floor+top)*.5f,minZ+(best.First+rows*.5f)*Cell),
                    new(columns*Cell,top-floor,rows*Cell),new(0,0,0)));
            }
        }
        return result.ToArray();
    }
}
