import json

data = open('Content/menus/character_select/bf/player/Animation.json', 'r').read()
d = json.loads(data)
symbols = {s['SN']: s for s in d['SD']['S']}

# Check what's inside each body part symbol
for body_part in ['head bf', 'face bf', 'body bf', 'arm right bf', 'arm left bf']:
    sym = symbols.get(body_part)
    if not sym:
        print(f'{body_part}: NOT FOUND')
        continue
    layers = sym['TL']['L']
    print(f'=== {body_part} ===')
    for li, layer in enumerate(layers):
        for fr in layer.get('FR', []):
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            for e in fr.get('E', []):
                asi = e.get('ASI')
                si = e.get('SI')
                if asi:
                    n = asi.get('N', '')
                    m3d = asi.get('M3D')
                    trp = asi.get('TRP')
                    tx = m3d[12] if m3d else 0
                    ty = m3d[13] if m3d else 0
                    trpx = trp.get('x', 0) if trp else 0
                    trpy = trp.get('y', 0) if trp else 0
                    trp_str = f' TRP=({trpx:.1f},{trpy:.1f})' if trp else ''
                    print(f'  ASI: {n} tx={tx:.1f} ty={ty:.1f}{trp_str}')
                if si:
                    sn = si.get('SN', '')
                    m3d = si.get('M3D')
                    trp = si.get('TRP')
                    tx = m3d[12] if m3d else 0
                    ty = m3d[13] if m3d else 0
                    trpx = trp.get('x', 0) if trp else 0
                    trpy = trp.get('y', 0) if trp else 0
                    trp_str = f' TRP=({trpx:.1f},{trpy:.1f})' if trp else ''
                    print(f'  SI: {sn} tx={tx:.1f} ty={ty:.1f}{trp_str}')

# Now compute the FULL transform chain for tick 0 of bf cs idle with TRP
print()
print('=== FULL TRANSFORM CHAIN: bf cs idle tick 0, WITH TRP ===')
idle = symbols['bf cs idle']
layers = idle['TL']['L']
for li in reversed(range(len(layers))):
    layer = layers[li]
    for fr in layer.get('FR', []):
        idx = fr.get('I', 0)
        dur = fr.get('DU', 1)
        if not (0 <= idx < idx + dur and idx == 0):
            continue
        for e in fr.get('E', []):
            si = e.get('SI')
            if si:
                sn = si.get('SN', '')
                m3d = si.get('M3D')
                trp = si.get('TRP')
                a = m3d[0] if m3d else 1
                b = m3d[1] if m3d else 0
                c = m3d[4] if m3d else 0
                d_val = m3d[5] if m3d else 1
                tx = m3d[12] if m3d else 0
                ty = m3d[13] if m3d else 0
                trpx = trp.get('x', 0) if trp else 0
                trpy = trp.get('y', 0) if trp else 0
                adj_tx = tx - trpx
                adj_ty = ty - trpy
                
                # Now get the ASI inside this symbol
                inner = symbols.get(sn, {})
                inner_layers = inner.get('TL', {}).get('L', [])
                for il in inner_layers:
                    for ifr in il.get('FR', []):
                        for ie in ifr.get('E', []):
                            iasi = ie.get('ASI')
                            if iasi:
                                in_name = iasi.get('N', '')
                                im3d = iasi.get('M3D')
                                itrp = iasi.get('TRP')
                                ia = im3d[0] if im3d else 1
                                ib = im3d[1] if im3d else 0
                                ic = im3d[4] if im3d else 0
                                id_val = im3d[5] if im3d else 1
                                itx = im3d[12] if im3d else 0
                                ity = im3d[13] if im3d else 0
                                itrpx = itrp.get('x', 0) if itrp else 0
                                itrpy = itrp.get('y', 0) if itrp else 0
                                iadj_tx = itx - itrpx
                                iadj_ty = ity - itrpy
                                
                                # Compose: parent SI × child ASI
                                final_tx = a * iadj_tx + c * iadj_ty + adj_tx
                                final_ty = b * iadj_tx + d_val * iadj_ty + adj_ty
                                
                                print(f'{sn} -> {in_name}:')
                                print(f'  SI: tx={tx:.1f} ty={ty:.1f} TRP=({trpx:.1f},{trpy:.1f}) adj=({adj_tx:.1f},{adj_ty:.1f})')
                                print(f'  ASI: tx={itx:.1f} ty={ity:.1f} TRP=({itrpx:.1f},{itrpy:.1f}) adj=({iadj_tx:.1f},{iadj_ty:.1f})')
                                print(f'  FINAL: tx={final_tx:.1f} ty={final_ty:.1f}')
