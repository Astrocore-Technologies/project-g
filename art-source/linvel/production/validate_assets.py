"""Read-only checks of the actual exported GLB buffers, independent of Blender."""
from pathlib import Path
import json, struct, math, hashlib
ROOT=Path(__file__).resolve().parents[3]
directory=ROOT/'Content.Client/Assets/World/Linvel/City/Production/Models'
reports=[]
for path in sorted(directory.glob('*.glb')):
    raw=path.read_bytes()
    assert struct.unpack_from('<4sII',raw)==(b'glTF',2,len(raw)),path
    offset=12;doc=None;binary=None
    while offset<len(raw):
        size,kind=struct.unpack_from('<II',raw,offset)
        block=raw[offset+8:offset+8+size];offset+=size+8
        if kind==0x4e4f534a:doc=json.loads(block)
        elif kind==0x004e4942:binary=block
    assert doc and binary is not None and len(doc['scenes'])==1,path
    assert not doc.get('animations') and not doc.get('cameras'),path
    assert not any(n.get('name') in ['Cube','Camera','Light'] for n in doc['nodes']),path
    def accessor(index):
        a=doc['accessors'][index];view=doc['bufferViews'][a['bufferView']]
        count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        fmt={5126:'f',5125:'I',5123:'H',5121:'B'}[a['componentType']]
        packed='<'+fmt*count;length=struct.calcsize(packed)
        start=view.get('byteOffset',0)+a.get('byteOffset',0);stride=view.get('byteStride',length)
        assert start+(a['count']-1)*stride+length<=len(binary),path
        return [struct.unpack_from(packed,binary,start+i*stride) for i in range(a['count'])]
    bounds=[[float('inf'),float('-inf')] for _ in range(3)]
    triangles=vertices=degenerate=0
    for node in doc['nodes']:
        if 'mesh' not in node:continue
        assert not node.get('rotation') and all(abs(v-1)<1e-6 for v in node.get('scale',[1]*3)),path
        translate=node.get('translation',[0]*3)
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            assert primitive.get('mode',4)==4 and all(k in primitive['attributes'] for k in ['POSITION','NORMAL','TEXCOORD_0']),path
            xyz=accessor(primitive['attributes']['POSITION'])
            for p in xyz:
                assert all(math.isfinite(v) for v in p),path
                for axis in range(3):
                    v=p[axis]+translate[axis];bounds[axis][0]=min(bounds[axis][0],v);bounds[axis][1]=max(bounds[axis][1],v)
            indices=[v[0] for v in accessor(primitive['indices'])]
            assert len(indices)%3==0 and min(indices)>=0 and max(indices)<len(xyz),path
            for i in range(0,len(indices),3):
                a,b,c=[xyz[indices[i+j]] for j in range(3)]
                ab=[b[j]-a[j] for j in range(3)];ac=[c[j]-a[j] for j in range(3)]
                cross=[ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0]]
                if sum(v*v for v in cross)<1e-18:degenerate+=1
            vertices+=len(xyz);triangles+=len(indices)//3
    for image in doc.get('images',[]):
        assert 'bufferView' in image and image.get('mimeType') in ['image/png','image/jpeg'],path
        v=doc['bufferViews'][image['bufferView']];assert v['byteOffset']+v['byteLength']<=len(binary)
    assert triangles>0 and doc.get('images'),path
    if path.stem=='merchant_ship_large':assert abs(bounds[1][0]+4)<.005 and abs(bounds[1][1]-31)<.005,(path,bounds)
    reports.append(dict(asset=path.stem,triangles=triangles,vertices=vertices,bounds_y_up_m=bounds,degenerate_triangles=degenerate,embedded_images=len(doc.get('images',[])),sha256=hashlib.sha256(raw).hexdigest()))
assert len(reports)==48,len(reports)
out=ROOT/'.artifacts/linvel-production/asset-bytes-validation.json'
out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(reports,indent=2),encoding='utf-8')
print(json.dumps(dict(ok=True,assets=len(reports),triangles=sum(r['triangles'] for r in reports),degenerate_triangles=sum(r['degenerate_triangles'] for r in reports),report=str(out))))
