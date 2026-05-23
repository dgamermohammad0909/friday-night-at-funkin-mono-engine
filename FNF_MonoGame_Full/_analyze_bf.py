"""Analyze BF player Animation.json for character select - check transforms and body parts."""
import json, sys

path = "Content/menus/character_select/bf/player/Animation.json"
d = json.load(open(path))

an = d.get("AN", {})
tl = an.get("TL", {})
layers = tl.get("L", [])
print(f"Main timeline layers: {len(layers)}")

sd = d.get("SD", {})
syms = sd.get("S", [])
print(f"Symbols: {len(syms)}")
print("Symbol names:")
for s in syms[:30]:
    sn = s.get("SN", "?")
    stl = s.get("TL", {})
    sl = stl.get("L", [])
    dur = 0
    for layer in sl:
        for fr in layer.get("FR", []):
            dur = max(dur, fr.get("I", 0) + fr.get("DU", 1))
    print(f"  {sn} ({dur} ticks, {len(sl)} layers)")

# Main timeline labels
print("\nMain timeline frame labels:")
for li, layer in enumerate(layers):
    for fr in layer.get("FR", []):
        label = fr.get("N", "")
        if label:
            print(f"  Layer {li}, Frame {fr.get('I',0)}: '{label}' (dur={fr.get('DU',1)})")

# Check first tick of "Idle" label - what symbols/sprites are referenced?
print("\n--- Analyzing 'Idle' animation body parts (tick 0) ---")
# Find Idle start frame
idle_start = None
for layer in layers:
    for fr in layer.get("FR", []):
        if fr.get("N") == "Idle":
            idle_start = fr.get("I", 0)
            break
    if idle_start is not None:
        break

if idle_start is not None:
    print(f"Idle starts at tick {idle_start}")
    # Collect all elements at idle_start across all layers
    for li, layer in enumerate(layers):
        for fr in layer.get("FR", []):
            idx = fr.get("I", 0)
            dur = fr.get("DU", 1)
            if idx <= idle_start < idx + dur:
                elements = fr.get("E", [])
                for ei, elem in enumerate(elements):
                    si = elem.get("SI")
                    asi = elem.get("ASI")
                    if si:
                        sn = si.get("SN", "?")
                        m3d = si.get("M3D", [])
                        trp = si.get("TRP", {})
                        ff = si.get("FF", 0)
                        print(f"  Layer {li}: SI '{sn}' FF={ff}")
                        if m3d:
                            a, b = m3d[0], m3d[1]
                            c, dd = m3d[4], m3d[5]
                            tx, ty = m3d[12], m3d[13]
                            print(f"    M3D: a={a:.3f} b={b:.3f} c={c:.3f} d={dd:.3f} tx={tx:.1f} ty={ty:.1f}")
                        if trp:
                            print(f"    TRP: x={trp.get('x',0):.1f} y={trp.get('y',0):.1f}")
                    if asi:
                        n = asi.get("N", "?")
                        m3d = asi.get("M3D", [])
                        print(f"  Layer {li}: ASI '{n}'")
                        if m3d:
                            a, b = m3d[0], m3d[1]
                            c, dd = m3d[4], m3d[5]
                            tx, ty = m3d[12], m3d[13]
                            print(f"    M3D: a={a:.3f} b={b:.3f} c={c:.3f} d={dd:.3f} tx={tx:.1f} ty={ty:.1f}")

# Also check spritemap for atlas frame info
print("\n--- Spritemap atlas frames ---")
sm = json.load(open("Content/menus/character_select/bf/player/spritemap1.json"))
sprites = sm.get("ATLAS", {}).get("SPRITES", [])
print(f"Total atlas sprites: {len(sprites)}")
for sp in sprites[:10]:
    s = sp.get("SPRITE", {})
    print(f"  '{s.get('name','')}': x={s.get('x',0)} y={s.get('y',0)} w={s.get('w',0)} h={s.get('h',0)} rotated={s.get('rotated',False)}")
if len(sprites) > 10:
    print(f"  ... and {len(sprites)-10} more")
