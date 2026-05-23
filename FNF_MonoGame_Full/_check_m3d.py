import json

with open('Content/menus/character_select/bf/player/Animation.json') as f:
    data = json.load(f)

tl = data['AN']['TL']['L']
layer1 = tl[1]

# Check several ticks to understand the transforms
for target_tick in [0, 9, 18, 20, 25, 31]:
    for fr in layer1.get('FR', []):
        idx = fr.get('I', 0)
        dur = fr.get('DU', 1)
        if idx <= target_tick < idx + dur:
            for e in fr.get('E', []):
                si = e.get('SI')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D')
                    trp = si.get('TRP')
                    ff = si.get('FF', 0)
                    print(f'Tick {target_tick}: SN="{sn}" FF={ff}')
                    if m3d:
                        print(f'  M3D: a={m3d[0]:.4f} b={m3d[1]:.4f} c={m3d[4]:.4f} d={m3d[5]:.4f} tx={m3d[12]:.1f} ty={m3d[13]:.1f}')
                    if trp:
                        print(f'  TRP: x={trp["x"]:.1f} y={trp["y"]:.1f}')
                    else:
                        print(f'  TRP: none')
            break
