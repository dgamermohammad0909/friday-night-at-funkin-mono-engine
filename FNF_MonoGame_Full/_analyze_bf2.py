"""Deep-trace BF player Idle animation body parts with full transform chain."""
import json

path = "Content/menus/character_select/bf/player/Animation.json"
d = json.load(open(path))

# Build symbol lookup
syms_by_name = {}
for s in d.get("SD", {}).get("S", []):
    sn = s.get("SN", "")
    if sn:
        syms_by_name[sn] = s

# Read spritemap
try:
    sm_text = open("Content/menus/character_select/bf/player/spritemap1.json", "rb").read()
    # Remove BOM if present
    if sm_text[:3] == b'\xef\xbb\xbf':
        sm_text = sm_text[3:]
    # Remove null bytes
    sm_text = sm_text.replace(b'\x00', b'')
    sm = json.loads(sm_text)
    sprites = {sp["SPRITE"]["name"]: sp["SPRITE"] for sp in sm.get("ATLAS", {}).get("SPRITES", []) if "SPRITE" in sp}
    print(f"Loaded {len(sprites)} atlas sprites")
except Exception as e:
    print(f"Spritemap load error: {e}")
    sprites = {}

def read_transform(elem, apply_trp=True):
    a, b, c, dd, tx, ty = 1, 0, 0, 1, 0, 0
    m3d = elem.get("M3D", [])
    if len(m3d) >= 16:
        a, b = m3d[0], m3d[1]
        c, dd = m3d[4], m3d[5]
        tx, ty = m3d[12], m3d[13]
    mx = elem.get("MX", [])
    if len(mx) >= 6:
        a, b, c, dd, tx, ty = mx[0], mx[1], mx[2], mx[3], mx[4], mx[5]
    if apply_trp:
        trp = elem.get("TRP", {})
        trpx = trp.get("x", 0)
        trpy = trp.get("y", 0)
        if trpx or trpy:
            tx -= a * trpx + c * trpy
            ty -= b * trpx + dd * trpy
    return a, b, c, dd, tx, ty

def compose(pa, pb, pc, pd, ptx, pty, ca, cb, cc, cd, ctx, cty):
    wa = pa*ca + pc*cb
    wb = pb*ca + pd*cb
    wc = pa*cc + pc*cd
    wd = pb*cc + pd*cd
    wtx = pa*ctx + pc*cty + ptx
    wty = pb*ctx + pd*cty + pty
    return wa, wb, wc, wd, wtx, wty

def collect_parts(sym, all_syms, parent_mx, tick, depth=0, visited=None):
    """Collect all atlas sprites at a given tick with accumulated transforms."""
    if visited is None:
        visited = set()
    sn = sym.get("SN", "")
    if sn in visited:
        return []
    visited.add(sn)
    
    parts = []
    layers = sym.get("TL", {}).get("L", [])
    
    # Reverse layer order (back to front)
    for li in range(len(layers)-1, -1, -1):
        layer = layers[li]
        for fr in layer.get("FR", []):
            idx = fr.get("I", 0)
            dur = fr.get("DU", 1)
            if idx <= tick < idx + dur:
                for elem in fr.get("E", []):
                    asi = elem.get("ASI")
                    if asi:
                        name = asi.get("N", "")
                        mx = read_transform(asi, apply_trp=True)
                        world = compose(*parent_mx, *mx)
                        sp = sprites.get(name, {})
                        w = sp.get("w", 0)
                        h = sp.get("h", 0)
                        rot = sp.get("rotated", False)
                        parts.append((name, world, w, h, rot, depth))
                    
                    si = elem.get("SI")
                    if si:
                        nested_name = si.get("SN", "")
                        if nested_name in all_syms:
                            mx = read_transform(si, apply_trp=True)
                            child_mx = compose(*parent_mx, *mx)
                            nested_sym = all_syms[nested_name]
                            # Get nested duration
                            nested_dur = 0
                            for nl in nested_sym.get("TL", {}).get("L", []):
                                for nf in nl.get("FR", []):
                                    nested_dur = max(nested_dur, nf.get("I", 0) + nf.get("DU", 1))
                            ff = si.get("FF", 0)
                            nested_tick = (ff + (tick - idx)) % nested_dur if nested_dur > 0 else 0
                            sub = collect_parts(nested_sym, all_syms, child_mx, nested_tick, depth+1, visited.copy())
                            parts.extend(sub)
    
    return parts

# Trace Idle tick 0 (main timeline tick 18)
print("\n=== IDLE tick 0 (main timeline tick 18) ===")
main_an = d["AN"]
identity = (1, 0, 0, 1, 0, 0)
parts = collect_parts(main_an, syms_by_name, identity, 18)

print(f"\nTotal parts: {len(parts)}")
min_x, min_y = float('inf'), float('inf')
max_x, max_y = float('-inf'), float('-inf')

for name, (a, b, c, dd, tx, ty), w, h, rot, depth in parts:
    indent = "  " * depth
    uw = h if rot else w
    uh = w if rot else h
    # Compute bounding box corners
    x0, y0 = tx, ty
    x1, y1 = a*uw+tx, b*uw+ty
    x2, y2 = c*uh+tx, dd*uh+ty
    x3, y3 = a*uw+c*uh+tx, b*uw+dd*uh+ty
    bmin_x = min(x0,x1,x2,x3)
    bmin_y = min(y0,y1,y2,y3)
    bmax_x = max(x0,x1,x2,x3)
    bmax_y = max(y0,y1,y2,y3)
    min_x = min(min_x, bmin_x)
    min_y = min(min_y, bmin_y)
    max_x = max(max_x, bmax_x)
    max_y = max(max_y, bmax_y)
    print(f"{indent}'{name}' ({w}x{h} rot={rot}) a={a:.3f} b={b:.3f} c={c:.3f} d={dd:.3f} tx={tx:.1f} ty={ty:.1f} -> bbox ({bmin_x:.0f},{bmin_y:.0f})-({bmax_x:.0f},{bmax_y:.0f})")

print(f"\nGlobal bbox: ({min_x:.0f},{min_y:.0f}) - ({max_x:.0f},{max_y:.0f})")
print(f"Size: {max_x-min_x:.0f} x {max_y-min_y:.0f}")
print(f"Origin (for RT): ({-min_x:.0f}, {-min_y:.0f})")
print(f"drawPos = Position - origin = Position - ({-min_x:.0f}, {-min_y:.0f})")
print(f"         = Position + ({min_x:.0f}, {min_y:.0f})")

# Also check Enter tick 0
print("\n\n=== ENTER tick 0 (main timeline tick 0) ===")
parts_enter = collect_parts(main_an, syms_by_name, identity, 0)
print(f"Total parts: {len(parts_enter)}")
for name, (a, b, c, dd, tx, ty), w, h, rot, depth in parts_enter:
    uw = h if rot else w
    uh = w if rot else h
    x0, y0 = tx, ty
    x1, y1 = a*uw+tx, b*uw+ty
    x2, y2 = c*uh+tx, dd*uh+ty
    x3, y3 = a*uw+c*uh+tx, b*uw+dd*uh+ty
    bmin_x = min(x0,x1,x2,x3)
    bmin_y = min(y0,y1,y2,y3)
    bmax_x = max(x0,x1,x2,x3)
    bmax_y = max(y0,y1,y2,y3)
    indent = "  " * depth
    print(f"{indent}'{name}' ({w}x{h}) a={a:.3f} b={b:.3f} c={c:.3f} d={dd:.3f} tx={tx:.1f} ty={ty:.1f} -> bbox ({bmin_x:.0f},{bmin_y:.0f})-({bmax_x:.0f},{bmax_y:.0f})")
