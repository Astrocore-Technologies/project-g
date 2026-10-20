"""Read-only checks of exported GLB bytes; independent of Blender authoring state."""
from pathlib import Path
import json,struct,math
ROOT=Path(__file__).resolve().parents[3]
directory=ROOT/'Content.Client/Assets/World/Linvel/City/Masterplan/Kit'
reports=[]
for path in sorted(directory.glob('*.glb')):
    raw=path.read_bytes();magic,version,size=struct.unpack_from('<4sII',raw)
    assert (magic,version,size)==(b'glTF',2,len(raw)),path
    offset=12;doc=None;binary=None
    while offset<len(raw):
        length,kind=struct.unpack_from('<II',raw,offset);data=raw[offset+8:offset+8+length];offset+=length+8
        if kind==0x4e4f534a:doc=json.loads(data)
        elif kind==0x004e4942:binary=data
    assert doc and binary is not None and len(doc['scenes'])==1,path
    assert not doc.get('animations') and not doc.get('images'),path
    assert not any(n.get('name') in ['Cube','Camera','Light'] for n in doc['nodes']),path
    bounds=[[float('inf'),float('-inf')] for _ in range(3)];triangles=0;positions=0
    for node in doc['nodes']:
        assert all(abs(s-1)<1e-6 for s in node.get('scale',[1,1,1])),path
        if 'mesh' not in node:continue
        assert not node.get('rotation'),(path,node)
        translate=node.get('translation',[0,0,0])
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            assert primitive.get('mode',4)==4 and 'NORMAL' in primitive['attributes'],path
            accessor=doc['accessors'][primitive['attributes']['POSITION']]
            assert accessor['componentType']==5126 and accessor['type']=='VEC3',path
            view=doc['bufferViews'][accessor['bufferView']];start=view.get('byteOffset',0)+accessor.get('byteOffset',0);stride=view.get('byteStride',12)
            for i in range(accessor['count']):
                xyz=struct.unpack_from('<3f',binary,start+i*stride)
                assert all(math.isfinite(v) for v in xyz),path
                for axis in range(3):
                    v=xyz[axis]+translate[axis];bounds[axis][0]=min(bounds[axis][0],v);bounds[axis][1]=max(bounds[axis][1],v)
            positions+=accessor['count'];triangles+=doc['accessors'][primitive['indices']]['count']//3
    assert abs(bounds[1][0])<.002,(path,bounds)
    reports.append(dict(file=path.name,triangles=triangles,positions=positions,bounds_y_up_m=bounds,untextured_review_proxy=True))
assert len(reports)==11
out=ROOT/'.artifacts/linvel-masterplan/kit-bytes-validation.json'
out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(reports,indent=2),encoding='utf-8')
print(json.dumps(dict(ok=True,assets=len(reports),triangles=sum(r['triangles'] for r in reports),report=str(out))))
