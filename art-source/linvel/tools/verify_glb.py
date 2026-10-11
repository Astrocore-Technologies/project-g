"""Read-only inspection of the actual binary GLB outputs, not export receipts."""
from pathlib import Path
import struct
import json
import math
import hashlib

ROOT = Path(__file__).resolve().parents[1]

def inspect(path):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from('<4sII', raw)
    assert magic == b'glTF' and version == 2 and length == len(raw), path.name
    json_len, json_type = struct.unpack_from('<II', raw, 12)
    assert json_type == 0x4E4F534A
    data = json.loads(raw[20:20 + json_len])
    pos = 20 + json_len
    binary_len, binary_type = struct.unpack_from('<II', raw, pos)
    assert binary_type == 0x004E4942
    binary = raw[pos + 8:pos + 8 + binary_len]
    failures = []
    triangles = 0
    for node in data.get('nodes', []):
        if node.get('name') in ('Cube', 'Camera', 'Light', 'StudioGround'):
            failures.append('unexpected studio/startup object ' + node['name'])
        if node.get('scale', [1, 1, 1]) != [1, 1, 1]:
            failures.append('non-unit node scale')
    for mesh in data['meshes']:
        for primitive in mesh['primitives']:
            attrs = primitive['attributes']
            assert 'POSITION' in attrs and 'NORMAL' in attrs and 'TEXCOORD_0' in attrs
            assert primitive.get('mode', 4) == 4
            pos_acc = data['accessors'][attrs['POSITION']]
            for key in ('min', 'max'):
                assert all(math.isfinite(v) for v in pos_acc[key])
            triangles += data['accessors'][primitive['indices']]['count'] // 3
    for image in data.get('images', []):
        assert 'uri' not in image and image['mimeType'] == 'image/png', 'image must be embedded PNG'
        view = data['bufferViews'][image['bufferView']]
        encoded = binary[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
        assert encoded.startswith(b'\x89PNG\r\n\x1a\n')
    assert not data.get('animations') and not data.get('cameras')
    assert not failures, (path.name, failures)
    assert triangles > 0 and data.get('materials')
    return {'file': path.name, 'bytes': len(raw), 'triangles': triangles,
            'meshes': len(data['meshes']), 'materials': len(data['materials']),
            'embedded_images': len(data.get('images', [])),
            'sha256': hashlib.sha256(raw).hexdigest(), 'failures': failures}

files = sorted((ROOT / 'exports').glob('*.glb'))
assert len(files) == 50, f'Expected 50 assets, found {len(files)}'
results = [inspect(path) for path in files]
report = {'asset_count': len(results), 'all_passed': True,
          'total_bytes': sum(r['bytes'] for r in results), 'assets': results}
(ROOT / 'reports/glb-validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({k: report[k] for k in ('asset_count', 'all_passed', 'total_bytes')}))
