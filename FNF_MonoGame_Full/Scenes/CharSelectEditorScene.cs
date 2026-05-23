using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

public class CharSelectEditorScene : Scene
{
    private struct SceneElement
    {
        public string Name;
        public float X, Y;
        public float ScrollFactor;
        public float ScaleX, ScaleY;
        public Color LabelColor;
    }

    private const int EL_BG = 0, EL_CROWD = 1, EL_STAGE = 2, EL_CURTAINS = 3;
    private const int EL_BAR = 4, EL_CHARLIGHT1 = 5, EL_CHARLIGHT2 = 6;
    private const int EL_GF = 7, EL_BF = 8, EL_SPKSTACK = 9, EL_SPKMONITOR = 10, EL_FGBLUR = 11;

    private SceneElement[] _elements;
    private int _selected;
    private bool _showOverlay = true;
    private KeyboardState _prevKb;

    // Stage assets
    private Texture2D _bgTex, _curtainsTex, _charLightTex, _fgBlurTex;
    private SpriteSheet _crowdSheet, _stageSheet, _barSheet;
    private AnimatedSprite _crowdSprite, _stageSprite, _barSprite;

    // Speaker raw sprites (split from composite for independent positioning)
    private Texture2D _speakerMapTex;
    private Rectangle _spkStackSrc = new(0, 0, 188, 335);
    private Rectangle _spkMonitorSrc = new(0, 339, 229, 117);

    // Characters (bf + pico, switchable with Tab)
    private readonly string[] _charIds = { "bf", "pico" };
    private int _charIndex;
    private readonly Dictionary<string, SpriteSheet> _playerSheets = new();
    private readonly Dictionary<string, AnimatedSprite> _playerSprites = new();
    private readonly Dictionary<string, SpriteSheet> _spectatorSheets = new();
    private readonly Dictionary<string, AnimatedSprite> _spectatorSprites = new();
    private bool _gfDanceLeft = true;

    private static readonly BlendState _multiplyBlend = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    private bool _loading = true;
    private int _loadPhase;
    private string _loadStatus = "Loading...";
    private int _debugOnce;

    public override void Load()
    {
        _elements = new SceneElement[]
        {
            new() { Name = "BG",         X = -153, Y = -140, ScrollFactor = 0.1f, ScaleX = 1, ScaleY = 1, LabelColor = Color.Gray },
            new() { Name = "Crowd",      X = 0,    Y = 0,    ScrollFactor = 0.3f, ScaleX = 1, ScaleY = 1, LabelColor = Color.Cyan },
            new() { Name = "Stage",      X = -2,   Y = 1,    ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.Yellow },
            new() { Name = "Curtains",   X = -212, Y = -99,  ScrollFactor = 1.4f, ScaleX = 1, ScaleY = 1, LabelColor = Color.Orange },
            new() { Name = "Bar",        X = 0,    Y = 0,    ScrollFactor = 0.0f, ScaleX = 2.5f, ScaleY = 1, LabelColor = Color.Red },
            new() { Name = "CharLight1", X = 800,  Y = 250,  ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.LightYellow },
            new() { Name = "CharLight2", X = 180,  Y = 240,  ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.LightYellow },
            new() { Name = "GF",         X = 0,    Y = 0,    ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.HotPink },
            new() { Name = "BF",         X = 0,    Y = 0,    ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.DeepSkyBlue },
            new() { Name = "SpkStack",   X = -10,  Y = 0,    ScrollFactor = 1.8f, ScaleX = 1.05f, ScaleY = 1.05f, LabelColor = Color.Lime },
            new() { Name = "SpkMonitor", X = -10,  Y = 0,    ScrollFactor = 1.8f, ScaleX = 1.05f, ScaleY = 1.05f, LabelColor = Color.LimeGreen },
            new() { Name = "FgBlur",     X = -125, Y = 170,  ScrollFactor = 1.0f, ScaleX = 1, ScaleY = 1, LabelColor = Color.MediumPurple },
        };
        _loading = true;
        _loadPhase = 0;
    }

    public override void Unload()
    {
        _crowdSheet?.Dispose();
        _stageSheet?.Dispose();
        _barSheet?.Dispose();
        foreach (var s in _playerSheets.Values) s?.Dispose();
        foreach (var s in _spectatorSheets.Values) s?.Dispose();
    }

    private void ProcessLoadPhase()
    {
        string b = "menus/character_select";
        switch (_loadPhase)
        {
            case 0:
                _loadStatus = "Loading textures...";
                _bgTex = LoadTex(b + "/stage/bg.png", premultiply: true);
                _curtainsTex = LoadTex(b + "/stage/curtains.png", premultiply: true);
                _charLightTex = LoadTex(b + "/stage/character_light.png", premultiply: true);
                _fgBlurTex = LoadTex(b + "/stage/fg_blur.png", premultiply: true);
                _speakerMapTex = LoadTex(b + "/stage/speakers/spritemap1.png", premultiply: true);
                break;
            case 1:
                _loadStatus = "Loading crowd/stage...";
                _crowdSheet = SpriteSheet.Load(Game, b + "/stage/crowd", preRenderComposites: true, deferComposites: true);
                if (_crowdSheet != null)
                {
                    _crowdSprite = new AnimatedSprite { Sheet = _crowdSheet };
                    var a = FindAnim(_crowdSheet, "default", "crowd");
                    if (a != null) _crowdSprite.PlayAnimation(a, true, true);
                }
                _stageSheet = SpriteSheet.Load(Game, b + "/stage/stage");
                if (_stageSheet != null)
                {
                    if (_stageSheet.Texture != null) SpriteSheet.PremultiplyAlpha(_stageSheet.Texture);
                    _stageSprite = new AnimatedSprite { Sheet = _stageSheet };
                    var a = FindAnim(_stageSheet, "default", "stage");
                    if (a != null) _stageSprite.PlayAnimation(a, true, true);
                }
                break;
            case 2:
                _loadStatus = "Loading bar...";
                _barSheet = SpriteSheet.Load(Game, b + "/stage/bar", preRenderComposites: true, deferComposites: true);
                if (_barSheet != null)
                {
                    if (_barSheet.Texture != null) SpriteSheet.PremultiplyAlpha(_barSheet.Texture);
                    _barSprite = new AnimatedSprite { Sheet = _barSheet };
                    var a = FindAnim(_barSheet, "default", "bar");
                    if (a != null) _barSprite.PlayAnimation(a, true, true);
                }
                break;
            case 3:
                _loadStatus = "Loading characters...";
                foreach (var cn in _charIds)
                {
                    var ps = SpriteSheet.Load(Game, b + "/" + cn + "/player", preRenderComposites: true,
                        preRenderFilter: new[] { "Enter", "Idle", "Confirm", "Cancel", "Exit" }, deferComposites: true);
                    if (ps != null)
                    {
                        _playerSheets[cn] = ps;
                        var spr = new AnimatedSprite { Sheet = ps };
                        var a = FindAnim(ps, "Idle", "cs idle", "idle");
                        if (a != null) spr.PlayAnimation(a, true, true);
                        _playerSprites[cn] = spr;
                    }
                    var ss = SpriteSheet.Load(Game, b + "/" + cn + "/spectator", preRenderComposites: true,
                        preRenderFilter: new[] { "Enter", "IdleLeft", "IdleRight", "Confirm", "Cancel", "Exit" }, deferComposites: true);
                    if (ss != null)
                    {
                        _spectatorSheets[cn] = ss;
                        var spr = new AnimatedSprite { Sheet = ss };
                        var a = FindAnim(ss, "IdleLeft", "Idle", "idle");
                        if (a != null) spr.PlayAnimation(a, true, true);
                        _spectatorSprites[cn] = spr;
                    }
                }
                break;
            case 4:
                _loadStatus = "Rendering composites...";
                _crowdSheet?.RenderRawComposites(Game.GraphicsDevice);
                _barSheet?.RenderRawComposites(Game.GraphicsDevice);
                foreach (var s in _playerSheets.Values) s?.RenderRawComposites(Game.GraphicsDevice);
                foreach (var s in _spectatorSheets.Values) s?.RenderRawComposites(Game.GraphicsDevice);
                ReplayDeferred();
                _loading = false;
                break;
        }
        _loadPhase++;
    }

    private Texture2D LoadTex(string path, bool premultiply = false)
    {
        var t = Assets.LoadTexture(path);
        if (t == Assets.Pixel) return null;
        if (t != null && premultiply) SpriteSheet.PremultiplyAlpha(t);
        return t;
    }

    private void ReplayDeferred()
    {
        if (_crowdSprite != null && _crowdSheet != null)
        { var a = FindAnim(_crowdSheet, "default", "crowd"); if (a != null) _crowdSprite.PlayAnimation(a, true, true); }
        if (_barSprite != null && _barSheet != null)
        { var a = FindAnim(_barSheet, "default", "bar"); if (a != null) _barSprite.PlayAnimation(a, true, true); }
        foreach (var cn in _charIds)
        {
            if (_playerSprites.TryGetValue(cn, out var ps) && _playerSheets.TryGetValue(cn, out var pSh))
            {
                System.Diagnostics.Debug.WriteLine($"[Editor] {cn} player anims: {string.Join(", ", pSh.Animations.Keys)} | composites: {string.Join(", ", pSh.CompositeAnimations.Keys)} | raw: {string.Join(", ", pSh.RawCompositeData.Keys)}");
                string enter = FindAnim(pSh, "Enter", "slidein");
                string idle = FindAnim(pSh, "Idle", "cs idle", "idle");
                System.Diagnostics.Debug.WriteLine($"[Editor] {cn} player: enter={enter} idle={idle}");
                if (enter != null)
                {
                    ps.PlayAnimation(enter, true, false);
                    var capturedIdle = idle;
                    var capturedPs = ps;
                    ps.OnFinish = () =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Editor] {cn} player Enter finished, playing {capturedIdle}");
                        if (capturedIdle != null) capturedPs.PlayAnimation(capturedIdle, true, true);
                        capturedPs.OnFinish = null;
                    };
                }
                else if (idle != null) ps.PlayAnimation(idle, true, true);
            }
            if (_spectatorSprites.TryGetValue(cn, out var ss) && _spectatorSheets.TryGetValue(cn, out var sSh))
            {
                System.Diagnostics.Debug.WriteLine($"[Editor] {cn} spectator anims: {string.Join(", ", sSh.Animations.Keys)} | composites: {string.Join(", ", sSh.CompositeAnimations.Keys)}");
                string enter = FindAnim(sSh, "Enter", "slidein");
                string idle = FindAnim(sSh, "IdleLeft", "Idle", "idle");
                bool hasDual = sSh.Animations.ContainsKey("IdleLeft") || sSh.CompositeAnimations.ContainsKey("IdleLeft");
                System.Diagnostics.Debug.WriteLine($"[Editor] {cn} spectator: enter={enter} idle={idle} hasDual={hasDual}");
                if (enter != null)
                {
                    ss.PlayAnimation(enter, true, false);
                    var capturedIdle = idle;
                    var capturedSs = ss;
                    var capturedHasDual = hasDual;
                    ss.OnFinish = () =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Editor] {cn} spectator Enter finished, playing {capturedIdle}");
                        if (capturedIdle != null) capturedSs.PlayAnimation(capturedIdle, true, !capturedHasDual);
                        _gfDanceLeft = true;
                        capturedSs.OnFinish = null;
                    };
                }
                else if (idle != null) ss.PlayAnimation(idle, true, !hasDual);
            }
        }
    }

    private static string FindAnim(SpriteSheet sheet, params string[] names)
    {
        if (sheet == null) return null;
        foreach (var n in names)
            if (sheet.CompositeAnimations.ContainsKey(n)) return n;
        foreach (var n in names)
            if (sheet.Animations.ContainsKey(n)) return n;
        foreach (var n in names)
            if (sheet.RawCompositeData.ContainsKey(n)) return n;
        foreach (var n in names)
            foreach (var k in sheet.CompositeAnimations.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        foreach (var n in names)
            foreach (var k in sheet.Animations.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        foreach (var n in names)
            foreach (var k in sheet.RawCompositeData.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        return sheet.Animations.Keys.FirstOrDefault();
    }

    public override void Update(GameTime gameTime)
    {
        EditorUI.UpdateInput();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();

        if (_loading) { ProcessLoadPhase(); _prevKb = kb; return; }

        // Debug: trace current animations once after loading
        if (_debugOnce == 0)
        {
            _debugOnce++;
            string dcid = _charIds[_charIndex];
            if (_playerSprites.TryGetValue(dcid, out var dps))
                System.Diagnostics.Debug.WriteLine($"[Editor] Player({dcid}) anim={dps.CurrentAnimation} finished={dps.Finished} frame={dps.FrameIndex}");
            if (_spectatorSprites.TryGetValue(dcid, out var dss))
                System.Diagnostics.Debug.WriteLine($"[Editor] Spectator({dcid}) anim={dss.CurrentAnimation} finished={dss.Finished} frame={dss.FrameIndex}");
        }

        // Update animated sprites
        _crowdSprite?.Update(dt);
        _stageSprite?.Update(dt);
        _barSprite?.Update(dt);

        // Update active character sprites
        string cid = _charIds[_charIndex];
        if (_playerSprites.TryGetValue(cid, out var pSpr)) pSpr?.Update(dt);
        if (_spectatorSprites.TryGetValue(cid, out var sSpr))
        {
            sSpr?.Update(dt);
            // GF dual-dance
            if (sSpr != null && sSpr.Finished && sSpr.Sheet != null)
            {
                string cur = sSpr.CurrentAnimation;
                if (cur != null && cur.Contains("Idle", StringComparison.OrdinalIgnoreCase))
                {
                    _gfDanceLeft = !_gfDanceLeft;
                    string next = _gfDanceLeft ? "IdleLeft" : "IdleRight";
                    if (sSpr.Sheet.Animations.ContainsKey(next) || sSpr.Sheet.CompositeAnimations.ContainsKey(next))
                        sSpr.PlayAnimation(next, true, false);
                    else
                        sSpr.PlayAnimation(cur, true, false);
                }
            }
        }

        // Tab = switch character
        if (kb.IsKeyDown(Keys.Tab) && !_prevKb.IsKeyDown(Keys.Tab))
        {
            _charIndex = (_charIndex + 1) % _charIds.Length;
            System.Diagnostics.Debug.WriteLine("[Editor] Char: " + _charIds[_charIndex]);
        }

        // Up/Down = select element
        if (kb.IsKeyDown(Keys.Up) && !_prevKb.IsKeyDown(Keys.Up))
            _selected = (_selected - 1 + _elements.Length) % _elements.Length;
        if (kb.IsKeyDown(Keys.Down) && !_prevKb.IsKeyDown(Keys.Down))
            _selected = (_selected + 1) % _elements.Length;

        // WASD = move selected element
        float step = kb.IsKeyDown(Keys.LeftShift) ? 10f : 1f;
        if (kb.IsKeyDown(Keys.W)) _elements[_selected].Y -= step;
        if (kb.IsKeyDown(Keys.S)) _elements[_selected].Y += step;
        if (kb.IsKeyDown(Keys.A)) _elements[_selected].X -= step;
        if (kb.IsKeyDown(Keys.D)) _elements[_selected].X += step;

        // Q/E = scroll factor
        if (kb.IsKeyDown(Keys.Q) && !_prevKb.IsKeyDown(Keys.Q))
            _elements[_selected].ScrollFactor = Math.Max(0, _elements[_selected].ScrollFactor - 0.1f);
        if (kb.IsKeyDown(Keys.E) && !_prevKb.IsKeyDown(Keys.E))
            _elements[_selected].ScrollFactor = Math.Round(_elements[_selected].ScrollFactor + 0.1, 1) is double d ? (float)d : _elements[_selected].ScrollFactor + 0.1f;

        // Z/X = scale
        if (kb.IsKeyDown(Keys.Z) && !_prevKb.IsKeyDown(Keys.Z))
        { _elements[_selected].ScaleX = Math.Max(0.1f, _elements[_selected].ScaleX - 0.05f); _elements[_selected].ScaleY = Math.Max(0.1f, _elements[_selected].ScaleY - 0.05f); }
        if (kb.IsKeyDown(Keys.X) && !_prevKb.IsKeyDown(Keys.X))
        { _elements[_selected].ScaleX += 0.05f; _elements[_selected].ScaleY += 0.05f; }

        // H = toggle overlay
        if (kb.IsKeyDown(Keys.H) && !_prevKb.IsKeyDown(Keys.H)) _showOverlay = !_showOverlay;
        // C = print positions
        if (kb.IsKeyDown(Keys.C) && !_prevKb.IsKeyDown(Keys.C)) PrintPositions();
        // P = print code snippet
        if (kb.IsKeyDown(Keys.P) && !_prevKb.IsKeyDown(Keys.P)) PrintCodeSnippet();
        // Esc = back to editor hub
        if (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape))
            Game.Scenes.ChangeScene(new EditorHubScene());

        _prevKb = kb;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (_loading)
        {
            sb.Begin(samplerState: SamplerState.PointClamp);
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black);
            var f = Assets.GetFont(20);
            if (f != null) sb.DrawString(f, "Loading CharSelect stage... phase " + _loadPhase, new Vector2(40, 40), Color.White);
            sb.End();
            return;
        }

        ref var bg = ref _elements[EL_BG];
        ref var crowd = ref _elements[EL_CROWD];
        ref var stage = ref _elements[EL_STAGE];
        ref var curt = ref _elements[EL_CURTAINS];
        ref var bar = ref _elements[EL_BAR];
        ref var cl1 = ref _elements[EL_CHARLIGHT1];
        ref var cl2 = ref _elements[EL_CHARLIGHT2];
        ref var gf = ref _elements[EL_GF];
        ref var bf = ref _elements[EL_BF];
        ref var spkS = ref _elements[EL_SPKSTACK];
        ref var spkM = ref _elements[EL_SPKMONITOR];
        ref var fgB = ref _elements[EL_FGBLUR];

        // === AlphaBlend batch 1: BG, Crowd, Stage, Curtains ===
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        sb.Draw(Assets.Pixel, new Rectangle(-500, -500, FNFGame.SCREEN_WIDTH + 1000, FNFGame.SCREEN_HEIGHT + 1000), Color.Black);
        if (_bgTex != null) sb.Draw(_bgTex, new Vector2(bg.X, bg.Y), Color.White);
        DrawAnim(sb, _crowdSprite, crowd.X, crowd.Y, crowd.ScaleX);
        DrawAnim(sb, _stageSprite, stage.X, stage.Y, stage.ScaleX);
        if (_curtainsTex != null) sb.Draw(_curtainsTex, new Vector2(curt.X, curt.Y), Color.White);
        sb.End();

        // === MULTIPLY batch: Bar ===
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: _multiplyBlend);
        if (_barSprite != null)
        {
            _barSprite.Position = new Vector2(bar.X, bar.Y);
            _barSprite.Scale = new Vector2(bar.ScaleX, bar.ScaleY);
            _barSprite.Draw(sb);
        }
        sb.End();

        // === AlphaBlend batch 2: CharLights, GF, BF, Speakers ===
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        if (_charLightTex != null)
        {
            sb.Draw(_charLightTex, new Vector2(cl1.X, cl1.Y), Color.White);
            sb.Draw(_charLightTex, new Vector2(cl2.X, cl2.Y), Color.White);
        }

        // Spectator (GF/Nene)
        string cid = _charIds[_charIndex];
        if (_spectatorSprites.TryGetValue(cid, out var specSpr) && specSpr != null)
        {
            specSpr.Position = new Vector2(gf.X, gf.Y);
            specSpr.Scale = new Vector2(gf.ScaleX, gf.ScaleY);
            specSpr.Draw(sb);
        }

        // Player (BF/Pico)
        if (_playerSprites.TryGetValue(cid, out var playSpr) && playSpr != null)
        {
            playSpr.Position = new Vector2(bf.X, bf.Y);
            playSpr.Scale = new Vector2(bf.ScaleX, bf.ScaleY);
            playSpr.Draw(sb);
        }

        // Speaker Stack (raw sprite from spritemap)
        if (_speakerMapTex != null)
        {
            sb.Draw(_speakerMapTex, new Vector2(spkS.X, spkS.Y), _spkStackSrc, Color.White, 0, Vector2.Zero, new Vector2(spkS.ScaleX, spkS.ScaleY), SpriteEffects.None, 0);
            sb.Draw(_speakerMapTex, new Vector2(spkM.X, spkM.Y), _spkMonitorSrc, Color.White, 0, Vector2.Zero, new Vector2(spkM.ScaleX, spkM.ScaleY), SpriteEffects.None, 0);
        }
        sb.End();

        // === MULTIPLY batch 2: FgBlur ===
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: _multiplyBlend);
        if (_fgBlurTex != null)
            sb.Draw(_fgBlurTex, new Vector2(fgB.X, fgB.Y), Color.White);
        sb.End();

        // === Overlay ===
        if (_showOverlay)
        {
            sb.Begin(samplerState: SamplerState.PointClamp);
            DrawOverlay(sb);
            sb.End();
        }
    }

    private void DrawAnim(SpriteBatch sb, AnimatedSprite sprite, float x, float y, float scale)
    {
        if (sprite == null) return;
        sprite.Position = new Vector2(x, y);
        sprite.Scale = new Vector2(scale, scale);
        sprite.Draw(sb);
    }

    private void DrawOverlay(SpriteBatch sb)
    {
        var f = Assets.GetFont(16);
        if (f == null) return;
        int pw = 380, ph = 22 * _elements.Length + 140;
        int px = FNFGame.SCREEN_WIDTH - pw - 10, py = 10;
        EditorUI.FillRect(sb, Assets.Pixel, new Rectangle(px, py, pw, ph), EditorUI.BgPanel * 0.85f);
        EditorUI.DrawBorder(sb, Assets.Pixel, new Rectangle(px, py, pw, ph), EditorUI.Border);
        sb.DrawString(f, "CharSelect Position Editor  [char: " + _charIds[_charIndex] + "]", new Vector2(px + 8, py + 6), EditorUI.Accent);

        int row = py + 30;
        for (int i = 0; i < _elements.Length; i++)
        {
            ref var el = ref _elements[i];
            Color c = i == _selected ? EditorUI.Gold : el.LabelColor;
            string line = $"{el.Name,-13} X: {el.X,6:F0}  Y: {el.Y,6:F0}  SF:{el.ScrollFactor:F1}  S:{el.ScaleX:F2}";
            if (i == _selected)
                EditorUI.FillRect(sb, Assets.Pixel, new Rectangle(px + 2, row - 1, pw - 4, 20), EditorUI.Selected);
            sb.DrawString(f, line, new Vector2(px + 8, row), c);
            row += 22;
        }

        row += 8;
        string[] help = {
            "Up/Down = Select element",
            "WASD = Move  (Shift = 10px)",
            "Q/E = Scroll Factor   Z/X = Scale",
            "Tab = Switch character",
            "H = Toggle overlay",
            "C = Print Positions  P = Code snippet",
            "Esc = Back to Editor Hub"
        };
        foreach (var h in help) { sb.DrawString(f, h, new Vector2(px + 8, row), EditorUI.TextDim); row += 18; }
    }

    private void PrintPositions()
    {
        System.Diagnostics.Debug.WriteLine("=== CharSelect Positions ==="  );
        foreach (var el in _elements)
            System.Diagnostics.Debug.WriteLine($"{el.Name}: X={el.X:F0} Y={el.Y:F0} SF={el.ScrollFactor:F1} Scale={el.ScaleX:F2}");
    }

    private void PrintCodeSnippet()
    {
        System.Diagnostics.Debug.WriteLine("=== CharSelect Code Snippet ===" );
        var n = new[] { "BG", "Crowd", "Stage", "Curtains", "Bar", "CharLight1", "CharLight2", "GF", "BF", "SpkStack", "SpkMonitor", "FgBlur" };
        for (int i = 0; i < _elements.Length && i < n.Length; i++)
        {
            ref var el = ref _elements[i];
            System.Diagnostics.Debug.WriteLine($"// {n[i]}: pos=({el.X:F0},{el.Y:F0}) sf={el.ScrollFactor:F1} scale={el.ScaleX:F2}");
        }
    }
}
