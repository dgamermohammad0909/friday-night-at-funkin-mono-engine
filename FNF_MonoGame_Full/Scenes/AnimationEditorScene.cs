using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Professional Animation Editor — browse spritesheets, preview animations,
/// inspect frame data with full EditorUI toolkit integration.
/// 
/// Layout: [Left Panel: Animation List] [Center: Viewport] [Right Panel: Properties]
///         [Toolbar across top] [Timeline at bottom] [Status bar]
/// </summary>
public class AnimationEditorScene : Scene
{
    // ── Layout constants ──────────────────────────────────────────
    const int LW = 260;   // left panel width
    const int RW = 250;   // right panel width
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

    // ── Playback ──────────────────────────────────────────────────
    private bool _playing = true;
    private int _frameIndex;
    private float _frameTimer;
    private float _frameRate = 24f;
    private readonly float[] _fpsPresets = { 6, 12, 18, 24, 30 };

    // ── Viewport ──────────────────────────────────────────────────
    private Vector2 _camOffset;
    private float _zoom = 1f;
    private bool _flipH;
    private bool _dragging;

    // ── Search / filter ───────────────────────────────────────────
    private string _searchFilter = "";

    public override void Load()
    {
        DiscoverSheets();
        if (_sheetPaths.Count > 0)
            LoadSheet(0);
    }

    public override void Unload() => _currentSheet?.Dispose();

    // ── Sheet discovery ───────────────────────────────────────────
    private void DiscoverSheets()
    {
        _sheetPaths.Clear();
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        if (!System.IO.Directory.Exists(root)) return;

        foreach (var png in System.IO.Directory.EnumerateFiles(root, "*.png", System.IO.SearchOption.AllDirectories))
        {
            string baseName = System.IO.Path.ChangeExtension(png, null);
            string rel = System.IO.Path.GetRelativePath(root, baseName).Replace('\\', '/');
            if (System.IO.File.Exists(baseName + ".xml") || System.IO.File.Exists(baseName + ".json") || System.IO.File.Exists(baseName + ".txt"))
                _sheetPaths.Add(rel);
        }
        foreach (var json in System.IO.Directory.EnumerateFiles(root, "spritemap1.json", System.IO.SearchOption.AllDirectories))
        {
            string folder = System.IO.Path.GetDirectoryName(json);
            string rel = System.IO.Path.GetRelativePath(root, folder).Replace('\\', '/');
            if (!_sheetPaths.Contains(rel)) _sheetPaths.Add(rel);
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

        if (index < 0 || index >= _sheetPaths.Count) return;
        _sheetIndex = index;
        _currentPath = _sheetPaths[index];

        _currentSheet = SpriteSheet.Load(Game, _currentPath, preRenderComposites: true);
        if (_currentSheet != null)
        {
            _animNames.AddRange(_currentSheet.Animations.Keys);
            _animNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
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

        // Animation navigation (Up/Down)
        if (Input.UpPressed && !Input.IsHeld(Keys.LeftControl) && _animNames.Count > 0)
        { _animIndex = (_animIndex - 1 + _animNames.Count) % _animNames.Count; _frameIndex = 0; _frameTimer = 0; }
        if (Input.DownPressed && !Input.IsHeld(Keys.LeftControl) && _animNames.Count > 0)
        { _animIndex = (_animIndex + 1) % _animNames.Count; _frameIndex = 0; _frameTimer = 0; }

        // Play/pause
        if (Input.IsPressed(Keys.Space)) _playing = !_playing;

        // Frame step
        if (Input.IsPressed(Keys.OemComma)) { _playing = false; _frameIndex = Math.Max(0, _frameIndex - 1); }
        if (Input.IsPressed(Keys.OemPeriod))
        { _playing = false; var f = GetFrames(); if (f != null) _frameIndex = Math.Min(_frameIndex + 1, f.Count - 1); }

        // FPS presets (1-5)
        for (int i = 0; i < _fpsPresets.Length; i++)
            if (Input.IsPressed(Keys.D1 + i)) _frameRate = _fpsPresets[i];

        // Zoom (keyboard + scroll in viewport area)
        if (Input.IsPressed(Keys.OemPlus) || Input.IsPressed(Keys.Add)) _zoom = Math.Min(8f, _zoom * 1.25f);
        if (Input.IsPressed(Keys.OemMinus) || Input.IsPressed(Keys.Subtract)) _zoom = Math.Max(0.1f, _zoom / 1.25f);
        var vpRect = ViewportRect();
        if (EditorUI.IsHovered(vpRect))
        {
            int scroll = Input.ScrollDelta;
            if (scroll > 0) _zoom = Math.Min(8f, _zoom * 1.1f);
            if (scroll < 0) _zoom = Math.Max(0.1f, _zoom / 1.1f);

            // Right-drag to pan
            if (EditorUI.Mouse.RightButton == ButtonState.Pressed)
            {
                _camOffset += EditorUI.MouseDelta;
                _dragging = true;
            }
            else _dragging = false;
        }

        // WASD pan
        float panSpeed = 300f / _zoom * dt;
        if (Input.IsHeld(Keys.A)) _camOffset.X += panSpeed;
        if (Input.IsHeld(Keys.D) && !Input.IsHeld(Keys.LeftControl)) _camOffset.X -= panSpeed;
        if (Input.IsHeld(Keys.W)) _camOffset.Y += panSpeed;
        if (Input.IsHeld(Keys.S) && !Input.IsHeld(Keys.LeftControl)) _camOffset.Y -= panSpeed;

        // Reset / Flip
        if (Input.IsPressed(Keys.R)) { _camOffset = Vector2.Zero; _zoom = 1f; }
        if (Input.IsPressed(Keys.F)) _flipH = !_flipH;

        // Playback
        if (_playing)
        {
            _frameTimer += dt;
            float dur = 1f / _frameRate;
            while (_frameTimer >= dur)
            {
                _frameTimer -= dur;
                var frames = GetFrames();
                if (frames != null && frames.Count > 0)
                    _frameIndex = (_frameIndex + 1) % frames.Count;
            }
        }

        if (Input.BackPressed) Game.Scenes.ChangeScene(new EditorHubScene());
    }

    private List<SpriteFrame> GetFrames()
    {
        if (_currentSheet == null || _animNames.Count == 0) return null;
        return _currentSheet.Animations.TryGetValue(_animNames[_animIndex], out var f) ? f : null;
    }

    private Rectangle ViewportRect() => new(LW, TH, FNFGame.SCREEN_WIDTH - LW - RW, FNFGame.SCREEN_HEIGHT - TH - TLH - SH);

    // ── Draw ──────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font = Assets.GetFont(13);
        var fontSm = Assets.GetFont(11);
        if (font == null) { sb.Begin(); sb.End(); return; }

        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;

        // ── Pass 1: Viewport (NonPremultiplied for sprite alpha) ──
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        var vp = ViewportRect();
        EditorUI.FillRect(sb, px, vp, EditorUI.BgDark);
        EditorUI.DrawCheckerboard(sb, px, vp, _camOffset, _zoom);

        // Origin crosshair
        float cx = vp.X + vp.Width / 2f + _camOffset.X;
        float cy = vp.Y + vp.Height / 2f + _camOffset.Y;
        EditorUI.DrawCrosshair(sb, px, cx, cy, 40, EditorUI.BorderLight);

        // Draw sprite
        DrawSprite(sb, cx, cy);
        sb.End();

        // ── Pass 2: UI panels (AlphaBlend) ────────────────────────
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

        // Toolbar
        DrawToolbar(sb, px, font, W);

        // Left panel: animation list
        DrawAnimList(sb, px, font, fontSm, H);

        // Right panel: properties
        DrawProperties(sb, px, font, fontSm, W, H);

        // Timeline
        DrawTimelineBar(sb, px, fontSm, W, H);

        // Status bar
        string state = _playing ? "PLAYING" : "PAUSED";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            $"{state}  {_frameRate:0}fps",
            $"Zoom: {_zoom:P0}",
            $"Sheet {_sheetIndex + 1}/{_sheetPaths.Count}",
            "Esc=back  Space=play  ,/.=step  WASD=pan  Scroll=zoom  R=reset  F=flip");

        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar(SpriteBatch sb, Texture2D px, SpriteFontBase font, int W)
    {
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        int x = LW + 8, y = 5;

        if (EditorUI.ToolButton(sb, px, font, x, y, 28, _playing ? "||" : ">", _playing))
            _playing = !_playing;
        x += 32;
        if (EditorUI.ToolButton(sb, px, font, x, y, 24, "|<")) { _playing = false; _frameIndex = Math.Max(0, _frameIndex - 1); }
        x += 28;
        if (EditorUI.ToolButton(sb, px, font, x, y, 24, ">|")) { _playing = false; var f = GetFrames(); if (f != null) _frameIndex = Math.Min(_frameIndex + 1, f.Count - 1); }
        x += 36;

        // FPS presets
        sb.Draw(px, new Rectangle(x, 8, 1, 18), EditorUI.Border);
        x += 8;
        for (int i = 0; i < _fpsPresets.Length; i++)
        {
            bool active = Math.Abs(_frameRate - _fpsPresets[i]) < 0.5f;
            if (EditorUI.ToolButton(sb, px, font, x, y, 36, $"{_fpsPresets[i]:0}f", active))
                _frameRate = _fpsPresets[i];
            x += 40;
        }

        sb.Draw(px, new Rectangle(x, 8, 1, 18), EditorUI.Border);
        x += 8;
        if (EditorUI.ToolButton(sb, px, font, x, y, 40, _flipH ? "FLIP" : "flip", _flipH))
            _flipH = !_flipH;
        x += 44;
        if (EditorUI.ToolButton(sb, px, font, x, y, 50, "Reset"))
        { _camOffset = Vector2.Zero; _zoom = 1f; }

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

        // Auto-scroll to keep selection visible
        if (_animIndex * rowH < _listScroll) _listScroll = _animIndex * rowH;
        if ((_animIndex + 1) * rowH > _listScroll + listH) _listScroll = (_animIndex + 1) * rowH - listH;
        _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, contentH - listH));

        // Scrollbar
        _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);

        int startRow = _listScroll / rowH;
        for (int i = startRow; i < _animNames.Count && (i - startRow) < visibleRows + 1; i++)
        {
            int ry = listY + i * rowH - _listScroll;
            if (ry + rowH < listY || ry > listY + listH) continue;

            string name = _animNames[i];
            bool selected = (i == _animIndex);
            int fc = _currentSheet?.Animations.TryGetValue(name, out var af) == true ? af.Count : 0;
            bool isComp = _currentSheet?.CompositeAnimations.ContainsKey(name) == true;
            string badge = isComp ? "COMP" : $"{fc}f";
            Color badgeCol = isComp ? EditorUI.Success : EditorUI.TextDim;

            var rowRect = new Rectangle(0, ry, LW - 10, rowH);
            if (EditorUI.ListItem(sb, px, fontSm, rowRect, name, selected, badge: badge, badgeColor: badgeCol))
            {
                _animIndex = i;
                _frameIndex = 0;
                _frameTimer = 0;
            }
        }
    }

    // ── Right panel: properties ───────────────────────────────────
    private void DrawProperties(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rx = W - RW;
        var panelRect = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, panelRect, "Properties", font);

        int y = panelRect.Y + 32;
        int pw = RW - 12;

        // Sheet info
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Sheet", System.IO.Path.GetFileName(_currentPath)); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Path", _currentPath.Length > 28 ? "..." + _currentPath[^28..] : _currentPath); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Anims", $"{_animNames.Count}"); y += 18;
        if (_currentSheet?.Texture != null)
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Texture", $"{_currentSheet.Texture.Width}x{_currentSheet.Texture.Height}");
        y += 18;

        sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;

        // Current animation
        string animName = _animNames.Count > 0 ? _animNames[_animIndex] : "(none)";
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Animation", animName, true); y += 18;

        var frames = GetFrames();
        int fc = frames?.Count ?? 0;
        int fi = fc > 0 ? _frameIndex % fc : 0;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Frame", $"{fi + 1} / {fc}"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "FPS", $"{_frameRate:0}"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "State", _playing ? "Playing" : "Paused"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Flip H", _flipH ? "Yes" : "No"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Zoom", $"{_zoom:P0}"); y += 18;

        bool isComp = _currentSheet?.CompositeAnimations.ContainsKey(animName) == true;
        if (isComp)
        {
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Type", "Composite");
            y += 18;
        }

        sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;

        // Frame details
        if (frames != null && fc > 0)
        {
            var f = frames[fi];
            fontSm.DrawText(sb, "Frame Details", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", f.Name ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Source", $"{f.SourceRect.X},{f.SourceRect.Y} {f.SourceRect.Width}x{f.SourceRect.Height}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Offset", $"{f.Offset.X:F1}, {f.Offset.Y:F1}"); y += 18;
            int fw = f.FrameWidth > 0 ? f.FrameWidth : (f.Rotated ? f.SourceRect.Height : f.SourceRect.Width);
            int fh = f.FrameHeight > 0 ? f.FrameHeight : (f.Rotated ? f.SourceRect.Width : f.SourceRect.Height);
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Frame Size", $"{fw}x{fh}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Rotated", f.Rotated ? "Yes" : "No"); y += 18;
        }
    }

    // ── Timeline bar ──────────────────────────────────────────────
    private void DrawTimelineBar(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, int W, int H)
    {
        int ty = H - SH - TLH;
        var frames = GetFrames();
        int fc = frames?.Count ?? 0;
        int fi = fc > 0 ? _frameIndex % fc : 0;

        EditorUI.DrawTimeline(sb, px, fontSm, LW, ty, W - LW - RW, fc, fi, _frameRate);
    }

    // ── Sprite rendering ──────────────────────────────────────────
    private void DrawSprite(SpriteBatch sb, float cx, float cy)
    {
        if (_currentSheet == null || _animNames.Count == 0) return;
        string animName = _animNames[_animIndex];

        // Composite animation
        if (_currentSheet.CompositeAnimations.TryGetValue(animName, out var compFrames) && compFrames.Count > 0)
        {
            int idx = _frameIndex % compFrames.Count;
            var (tex, rect, origin) = compFrames[idx];
            sb.Draw(tex, new Vector2(cx, cy), rect, Color.White, 0f, origin,
                _zoom, _flipH ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
            return;
        }

        // Standard sparrow frames
        var frames = GetFrames();
        if (frames == null || frames.Count == 0 || _currentSheet.Texture == null) return;

        int fi = _frameIndex % frames.Count;
        var frame = frames[fi];
        float drawX = cx + frame.Offset.X * _zoom;
        float drawY = cy + frame.Offset.Y * _zoom;

        if (frame.Rotated)
        {
            var rotOrigin = new Vector2(frame.SourceRect.Width, 0);
            sb.Draw(_currentSheet.Texture, new Vector2(drawX, drawY), frame.SourceRect, Color.White,
                -MathF.PI / 2f, rotOrigin, _zoom,
                _flipH ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);
        }
        else
        {
            sb.Draw(_currentSheet.Texture, new Vector2(drawX, drawY), frame.SourceRect, Color.White,
                0f, Vector2.Zero, _zoom,
                _flipH ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }
    }
}
