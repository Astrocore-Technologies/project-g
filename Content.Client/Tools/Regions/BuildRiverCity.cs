using Godot;
using ProjectG.Regions;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tools.Regions;

/// <summary>Offline authoring tool. Produces an editable PackedScene, never runs in the game.</summary>
public partial class BuildRiverCity : Node
{
    private readonly Dictionary<string, StandardMaterial3D> _materials = new();
    private readonly Dictionary<Vector3, BoxMesh> _boxes = new();
    private readonly Dictionary<Vector3, BoxShape3D> _shapes = new();
    private int _serial;

    public override void _Ready()
    {
        try
        {
            var root = new RegionRoot { Name = "RiverCity", RegionId = "river_city", MovementBounds = new(-40,-32,80,64) };
            var environment = Group(root,"Environment");
            var terrain = Group(root,"Terrain");
            var decorations = Group(root,"Decorations");
            Group(root,"PublicStateBindings");
            var anchors = Group(root,"AuthoringAnchors");
            var walls = Group(decorations,"Fortifications");
            var homes = Group(decorations,"ResidentialQuarters");
            var market = Group(decorations,"MarketSquare");
            var arena = Group(decorations,"TrainingArena");
            var shop = Group(decorations,"MagicShop");
            var greenery = Group(decorations,"Greenery");

            environment.AddChild(new WorldEnvironment { Name="Daylight", Environment = new Godot.Environment {
                BackgroundMode=Godot.Environment.BGMode.Color, BackgroundColor=new Color("a3c9dc"),
                AmbientLightSource=Godot.Environment.AmbientSource.Color, AmbientLightColor=new Color("bfcfdb"),
                AmbientLightEnergy=.45f, TonemapMode=Godot.Environment.ToneMapper.Filmic } });
            environment.AddChild(new DirectionalLight3D { Name="Sun", RotationDegrees=new(-52,-28,0),
                LightColor=new Color("fff0d2"), LightEnergy=1.1f, ShadowEnabled=true, DirectionalShadowMaxDistance=100 });
            Box(terrain,"Landscape",new(0,-.6f,0),new(112,1,86),"799954");
            Solid(terrain,"WalkableGround",new(0,-.2f,0),new(80,.4f,64),"91a16c",true);
            Box(terrain,"River",new(-47,-.08f,0),new(13,.1f,86),"409cbd");
            Box(terrain,"Riverbank",new(-39,-.09f,0),new(2,.12f,66),"c7b388");
            var roads = Group(terrain,"Roads");
            Box(roads,"SouthAvenue",new(0,.012f,19),new(6,.024f,26),"d5bc86");
            Box(roads,"RiverStreet",new(-23,.012f,0),new(34,.024f,6),"d5bc86");
            Box(roads,"ArenaStreet",new(20,.012f,0),new(26,.024f,6),"d5bc86");
            Box(roads,"NorthStreet",new(0,.012f,-16),new(4,.024f,18),"d5bc86");
            Box(roads,"SouthResidentialLane",new(0,.014f,22),new(61,.028f,3),"cfb681");
            Box(roads,"WestResidentialLane",new(-30,.014f,-1),new(3,.028f,44),"cfb681");
            Box(roads,"NorthResidentialLane",new(-14,.014f,-23),new(30,.028f,3),"cfb681");
            Box(roads,"SouthCrossStreet",new(0,.015f,13),new(60,.03f,3),"cfb681");
            Disc(market,"PavedSquare",new(0,.045f,0),11.5f,.05f,"bdad8f",16);
            Disc(market,"InnerPaving",new(0,.08f,0),8,.04f,"d9cbaa",16);
            Solid(market,"FountainBase",new(0,.25f,0),new(3,.5f,3),"b6b5a2");
            Disc(market,"FountainBasin",new(0,.6f,0),1.65f,.35f,"d2cebc",12);
            Disc(market,"FountainWater",new(0,.81f,0),1.35f,.05f,"62b3cc",12);
            Disc(market,"FountainColumn",new(0,1.25f,0),.32f,1.1f,"d2cebc",8);
            Disc(market,"FountainCrown",new(0,1.9f,0),.8f,.22f,"d2cebc",10);
            foreach (var (x,z,color) in new (float,float,string)[] {(-8,-2,"536f92"),(-8,5,"b76b49"),(8,5,"57775b"),(7,-5,"be9748")})
                Stall(market,x,z,color);

            Wall(walls,0,-27,60,1.2f);
            Wall(walls,-16.5f,27,27,1.2f); Wall(walls,16.5f,27,27,1.2f);
            Wall(walls,-35,-12.5f,1.2f,19); Wall(walls,-35,12.5f,1.2f,19);
            Wall(walls,35,0,1.2f,44);
            foreach (var side in new[]{-1,1})
            foreach (var end in new[]{-1,1})
            {
                Wall(walls,side*32.5f,end*22,5,1.2f);
                Wall(walls,side*30,end*24.5f,1.2f,5);
                Tower(walls,side*30,end*24);
            }
            Tower(walls,-4.5f,27); Tower(walls,4.5f,27);
            Tower(walls,-35,-4.5f); Tower(walls,-35,4.5f);
            // Lintels are visual only: flat navigation must keep the gate passage open.
            Box(walls,"SouthGateLintel",new(0,4.4f,27),new(6,1,1.7f),"a7ac9d");
            Box(walls,"RiverGateLintel",new(-35,4.4f,0),new(1.7f,1,6),"a7ac9d");
            Flag(walls,-4.5f,6.3f,27,"426585"); Flag(walls,4.5f,6.3f,27,"426585");
            Flag(walls,-35,6.3f,-4.5f,"426585");
            Box(terrain,"DockDeck",new(-41,.04f,0),new(7,.12f,4),"a78859");
            for(var i=0;i<7;i++) Box(terrain,"DockPlank",new(-38-i,.12f,0),new(.08f,.04f,4),"776246");
            foreach(var z in new[]{-2.1f,2.1f}) foreach(var x in new[]{-38.5f,-43.5f})
                Box(terrain,"DockPost",new(x,.5f,z),new(.3f,1.4f,.3f),"715338");

            foreach (var (x,z) in new (float,float)[]{(-25,-17),(-18,-17),(-25,-10),(-18,-10),(-11,-19),(-5,-20),
                (-25,8),(-18,8),(-25,17),(-18,17),(-10,17),(12,8),(20,8),(27,8),(12,18),(20,18),(27,18)})
                House(homes,x,z,4.4f,4.4f,3.2f,(_serial%3)==0?"ad6246":"bb754d");
            House(shop,-7,-10,7,6,4,"626096");
            Disc(shop,"Tower",new(-10,3.3f,-11),1.65f,6.6f,"d5c9a4",8);
            Cone(shop,"TowerRoof",new(-10,7.1f,-11),2.1f,2.8f,"696397");
            Cone(shop,"Crystal",new(-7,5.5f,-7),.55f,1.5f,"86dcde");
            Box(shop,"ShopSign",new(-4.2f,2.2f,-6.7f),new(1.4f,.8f,.18f),"536486");
            Label(shop,"ЛАВКА",new(-4.2f,2.2f,-6.55f),.012f,24);

            Box(arena,"ArenaSand",new(20,.03f,-14),new(25,.04f,18),"d6bd86");
            Fence(arena,20,-23.5f,26,.3f); Fence(arena,32.5f,-14,.3f,19);
            Fence(arena,7.5f,-14,.3f,19); Fence(arena,10.5f,-4.5f,6,.3f);
            Fence(arena,21.5f,-4.5f,10,.3f); Fence(arena,31,-4.5f,3,.3f);
            Fence(arena,22,-14,.3f,19);
            Disc(arena,"SwordRingBorder",new(15,.08f,-14),5.7f,.08f,"ad8e58",24);
            Disc(arena,"SwordRing",new(15,.13f,-14),5.3f,.04f,"e4cb95",24);
            for(var i=0;i<3;i++)
            {
                var x=24.2f+i*2.7f;
                Box(arena,"ArcheryLane",new(x,.085f,-13.5f),new(.08f,.04f,14),"f0dfb6");
                Box(arena,"FiringLine",new(x,.09f,-7.5f),new(1.7f,.04f,.16f),"f0dfb6");
                Solid(arena,"TargetStand",new(x,.9f,-21),new(.25f,1.8f,.4f),"806243");
                var target=Disc(arena,"ArcheryTarget",new(x,1.7f,-21),.78f,.18f,"e9d6a7",16);
                target.RotationDegrees=new(90,0,0);
                var center=Disc(arena,"Bullseye",new(x,1.7f,-20.88f),.26f,.03f,"bc664b",16);
                center.RotationDegrees=new(90,0,0);
            }
            Box(arena,"Backstop",new(27,1.3f,-23),new(9,2.6f,.4f),"887250");
            Box(arena,"WeaponRack",new(9,1,-10),new(.3f,2,3),"806243");
            foreach(var z in new[]{-11f,-10f,-9f}) Box(arena,"PracticeSword",new(9,1.5f,z),new(.08f,1.6f,.15f),"b9bbb4");
            foreach(var (x,z) in new (float,float)[]{(-31,-19),(-31,8),(-31,18),(31,17),(31,6),(-14,-4),(-12,7),(5,17),(-20,-23),(-5,23)})
                Tree(greenery,x,z);
            foreach(var (x,z) in new (float,float)[]{(-9,9),(9,10),(-28,-3),(-3,23),(3,23),(17,-2),(-12,-6)})
                Lamp(decorations,x,z);
            foreach(var (x,z) in new (float,float)[]{(-25,26),(-22,-29),(23,-29),(37,14),(38,-17),(-37,-25),(17,30)})
                Tree(greenery,x,z);

            Marker<EntryMarker>(anchors,"Spawn",0,18,1);
            Marker<ActorSpawnMarker>(anchors,"Guide",-2,17,2);
            Marker<InteractionMarker>(anchors,"MagicShop",-7,-5,3);
            Marker<InteractionMarker>(anchors,"Arena",15,-2,4);
            Marker<ActorSpawnMarker>(anchors,"SwordTarget",15,-14,5);
            Marker<GateMarker>(anchors,"SouthGate",0,29,6).Radius=2.8f;
            Marker<EntryMarker>(anchors,"SouthArrival",0,23,7);
            Marker<GateMarker>(anchors,"RiverGate",-36,0,8).Radius=2.8f;
            Marker<EntryMarker>(anchors,"RiverArrival",-30,0,9);
            Preview(root);
            Own(root,root);
            var scene=new PackedScene();
            if(scene.Pack(root)!=Error.Ok) throw new InvalidOperationException("Could not pack city scene.");
            var directory=ProjectSettings.GlobalizePath("res://Scenes/Regions/RiverCity");
            Directory.CreateDirectory(directory);
            if(ResourceSaver.Save(scene,$"{directory}/RiverCity.tscn")!=Error.Ok) throw new IOException("Could not save city scene.");
            // Scene generation never publishes server content; use the common exporter afterwards.
            var geometry=FlatGeometryBake.Bake(root);
            root.Free();
            GD.Print($"RIVER_CITY_BUILT: editable scene, {geometry.CreateGrid().CellCount} cells, hash {geometry.Hash:X16}");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static Node3D Group(Node parent,string name) { var node=new Node3D{Name=name}; parent.AddChild(node); return node; }
    private StandardMaterial3D Material(string color)
    {
        if(!_materials.TryGetValue(color,out var material)) _materials[color]=material=new(){AlbedoColor=new Color(color),Roughness=.9f};
        return material;
    }
    private MeshInstance3D Box(Node parent,string name,Vector3 position,Vector3 size,string color)
    {
        if(!_boxes.TryGetValue(size,out var mesh)) _boxes[size]=mesh=new(){Size=size};
        var node=new MeshInstance3D{Name=$"{name}_{++_serial}",Position=position,Mesh=mesh,MaterialOverride=Material(color)};
        parent.AddChild(node); return node;
    }
    private void Solid(Node parent,string name,Vector3 position,Vector3 size,string color,bool floor=false)
    {
        var body=new StaticBody3D{Name=$"{name}_{++_serial}",Position=position,CollisionLayer=floor?1u:2u,CollisionMask=0};
        parent.AddChild(body); body.AddToGroup(floor?"region_walkable":"region_movement_blocker",true);
        if(!_shapes.TryGetValue(size,out var shape)) _shapes[size]=shape=new(){Size=size};
        body.AddChild(new CollisionShape3D{Name="Collision",Shape=shape});
        Box(body,"Mesh",Vector3.Zero,size,color);
    }
    private MeshInstance3D Disc(Node parent,string name,Vector3 position,float radius,float height,string color,int sides=12)
    {
        var node=new MeshInstance3D{Name=$"{name}_{++_serial}",Position=position,
            Mesh=new CylinderMesh{TopRadius=radius,BottomRadius=radius,Height=height,RadialSegments=sides},MaterialOverride=Material(color)};
        parent.AddChild(node); return node;
    }
    private void Cone(Node parent,string name,Vector3 position,float radius,float height,string color)
    {
        var node=Disc(parent,name,position,radius,height,color,8);
        ((CylinderMesh)node.Mesh).TopRadius=0;
    }
    private void House(Node parent,float x,float z,float width,float depth,float height,string roof)
    {
        var house=Group(parent,$"House_{++_serial}"); house.Position=new(x,0,z);
        Solid(house,"Walls",new(0,height/2,0),new(width,height,depth),"e1ce9d");
        Box(house,"Foundation",new(0,.25f,0),new(width+.2f,.5f,depth+.2f),"a7a797");
        var mesh=new PrismMesh{Size=new(width+.8f,2.4f,depth+.8f)};
        house.AddChild(new MeshInstance3D{Name="Roof",Position=new(0,height+1.1f,0),Mesh=mesh,MaterialOverride=Material(roof)});
        Box(house,"Door",new(0,.95f,depth/2+.035f),new(1.1f,1.9f,.08f),"765335");
        foreach(var side in new[]{-1,1})
        {
            Box(house,"CornerBeam",new(side*(width/2-.08f),height/2,depth/2+.04f),new(.18f,height,.16f),"865d39");
            Box(house,"WindowFrame",new(side*width*.29f,1.8f,depth/2+.07f),new(.95f,1.05f,.14f),"855d3a");
            Box(house,"Window",new(side*width*.29f,1.8f,depth/2+.15f),new(.65f,.75f,.04f),"6c919c");
        }
        Box(house,"Beam",new(0,height-.15f,depth/2+.06f),new(width,.22f,.16f),"865d39");
        Box(house,"Chimney",new(width*.28f,height+1.8f,-depth*.22f),new(.65f,2,.65f),"aa9e87");
    }
    private void Wall(Node parent,float x,float z,float width,float depth)
    {
        Solid(parent,"Wall",new(x,1.8f,z),new(width,3.6f,depth),"a5ae9e");
        var horizontal=width>depth; var length=horizontal?width:depth;
        for(var i=-length/2+.5f;i<length/2;i+=1.6f)
            Box(parent,"Merlon",new(x+(horizontal?i:0),4,z+(horizontal?0:i)),new(horizontal?.8f:width,.8f,horizontal?depth:.8f),"b6bdaa");
    }
    private void Tower(Node parent,float x,float z)
    {
        Solid(parent,"GateTower",new(x,2.5f,z),new(3,5,3),"b0b5a2");
        Box(parent,"TowerCrown",new(x,5,z),new(3.5f,.35f,3.5f),"c3c7b2");
        foreach(var dx in new[]{-1.2f,1.2f}) foreach(var dz in new[]{-1.2f,1.2f})
            Box(parent,"TowerMerlon",new(x+dx,5.5f,z+dz),new(.8f,.8f,.8f),"b0b5a2");
    }
    private void Fence(Node parent,float x,float z,float width,float depth)
    {
        Solid(parent,"Fence",new(x,.55f,z),new(width,1.1f,depth),"95754d");
        var horizontal=width>depth; var length=Math.Max(width,depth);
        for(var i=-length/2;i<=length/2;i+=2)
            Box(parent,"FencePost",new(x+(horizontal?i:0),.75f,z+(horizontal?0:i)),new(.25f,1.5f,.25f),"6e563a");
    }
    private void Stall(Node parent,float x,float z,string color)
    {
        var stall=Group(parent,$"MarketStall_{++_serial}"); stall.Position=new(x,0,z);
        Solid(stall,"Counter",new(0,.55f,0),new(2.8f,1.1f,1.5f),"a9844f");
        foreach(var dx in new[]{-1.5f,1.5f}) Box(stall,"Post",new(dx,1.15f,0),new(.15f,2.3f,.15f),"735738");
        Box(stall,"Canopy",new(0,2.35f,0),new(3.5f,.18f,2.4f),color);
        for(var i=-1;i<=1;i++) Box(stall,"CanopyStripe",new(i*.95f,2.46f,0),new(.45f,.025f,2.4f),"e8dcc0");
        foreach(var dx in new[]{-.8f,0,.8f}) Box(stall,"Goods",new(dx,1.25f,0),new(.5f,.25f,.7f),dx==0?"b9ae5b":"a87050");
    }
    private void Tree(Node parent,float x,float z)
    {
        Solid(parent,"TreeTrunk",new(x,1.2f,z),new(.5f,2.4f,.5f),"7b6540");
        Cone(parent,"TreeCrown",new(x,3.2f,z),1.6f,3.5f,"507855");
        Cone(parent,"TreeTop",new(x,4.7f,z),1.2f,2.8f,"658b57");
    }
    private void Lamp(Node parent,float x,float z)
    {
        Solid(parent,"LampPost",new(x,1.35f,z),new(.18f,2.7f,.18f),"6f593c");
        Box(parent,"Lantern",new(x,2.9f,z),new(.45f,.65f,.45f),"f0c775");
        Cone(parent,"LanternCap",new(x,3.32f,z),.4f,.3f,"586066");
    }
    private void Flag(Node parent,float x,float y,float z,string color)
    {
        Box(parent,"FlagPole",new(x,y,z),new(.09f,2.5f,.09f),"6c5840");
        Box(parent,"Banner",new(x+.55f,y+.3f,z),new(1.1f,1.5f,.035f),color);
        Box(parent,"BannerEmblem",new(x+.55f,y+.3f,z+.025f),new(.18f,.8f,.025f),"dcc28a");
    }
    private static T Marker<T>(Node parent,string name,float x,float z,int id) where T:RegionMarker,new()
    {
        var marker=new T{Name=name,Position=new(x,0,z),AuthoredObjectId=$"41000000-0000-4000-8000-{id:000000000000}"};
        parent.AddChild(marker); return marker;
    }
    private static void Label(Node parent,string text,Vector3 position,float size,int font=32)
    {
        parent.AddChild(new Label3D{Name=$"Label_{parent.GetChildCount()}",Text=text,Position=position,PixelSize=size,FontSize=font,
            Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,Modulate=new Color("fff2cd"),OutlineSize=8});
    }
    private static void Preview(RegionRoot root)
    {
        var preview=new BlockoutPreview{Name="Preview",OverviewSize=84,RegionTitle="РЕЧНАЯ ПРИСТАНЬ · ГОРОД"};
        root.AddChild(preview);
        preview.Camera=new Camera3D{Name="Camera",Current=true,Far=220}; preview.AddChild(preview.Camera);
        preview.Viewpoints=Group(preview,"Viewpoints");
        foreach(var (name,x,z) in new (string,float,float)[]{("Market",0,6),("MagicShop",-7,-5),("Swords",15,-11),("Archery",27.5f,-7),("Houses",-21,13),("SouthGate",0,24)})
            preview.Viewpoints.AddChild(new Node3D{Name=name,Position=new(x,0,z)});
        preview.OverviewLabels=Group(preview,"OverviewLabels");
        foreach(var (text,x,z) in new (string,float,float)[]{("РЫНОЧНАЯ ПЛОЩАДЬ",0,4),("МАГИЧЕСКАЯ ЛАВКА",-7,-13),
            ("МЕЧНИКИ",14,-17),("ЛУЧНИКИ",27,-17),("ЖИЛОЙ КВАРТАЛ",-20,11),
            ("ЮЖНЫЕ ВОРОТА",0,30),("РЕЧНЫЕ ВОРОТА",-31,2)})
            Label(preview.OverviewLabels,text,new(x,8,z),.028f,28);
        var canvas=new CanvasLayer{Name="PreviewControls"}; preview.AddChild(canvas);
        var panel=new PanelContainer{Name="Panel",Position=new(12,12)}; canvas.AddChild(panel);
        preview.Status=new Label{Name="Status"}; preview.Status.AddThemeFontSizeOverride("font_size",16); panel.AddChild(preview.Status);
    }
    private static void Own(Node node,Node root)
    {
        foreach(var child in node.GetChildren()) { child.Owner=root; Own(child,root); }
    }
}
