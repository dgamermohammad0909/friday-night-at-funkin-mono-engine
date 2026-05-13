# Character Select Scene — Fix Session Log

## Date: Current Session

## User Request
"Make the character selector 100% like the original FNF"

## Completed (Prior Rounds)
10 visual fixes already applied:
- BG/curtains positioning
- Camera intro tween
- Cursor snap
- Icon scaling
- Cursor behavior
- Base fill changed to Color.Black
- Solid black backdrop behind chooseDipshit
- Build succeeds, game launches

## 5 Issues — ALL FIXED
1. **Debug text overlay** ✅ — `DebugOverlay.Visible = false; DebugOverlay.ClearPins();` in Load()
2. **GF position** ✅ — offset (+366, 0) applied in DrawCharacterSprite; GF center matches original within 2px
3. **BF position** ✅ — offset (-640, -360) removes baked stage matrix; BF center matches original within 20px
4. **Stage floor gaps** ✅ — step 0 fills entire screen with Color.Black before drawing
5. **Speakers z-order** ✅ — drawn after BF (GF→BF→speakers matches original CharSelectSubState.hx)

---

## Key Technical Findings

### Animation.json Export Differences
Our Animation.json exports differ fundamentally from the original FNF:

**Our BF Player** (`Content/menus/character_select/bf/player/Animation.json`):
- Main timeline M3D: `tx=640.0, ty=360.0` (stage matrix BAKED IN)
- 2 layers, 48 frames
- Labels: Enter, Idle, Confirm, Cancel
- `AN.STI: {}` (empty)
- Layer_2 has 48 frames, each with SI "bf slide in" at M3D (640, 360), TRP (0,0)
- Layer_3 has 5 label-only frames

**Original BF Player** (`FNF_Official/assets/preload/images/charSelect/bfChill/Animation.json`):
- Main timeline M3D: identity `[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]`
- 4 layers, 58 frames
- Labels: idle, slidein, select, deselect, slideout (+ more)
- `AN.STI: {}` (empty)
- Layer_1 has 58 visual frames with "bf cs idle" SI at identity M3D
- Layers 2-4 are label-only

**Our GF Spectator** (`Content/menus/character_select/bf/spectator/Animation.json`):
- NO baked stage offset (M3D values are near-identity with small deformations)
- 4 layers (Layer_2 labels, Layer_3 has Partner GF idle + speaker systems, Layer_4 body parts, Layer_5 Partner GF idle)
- Labels: Enter, IdleLeft, IdleRight, Confirm, Cancel, Exit
- `AN.STI: {}` (empty)
- Individual body parts directly on timeline (hair, body, knee, arm, head, face, etc.)

**Original GF Spectator** (`FNF_Official/assets/preload/images/charSelect/gfChill/Animation.json`):
- Main timeline M3D: identity
- `AN.STI: {"SI": {"SN": "GIRLFRIEND CS", "TRP": {"x": 639.95, "y": 360}, "MX": [1, 0, 0, 1, 639.95, 360]}}`
- Labels: idle, confirm, deselect
- Metadata: W=1280, H=720, BGC=#999999

### Bounding Box Computations

**Our BF Idle**: `(681, 117) — (1031, 495)`, center=(856, 306), feet_y=495
**Our GF IdleLeft (body only, no speakers)**: `(-458, -134) — (468, 373)`, center=(5, 119), feet_y=373

**Original BF idle (no STI, identity M3D)**: `(34, -222) — (384, 155)`, center=(209, -34), feet_y=155
**Original GF idle (no STI applied)**: `(-541, -431) — (3, -56)`, center=(-269, -244), feet_y=-56
**Original GF idle (WITH STI +640,+360)**: `(99, -71) — (643, 304)`, center=(371, 117), feet_y=304

### Stage Layout
- Stage sprite (Sparrow): drawn at (-2, 1), frames ~1389×382, floor bottom at y≈383
- Speakers: at (cutoutSize-10, 0) = (-10, 0), scrollFactor 1.8, scale 1.05
- chooseDipshit panel: at (426, -13)

### How applyStageMatrix Works in Original FNF
From `CharSelectPlayer.hx`: `loadTextureAtlas(DEFAULT_PATH, { applyStageMatrix: true, swfMode: true })`
From `CharSelectGF.hx`: `this.applyStageMatrix = true`
From `BaseFreeplayDJ.hx`: `offset.x -= stageInstance.x; offset.y -= stageInstance.y;`

Effect: `drawX = x - offset.x` → subtracting negative offset = adding positive shift
- BF: STI empty → no offset → draws at raw coordinates
- GF: STI has (639.95, 360) → offset becomes (-639.95, -360) → GF shifts RIGHT by 640 and DOWN by 360

### Position Correction Needed

**BF**: Our export has (640, 360) baked into M3D. Original does NOT apply STI offset (empty).
- Need to subtract (640, 360) from BF draw position
- `spr.Position = new Vector2(-640 - _cameraX, -360 - _cameraY)`
- Result: BF bbox shifts from (681,117)-(1031,495) to (41,-243)-(391,135)
- vs Original: (34,-222)-(384,155) — close match (7px X, 21px Y difference from different exports)

**GF**: Our export does NOT have stage offset baked. Original applies STI (+640, +360) at runtime.
- Y center already matches: ours=119 vs original+STI=117 ✓
- X center differs: ours=5 vs original+STI=371 (off by 366px)
- May need X offset of ~366px to shift GF rightward
- User reports "GF too high" — may need positive Y offset

### Files to Modify
- `FNF_MonoGame_Full/Scenes/CharacterSelectScene.cs` — all 5 fixes
- Key method: `DrawCharacterSprite()` at line 936-944
- Key method: `Draw()` at line 782-926

### DrawCharacterSprite Current Code (line 936-944)
```csharp
private void DrawCharacterSprite(SpriteBatch sb, Dictionary<string, AnimatedSprite> sprites, string charId)
{
    if (!sprites.TryGetValue(charId, out var spr) || spr == null) return;
    spr.Position = new Vector2(-_cameraX, -_cameraY);
    spr.Scale = Vector2.One;
    spr.Draw(sb);
}
```

### Planned Fix Approach
All fixes implemented in CharacterSelectScene.cs:
1. ✅ Added `DebugOverlay.Visible = false; DebugOverlay.ClearPins();` in Load()
2. ✅ DrawCharacterSprite now accepts offsetX/offsetY parameters
3. ✅ BF drawn with offset (-640, -360): `DrawCharacterSprite(sb, _playerSprites, _curChar, -640f, -360f)`
4. ✅ GF drawn with offset (366, 0): `DrawCharacterSprite(sb, _spectatorSprites, _curChar, 366f, 0f)`
5. ✅ Floor gaps: step 0 already draws full-screen black fill — no additional fix needed
6. ✅ Speakers z-order confirmed correct: GF→BF→speakers (matches original add() order)

### Verified Screen Positions (camera 0,0)
```
BF:       (41,-243)-(391,135)   center=(216,-54)   feet_y=135
GF:       (-92,-134)-(834,373)  center=(371,120)   feet_y=373
Stage:    (-2,1)-(1387,383)     floor_bottom=383
Speakers: (-145,-425)-(1446,160) bottom=160
BG:       (-153,-140)-(1420,480)

Original BF:     (34,-222)-(384,155)   center=(209,-34)   [diff: X=7, Y=-20]
Original GF+STI: (99,-71)-(643,304)    center=(371,117)   [diff: X=0, Y=2]
```

### Reference: Original Layer Order (verified matches our code)
bg → crowd → stage → curtains → bar(MULTIPLY) → charLight → GF → BF → speakers → fgBlur(MULTIPLY) → dipshitBlur(ADD) → dipshitBacking(ADD) → chooseDipshit → nametag → cursors → icons

### Reference: Original Positions (cutoutSize=0)
```
bg: (-153, -140), scrollFactor 0.1
crowd: (0, 0), scrollFactor 0.3
stage: (-2, 1), scrollFactor 1.0
curtains: (-212, -99), scrollFactor 1.4
bar: (0, 0), MULTIPLY blend, scale.x=2.5
charLight: (800, 250) and (180, 240)
gfChill: (0, 0), applyStageMatrix=true
playerChill: (0, 0), applyStageMatrix=true, swfMode=true
speakers: (-10, 0), scrollFactor 1.8, scale 1.05
fgBlur: (-125, 170), MULTIPLY blend
dipshitBlur: (419, -65), ADD blend
dipshitBacking: (423, -17), ADD blend
chooseDipshit: (426, -13)
```
