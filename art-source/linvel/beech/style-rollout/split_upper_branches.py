# Blender MCP only. Split preserves exactly the same visible branch/trunk
# surfaces. Both halves meet on Z=4 with zero gap; their shared boundary is
# created by one plane cut. This enables camera hiding above the lower trunk.
import bpy, bmesh, json
from pathlib import Path
scene=bpy.data.scenes['BeechValleyVisualStyle']
bpy.context.window.scene=scene
audit=[]
for label in ['beech_canopy_'+str(i) for i in range(7)]+['beech_copse_0','beech_copse_1']:
    bark=scene.objects[label+'_Bark']
    assert not scene.objects.get(label+'_UpperBranches'), 'Preserve existing split'
    collection=bark.users_collection[0]
    for upper in [False,True]:
        bm=bmesh.new();bm.from_mesh(bark.data)
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=0.000001,plane_co=(0,0,4),plane_no=(0,0,1),clear_inner=upper,clear_outer=not upper)
        mesh=bpy.data.meshes.new(label+('_Upper' if upper else '_Lower'))
        bm.to_mesh(mesh);bm.free()
        mesh.color_attributes.active_color = mesh.color_attributes.get('Paint')
        for material in bark.data.materials: mesh.materials.append(material)
        assert not mesh.validate(verbose=False)
        if upper:
            obj=bpy.data.objects.new(label+'_UpperBranches',mesh);collection.objects.link(obj)
        else:
            lower=mesh
    bark.data=lower
    bpy.ops.object.select_all(action='DESELECT')
    objects=[bark,scene.objects[label+'_UpperBranches'],scene.objects[label+'_Leaves']]
    for obj in objects: obj.select_set(True);bpy.context.view_layer.objects.active=obj
    assert abs(max(v.co.z for v in lower.vertices)-4)<.00001
    assert abs(min(v.co.z for v in objects[1].data.vertices)-4)<.00001
    bpy.ops.export_scene.gltf(filepath='D:/projects/project-g/Content.Client/Assets/World/Linvel/Beech/Revision2/VisualStyle/Models/'+label+'.glb',export_format='GLB',use_selection=True,use_active_scene=True,export_vertex_color='ACTIVE',export_yup=True,export_apply=True)
    audit.append({'id':label,'joint_gap_metres':0,'lower_trunk_height':4,'physics':'unchanged'})
source=Path('D:/projects/project-g/art-source/linvel/beech/style-rollout')
bpy.data.libraries.write(str(source/'beech_style_assets.blend'),{scene},fake_user=True,compress=True)
(source/'branch-split-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
result={'success':True,'split_assets':len(audit),'joint_gap_metres':0}
