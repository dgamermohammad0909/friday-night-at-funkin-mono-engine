import json

# Trace BF player composite positions - find where body parts actually end up
d = json.load(open('Content/menus/character_select/bf/player/Animation.json'))
sd = {s['SN']: s for s in d.get('SD', {}).get('S', [])}

def trace_positions(sym, symbols, depth=0, parent_tx=0, parent_ty=0):
    """Recursively find all ASI positions with accumulated transforms"""
    tl = sym.get('TL', {}).get('L', [])
    for li, layer in enumerate(tl):
        for fr in layer.get('FR', [])[:3]:  # Just first 3 frames
            for e in fr.get('E', []):
                m3d = e.get('M3D', [])
                trp = e.get('TRP', {})
                tx = m3d[12] if len(m3d) > 12 else (m3d[4] if len(m3d) > 4 else 0)
                ty = m3d[13] if len(m3d) > 13 else (m3d[5] if len(m3d) > 5 else 0)
                trpx = trp.get('x', 0)
                trpy = trp.get('y', 0)
                final_tx = parent_tx + tx - trpx
                final_ty = parent_ty + ty - trpy
                
                asi = e.get('ASI', {})
                if asi:
                    name = asi.get('N', '')
                    am = asi.get('M3D', [])
                    atx = am[12] if len(am) > 12 else (am[4] if len(am) > 4 else 0)
                    aty = am[13] if len(am) > 13 else (am[5] if len(am) > 5 else 0)
                    atrp = asi.get('TRP', {})
                    atrpx = atrp.get('x', 0)
                    atrpy = atrp.get('y', 0)
                    abs_x = final_tx + atx - atrpx
                    abs_y = final_ty + aty - atrpy
                    print(f'{"  "*depth}ASI: {name[:40]:40s} abs=({abs_x:.1f}, {abs_y:.1f}) local_m3d=({atx:.1f},{aty:.1f}) trp=({atrpx:.1f},{atrpy:.1f})')
                
                si = e.get('SI', {})
                if si:
                    sn = si.get('SN', '')
                    print(f'{"  "*depth}SI: {sn} tx={tx:.1f} ty={ty:.1f} trp=({trpx:.1f},{trpy:.1f}) acc=({final_tx:.1f},{final_ty:.1f})')
                    if sn in symbols and depth < 4:
                        trace_positions(symbols[sn], symbols, depth+1, final_tx, final_ty)

print("=== BF PLAYER - Tracing body part positions ===")
# Start from main timeline
an = d['AN']
tl = an['TL']['L']
print(f'Main timeline: {len(tl)} layers')
for li, layer in enumerate(tl):
    frs = layer.get('FR', [])
    for j, fr in enumerate(frs[:3]):
        els = fr.get('E', [])
        if not els: continue
        print(f'\nLayer {li} Frame {j} (idx={fr.get("I",0)} label={fr.get("N","")}):')
        for e in els:
            si = e.get('SI', {})
            if si:
                sn = si.get('SN', '')
                m3d = si.get('M3D', [])
                tx = m3d[12] if len(m3d) > 12 else (m3d[4] if len(m3d) > 4 else 0)
                ty = m3d[13] if len(m3d) > 13 else (m3d[5] if len(m3d) > 5 else 0)
                trp = si.get('TRP', {})
                trpx = trp.get('x', 0)
                trpy = trp.get('y', 0)
                print(f'  SI: {sn} tx={tx:.1f} ty={ty:.1f} trp=({trpx:.1f},{trpy:.1f})')
                if sn in sd:
                    trace_positions(sd[sn], sd, depth=2, parent_tx=tx-trpx, parent_ty=ty-trpy)
