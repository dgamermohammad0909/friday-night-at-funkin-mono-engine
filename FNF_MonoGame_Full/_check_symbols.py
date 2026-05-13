import json, os

for p, label in [
    ('Content/menus/character_select/bf/player', 'BF Player'),
    ('Content/menus/character_select/bf/spectator', 'BF Spectator'),
    ('Content/menus/character_select/pico/player', 'Pico Player'),
    ('Content/menus/character_select/pico/spectator', 'Pico Spectator'),
]:
    anim = os.path.join(p, 'Animation.json')
    if not os.path.exists(anim):
        print(f'{label}: NOT FOUND at {anim}')
        continue
    with open(anim) as f:
        data = json.load(f)
    syms = data.get('SD', {}).get('S', [])
    an = data.get('AN', {})
    tl = an.get('TL', {}).get('L', [])
    
    # Find top-level symbols (directly referenced from main TL)
    top = set()
    for layer in tl:
        for fr in layer.get('FR', []):
            for e in fr.get('E', []):
                si = e.get('SI')
                if si:
                    top.add(si.get('SN', ''))
    
    print(f'=== {label} ===')
    print(f'Symbol count: {len(syms)}')
    print(f'Top-level symbols: {top}')
    for s in syms:
        sn = s.get('SN', '')
        dur = 0
        for l in s.get('TL', {}).get('L', []):
            for fr in l.get('FR', []):
                dur = max(dur, fr.get('I',0) + fr.get('DU',1))
        is_top = '(TOP)' if sn in top else ''
        print(f'  {sn} (dur={dur}) {is_top}')
    
    print(f'Main TL labels:')
    for layer in tl:
        for fr in layer.get('FR', []):
            n = fr.get('N', '')
            idx = fr.get('I', 0)
            dur = fr.get('DU', 1)
            elems = fr.get('E', [])
            elem_names = [e.get('SI', {}).get('SN', '') for e in elems if 'SI' in e]
            if n or elem_names:
                print(f'  I={idx} DU={dur} LABEL="{n}" refs={elem_names}')
    print()
