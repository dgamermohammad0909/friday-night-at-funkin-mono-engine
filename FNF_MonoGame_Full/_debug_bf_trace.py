import json

with open('Content/menus/character_select/bf/player/Animation.json') as f:
    data = json.load(f)

symbols = {s['SN']: s for s in data['SD']['S']}

# Main timeline labels
an = data['AN']
layers = an['TL']['L']
print('=== MAIN TIMELINE LABELS ===')
for li, layer in enumerate(layers):
    for fr in layer.get('FR', []):
        label = fr.get('N', '')
        idx = fr.get('I', 0)
        dur = fr.get('DU', 1)
        if label:
            print(f'  Layer {li}: LABEL="{label}" at I={idx} DU={dur}')

# Show what the main timeline references at each frame
print()
print('=== MAIN TIMELINE LAYER 0 FRAMES ===')
layer0 = layers[0]
for fr in layer0.get('FR', []):
    idx = fr.get('I', 0)
    dur = fr.get('DU', 1)
    label = fr.get('N', '')
    elems = fr.get('E', [])
    label_str = f' LABEL="{label}"' if label else ''
    elem_names = []
    for e in elems:
        si = e.get('SI')
        if si:
            elem_names.append(si.get('SN', '?'))
    print(f'  I={idx} DU={dur}{label_str} -> {elem_names}')

# Now compute body part positions through the full nesting
# for tick 0 of the Idle label
print()
print('=== TRANSFORM CHAIN TRACE (Tick 0) ===')

def apply_trp(m3d_or_mx, trp):
    """Extract transform from M3D array and apply TRP"""
    if m3d_or_mx and len(m3d_or_mx) >= 16:
        a, b = m3d_or_mx[0], m3d_or_mx[1]
        c, d = m3d_or_mx[4], m3d_or_mx[5]
        tx, ty = m3d_or_mx[12], m3d_or_mx[13]
    else:
        a, b, c, d, tx, ty = 1, 0, 0, 1, 0, 0
    if trp:
        tx -= trp.get('x', 0)
        ty -= trp.get('y', 0)
    return a, b, c, d, tx, ty

def compose(pa, pb, pc, pd, ptx, pty, ca, cb, cc, cd, ctx, cty):
    """Compose parent * child affine transforms"""
    wa = pa*ca + pc*cb
    wb = pb*ca + pd*cb
    wc = pa*cc + pc*cd
    wd = pb*cc + pd*cd
    wtx = pa*ctx + pc*cty + ptx
    wty = pb*ctx + pd*cty + pty
    return wa, wb, wc, wd, wtx, wty

def get_frame_at_tick(sym, tick):
    """Get the frame data active at a given tick for all layers"""
    result = []
    layers = sym.get('TL', {}).get('L', [])
    for layer in layers:
        for fr in layer.get('FR', []):
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            if idx <= tick < idx + dur:
                result.append(fr)
                break
    return result

def trace_at_tick(sym_name_or_data, tick, pa, pb, pc, pd, ptx, pty, depth, visited, with_trp=True):
    """Recursively trace all atlas sprites at a given tick"""
    parts = []
    
    if isinstance(sym_name_or_data, str):
        if sym_name_or_data in visited:
            return parts
        visited.add(sym_name_or_data)
        sym = symbols.get(sym_name_or_data)
        if not sym:
            return parts
    else:
        sym = sym_name_or_data
        sym_name_or_data = sym.get('SN', 'AN')
        visited.add(sym_name_or_data)
    
    layers = sym.get('TL', {}).get('L', [])
    for layer in reversed(layers):
        for fr in layer.get('FR', []):
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            if not (idx <= tick < idx + dur):
                continue
            for e in fr.get('E', []):
                asi = e.get('ASI')
                si = e.get('SI')
                if asi:
                    n = asi.get('N', '')
                    m3d = asi.get('M3D')
                    trp = asi.get('TRP') if with_trp else None
                    a, b, c, d, tx, ty = apply_trp(m3d, trp)
                    wa, wb, wc, wd, wtx, wty = compose(pa, pb, pc, pd, ptx, pty, a, b, c, d, tx, ty)
                    parts.append((n, wa, wb, wc, wd, wtx, wty))
                    indent = '  ' * depth
                    print(f'{indent}SPRITE: {n} -> tx={wtx:.1f} ty={wty:.1f}')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D')
                    trp = si.get('TRP') if with_trp else None
                    ff = si.get('FF', 0)
                    a, b, c, d, tx, ty = apply_trp(m3d, trp)
                    ca, cb, cc, cd, ctx, cty = compose(pa, pb, pc, pd, ptx, pty, a, b, c, d, tx, ty)
                    indent = '  ' * depth
                    print(f'{indent}SYMBOL: {sn} (FF={ff}) composed_tx={ctx:.1f} composed_ty={cty:.1f}')
                    
                    nested_sym = symbols.get(sn)
                    if nested_sym:
                        nested_dur = 0
                        for nl in nested_sym.get('TL', {}).get('L', []):
                            for nfr in nl.get('FR', []):
                                ni = nfr.get('I', 0)
                                nd = nfr.get('DU', 1)
                                nested_dur = max(nested_dur, ni + nd)
                        nested_tick = (ff + (tick - idx)) % max(1, nested_dur)
                        sub = trace_at_tick(sn, nested_tick, ca, cb, cc, cd, ctx, cty, depth+1, visited, with_trp)
                        parts.extend(sub)
    
    visited.discard(sym_name_or_data if isinstance(sym_name_or_data, str) else '')
    return parts

# Find the Idle label start frame
idle_start = 0
for fr in layers[0].get('FR', []):
    if fr.get('N', '') == 'Idle':
        idle_start = fr.get('I', 0)
        break

print(f'Idle label starts at tick {idle_start}')
print()
print('--- WITH TRP (applyTRP=true, main timeline path) ---')
parts_trp = trace_at_tick(an, idle_start, 1, 0, 0, 1, 0, 0, 0, set(), with_trp=True)
print()
print(f'Total sprites: {len(parts_trp)}')
for name, a, b, c, d, tx, ty in parts_trp:
    print(f'  {name}: tx={tx:.1f} ty={ty:.1f} a={a:.3f} b={b:.3f} c={c:.3f} d={d:.3f}')

print()
print('--- WITHOUT TRP (applyTRP=false, per-symbol path) ---')
# Per-symbol: trace "bf cs idle" directly
if 'bf cs idle' in symbols:
    parts_notrp = trace_at_tick('bf cs idle', 0, 1, 0, 0, 1, 0, 0, 0, set(), with_trp=False)
    print()
    print(f'Total sprites: {len(parts_notrp)}')
    for name, a, b, c, d, tx, ty in parts_notrp:
        print(f'  {name}: tx={tx:.1f} ty={ty:.1f}')

print()
print('--- WITH TRP, per-symbol "bf cs idle" ---')
if 'bf cs idle' in symbols:
    parts_sym = trace_at_tick('bf cs idle', 0, 1, 0, 0, 1, 0, 0, 0, set(), with_trp=True)
    print()
    print(f'Total sprites: {len(parts_sym)}')
    for name, a, b, c, d, tx, ty in parts_sym:
        print(f'  {name}: tx={tx:.1f} ty={ty:.1f}')
