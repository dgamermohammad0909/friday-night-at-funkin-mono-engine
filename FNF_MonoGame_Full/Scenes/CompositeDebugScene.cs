using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Composite Animation Debug Editor — shows individual body parts with their
/// affine transforms so you can visually identify broken positioning.
/// Each part is drawn with a colored outline and can be toggled on/off.
/// Uses the same full-Matrix approach as PreRenderComposite for accurate rendering.
/// </summary>
public class CompositeDebugScene : Scene
{
    // ── Layout constants ──────────────────────────────────────────
    const int LW = 240;   // left panel width
    const int RW = 280;   // right panel width (wider for transform values)
    const int TH = 34;    // toolbar height
    const int TLH = 38;   // timeline height
    const int SH = 22;    // status bar height

    // ── Sheet discovery ───────────────────────────────────────────
    private readonly List<string> _sheetPaths = new();
    private int _sheetIndex;
    private SpriteSheet _currentSheet;
    private string _currentPath = "";

    // ── Animation list ────────────────────────────────────────────
    private readonly List<string> _animNames = new();
    private int _animIndex;
    private int _listScroll;

    // ── Part data for current frame ───────────────────────────────
    private List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> _currentParts = new();
    private readonly HashSet<int> _hiddenParts = new();
    private int _selectedPart = -1;
    private int _partListScroll;

    // ── Playback ──────────────────────────────────────────────────
    private bool _playing;
    private int _frameIndex;
    private float _frameTimer;
    private float _frameRate = 24f;

    // ── Viewport ──────────────────────────────────────────────────
    private Vector2 _camOffset;
    private float _zoom = 1f;
    private bool _dragging;

    // ── Display modes ─────────────────────────────────────────────
    private bool _showOutlines = true;
    private bool _showLabels = true;
    private bool _showOrigin = true;
    private bool _useMatrixDraw = true; // true = full Matrix (PreRenderComposite), false = decomposed

    // ── Editing state ─────────────────────────────────────────────
    private bool _isDraggingPart;
    private bool _applyAllFrames; // when true, offset edits apply to matching part across all frames
    private readonly List<(string Anim, int Frame, int Part, float OldTX, float OldTY)> _undoStack = new();
    private int _totalEdits;

    // ── Part colors (cycle for visual distinction) ────────────────
    private static readonly Color[] _partColors =
    {
        new(255, 80, 80),    // red
        new(80, 200, 80),    // green
        new(80, 120, 255),   // blue
        new(255, 200, 50),   // yellow
        new(200, 80, 255),   // purple
        new(80, 220, 220),   // cyan
        new(255, 140, 50),   // orange
        new(255, 80, 200),   // pink
        new(120, 255, 120),  // lime
        new(180, 180, 255),  // lavender
    };

    // ── Blend state for Matrix-based rendering (same as PreRenderComposite) ──
    private static readonly BlendState _compositeBlend = new()
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    public override void Load()
    {
        DiscoverSheets();

        // Auto-select BF character select player sheet if available
        int bfIdx = _sheetPaths.FindIndex(p =>
            p.Contains("character_select", StringComparison.OrdinalIgnoreCase) &&
            p.Contains("bf", StringComparison.OrdinalIgnoreCase) &&
            p.Contains("player", StringComparison.OrdinalIgnoreCase));
        if (bfIdx >= 0)
            LoadSheet(bfIdx);
        else if (_sheetPaths.Count > 0)
            LoadSheet(0);
    }

    public override void Unload() => _currentSheet?.Dispose();

    // ── Sheet discovery (only AnimateAtlas folders with spritemap + Animation.json) ──
    private void DiscoverSheets()
    {
        _sheetPaths.Clear();
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        if (!System.IO.Directory.Exists(root)) return;

        // Find AnimateAtlas folders (spritemap1.json)
        foreach (var json in System.IO.Directory.EnumerateFiles(root, "spritemap1.json", System.IO.SearchOption.AllDirectories))
        {
            string folder = System.IO.Path.GetDirectoryName(json);
            string rel = System.IO.Path.GetRelativePath(root, folder).Replace('\\', '/');
            // Only include folders that also have Animation.json (actual AnimateAtlas)
            if (System.IO.File.Exists(System.IO.Path.Combine(folder, "Animation.json")))
                _sheetPaths.Add(rel);
        }

        // Also include standard spritesheets with composites
        foreach (var png in System.IO.Directory.EnumerateFiles(root, "*.png", System.IO.SearchOption.AllDirectories))
        {
            string baseName = System.IO.Path.ChangeExtension(png, null);
            string rel = System.IO.Path.GetRelativePath(root, baseName).Replace('\\', '/');
            if (_sheetPaths.Contains(rel)) continue;
            if (System.IO.File.Exists(baseName + ".xml") || System.IO.File.Exists(baseName + ".json"))
                _sheetPaths.Add(rel);
        }
        _sheetPaths.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private void LoadSheet(int index)
    {
        _currentSheet?.Dispose();
        _currentSheet = null;
        _animNames.Clear();
        _animIndex = 0;
        _frameIndex = 0;
        _frameTimer = 0;
        _listScroll = 0;
        _selectedPart = -1;
        _hiddenParts.Clear();
        _currentParts.Clear();
        _partListScroll = 0;
        _undoStack.Clear();
        _totalEdits = 0;
        _isDraggingPart = false;

        if (index < 0 || index >= _sheetPaths.Count) return;
        _sheetIndex = index;
        _currentPath = _sheetPaths[index];

        // Load with deferComposites: true to KEEP RawCompositeData (not pre-render it away)
        _currentSheet = SpriteSheet.Load(Game, _currentPath, preRenderComposites: true, deferComposites: true);
        if (_currentSheet == null) return;

        // Collect animation names from RawCompositeData (the composite ones we want to debug)
        foreach (var key in _currentSheet.RawCompositeData.Keys)
            _animNames.Add(key);

        // Also add standard animations that aren't in RawCompositeData
        foreach (var key in _currentSheet.Animations.Keys)
        {
            if (!_animNames.Contains(key))
                _animNames.Add(key);
        }

        _animNames.Sort(StringComparer.OrdinalIgnoreCase);
        UpdateCurrentParts();
    }

    private void UpdateCurrentParts()
    {
        _currentParts.Clear();
        if (_currentSheet == null || _animNames.Count == 0) return;

        string animName = _animNames[_animIndex];
        if (_currentSheet.RawCompositeData.TryGetValue(animName, out var ticks))
        {
            int fi = ticks.Count > 0 ? _frameIndex % ticks.Count : 0;
            if (fi < ticks.Count)
                _currentParts = new List<(SpriteFrame, float, float, float, float, float, float)>(ticks[fi]);
        }
    }

    private int GetFrameCount()
    {
        if (_currentSheet == null || _animNames.Count == 0) return 0;
        string animName = _animNames[_animIndex];
        if (_currentSheet.RawCompositeData.TryGetValue(animName, out var ticks))
            return ticks.Count;
        if (_currentSheet.Animations.TryGetValue(animName, out var frames))
            return frames.Count;
        return 0;
    }

    // ── Update ────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        // Sheet navigation (Ctrl+Left/Right)
        if (Input.IsPressed(Keys.Right) && Input.IsHeld(Keys.LeftControl) && _sheetPaths.Count > 0)
            LoadSheet((_sheetIndex + 1) % _sheetPaths.Count);
        if (Input.IsPressed(Keys.Left) && Input.IsHeld(Keys.LeftControl) && _sheetPaths.Count > 0)
            LoadSheet((_sheetIndex - 1 + _sheetPaths.Count) % _sheetPaths.Count);

        // Animation navigation (Up/Down without Ctrl, only when no part selected)
        if (_selectedPart < 0 && Input.UpPressed && !Input.IsHeld(Keys.LeftControl) && _animNames.Count > 0)
        {
            _animIndex = (_animIndex - 1 + _animNames.Count) % _animNames.Count;
            _frameIndex = 0; _frameTimer = 0; _selectedPart = -1;
            UpdateCurrentParts();
        }
        if (_selectedPart < 0 && Input.DownPressed && !Input.IsHeld(Keys.LeftControl) && _animNames.Count > 0)
        {
            _animIndex = (_animIndex + 1) % _animNames.Count;
            _frameIndex = 0; _frameTimer = 0; _selectedPart = -1;
            UpdateCurrentParts();
        }

        // Play/pause
        if (Input.IsPressed(Keys.Space)) _playing = !_playing;

        // Frame step
        if (Input.IsPressed(Keys.OemComma))
        {
            _playing = false;
            _frameIndex = Math.Max(0, _frameIndex - 1);
            UpdateCurrentParts();
        }
        if (Input.IsPressed(Keys.OemPeriod))
        {
            _playing = false;
            int fc = GetFrameCount();
            if (fc > 0) _frameIndex = Math.Min(_frameIndex + 1, fc - 1);
            UpdateCurrentParts();
        }

        // Toggle display modes
        if (Input.IsPressed(Keys.O)) _showOutlines = !_showOutlines;
        if (Input.IsPressed(Keys.L)) _showLabels = !_showLabels;
        if (Input.IsPressed(Keys.G)) _showOrigin = !_showOrigin;
        if (Input.IsPressed(Keys.M)) _useMatrixDraw = !_useMatrixDraw;

        // Toggle selected part visibility
        if (Input.IsPressed(Keys.H) && _selectedPart >= 0)
        {
            if (_hiddenParts.Contains(_selectedPart))
                _hiddenParts.Remove(_selectedPart);
            else
                _hiddenParts.Add(_selectedPart);
        }

        // Show all / hide all
        if (Input.IsPressed(Keys.V))
        {
            if (_hiddenParts.Count > 0) _hiddenParts.Clear();
            else for (int i = 0; i < _currentParts.Count; i++) _hiddenParts.Add(i);
        }

        // Solo mode: show only selected part (Ctrl+S)
        if (Input.IsPressed(Keys.S) && Input.IsHeld(Keys.LeftControl) && _selectedPart >= 0)
        {
            _hiddenParts.Clear();
            for (int i = 0; i < _currentParts.Count; i++)
                if (i != _selectedPart) _hiddenParts.Add(i);
        }

        // Part selection (Tab cycles, Escape deselects)
        if (Input.IsPressed(Keys.Tab))
        {
            if (_currentParts.Count > 0)
                _selectedPart = (_selectedPart + 1) % _currentParts.Count;
        }
        if (Input.IsPressed(Keys.Escape))
            _selectedPart = -1;

        // Undo (Ctrl+Z)
        if (Input.IsPressed(Keys.Z) && Input.IsHeld(Keys.LeftControl))
            Undo();

        // Export (Ctrl+E)
        if (Input.IsPressed(Keys.E) && Input.IsHeld(Keys.LeftControl))
            ExportOffsets();

        // Toggle apply-all-frames (Ctrl+A)
        if (Input.IsPressed(Keys.A) && Input.IsHeld(Keys.LeftControl))
        {
            _applyAllFrames = !_applyAllFrames;
            EditorUI.ShowToast(_applyAllFrames ? "Apply-All ON: edits apply to all frames" : "Apply-All OFF: edits apply to current frame only");
        }

        // Arrow key nudge selected part (1px, Shift=10px)
        if (_selectedPart >= 0 && !Input.IsHeld(Keys.LeftControl))
        {
            float nudge = Input.IsHeld(Keys.LeftShift) ? 10f : 1f;
            if (Input.IsPressed(Keys.Left) && !Input.IsHeld(Keys.LeftControl))
                ModifyPartOffset(_selectedPart, -nudge, 0);
            if (Input.IsPressed(Keys.Right) && !Input.IsHeld(Keys.LeftControl))
                ModifyPartOffset(_selectedPart, nudge, 0);
            if (Input.IsPressed(Keys.Up) && !Input.IsHeld(Keys.LeftControl))
                ModifyPartOffset(_selectedPart, 0, -nudge);
            if (Input.IsPressed(Keys.Down) && !Input.IsHeld(Keys.LeftControl))
                ModifyPartOffset(_selectedPart, 0, nudge);
        }

        // Zoom
        if (Input.IsPressed(Keys.OemPlus) || Input.IsPressed(Keys.Add)) _zoom = Math.Min(8f, _zoom * 1.25f);
        if (Input.IsPressed(Keys.OemMinus) || Input.IsPressed(Keys.Subtract)) _zoom = Math.Max(0.1f, _zoom / 1.25f);
        var vpRect = ViewportRect();
        if (EditorUI.IsHovered(vpRect))
        {
            int scroll = Input.ScrollDelta;
            if (scroll > 0) _zoom = Math.Min(8f, _zoom * 1.1f);
            if (scroll < 0) _zoom = Math.Max(0.1f, _zoom / 1.1f);

            // Left-drag to move selected part
            if (_selectedPart >= 0 && EditorUI.MouseDown && !EditorUI.MouseClicked && _isDraggingPart)
            {
                var delta = EditorUI.MouseDelta;
                if (delta != Vector2.Zero)
                    ModifyPartOffset(_selectedPart, delta.X / _zoom, delta.Y / _zoom);
            }

            // Start drag on left click when over a part
            if (EditorUI.MouseClicked)
            {
                SelectPartAtMouse(vpRect);
                _isDraggingPart = _selectedPart >= 0;
            }

            // Right-drag to pan camera
            if (EditorUI.Mouse.RightButton == ButtonState.Pressed)
            {
                _camOffset += EditorUI.MouseDelta;
                _dragging = true;
            }
            else _dragging = false;
        }

        // Stop drag when mouse released
        if (EditorUI.MouseReleased)
            _isDraggingPart = false;

        // WASD pan (only when no part selected or Ctrl not held for pan override)
        float panSpeed = 300f / _zoom * dt;
        if (Input.IsHeld(Keys.A) && !Input.IsHeld(Keys.LeftControl)) _camOffset.X += panSpeed;
        if (Input.IsHeld(Keys.D) && !Input.IsHeld(Keys.LeftControl)) _camOffset.X -= panSpeed;
        if (Input.IsHeld(Keys.W)) _camOffset.Y += panSpeed;
        if (Input.IsHeld(Keys.S) && !Input.IsHeld(Keys.LeftControl)) _camOffset.Y -= panSpeed;

        // Reset view
        if (Input.IsPressed(Keys.R) && !Input.IsHeld(Keys.LeftControl)) { _camOffset = Vector2.Zero; _zoom = 1f; }

        // Playback
        if (_playing)
        {
            _frameTimer += dt;
            float dur = 1f / _frameRate;
            while (_frameTimer >= dur)
            {
                _frameTimer -= dur;
                int fc = GetFrameCount();
                if (fc > 0)
                {
                    _frameIndex = (_frameIndex + 1) % fc;
                    UpdateCurrentParts();
                }
            }
        }

        if (Input.BackPressed) Game.Scenes.ChangeScene(new EditorHubScene());
    }

    private void SelectPartAtMouse(Rectangle vpRect)
    {
        if (_currentSheet?.Texture == null || _currentParts.Count == 0) return;
        float cx = vpRect.X + vpRect.Width / 2f + _camOffset.X;
        float cy = vpRect.Y + vpRect.Height / 2f + _camOffset.Y;
        float mx = EditorUI.Mouse.X;
        float my = EditorUI.Mouse.Y;

        // Check parts in reverse draw order (topmost first)
        for (int i = _currentParts.Count - 1; i >= 0; i--)
        {
            if (_hiddenParts.Contains(i)) continue;
            var (frame, a, b, c, d, tx, ty) = _currentParts[i];
            float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;

            // Transformed bounding box corners
            float x0 = cx + tx * _zoom;
            float y0 = cy + ty * _zoom;
            float x1 = cx + (a * w + tx) * _zoom;
            float y1 = cy + (b * w + ty) * _zoom;
            float x2 = cx + (c * h + tx) * _zoom;
            float y2 = cy + (d * h + ty) * _zoom;
            float x3 = cx + (a * w + c * h + tx) * _zoom;
            float y3 = cy + (b * w + d * h + ty) * _zoom;

            float minX = MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3));
            float maxX = MathF.Max(MathF.Max(x0, x1), MathF.Max(x2, x3));
            float minY = MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3));
            float maxY = MathF.Max(MathF.Max(y0, y1), MathF.Max(y2, y3));

            if (mx >= minX && mx <= maxX && my >= minY && my <= maxY)
            {
                _selectedPart = i;
                return;
            }
        }
        _selectedPart = -1;
    }

    private Rectangle ViewportRect() => new(LW, TH, FNFGame.SCREEN_WIDTH - LW - RW, FNFGame.SCREEN_HEIGHT - TH - TLH - SH);

    // ── Editing helpers ────────────────────────────────────────────

    /// <summary>
    /// Modify tx/ty of the given part in _currentParts AND write back to RawCompositeData.
    /// Pushes undo state before modification.
    /// If _applyAllFrames is on, applies the same delta to matching parts (same Frame.Name) across all ticks.
    /// </summary>
    private void ModifyPartOffset(int partIndex, float dx, float dy)
    {
        if (_currentSheet == null || _animNames.Count == 0) return;
        if (partIndex < 0 || partIndex >= _currentParts.Count) return;

        string animName = _animNames[_animIndex];
        if (!_currentSheet.RawCompositeData.TryGetValue(animName, out var ticks)) return;

        int fi = ticks.Count > 0 ? _frameIndex % ticks.Count : -1;
        if (fi < 0 || fi >= ticks.Count) return;
        if (partIndex >= ticks[fi].Count) return;

        var (frame, a, b, c, d, oldTX, oldTY) = ticks[fi][partIndex];

        // Push undo for current frame
        _undoStack.Add((animName, fi, partIndex, oldTX, oldTY));

        float newTX = oldTX + dx;
        float newTY = oldTY + dy;

        // Write back to RawCompositeData
        ticks[fi][partIndex] = (frame, a, b, c, d, newTX, newTY);

        // Update local copy
        _currentParts[partIndex] = (frame, a, b, c, d, newTX, newTY);

        // Apply to all frames if toggled
        if (_applyAllFrames && frame.Name != null)
        {
            for (int t = 0; t < ticks.Count; t++)
            {
                if (t == fi) continue;
                for (int p = 0; p < ticks[t].Count; p++)
                {
                    var (pf, pa, pb, pc, pd, ptx, pty) = ticks[t][p];
                    if (pf.Name == frame.Name)
                    {
                        _undoStack.Add((animName, t, p, ptx, pty));
                        ticks[t][p] = (pf, pa, pb, pc, pd, ptx + dx, pty + dy);
                    }
                }
            }
        }

        _totalEdits++;
    }

    /// <summary>
    /// Undo the last edit operation by restoring old tx/ty values.
    /// </summary>
    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            EditorUI.ShowToast("Nothing to undo");
            return;
        }

        if (_currentSheet == null) return;

        // Pop all entries from the last edit (they share the same _totalEdits batch).
        // If _applyAllFrames was on, multiple entries were pushed for one edit.
        // Pop entries until we hit a frame boundary (the first push was the direct edit).
        // Simple approach: pop entries while they share the same anim name.
        // More robust: pop all entries added in the last ModifyPartOffset call.
        // Since ModifyPartOffset pushes current-frame first, then all-frame entries,
        // we pop backwards until we find the original frame entry.
        string lastAnim = _undoStack[^1].Anim;
        int lastFrame = _undoStack[^1].Frame;
        int lastPart = _undoStack[^1].Part;

        // Pop all entries from the end that belong to the same logical edit
        // (same anim, and the first one has the direct part/frame)
        while (_undoStack.Count > 0)
        {
            var (anim, fi, pi, oldTX, oldTY) = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            if (_currentSheet.RawCompositeData.TryGetValue(anim, out var ticks))
            {
                if (fi < ticks.Count && pi < ticks[fi].Count)
                {
                    var (frame, a, b, c, d, _, _) = ticks[fi][pi];
                    ticks[fi][pi] = (frame, a, b, c, d, oldTX, oldTY);
                }
            }

            // The direct edit entry is the first one pushed (now last remaining in batch).
            // Stop after restoring the primary entry (anim matches, but fi == lastFrame is the anchor).
            // Check if next entry (if any) belongs to a different logical edit.
            if (_undoStack.Count == 0) break;
            var next = _undoStack[^1];
            // Different anim or it's the primary edit frame → this batch is done
            if (next.Anim != anim) break;
            // If the next entry is the primary (same frame+part as the first push), stop
            // Actually, the primary is pushed first. We're popping in reverse so primaries come last.
            // Continue popping all-frame entries until we hit the primary.
            if (next.Frame == fi && next.Part == pi) break; // avoid infinite loop on duplicates
        }

        _totalEdits = Math.Max(0, _totalEdits - 1);
        UpdateCurrentParts();
        EditorUI.ShowToast("Undo");
    }

    /// <summary>
    /// Export all current RawCompositeData offsets to a JSON file for external use.
    /// </summary>
    private void ExportOffsets()
    {
        if (_currentSheet == null || _totalEdits == 0)
        {
            EditorUI.ShowToast("No edits to export");
            return;
        }

        try
        {
            string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
            string outDir = System.IO.Path.Combine(root, "_exports");
            System.IO.Directory.CreateDirectory(outDir);

            string safeName = _currentPath.Replace('/', '_').Replace('\\', '_');
            string outPath = System.IO.Path.Combine(outDir, $"{safeName}_offsets.json");

            using var writer = new System.IO.StreamWriter(outPath);
            writer.WriteLine("{");
            writer.WriteLine($"  \"sheet\": \"{_currentPath}\",");
            writer.WriteLine($"  \"totalEdits\": {_totalEdits},");
            writer.WriteLine("  \"animations\": {");

            int animIdx = 0;
            foreach (var kvp in _currentSheet.RawCompositeData)
            {
                writer.WriteLine($"    \"{kvp.Key}\": [");
                for (int fi = 0; fi < kvp.Value.Count; fi++)
                {
                    writer.Write("      [");
                    var parts = kvp.Value[fi];
                    for (int pi = 0; pi < parts.Count; pi++)
                    {
                        var (frame, a, b, c, d, tx, ty) = parts[pi];
                        string name = frame.Name?.Replace("\"", "\\\"") ?? "";
                        writer.Write($"{{\"name\":\"{name}\",\"a\":{a:F4},\"b\":{b:F4},\"c\":{c:F4},\"d\":{d:F4},\"tx\":{tx:F2},\"ty\":{ty:F2}}}");
                        if (pi < parts.Count - 1) writer.Write(",");
                    }
                    writer.Write("]");
                    if (fi < kvp.Value.Count - 1) writer.WriteLine(",");
                    else writer.WriteLine();
                }
                writer.Write("    ]");
                if (animIdx < _currentSheet.RawCompositeData.Count - 1) writer.WriteLine(",");
                else writer.WriteLine();
                animIdx++;
            }

            writer.WriteLine("  }");
            writer.WriteLine("}");

            EditorUI.ShowToast($"Exported to _exports/{safeName}_offsets.json");
        }
        catch (Exception ex)
        {
            EditorUI.ShowToast($"Export failed: {ex.Message}");
        }
    }

    // ── Draw ──────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font = Assets.GetFont(13);
        var fontSm = Assets.GetFont(11);
        if (font == null) { sb.Begin(); sb.End(); return; }

        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;

        // ── Pass 1: Viewport background ───────────────────────────
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        var vp = ViewportRect();
        EditorUI.FillRect(sb, px, vp, EditorUI.BgDark);
        EditorUI.DrawCheckerboard(sb, px, vp, _camOffset, _zoom);

        float cx = vp.X + vp.Width / 2f + _camOffset.X;
        float cy = vp.Y + vp.Height / 2f + _camOffset.Y;
        if (_showOrigin)
            EditorUI.DrawCrosshair(sb, px, cx, cy, 60, EditorUI.BorderLight);
        sb.End();

        // ── Pass 2: Draw body parts ───────────────────────────────
        DrawBodyParts(sb, cx, cy, vp);

        // ── Pass 3: Draw outlines and labels overlay ──────────────
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        if (_showOutlines || _showLabels)
            DrawPartOverlays(sb, px, font, cx, cy);
        sb.End();

        // ── Pass 4: UI panels ─────────────────────────────────────
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        DrawToolbar(sb, px, font, W);
        DrawAnimList(sb, px, font, fontSm, H);
        DrawPartPanel(sb, px, font, fontSm, W, H);
        DrawTimelineBar(sb, px, fontSm, W, H);

        string state = _playing ? "PLAYING" : "PAUSED";
        string mode = _useMatrixDraw ? "MATRIX" : "DECOMP";
        string allStr = _applyAllFrames ? " ALL-FRAMES" : "";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            $"{state}  {_frameRate:0}fps  {mode}{allStr}  Edits:{_totalEdits}",
            $"Zoom: {_zoom:P0}  Parts: {_currentParts.Count}",
            $"Sheet {_sheetIndex + 1}/{_sheetPaths.Count}",
            "Arrows=nudge(+Shift=10) Drag=move Ctrl+Z=undo Ctrl+E=export Ctrl+A=all-frames");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }

    // ── Body part rendering ───────────────────────────────────────
    private void DrawBodyParts(SpriteBatch sb, float cx, float cy, Rectangle vp)
    {
        if (_currentSheet?.Texture == null || _currentParts.Count == 0) return;
        var atlas = _currentSheet.Texture;

        if (_useMatrixDraw)
        {
            // Full affine Matrix draw — one Begin/End per part (same approach as PreRenderComposite)
            for (int i = 0; i < _currentParts.Count; i++)
            {
                if (_hiddenParts.Contains(i)) continue;
                var (frame, a, b, c, d, tx, ty) = _currentParts[i];

                float adjustedTx = tx * _zoom + cx;
                float adjustedTy = ty * _zoom + cy;

                Matrix transformMatrix;
                if (frame.Rotated)
                {
                    float sw = frame.SourceRect.Width;
                    transformMatrix = new Matrix(
                        -c * _zoom, -d * _zoom, 0, 0,
                         a * _zoom,  b * _zoom, 0, 0,
                         0,  0, 1, 0,
                        c * sw * _zoom + adjustedTx, d * sw * _zoom + adjustedTy, 0, 1
                    );
                }
                else
                {
                    transformMatrix = new Matrix(
                        a * _zoom, b * _zoom, 0, 0,
                        c * _zoom, d * _zoom, 0, 0,
                        0, 0, 1, 0,
                        adjustedTx, adjustedTy, 0, 1
                    );
                }

                Color tint = Color.White;
                if (_selectedPart == i)
                    tint = new Color(255, 255, 200); // slight highlight

                // CullNone: flipped transforms (a<0) reverse triangle winding
                sb.Begin(SpriteSortMode.Deferred, _compositeBlend, rasterizerState: RasterizerState.CullNone, transformMatrix: transformMatrix);
                sb.Draw(atlas, Vector2.Zero, frame.SourceRect, tint);
                sb.End();
            }
        }
        else
        {
            // Decomposed rotation+scale draw (same as DrawTransformedPart)
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
            for (int i = 0; i < _currentParts.Count; i++)
            {
                if (_hiddenParts.Contains(i)) continue;
                var (frame, a, b, c, d, tx, ty) = _currentParts[i];
                DrawDecomposed(sb, atlas, frame, a, b, c, d, tx, ty, cx, cy, i == _selectedPart);
            }
            sb.End();
        }
    }

    private void DrawDecomposed(SpriteBatch sb, Texture2D atlas, SpriteFrame frame,
        float a, float b, float c, float d, float tx, float ty,
        float cx, float cy, bool selected)
    {
        float det = a * d - b * c;
        float rotation;
        float scaleX, scaleY;
        var effects = SpriteEffects.None;
        float drawX = tx * _zoom + cx;
        float drawY = ty * _zoom + cy;
        Vector2 drawOrigin = Vector2.Zero;

        float rotOffset = frame.Rotated ? -MathF.PI / 2f : 0f;
        if (frame.Rotated)
            drawOrigin = new Vector2(frame.SourceRect.Width, 0);

        float unrotW = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
        float unrotH = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;

        if (MathF.Abs(b) < 0.001f && MathF.Abs(c) < 0.001f)
        {
            rotation = rotOffset;
            scaleX = MathF.Abs(a) * _zoom;
            scaleY = MathF.Abs(d) * _zoom;
            if (a < 0)
            {
                if (frame.Rotated) effects |= SpriteEffects.FlipVertically;
                else effects |= SpriteEffects.FlipHorizontally;
                drawX = a * unrotW * _zoom + tx * _zoom + cx;
            }
            if (d < 0)
            {
                if (frame.Rotated) effects |= SpriteEffects.FlipHorizontally;
                else effects |= SpriteEffects.FlipVertically;
                drawY = d * unrotH * _zoom + ty * _zoom + cy;
            }
        }
        else
        {
            rotation = MathF.Atan2(b, a) + rotOffset;
            scaleX = MathF.Sqrt(a * a + b * b) * _zoom;
            scaleY = MathF.Sqrt(c * c + d * d) * _zoom;
            if (det < 0) scaleY = -scaleY;
        }

        Color tint = selected ? new Color(255, 255, 200) : Color.White;
        sb.Draw(atlas, new Vector2(drawX, drawY), frame.SourceRect, tint,
            rotation, drawOrigin, new Vector2(scaleX, scaleY), effects, 0f);
    }

    // ── Part overlay (outlines + labels) ──────────────────────────
    private void DrawPartOverlays(SpriteBatch sb, Texture2D px, SpriteFontBase font, float cx, float cy)
    {
        var fontSm = Assets.GetFont(10);
        for (int i = 0; i < _currentParts.Count; i++)
        {
            if (_hiddenParts.Contains(i)) continue;
            var (frame, a, b, c, d, tx, ty) = _currentParts[i];
            Color partColor = _partColors[i % _partColors.Length];
            if (_selectedPart == i)
                partColor = EditorUI.Gold;

            float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;

            // 4 corners of the transformed quad
            float x0 = cx + tx * _zoom;
            float y0 = cy + ty * _zoom;
            float x1 = cx + (a * w + tx) * _zoom;
            float y1 = cy + (b * w + ty) * _zoom;
            float x2 = cx + (c * h + tx) * _zoom;
            float y2 = cy + (d * h + ty) * _zoom;
            float x3 = cx + (a * w + c * h + tx) * _zoom;
            float y3 = cy + (b * w + d * h + ty) * _zoom;

            if (_showOutlines)
            {
                // Draw quad outline (4 edges)
                DrawLine(sb, px, x0, y0, x1, y1, partColor * 0.8f);
                DrawLine(sb, px, x1, y1, x3, y3, partColor * 0.8f);
                DrawLine(sb, px, x3, y3, x2, y2, partColor * 0.8f);
                DrawLine(sb, px, x2, y2, x0, y0, partColor * 0.8f);

                // Origin dot
                sb.Draw(px, new Rectangle((int)x0 - 2, (int)y0 - 2, 5, 5), partColor);
            }

            if (_showLabels && fontSm != null)
            {
                // Label at top-left corner
                string label = $"{i}: {frame.Name ?? "?"}";
                float labelX = MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3));
                float labelY = MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3)) - 12;
                fontSm.DrawText(sb, label, new Vector2(labelX, labelY), partColor);
            }
        }
    }

    /// <summary>
    /// Draw a 1px line between two points using the pixel texture.
    /// </summary>
    private static void DrawLine(SpriteBatch sb, Texture2D px, float x1, float y1, float x2, float y2, Color color)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f) return;
        float angle = MathF.Atan2(dy, dx);
        sb.Draw(px, new Vector2(x1, y1), null, color, angle, Vector2.Zero,
            new Vector2(length, 1f), SpriteEffects.None, 0f);
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar(SpriteBatch sb, Texture2D px, SpriteFontBase font, int W)
    {
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        int x = LW + 8, y = 5;

        if (EditorUI.ToolButton(sb, px, font, x, y, 28, _playing ? "||" : ">", _playing))
            _playing = !_playing;
        x += 32;
        if (EditorUI.ToolButton(sb, px, font, x, y, 24, "|<"))
        {
            _playing = false; _frameIndex = Math.Max(0, _frameIndex - 1);
            UpdateCurrentParts();
        }
        x += 28;
        if (EditorUI.ToolButton(sb, px, font, x, y, 24, ">|"))
        {
            _playing = false; int fc = GetFrameCount();
            if (fc > 0) _frameIndex = Math.Min(_frameIndex + 1, fc - 1);
            UpdateCurrentParts();
        }
        x += 36;

        // Mode toggle
        sb.Draw(px, new Rectangle(x, 8, 1, 18), EditorUI.Border);
        x += 8;
        if (EditorUI.ToolButton(sb, px, font, x, y, 60, _useMatrixDraw ? "MATRIX" : "DECOMP", true))
        {
            _useMatrixDraw = !_useMatrixDraw;
            EditorUI.ShowToast(_useMatrixDraw ? "Matrix mode (PreRenderComposite)" : "Decomposed mode (DrawTransformedPart)");
        }
        x += 64;

        // Display toggles
        sb.Draw(px, new Rectangle(x, 8, 1, 18), EditorUI.Border);
        x += 8;
        if (EditorUI.ToolButton(sb, px, font, x, y, 36, "OUT", _showOutlines))
            _showOutlines = !_showOutlines;
        x += 40;
        if (EditorUI.ToolButton(sb, px, font, x, y, 36, "LBL", _showLabels))
            _showLabels = !_showLabels;
        x += 40;
        if (EditorUI.ToolButton(sb, px, font, x, y, 36, "ORI", _showOrigin))
            _showOrigin = !_showOrigin;
        x += 44;

        // Edit controls
        sb.Draw(px, new Rectangle(x, 8, 1, 18), EditorUI.Border);
        x += 8;
        if (EditorUI.ToolButton(sb, px, font, x, y, 36, "ALL", _applyAllFrames))
        {
            _applyAllFrames = !_applyAllFrames;
            EditorUI.ShowToast(_applyAllFrames ? "Apply-All ON" : "Apply-All OFF");
        }
        x += 40;
        if (EditorUI.ToolButton(sb, px, font, x, y, 42, "UNDO"))
            Undo();
        x += 46;
        if (EditorUI.ToolButton(sb, px, font, x, y, 52, "EXPORT"))
            ExportOffsets();
        x += 56;
        if (_totalEdits > 0)
        {
            font.DrawText(sb, $"{_totalEdits} edits", new Vector2(x, y + 4), EditorUI.Warning);
            x += (int)font.MeasureString($"{_totalEdits} edits").X + 8;
        }

        // Sheet nav (right side)
        int rx = W - RW - 180;
        sb.Draw(px, new Rectangle(rx, 8, 1, 18), EditorUI.Border);
        rx += 8;
        if (EditorUI.ToolButton(sb, px, font, rx, y, 24, "<") && _sheetPaths.Count > 0)
            LoadSheet((_sheetIndex - 1 + _sheetPaths.Count) % _sheetPaths.Count);
        rx += 28;
        string sheetLabel = _sheetPaths.Count > 0 ? $"{_sheetIndex + 1}/{_sheetPaths.Count}" : "---";
        font.DrawText(sb, sheetLabel, new Vector2(rx, y + 4), EditorUI.TextPrimary);
        rx += (int)font.MeasureString(sheetLabel).X + 8;
        if (EditorUI.ToolButton(sb, px, font, rx, y, 24, ">") && _sheetPaths.Count > 0)
            LoadSheet((_sheetIndex + 1) % _sheetPaths.Count);
    }

    // ── Left panel: animation list ────────────────────────────────
    private void DrawAnimList(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int H)
    {
        var panelRect = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, panelRect, $"Animations ({_animNames.Count})", font);
        int listY = panelRect.Y + 28;
        int listH = panelRect.Height - 28;
        int rowH = 22;
        int visibleRows = listH / rowH;
        int contentH = _animNames.Count * rowH;

        if (_animIndex * rowH < _listScroll) _listScroll = _animIndex * rowH;
        if ((_animIndex + 1) * rowH > _listScroll + listH) _listScroll = (_animIndex + 1) * rowH - listH;
        _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, contentH - listH));
        _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);

        int startRow = _listScroll / rowH;
        for (int i = startRow; i < _animNames.Count && (i - startRow) < visibleRows + 1; i++)
        {
            int ry = listY + i * rowH - _listScroll;
            if (ry + rowH < listY || ry > listY + listH) continue;

            string name = _animNames[i];
            bool selected = (i == _animIndex);
            bool isRaw = _currentSheet?.RawCompositeData.ContainsKey(name) == true;
            int fc = 0;
            if (isRaw && _currentSheet.RawCompositeData.TryGetValue(name, out var ticks))
                fc = ticks.Count;
            else if (_currentSheet?.Animations.TryGetValue(name, out var frames) == true)
                fc = frames.Count;

            string badge = isRaw ? $"C:{fc}f" : $"{fc}f";
            Color badgeCol = isRaw ? EditorUI.Success : EditorUI.TextDim;

            var rowRect = new Rectangle(0, ry, LW - 10, rowH);
            if (EditorUI.ListItem(sb, px, fontSm, rowRect, name, selected, badge: badge, badgeColor: badgeCol))
            {
                _animIndex = i;
                _frameIndex = 0; _frameTimer = 0; _selectedPart = -1;
                UpdateCurrentParts();
            }
        }
    }

    // ── Right panel: parts list and properties ────────────────────
    private void DrawPartPanel(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rx = W - RW;
        var panelRect = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, panelRect, $"Parts ({_currentParts.Count}) — Frame {_frameIndex + 1}/{GetFrameCount()}", font);

        int y = panelRect.Y + 32;
        int pw = RW - 16;

        // Sheet info
        string pathDisplay = _currentPath.Length > 32 ? "..." + _currentPath[^32..] : _currentPath;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Sheet", pathDisplay); y += 16;

        string animName = _animNames.Count > 0 ? _animNames[_animIndex] : "(none)";
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Anim", animName, true); y += 16;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Mode", _useMatrixDraw ? "Full Matrix" : "Decomposed"); y += 16;

        sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 6;

        // Part list with scroll
        int partAreaTop = y;
        int partAreaH = panelRect.Bottom - y - 4;
        int partRowH = 68;
        int partContentH = _currentParts.Count * partRowH;

        if (_selectedPart >= 0)
        {
            if (_selectedPart * partRowH < _partListScroll)
                _partListScroll = _selectedPart * partRowH;
            if ((_selectedPart + 1) * partRowH > _partListScroll + partAreaH)
                _partListScroll = (_selectedPart + 1) * partRowH - partAreaH;
        }
        _partListScroll = Math.Clamp(_partListScroll, 0, Math.Max(0, partContentH - partAreaH));

        // Scrollbar for part list
        if (partContentH > partAreaH)
            _partListScroll = EditorUI.DrawScrollbar(sb, px, rx + RW - 11, partAreaTop, partAreaH, partContentH, _partListScroll, partAreaH);

        for (int i = 0; i < _currentParts.Count; i++)
        {
            int ry = partAreaTop + i * partRowH - _partListScroll;
            if (ry + partRowH < partAreaTop || ry > partAreaTop + partAreaH) continue;

            var (frame, a, b, c, d, tx, ty) = _currentParts[i];
            bool isSel = (i == _selectedPart);
            bool isHidden = _hiddenParts.Contains(i);
            Color partColor = _partColors[i % _partColors.Length];

            // Part row background
            var rowRect = new Rectangle(rx + 2, ry, pw - 4, partRowH - 2);
            if (isSel)
                EditorUI.FillRect(sb, px, rowRect, EditorUI.Selected);
            else if (EditorUI.IsHovered(rowRect))
                EditorUI.FillRect(sb, px, rowRect, EditorUI.Hover);

            // Click to select
            if (EditorUI.IsHovered(rowRect) && EditorUI.MouseClicked)
                _selectedPart = i;

            // Color indicator bar
            sb.Draw(px, new Rectangle(rx + 2, ry, 4, partRowH - 2), isHidden ? EditorUI.TextDim : partColor);

            // Part header: index + name + visibility
            string nameStr = $"[{i}] {frame.Name ?? "?"}";
            if (isHidden) nameStr += " [HIDDEN]";
            Color nameCol = isHidden ? EditorUI.TextDim : (isSel ? EditorUI.Gold : EditorUI.TextPrimary);
            fontSm.DrawText(sb, nameStr, new Vector2(rx + 10, ry + 2), nameCol);

            // Transform values (compact format)
            fontSm.DrawText(sb, $"a={a:F3}  b={b:F3}  c={c:F3}  d={d:F3}", new Vector2(rx + 10, ry + 16), EditorUI.TextSecondary);
            Color txColor = isSel ? EditorUI.Warning : EditorUI.Accent;
            fontSm.DrawText(sb, $"tx={tx:F1}  ty={ty:F1}", new Vector2(rx + 10, ry + 30), txColor);

            // Source rect
            int srcW = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            int srcH = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
            fontSm.DrawText(sb, $"src={frame.SourceRect.X},{frame.SourceRect.Y} {srcW}x{srcH}{(frame.Rotated ? " ROT" : "")}",
                new Vector2(rx + 10, ry + 44), EditorUI.TextDim);

            // Separator
            sb.Draw(px, new Rectangle(rx + 8, ry + partRowH - 3, pw - 16, 1), EditorUI.Border * 0.5f);
        }
    }

    // ── Timeline ──────────────────────────────────────────────────
    private void DrawTimelineBar(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, int W, int H)
    {
        int ty = H - SH - TLH;
        int fc = GetFrameCount();
        int fi = fc > 0 ? _frameIndex % fc : 0;
        EditorUI.DrawTimeline(sb, px, fontSm, LW, ty, W - LW - RW, fc, fi, _frameRate);
    }
}
