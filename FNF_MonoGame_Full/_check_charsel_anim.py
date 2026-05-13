import json

# Check BF player Animation.json structure
print("=== BF PLAYER ===")
d = json.load(open('Content/menus/character_select/bf/player/Animation.json'))
tl = d['AN']['TL']['L']
print(f'Layers: {len(tl)}')
for i, layer in enumerate(tl):
    frs = layer.get('FR', [])
    print(f'Layer {i}: {len(frs)} frames')
    for j, fr in enumerate(frs[:3]):
        els = fr.get('E', [])
        print(f'  Frame {j}: idx={fr.get("I",0)} dur={fr.get("DU",1)} els={len(els)} label={fr.get("N","")}')
        for k, e in enumerate(els[:2]):
            print(f'    E{k} keys: {list(e.keys())}')
            si = e.get('SI', {})
            if si:
                print(f'      SI.SN={si.get("SN","")} M3D={si.get("M3D",[])[:6]}... TRP={si.get("TRP",{})}')
            asi = e.get('ASI', {})
            if asi:
                print(f'      ASI.N={asi.get("N","")} M3D={asi.get("M3D",[])[:6]}...')

# Check symbol dictionary
sd = d.get('SD', {}).get('S', [])
print(f'\nSymbol Dictionary: {len(sd)} symbols')
for s in sd[:5]:
    sn = s.get('SN', '')
    stl = s.get('TL', {}).get('L', [])
    dur = 0
    for layer in stl:
        for fr in layer.get('FR', []):
            dur = max(dur, fr.get('I', 0) + fr.get('DU', 1))
    print(f'  Symbol: {sn} layers={len(stl)} duration={dur}')

print("\n=== BF SPECTATOR ===")
d2 = json.load(open('Content/menus/character_select/bf/spectator/Animation.json'))
tl2 = d2['AN']['TL']['L']
print(f'Layers: {len(tl2)}')
for i, layer in enumerate(tl2):
    frs = layer.get('FR', [])
    print(f'Layer {i}: {len(frs)} frames')
    for j, fr in enumerate(frs[:3]):
        els = fr.get('E', [])
        print(f'  Frame {j}: idx={fr.get("I",0)} dur={fr.get("DU",1)} els={len(els)} label={fr.get("N","")}')
        for k, e in enumerate(els[:2]):
            si = e.get('SI', {})
            if si:
                print(f'    SI.SN={si.get("SN","")} M3D={si.get("M3D",[])[:6]}... TRP={si.get("TRP",{})}')

sd2 = d2.get('SD', {}).get('S', [])
print(f'\nSymbol Dictionary: {len(sd2)} symbols')
for s in sd2[:5]:
    sn = s.get('SN', '')
    print(f'  Symbol: {sn}')
