import json

with open('Content/menus/character_select/bf/player/Animation.json') as f:
    data = json.load(f)

# Show main timeline structure - what labels exist
an = data['AN']
tl = an['TL']
layers = tl['L']
print('=== Main Timeline ===')
for li, layer in enumerate(layers):
    frames = layer.get('FR', [])
    print(f'Layer {li}: {len(frames)} frames')
    for fr in frames:
        idx = fr.get('I', 0)
        dur = fr.get('DU', 1)
        label = fr.get('N', '')
        elems = fr.get('E', [])
        label_str = f' LABEL="{label}"' if label else ''
        print(f'  I={idx} DU={dur}{label_str} elems={len(elems)}')
        for e in elems[:2]:
            si = e.get('SI')
            if si:
                sn = si.get('SN', '')
                m3d = si.get('M3D')
                trp = si.get('TRP')
                ff = si.get('FF', 0)
                lp = si.get('LP', '')
                print(f'    SI: {sn} FF={ff} LP={lp}')
                if m3d:
                    print(f'      M3D: a={m3d[0]:.4f} b={m3d[1]:.4f} c={m3d[4]:.4f} d={m3d[5]:.4f}')
                    print(f'      M3D tx={m3d[12]:.2f} ty={m3d[13]:.2f}')
                if trp:
                    print(f'      TRP: x={trp["x"]:.2f} y={trp["y"]:.2f}')

# Now look at the "bf cs idle" symbol - the main idle animation
print()
print('=== bf cs idle symbol ===')
symbols = {s['SN']: s for s in data['SD']['S']}
idle_sym = symbols.get('bf cs idle')
if idle_sym:
    idle_layers = idle_sym['TL']['L']
    for li, layer in enumerate(idle_layers):
        frames = layer.get('FR', [])
        print(f'Layer {li}: {len(frames)} frames')
        for fr in frames[:3]:
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            elems = fr.get('E', [])
            print(f'  I={idx} DU={dur} elems={len(elems)}')
            for e in elems:
                si = e.get('SI')
                asi = e.get('ASI')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D')
                    trp = si.get('TRP')
                    ff = si.get('FF', 0)
                    print(f'    SI: {sn} FF={ff}')
                    if m3d:
                        print(f'      M3D: a={m3d[0]:.4f} b={m3d[1]:.4f} c={m3d[4]:.4f} d={m3d[5]:.4f} tx={m3d[12]:.2f} ty={m3d[13]:.2f}')
                    if trp:
                        print(f'      TRP: x={trp["x"]:.2f} y={trp["y"]:.2f}')
                if asi:
                    n = asi.get('N', '')
                    m3d = asi.get('M3D')
                    print(f'    ASI: {n}')
                    if m3d:
                        print(f'      M3D: a={m3d[0]:.4f} b={m3d[1]:.4f} c={m3d[4]:.4f} d={m3d[5]:.4f} tx={m3d[12]:.2f} ty={m3d[13]:.2f}')

# Trace full hierarchy: bf cs idle -> body parts -> atlas sprites for tick 0
print()
print('=== Full trace for tick 0 of Idle ===')
def trace_symbol(sym_name, depth=0, parent_matrix=None):
    indent = '  ' * depth
    sym = symbols.get(sym_name)
    if not sym:
        print(f'{indent}[NOT FOUND: {sym_name}]')
        return
    layers = sym['TL']['L']
    for li, layer in enumerate(layers):
        frames = layer.get('FR', [])
        for fr in frames:
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            if idx > 0:
                continue  # only tick 0
            elems = fr.get('E', [])
            for e in elems:
                si = e.get('SI')
                asi = e.get('ASI')
                if asi:
                    n = asi.get('N', '')
                    m3d = asi.get('M3D', [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])
                    tx = m3d[12] if len(m3d) > 12 else 0
                    ty = m3d[13] if len(m3d) > 13 else 0
                    a, b, c, d = m3d[0], m3d[1], m3d[4], m3d[5]
                    print(f'{indent}SPRITE: {n} a={a:.3f} b={b:.3f} c={c:.3f} d={d:.3f} tx={tx:.1f} ty={ty:.1f}')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D', [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])
                    trp = si.get('TRP')
                    tx = m3d[12] if len(m3d) > 12 else 0
                    ty = m3d[13] if len(m3d) > 13 else 0
                    a, b, c, d = m3d[0], m3d[1], m3d[4], m3d[5]
                    trp_str = ''
                    if trp:
                        trp_str = f' TRP=({trp["x"]:.1f},{trp["y"]:.1f})'
                    print(f'{indent}SYMBOL: {sn} a={a:.3f} b={b:.3f} c={c:.3f} d={d:.3f} tx={tx:.1f} ty={ty:.1f}{trp_str}')
                    trace_symbol(sn, depth + 1)

# Find what the main timeline references for Idle
print('Main TL -> Idle:')
for layer in layers:
    for fr in layer.get('FR', []):
        label = fr.get('N', '')
        if label == 'Idle' or (not label and fr.get('I', 0) == 0):
            for e in fr.get('E', []):
                si = e.get('SI')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D', [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])
                    trp = si.get('TRP')
                    tx = m3d[12] if len(m3d) > 12 else 0
                    ty = m3d[13] if len(m3d) > 13 else 0
                    a, b, c, d = m3d[0], m3d[1], m3d[4], m3d[5]
                    trp_str = ''
                    if trp:
                        trp_str = f' TRP=({trp["x"]:.1f},{trp["y"]:.1f})'
                    print(f'  SYMBOL: {sn} a={a:.3f} b={b:.3f} c={c:.3f} d={d:.3f} tx={tx:.1f} ty={ty:.1f}{trp_str}')
                    trace_symbol(sn, 2)
