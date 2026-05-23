using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Character Select scene — faithful recreation of the original FNF CharSelectSubState.
/// 3×3 icon grid, animated player/spectator, stage background with crowd/speakers/bar,
/// cursor navigation, confirm → Freeplay, back → Freeplay.
/// </summary>
public class CharacterSelectScene : Scene
{
    // Available playable characters (id → grid position 0-8)
    private readonly string[] _charIds = { "bf", "pico" };
    private readonly Dictionary<int, string> _availableChars = new();

    // Per-character offsets. Original FNF: gfChill(0,0), playerChill(0,0) with
    // applyStageMatrix:true — AnimateAtlas internal M3D transforms position sprites correctly.
    // Offsets here are additional adjustments only (all zero to match original).
    private static readonly Dictionary<string, Vector2> _playerOffsets = new()
    {
        { "bf", new Vector2(0f, 0f) },
        { "pico", new Vector2(0f, 0f) }
    };
    private static readonly Dictionary<string, Vector2> _spectatorOffsets = new()
    {
        { "bf", new Vector2(0f, 0f) },
        { "pico", new Vector2(0f, 0f) }
    };

    // Grid cursor (original: cursorX/cursorY range -1..1, maps to 3×3 grid center at index 4)
    private int _cursorX = 0;
    private int _cursorY = 0;

    // Grid layout (original: grpIcons at x=450, y=120, spread 107×127)
    private const float GRID_X = 450f;
    private const float GRID_Y = 120f;
    private const float GRID_X_SPREAD = 107f;
    private const float GRID_Y_SPREAD = 127f;
    private const float ZOOM = 1.0f; // Original FNF CharSelectSubState has no zoom (default 1.0)
    private Matrix _zoomMatrix; // Computed in Draw, Identity (original FNF has no zoom)

    // State
    private string _curChar = "bf";
    private bool _pressedSelect;
    private bool _allowInput;
    private float _selectTimer;
    private float _fadeAlpha = 1f; // Start black, fade in
    private float _introTimer;
    private bool _onLockedSlot; // True when cursor is on a locked grid position

    // Stage textures
    private Texture2D _bgTex;
    private Texture2D _curtainsTex;
    private Texture2D _charLightTex;
    private Texture2D _chooseDipshitTex;
    private Texture2D _chooseDarkTex;
    private Texture2D _fgBlurTex;
    private Texture2D _selectorTex;

    // Stage animated sprites
    private SpriteSheet _crowdSheet;
    private AnimatedSprite _crowdSprite;
    private SpriteSheet _stageSheet;
    private AnimatedSprite _stageSprite;
    private SpriteSheet _speakersSheet;
    private AnimatedSprite _speakersSprite;
    private SpriteSheet _barSheet;
    private AnimatedSprite _barSprite;

    // Selector cursor sprites
    private SpriteSheet _confirmSelectorSheet;
    private SpriteSheet _deniedSelectorSheet;
    private AnimatedSprite _confirmSelectorSprite;
    private AnimatedSprite _deniedSelectorSprite;

    // Choose backing/blur animated sprites
    private SpriteSheet _chooseBackingSheet;
    private AnimatedSprite _chooseBackingSprite;
    private SpriteSheet _chooseBlurSheet;
    private AnimatedSprite _chooseBlurSprite;

    // Character icon spritesheets (grid icons)
    private readonly Dictionary<string, SpriteSheet> _iconSheets = new();
    private readonly Dictionary<string, AnimatedSprite> _iconSprites = new();
    private Texture2D _lockedIconTex;

    // Character title nametag
    private readonly Dictionary<string, Texture2D> _titleTextures = new();

    // Player and spectator per character (sheets for data, CharAnim for self-contained animation)
    private readonly Dictionary<string, SpriteSheet> _playerSheets = new();
    private readonly Dictionary<string, CharAnim> _playerAnims = new();
    private readonly Dictionary<string, SpriteSheet> _spectatorSheets = new();
    private readonly Dictionary<string, CharAnim> _spectatorAnims = new();

    // GF dual-dance state
    private bool _gfDanceLeft = true;

    // Animation timer
    private float _animTimer;
    private int _animFrame;

    // Selector visual state
    private bool _selectorConfirmed;
    private bool _selectorDenied;
    private float _selectorDeniedTimer;

    // Smooth cursor (3-layer trailing system from CharSelectCursors.hx)
    private Vector2 _cursorIntended;
    private Vector2 _cursorMain;
    private Vector2 _cursorLightBlue;
    private Vector2 _cursorDarkBlue;
    private bool _cursorInitialized;

    // Intro slide-up animation offsets (original: FlxTween with expoOut)
    private float _introSlideTimer;
    private float _barSlideY;
    private float _backingSlideY;
    private float _platformSlideY;
    private float _blurSlideY;
    private float _iconSlideY;
    private float _nametagSlideY;
    private bool _introStarted;

    // Nametag switch animation (original: smooth slide-in on character change)
    private float _nametagSwitchTimer;
    private string _nametagLastChar = "";

    // Exit slide-out animation (original: FlxTween with backIn, 0.8s)
    private bool _exiting;
    private float _exitSlideTimer;
    private float _cursorAlpha = 1f;
    private bool _exitWithConfirm; // Only save character when confirmed (not on Back)

    // Hold-repeat cursor navigation (original: holdTmr per direction, spam after 0.25s)
    private float _holdTmrUp, _holdTmrDown, _holdTmrLeft, _holdTmrRight;
    private const float HOLD_DELAY = 0.25f;

    // MULTIPLY blend state (original: bar and fgBlur use BlendMode.MULTIPLY)
    private static readonly BlendState _multiplyBlend = new BlendState
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    // Blend state for per-part composite rendering (non-premultiplied, matching CompositeDebugScene)
    private static readonly BlendState _compositeBlend = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    // Camera follow (original: autoFollow, camFollow screenCenter + cursor*10, scrollFactors for parallax)
    private float _cameraX, _cameraY;
    private bool _autoFollow;
    private float _exitCameraStartY;

    // Phased loading
    private bool _loading = true;
    private int _loadPhase;
    private string _loadStatus = "Loading...";

    public override void Load()
    {
        // Disable debug overlay in character select (user-reported stray text)
#if !XBOX_UWP
        DebugOverlay.Visible = false;
        DebugOverlay.ClearPins();
#endif

        // Set up available character positions (original: position from player registry)
        _availableChars[4] = "bf";   // Center of 3×3 grid
        if (_charIds.Length > 1)
            _availableChars[5] = "pico"; // Right of center

        // Read remembered character
        _curChar = HighscoreManager.Data.SelectedCharacter ?? "bf";

        // Set cursor to remembered character position
        foreach (var kvp in _availableChars)
        {
            if (kvp.Value == _curChar)
            {
                SetCursorFromIndex(kvp.Key);
                break;
            }
        }

        _loading = true;
        _loadPhase = 0;
    }

    private void SetCursorFromIndex(int index)
    {
        _cursorX = (index % 3) - 1;
        _cursorY = (index / 3) - 1;
    }

    private int GetCurrentSelected()
    {
        return (_cursorY + 1) * 3 + (_cursorX + 1);
    }

    private void ProcessLoadPhase()
    {
        string b = "menus/character_select";
        switch (_loadPhase)
        {
            case 0:
                _loadStatus = "Loading stage...";
                _bgTex = Assets.LoadTexture(b + "/stage/bg.png");
                if (_bgTex == Assets.Pixel) _bgTex = null;
                if (_bgTex != null) SpriteSheet.PremultiplyAlpha(_bgTex);
                _curtainsTex = Assets.LoadTexture(b + "/stage/curtains.png");
                if (_curtainsTex == Assets.Pixel) _curtainsTex = null;
                if (_curtainsTex != null) SpriteSheet.PremultiplyAlpha(_curtainsTex);
                _charLightTex = Assets.LoadTexture(b + "/stage/character_light.png");
                if (_charLightTex == Assets.Pixel) _charLightTex = null;
                if (_charLightTex != null) SpriteSheet.PremultiplyAlpha(_charLightTex);
                _chooseDipshitTex = Assets.LoadTexture(b + "/stage/choose_dipshit.png");
                if (_chooseDipshitTex == Assets.Pixel) _chooseDipshitTex = null;
                _fgBlurTex = Assets.LoadTexture(b + "/stage/fg_blur.png");
                if (_fgBlurTex == Assets.Pixel) _fgBlurTex = null;
                _chooseDarkTex = Assets.LoadTexture(b + "/stage/choose_dark.png");
                if (_chooseDarkTex == Assets.Pixel) _chooseDarkTex = null;
                break;

            case 1:
                _loadStatus = "Loading animations...";
                _crowdSheet = SpriteSheet.Load(Game, b + "/stage/crowd", preRenderComposites: true, deferComposites: true);
                if (_crowdSheet != null)
                {
                    _crowdSprite = new AnimatedSprite { Sheet = _crowdSheet };
                    var anim = FindAnim(_crowdSheet, "default", "crowd");
                    if (anim != null) _crowdSprite.PlayAnimation(anim, true, true);
                }
                // Stage is AnimateAtlas (original uses createTextureAtlas with applyStageMatrix:true)
                // SpriteSheet.Load detects the stage/stage/ folder with spritemap1.png + Animation.json
                _stageSheet = SpriteSheet.Load(Game, b + "/stage/stage", preRenderComposites: true, deferComposites: true);
                if (_stageSheet != null)
                {
                    _stageSprite = new AnimatedSprite { Sheet = _stageSheet };
                    var anim = FindAnim(_stageSheet, "default", "stage");
                    if (anim != null) _stageSprite.PlayAnimation(anim, true, true);
                }
                break;

            case 2:
                _loadStatus = "Loading speakers...";
                _speakersSheet = SpriteSheet.Load(Game, b + "/stage/speakers", preRenderComposites: true, deferComposites: true);
                if (_speakersSheet != null)
                {
                    _speakersSprite = new AnimatedSprite { Sheet = _speakersSheet };
                    var anim = FindAnim(_speakersSheet, "default", "speakers");
                    if (anim != null) _speakersSprite.PlayAnimation(anim, true, true);
                }
                _barSheet = SpriteSheet.Load(Game, b + "/stage/bar", preRenderComposites: true, deferComposites: true);
                if (_barSheet != null)
                {
                    _barSprite = new AnimatedSprite { Sheet = _barSheet };
                    var anim = FindAnim(_barSheet, "default", "bar");
                    if (anim != null) _barSprite.PlayAnimation(anim, true, true);
                }
                break;

            case 3:
                _loadStatus = "Loading icons...";
                foreach (var cn in _charIds)
                {
                    var iconSheet = SpriteSheet.Load(Game, b + "/" + cn + "/icon");
                    if (iconSheet != null)
                    {
                        _iconSheets[cn] = iconSheet;
                        var spr = new AnimatedSprite { Sheet = iconSheet };
                            var anim = FindAnim(iconSheet, "BF ICON", "PICO ICON", "idle", "icon");
                            if (anim != null) spr.PlayAnimation(anim, true, false); // Don't auto-loop
                            _iconSprites[cn] = spr;
                    }
                    var titleTex = Assets.LoadTexture(b + "/" + cn + "/title.png");
                    if (titleTex != null && titleTex != Assets.Pixel)
                        _titleTextures[cn] = titleTex;
                }
                _lockedIconTex = Assets.LoadTexture(b + "/locked/icon.png");
                if (_lockedIconTex == Assets.Pixel) _lockedIconTex = null;
                var lockedTitle = Assets.LoadTexture(b + "/locked/title.png");
                if (lockedTitle != null && lockedTitle != Assets.Pixel)
                    _titleTextures["locked"] = lockedTitle;
                break;

            case 4:
                _loadStatus = "Loading selector...";
                _selectorTex = Assets.LoadTexture(b + "/selector/selector.png");
                if (_selectorTex == Assets.Pixel) _selectorTex = null;
                _chooseBackingSheet = SpriteSheet.Load(Game, b + "/stage/choose_backing");
                if (_chooseBackingSheet != null)
                {
                    _chooseBackingSprite = new AnimatedSprite { Sheet = _chooseBackingSheet };
                    var anim = FindAnim(_chooseBackingSheet, "CHOOSE horizontal offset", "idle");
                    if (anim != null) _chooseBackingSprite.PlayAnimation(anim, true, true);
                }
                _chooseBlurSheet = SpriteSheet.Load(Game, b + "/stage/choose_blur");
                if (_chooseBlurSheet != null)
                {
                    _chooseBlurSprite = new AnimatedSprite { Sheet = _chooseBlurSheet };
                    var anim = FindAnim(_chooseBlurSheet, "CHOOSE vertical offset", "idle");
                    if (anim != null) _chooseBlurSprite.PlayAnimation(anim, true, true);
                }
                _confirmSelectorSheet = SpriteSheet.Load(Game, b + "/selector/confirm");
                if (_confirmSelectorSheet != null)
                {
                    _confirmSelectorSprite = new AnimatedSprite { Sheet = _confirmSelectorSheet };
                    var confirmAnim = FindAnim(_confirmSelectorSheet, "cursor ACCEPTED", "idle");
                    if (confirmAnim != null) _confirmSelectorSprite.PlayAnimation(confirmAnim, true, true);
                }
                _deniedSelectorSheet = SpriteSheet.Load(Game, b + "/selector/denied");
                if (_deniedSelectorSheet != null)
                {
                    _deniedSelectorSprite = new AnimatedSprite { Sheet = _deniedSelectorSheet };
                    var deniedAnim = FindAnim(_deniedSelectorSheet, "cursor DENIED", "idle");
                    if (deniedAnim != null) _deniedSelectorSprite.PlayAnimation(deniedAnim, true, false);
                }
                break;

            case 5:
                _loadStatus = "Loading characters...";
                LoadCharacterSprites();
                break;

            case 6:
                _loadStatus = "Rendering...";
                // Convert RawCompositeData → pre-rendered CompositeAnimations (GPU work, main thread).
                // PreRenderComposite uses full affine Matrix transforms for pixel-perfect body part
                // assembly — no skew/shear loss from runtime M3D decomposition.
                _crowdSheet?.RenderRawComposites(Game.GraphicsDevice);
                _stageSheet?.RenderRawComposites(Game.GraphicsDevice);
                _speakersSheet?.RenderRawComposites(Game.GraphicsDevice);
                _barSheet?.RenderRawComposites(Game.GraphicsDevice);
                // Player/spectator: skip RenderRawComposites — keep RawCompositeData alive
                // for runtime per-part Matrix rendering (bypasses broken pre-rendering pipeline)

                // MonoGame 3.8.1 DesktopGL SpriteBatch automatically handles Y-flip
                // compensation when drawing from RenderTarget2D textures. No need to
                // convert to regular Texture2D — GetData on OpenGL RTs can return
                // Y-flipped pixel data, corrupting the converted texture.

                // Replay animations on deferred sprites
                // FindAnim found keys in RawCompositeData but PlayAnimation couldn't set them up.
                // Now that RenderRawComposites has moved data into CompositeAnimations/Animations,
                // we can properly play them.
                ReplayDeferredAnimations();
                break;

            case 7:
                _loadStatus = "Starting...";
                Audio.PlayMusic("menus/character_select/character_select", true);
                _allowInput = true;
                _fadeAlpha = 1f;
                _introTimer = 0;
                _cameraY = -150f; // Camera starts panned up (original: camFollow.y -= 150)
                // Initialize intro slide-up offsets (original values from CharSelectSubState.hx)
                // All HUD elements start BELOW rest position and slide UP (y += offset, tween y - offset)
                _barSlideY = 80f;   // Original: barthing.y += 80
                _backingSlideY = 210f;
                _platformSlideY = 200f;
                _blurSlideY = 220f;
                _iconSlideY = 300f;
                _nametagSlideY = 200f;
                _introSlideTimer = 0f;
                _introStarted = true;
                _loading = false;
                return;
        }
        _loadPhase++;
    }

    private void LoadCharacterSprites()
    {
        string b = "menus/character_select";
        foreach (var cn in _charIds)
        {
            // Player sprite (BF/Pico on the right) — NO PremultiplyAlpha (use _compositeBlend)
            var ps = SpriteSheet.Load(Game, b + "/" + cn + "/player", preRenderComposites: true, preRenderFilter: new[] { "Enter", "Idle", "Confirm", "Cancel", "Exit" }, deferComposites: true, applyStageInstanceTransform: true);
            Console.WriteLine($"[CharSelect] {cn}/player: loaded={ps != null} tex={ps?.Texture != null} anims=[{(ps != null ? string.Join(",", ps.Animations.Keys) : "")}] raw=[{(ps != null ? string.Join(",", ps.RawCompositeData.Keys) : "")}] rawCounts=[{(ps != null ? string.Join(",", ps.RawCompositeData.Select(r => $"{r.Key}:{r.Value.Count}t/{(r.Value.Count > 0 ? r.Value[0].Count.ToString() : "0")}p")) : "")}]");
            if (ps != null)
            {
                _playerSheets[cn] = ps;
                var anim = new CharAnim();
                string enterAnim = FindAnim(ps, "Enter", "slidein");
                string idleAnim = FindAnim(ps, "Idle", "cs idle", "idle");
                if (enterAnim != null)
                {
                    anim.Play(ps, enterAnim, false);
                    anim.OnFinish = () =>
                    {
                        if (idleAnim != null) anim.Play(ps, idleAnim, true);
                        anim.OnFinish = null;
                    };
                }
                else if (idleAnim != null)
                {
                    anim.Play(ps, idleAnim, true);
                }
                _playerAnims[cn] = anim;
            }

            // Spectator sprite (GF/Nene on the left) — NO PremultiplyAlpha (use _compositeBlend)
            var ss = SpriteSheet.Load(Game, b + "/" + cn + "/spectator", preRenderComposites: true, preRenderFilter: new[] { "Enter", "IdleLeft", "IdleRight", "Confirm", "Cancel", "Exit" }, deferComposites: true, applyStageInstanceTransform: true);
            if (ss != null)
            {
                _spectatorSheets[cn] = ss;
                var anim = new CharAnim();
                string specEnterAnim = FindAnim(ss, "Enter", "slidein");
                string specIdleAnim = FindAnim(ss, "IdleLeft", "Idle", "idle", "gf", "dance");
                bool hasDualDance = SheetHasAnim(ss, "IdleLeft");
                if (specEnterAnim != null)
                {
                    anim.Play(ss, specEnterAnim, false);
                    anim.OnFinish = () =>
                    {
                        if (specIdleAnim != null) anim.Play(ss, specIdleAnim, !hasDualDance);
                        _gfDanceLeft = true;
                        anim.OnFinish = null;
                    };
                }
                else if (specIdleAnim != null)
                {
                    anim.Play(ss, specIdleAnim, !hasDualDance);
                }
                _spectatorAnims[cn] = anim;
            }
        }
    }

    /// <summary>
    /// After RenderRawComposites converts deferred RawCompositeData into CompositeAnimations/Animations,
    /// replay animations on all sprites that were loaded with deferComposites:true.
    /// During initial load, PlayAnimation couldn't set up animations that only existed in RawCompositeData.
    /// </summary>
    private void ReplayDeferredAnimations()
    {
        // Stage sprites: crowd, stage, speakers, bar
        if (_crowdSprite != null && _crowdSheet != null)
        {
            var anim = FindAnim(_crowdSheet, "default", "crowd");
            if (anim != null) _crowdSprite.PlayAnimation(anim, true, true);
        }
        if (_stageSprite != null && _stageSheet != null)
        {
            var anim = FindAnim(_stageSheet, "default", "stage");
            if (anim != null) _stageSprite.PlayAnimation(anim, true, true);
        }
        if (_speakersSprite != null && _speakersSheet != null)
        {
            var anim = FindAnim(_speakersSheet, "default", "speakers");
            if (anim != null) _speakersSprite.PlayAnimation(anim, true, true);
        }
        if (_barSprite != null && _barSheet != null)
        {
            var anim = FindAnim(_barSheet, "default", "bar");
            if (anim != null) _barSprite.PlayAnimation(anim, true, true);
        }

        // Player animations (BF/Pico) — replay Enter→Idle chain
        foreach (var cn in _charIds)
        {
            if (_playerAnims.TryGetValue(cn, out var pa) && _playerSheets.TryGetValue(cn, out var pSheet))
            {
                string enterAnim = FindAnim(pSheet, "Enter", "slidein");
                string idleAnim = FindAnim(pSheet, "Idle", "cs idle", "idle");
                if (enterAnim != null)
                {
                    pa.Play(pSheet, enterAnim, false);
                    pa.OnFinish = () =>
                    {
                        if (idleAnim != null) pa.Play(pSheet, idleAnim, true);
                        pa.OnFinish = null;
                    };
                }
                else if (idleAnim != null)
                {
                    pa.Play(pSheet, idleAnim, true);
                }
            }
        }

        // Spectator animations (GF/Nene) — replay Enter→Idle chain with dual-dance
        foreach (var cn in _charIds)
        {
            if (_spectatorAnims.TryGetValue(cn, out var sa) && _spectatorSheets.TryGetValue(cn, out var sSheet))
            {
                string specEnterAnim = FindAnim(sSheet, "Enter", "slidein");
                string specIdleAnim = FindAnim(sSheet, "IdleLeft", "Idle", "idle", "gf", "dance");
                bool hasDualDance = SheetHasAnim(sSheet, "IdleLeft");
                if (specEnterAnim != null)
                {
                    sa.Play(sSheet, specEnterAnim, false);
                    sa.OnFinish = () =>
                    {
                        if (specIdleAnim != null) sa.Play(sSheet, specIdleAnim, !hasDualDance);
                        _gfDanceLeft = true;
                        sa.OnFinish = null;
                    };
                }
                else if (specIdleAnim != null)
                {
                    sa.Play(sSheet, specIdleAnim, !hasDualDance);
                }
                _gfDanceLeft = true;
            }
        }
    }

    private static string FindAnim(SpriteSheet sheet, params string[] names)
    {
        if (sheet == null) return null;
        // Exact match in composites first
        foreach (var n in names)
            if (sheet.CompositeAnimations.ContainsKey(n)) return n;
        // Exact match in standard animations
        foreach (var n in names)
            if (sheet.Animations.ContainsKey(n)) return n;
        // Exact match in raw composites (deferred, not yet rendered)
        foreach (var n in names)
            if (sheet.RawCompositeData.ContainsKey(n)) return n;
        // Fuzzy match in composites
        foreach (var n in names)
            foreach (var k in sheet.CompositeAnimations.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        // Fuzzy match in standard
        foreach (var n in names)
            foreach (var k in sheet.Animations.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        // Fuzzy match in raw composites (deferred)
        foreach (var n in names)
            foreach (var k in sheet.RawCompositeData.Keys)
                if (k.Contains(n, StringComparison.OrdinalIgnoreCase)) return k;
        // Fallback
        return sheet.Animations.Keys.FirstOrDefault();
    }

    /// <summary>
    /// Check if an animation name exists in any of the sheet's animation dictionaries.
    /// </summary>
    private static bool SheetHasAnim(SpriteSheet sheet, string name) =>
        sheet != null && (
            sheet.Animations.ContainsKey(name) ||
            sheet.CompositeAnimations.ContainsKey(name) ||
            sheet.RawCompositeData.ContainsKey(name));

    public override void Unload()
    {
        _crowdSheet?.Dispose();
        _stageSheet?.Dispose();
        _speakersSheet?.Dispose();
        _barSheet?.Dispose();
        _confirmSelectorSheet?.Dispose();
        _deniedSelectorSheet?.Dispose();
        _chooseBackingSheet?.Dispose();
        _chooseBlurSheet?.Dispose();
        foreach (var s in _iconSheets.Values) s?.Dispose();
        foreach (var s in _playerSheets.Values) s?.Dispose();
        foreach (var s in _spectatorSheets.Values) s?.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Phased loading
        if (_loading)
        {
            ProcessLoadPhase();
            return;
        }

        // Fade in from black (original: FlxTween blackScreen alpha 0→1, 0.8s linear)
        if (_fadeAlpha > 0)
        {
            _introTimer += dt;
            _fadeAlpha = Math.Max(0, 1f - _introTimer * 1.25f);
        }

        // Intro slide-up animation (original: expoOut tweens from CharSelectSubState.hx)
        if (_introStarted && !_exiting && _introSlideTimer < 1.5f)
        {
            _introSlideTimer += dt;
            _barSlideY = 80f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.3f, 1f)));
            _backingSlideY = 210f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.1f, 1f)));
            _platformSlideY = 200f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.0f, 1f)));
            _blurSlideY = 220f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.2f, 1f)));
            _iconSlideY = 300f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.0f, 1f)));
            _nametagSlideY = 200f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.0f, 1f)));
            // Camera intro: pan from -150 to 0 (original: camFollow.y tween over 1.5s expoOut)
            _cameraY = -150f * (1f - ExpoOut(Math.Min(_introSlideTimer / 1.5f, 1f)));
        }

        // Enable camera auto-follow after intro completes (original: onComplete of 1.5s tween)
        if (_introStarted && !_exiting && _introSlideTimer >= 1.5f)
            _autoFollow = true;

        // Exit slide-out animation (original: backIn tweens, 0.8s, from goToFreeplay)
        if (_exiting)
        {
            _exitSlideTimer += dt;
            float t = Math.Min(_exitSlideTimer / 0.8f, 1f);
            float e = BackIn(t);
            _barSlideY = 80f * e;
            _nametagSlideY = 80f * e;
            _backingSlideY = 210f * e;
            _platformSlideY = 200f * e;
            _blurSlideY = 220f * e;
            _iconSlideY = 300f * e;
            _cursorAlpha = 1f - ExpoOut(t);
            // Fade to black during exit (original: FlxTween fadeAlpha 0→1, 0.8s linear)
            _fadeAlpha = t;
            // Camera pans up during exit (original: camFollow.y - 150, backIn)
            _cameraY = _exitCameraStartY + (-150f * e);

            if (_exitSlideTimer >= 0.8f)
            {
                if (_exitWithConfirm)
                {
                    HighscoreManager.Data.SelectedCharacter = _curChar;
                    HighscoreManager.SavePreferences();
                }
                Game.Scenes.ChangeScene(new FreeplayScene());
                return;
            }
        }

        // Smooth cursor update (original: 3-layer trailing from CharSelectCursors.hx)
        // Cursor position matches icon grid: cursorLocIntended = (grpXSpread*posX+grpIcons.x, grpYSpread*posY+grpIcons.y)
        // posX/posY are 0-2, _cursorX/_cursorY are -1..1, so posX = _cursorX+1.
        // Includes _iconSlideY so cursor follows the icon group during intro/exit slide animations.
        {
            _cursorIntended = new Vector2(
                GRID_X_SPREAD * _cursorX + (GRID_X + GRID_X_SPREAD),
                GRID_Y_SPREAD * _cursorY + (GRID_Y + GRID_Y_SPREAD) + _iconSlideY);
            if (!_cursorInitialized)
            {
                _cursorMain = _cursorIntended;
                _cursorLightBlue = _cursorIntended;
                _cursorDarkBlue = _cursorIntended;
                _cursorInitialized = true;
            }
            else
            {
                // Decay rates — faster than original for snappier feel.
                // Original CharSelectCursors.hx: sp=0.1/0.202/0.404 → 0.4343/0.6252/1.1033
                // Reduced by ~50% for quicker cursor snap to target.
                _cursorMain.X = SmoothLerp(_cursorMain.X, _cursorIntended.X, dt, 0.20f);
                _cursorMain.Y = SmoothLerp(_cursorMain.Y, _cursorIntended.Y, dt, 0.20f);
                _cursorLightBlue.X = SmoothLerp(_cursorLightBlue.X, _cursorMain.X, dt, 0.30f);
                _cursorLightBlue.Y = SmoothLerp(_cursorLightBlue.Y, _cursorMain.Y, dt, 0.30f);
                _cursorDarkBlue.X = SmoothLerp(_cursorDarkBlue.X, _cursorIntended.X, dt, 0.55f);
                _cursorDarkBlue.Y = SmoothLerp(_cursorDarkBlue.Y, _cursorIntended.Y, dt, 0.55f);
                // Snap within 1 pixel (original: only main cursor snaps — CharSelectCursors.hx)
                if (MathF.Abs(_cursorMain.X - _cursorIntended.X) < 1f) _cursorMain.X = _cursorIntended.X;
                if (MathF.Abs(_cursorMain.Y - _cursorIntended.Y) < 1f) _cursorMain.Y = _cursorIntended.Y;
            }
        }

        // Camera auto-follow (original: camFollow = screenCenter + cursor*10, camera.follow LOCKON 0.01)
        if (_autoFollow && !_exiting)
        {
            float targetCamX = _cursorX * 10f;
            float targetCamY = _cursorY * 10f;
            _cameraX = SmoothLerp(_cameraX, targetCamX, dt, 1.66f);
            _cameraY = SmoothLerp(_cameraY, targetCamY, dt, 1.66f);
        }

        // Animation timer
        _animTimer += dt;
        if (_animTimer >= 1f / 24f) { _animTimer = 0; _animFrame++; }

        // Nametag switch animation timer
        if (_nametagSwitchTimer > 0) _nametagSwitchTimer -= dt;

        // Update animated sprites
        _crowdSprite?.Update(dt);
        _stageSprite?.Update(dt);
        _speakersSprite?.Update(dt);
        _barSprite?.Update(dt);
        _chooseBackingSprite?.Update(dt);
        _chooseBlurSprite?.Update(dt);
        _confirmSelectorSprite?.Update(dt);
        _deniedSelectorSprite?.Update(dt);

        // Update player animations
        foreach (var kvp in _playerAnims) kvp.Value?.Update(dt);

        // Update spectator animations with GF dual-dance (only for current character)
        foreach (var kvp in _spectatorAnims)
        {
            kvp.Value?.Update(dt);
            if (kvp.Key == _curChar && kvp.Value != null && kvp.Value.Finished &&
                _spectatorSheets.TryGetValue(kvp.Key, out var specSheet))
            {
                string cur = kvp.Value.CurrentAnim;
                if (cur != null && cur.Contains("Idle", StringComparison.OrdinalIgnoreCase))
                {
                    _gfDanceLeft = !_gfDanceLeft;
                    string next = _gfDanceLeft ? "IdleLeft" : "IdleRight";
                    if (SheetHasAnim(specSheet, next))
                        kvp.Value.Play(specSheet, next, false);
                    else
                        kvp.Value.Play(specSheet, cur, false);
                }
            }
        }

        // Icon sprites: don't auto-update (animation plays only on selection press)

        // Selector denied timer
        if (_selectorDenied)
        {
            _selectorDeniedTimer -= dt;
            if (_selectorDeniedTimer <= 0)
                _selectorDenied = false;
        }

        // Skip input processing during exit (sprites keep updating above)
        if (_exiting) return;

        // Confirm timer (after pressing accept on a character)
        if (_pressedSelect)
        {
            _selectTimer -= dt;
            if (_selectTimer <= 0)
            {
                _exitWithConfirm = true;
                GoToFreeplay();
            }

            // Allow deselect with back
            if (Input.BackPressed)
            {
                _pressedSelect = false;
                _selectorConfirmed = false;
                Audio.PlaySound("CS_select");
                // Play deselect animation on player
                if (_playerAnims.TryGetValue(_curChar, out var pa) &&
                    _playerSheets.TryGetValue(_curChar, out var pSheet))
                {
                    string deselAnim = FindAnim(pSheet, "Cancel", "deselect");
                    string idleAnim = FindAnim(pSheet, "Idle", "cs idle", "idle");
                    if (deselAnim != null)
                    {
                        pa.Play(pSheet, deselAnim, false);
                        pa.OnFinish = () =>
                        {
                            if (idleAnim != null) pa.Play(pSheet, idleAnim, true);
                            pa.OnFinish = null;
                        };
                    }
                    else if (idleAnim != null)
                    {
                        pa.Play(pSheet, idleAnim, true);
                    }
                }
                if (_spectatorAnims.TryGetValue(_curChar, out var sa) &&
                    _spectatorSheets.TryGetValue(_curChar, out var sSheet))
                {
                    string deselAnim = FindAnim(sSheet, "Cancel", "deselect");
                    string idleAnim = FindAnim(sSheet, "IdleLeft", "Idle", "idle");
                    bool hasDual = SheetHasAnim(sSheet, "IdleLeft");
                    if (deselAnim != null)
                    {
                        sa.Play(sSheet, deselAnim, false);
                        sa.OnFinish = () =>
                        {
                            if (idleAnim != null) sa.Play(sSheet, idleAnim, !hasDual);
                            _gfDanceLeft = true;
                            sa.OnFinish = null;
                        };
                    }
                    else if (idleAnim != null)
                    {
                        sa.Play(sSheet, idleAnim, !hasDual);
                        _gfDanceLeft = true;
                    }
                }
            }
            return;
        }

        if (!_allowInput) return;

        // Cursor navigation (original: wraps -1 to 1)
        if (Input.UpPressed)
        {
            _cursorY--;
            Audio.PlaySound("menus/character_select/scroll");
            _selectorDenied = false;
            _holdTmrUp = 0;
        }
        if (Input.DownPressed)
        {
            _cursorY++;
            Audio.PlaySound("menus/character_select/scroll");
            _selectorDenied = false;
            _holdTmrDown = 0;
        }
        if (Input.LeftPressed)
        {
            _cursorX--;
            Audio.PlaySound("menus/character_select/scroll");
            _selectorDenied = false;
            _holdTmrLeft = 0;
        }
        if (Input.RightPressed)
        {
            _cursorX++;
            Audio.PlaySound("menus/character_select/scroll");
            _selectorDenied = false;
            _holdTmrRight = 0;
        }

        // Hold-repeat cursor spam (original: holdTmr per direction, spam after 0.25s, reset on repeat)
        if (Input.UpHeld) _holdTmrUp += dt; else _holdTmrUp = 0;
        if (Input.DownHeld) _holdTmrDown += dt; else _holdTmrDown = 0;
        if (Input.LeftHeld) _holdTmrLeft += dt; else _holdTmrLeft = 0;
        if (Input.RightHeld) _holdTmrRight += dt; else _holdTmrRight = 0;

        if (_holdTmrUp >= HOLD_DELAY)
        { _cursorY--; _holdTmrUp = 0; Audio.PlaySound("menus/character_select/scroll"); _selectorDenied = false; }
        if (_holdTmrDown >= HOLD_DELAY)
        { _cursorY++; _holdTmrDown = 0; Audio.PlaySound("menus/character_select/scroll"); _selectorDenied = false; }
        if (_holdTmrLeft >= HOLD_DELAY)
        { _cursorX--; _holdTmrLeft = 0; Audio.PlaySound("menus/character_select/scroll"); _selectorDenied = false; }
        if (_holdTmrRight >= HOLD_DELAY)
        { _cursorX++; _holdTmrRight = 0; Audio.PlaySound("menus/character_select/scroll"); _selectorDenied = false; }

        // Wrap cursor (original: FlxMath.wrap)
        _cursorX = Wrap(_cursorX, -1, 1);
        _cursorY = Wrap(_cursorY, -1, 1);

        // Determine current character at cursor
        int selected = GetCurrentSelected();
        if (_availableChars.TryGetValue(selected, out var charId))
        {
            _onLockedSlot = false;
            if (charId != _curChar)
            {
                _curChar = charId;
                Audio.PlaySound("CS_select");
                SwitchCharacterSprites(_curChar);
                // Play icon animation once on character switch
                if (_iconSprites.TryGetValue(charId, out var selIcon) &&
                    _iconSheets.TryGetValue(charId, out var selSheet))
                {
                    var iconAnim = FindAnim(selSheet, "BF ICON", "PICO ICON", "idle", "icon");
                    if (iconAnim != null) selIcon.PlayAnimation(iconAnim, true, false);
                }
            }

            // Confirm selection
            if (Input.ConfirmPressed)
            {
                Audio.PlaySound("CS_confirm");
                _pressedSelect = true;
                _selectorConfirmed = true;
                _selectTimer = 1.5f;
                if (_confirmSelectorSprite != null)
                {
                    var ca = FindAnim(_confirmSelectorSheet, "cursor ACCEPTED", "idle");
                    if (ca != null) _confirmSelectorSprite.PlayAnimation(ca, true, false);
                }

                // Play confirm animation (non-looping — stays on last frame like original)
                if (_playerAnims.TryGetValue(_curChar, out var pa) &&
                    _playerSheets.TryGetValue(_curChar, out var pSheet))
                {
                    pa.OnFinish = null;
                    string confirmAnim = FindAnim(pSheet, "Confirm", "select", "confirm");
                    if (confirmAnim != null) pa.Play(pSheet, confirmAnim, false);
                }
                if (_spectatorAnims.TryGetValue(_curChar, out var sa) &&
                    _spectatorSheets.TryGetValue(_curChar, out var sSheet))
                {
                    sa.OnFinish = null;
                    string confirmAnim = FindAnim(sSheet, "Confirm", "confirm");
                    if (confirmAnim != null) sa.Play(sSheet, confirmAnim, false);
                }
            }
        }
        else
        {
            _onLockedSlot = true;

            // Locked slot - deny
            if (Input.ConfirmPressed)
            {
                Audio.PlaySound("CS_locked");
                _selectorDenied = true;
                _selectorDeniedTimer = 0.5f; // Fallback for when denied sprite is unavailable
                if (_deniedSelectorSprite != null)
                {
                    var da = FindAnim(_deniedSelectorSheet, "cursor DENIED", "idle");
                    if (da != null)
                    {
                        _deniedSelectorSprite.PlayAnimation(da, true, false);
                        // Original: cursorDenied hides via finishCallback, not a fixed timer
                        _deniedSelectorSprite.OnFinish = () =>
                        {
                            _selectorDenied = false;
                            _deniedSelectorSprite.OnFinish = null;
                        };
                    }
                }
            }
        }

        // Back to freeplay
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            GoToFreeplay();
        }
    }

    private void SwitchCharacterSprites(string charId)
    {
        // Play slide-in on player at 48fps (2x speed) for snappy character switching.
        // Original runs at 24fps which feels sluggish with our cursor speed.
        if (_playerAnims.TryGetValue(charId, out var pa) &&
            _playerSheets.TryGetValue(charId, out var pSheet))
        {
            string enterAnim = FindAnim(pSheet, "Enter", "slidein");
            string idleAnim = FindAnim(pSheet, "Idle", "cs idle", "idle");
            if (enterAnim != null)
            {
                pa.Play(pSheet, enterAnim, false, frameRate: 48f);
                pa.OnFinish = () =>
                {
                    if (idleAnim != null) pa.Play(pSheet, idleAnim, true);
                    pa.OnFinish = null;
                };
            }
            else if (idleAnim != null)
            {
                pa.Play(pSheet, idleAnim, true);
            }
        }
        // Play spectator enter at 48fps too for matching speed
        if (_spectatorAnims.TryGetValue(charId, out var sa) &&
            _spectatorSheets.TryGetValue(charId, out var sSheet))
        {
            string enterAnim = FindAnim(sSheet, "Enter", "slidein");
            string idleAnim = FindAnim(sSheet, "IdleLeft", "Idle", "idle");
            bool hasDual = SheetHasAnim(sSheet, "IdleLeft");
            if (enterAnim != null)
            {
                sa.Play(sSheet, enterAnim, false, frameRate: 48f);
                sa.OnFinish = () =>
                {
                    if (idleAnim != null) sa.Play(sSheet, idleAnim, !hasDual);
                    _gfDanceLeft = true;
                    sa.OnFinish = null;
                };
            }
            else if (idleAnim != null)
            {
                sa.Play(sSheet, idleAnim, !hasDual);
            }
            _gfDanceLeft = true;
        }
    }

    private void GoToFreeplay()
    {
        _exiting = true;
        _exitSlideTimer = 0f;
        _allowInput = false;
        _pressedSelect = false;
        _autoFollow = false;
        _exitCameraStartY = _cameraY;
        Audio.FadeOutMusic(0.8f);

        // Play exit animations on characters (original: boyfriend.anim.play("exit"), gfChill.anim.play("exit"))
        if (_playerAnims.TryGetValue(_curChar, out var pa) &&
            _playerSheets.TryGetValue(_curChar, out var pSheet))
        {
            pa.OnFinish = null;
            string exitAnim = FindAnim(pSheet, "Exit", "exit");
            if (exitAnim != null) pa.Play(pSheet, exitAnim, false);
        }
        if (_spectatorAnims.TryGetValue(_curChar, out var sa) &&
            _spectatorSheets.TryGetValue(_curChar, out var sSheet))
        {
            sa.OnFinish = null;
            string exitAnim = FindAnim(sSheet, "Exit", "exit");
            if (exitAnim != null) sa.Play(sSheet, exitAnim, false);
        }
    }

    /// <summary>
    /// Convert composite RenderTarget2D textures to regular Texture2D via GetData/SetData.
    /// MonoGame DesktopGL (OpenGL) applies Y-flip compensation when drawing from RenderTarget2D
    /// with source rectangles, which can cause incorrect UV mapping for grid-atlas composites.
    /// Regular Texture2D has no such compensation, giving pixel-accurate rendering.
    /// </summary>
    private void ConvertCompositesToRegularTextures(SpriteSheet sheet)
    {
        if (sheet == null) return;
        var converted = new Dictionary<RenderTarget2D, Texture2D>();
        foreach (var animKey in sheet.CompositeAnimations.Keys.ToList())
        {
            var frames = sheet.CompositeAnimations[animKey];
            if (frames.Count == 0) continue;

            var newFrames = new List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>(frames.Count);
            foreach (var (tex, rect, origin) in frames)
            {
                if (tex is RenderTarget2D rt)
                {
                    if (!converted.TryGetValue(rt, out var regularTex))
                    {
                        var pixels = new Color[rt.Width * rt.Height];
                        rt.GetData(pixels);
                        regularTex = new Texture2D(Game.GraphicsDevice, rt.Width, rt.Height);
                        regularTex.SetData(pixels);
                        converted[rt] = regularTex;
                    }
                    newFrames.Add((regularTex, rect, origin));
                }
                else
                {
                    newFrames.Add((tex, rect, origin));
                }
            }
            sheet.CompositeAnimations[animKey] = newFrames;
        }
        foreach (var rt in converted.Keys) rt.Dispose();
    }

    private static int Wrap(int value, int min, int max)
    {
        int range = max - min + 1;
        value = ((value - min) % range + range) % range + min;
        return value;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_loading)
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black);
            var font = Assets.GetFont(20);
            if (font != null)
            {
                var sz = font.MeasureString(_loadStatus);
                font.DrawText(spriteBatch, _loadStatus,
                    new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, (FNFGame.SCREEN_HEIGHT - sz.Y) / 2), Color.White);
            }
            spriteBatch.End();
            return;
        }

        int W = FNFGame.SCREEN_WIDTH;
        int H = FNFGame.SCREEN_HEIGHT;
        float cx = _cameraX;
        float cy = _cameraY;
        // Original FNF CharSelectSubState uses default camera zoom (1.0) — no zoom applied.
        // HaxeFlixel camera scroll: camFollow - screenCenter = (cx, cy).
        // Each sprite's draw position = spritePos - scroll * scrollFactor.
        float scrollX = cx;
        float scrollY = cy;
        _zoomMatrix = Matrix.Identity;

        // === AlphaBlend batch 1: background through curtains ===
        // Original add() order: bg, crowd, stage, curtains, bar(MULTIPLY), charLight, GF, BF, speakers, fgBlur(MULTIPLY)
        // AlphaBlend required: composite RTs (crowd) have premultiplied alpha (_rtCompositeBlend).
        // Static textures (bg, curtains) are premultiplied on load to match.
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: _zoomMatrix);

        // 0. Dark base fill (original: camera bgColor is black)
        spriteBatch.Draw(Assets.Pixel, new Rectangle(-500, -500, W + 1000, H + 1000), Color.Black);

        // 1. Background (original: position (-153,-140), scrollFactor 0.1)
        if (_bgTex != null)
        {
            spriteBatch.Draw(_bgTex, new Vector2(-153 - scrollX * 0.1f, -140 - scrollY * 0.1f), Color.White);
        }

        // 2. Crowd (original: position (0,0), scrollFactor 0.3, applyStageMatrix)
        {
            var co = _crowdSheet?.StageOffset ?? Vector2.Zero;
            DrawAnimatedSprite(spriteBatch, _crowdSprite, co.X - scrollX * 0.3f, co.Y - scrollY * 0.3f, 1f);
        }

        // 3. Stage (original: position (-2, 1), scrollFactor 1, applyStageMatrix)
        {
            var so = _stageSheet?.StageOffset ?? Vector2.Zero;
            DrawAnimatedSprite(spriteBatch, _stageSprite, -2f + so.X - scrollX, 1f + so.Y - scrollY, 1f);
        }

        // 4. Curtains (original: position (-212,-99), scrollFactor 1.4)
        if (_curtainsTex != null)
        {
            spriteBatch.Draw(_curtainsTex, new Vector2(-212 - scrollX * 1.4f, -99 - scrollY * 1.4f), Color.White);
        }

        spriteBatch.End();

        // === MULTIPLY blend batch 1: bar (original: between curtains and charLight) ===
        // Bar darkens stage/bg but NOT characters/speakers (matching original add order).
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: _multiplyBlend, transformMatrix: _zoomMatrix);

        // 5. Bar (original: (0,0), blend MULTIPLY, scale.x=2.5, scrollFactor 0, applyStageMatrix)
        if (_barSprite != null)
        {
            var bo = _barSheet?.StageOffset ?? Vector2.Zero;
            _barSprite.Position = new Vector2(bo.X, _barSlideY + bo.Y);
            _barSprite.Scale = new Vector2(2.5f, 1f);
            _barSprite.Draw(spriteBatch);
        }

        spriteBatch.End();

        // === Additive batch: charLight (original: blend = BlendMode.ADD) ===
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: _zoomMatrix);

        // 6. Character lights (original: (800, 250) and (180, 240), scrollFactor 1, blend ADD)
        if (_charLightTex != null)
        {
            spriteBatch.Draw(_charLightTex, new Vector2(800f - scrollX, 250f - scrollY), Color.White);
            spriteBatch.Draw(_charLightTex, new Vector2(180f - scrollX, 240f - scrollY), Color.White);
        }

        spriteBatch.End();

        // === Character composites (per-part Matrix rendering — each part gets its own Begin/End) ===
        // Bypasses broken pre-rendering pipeline, uses full affine transforms for pixel-perfect assembly.

        // 7. Spectator GF/Nene (original: position (0,0), scrollFactor 1)
        if (!_onLockedSlot)
        {
            var specOff = _spectatorOffsets.GetValueOrDefault(_curChar, Vector2.Zero);
            DrawCharacterComposite(spriteBatch, _spectatorAnims, _spectatorSheets, _curChar,
                specOff.X - scrollX, specOff.Y - scrollY);
        }

        // 8. Player BF/Pico (original: position (0,0), scrollFactor 1)
        if (!_onLockedSlot)
        {
            var playerOff = _playerOffsets.GetValueOrDefault(_curChar, Vector2.Zero);
            DrawCharacterComposite(spriteBatch, _playerAnims, _playerSheets, _curChar,
                playerOff.X - scrollX, playerOff.Y - scrollY);
        }
        else
        {
            // Locked slot: draw a '?' placeholder where the player character would be
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _zoomMatrix);
            var qFont = Assets.GetFont(72);
            if (qFont != null)
            {
                var qSz = qFont.MeasureString("?");
                float qx = 820f - scrollX - qSz.X / 2f;
                float qy = 350f - scrollY - qSz.Y / 2f;
                qFont.DrawText(spriteBatch, "?", new Vector2(qx + 3, qy + 3), Color.Black * 0.5f);
                qFont.DrawText(spriteBatch, "?", new Vector2(qx, qy), Color.White * 0.7f);
            }
            spriteBatch.End();
        }

        // === AlphaBlend batch 2b: speakers ===
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: _zoomMatrix);

        // 9. Speakers (original: (-10, 0), scrollFactor 1.8, scale 1.05, applyStageMatrix)
        {
            var spo = _speakersSheet?.StageOffset ?? Vector2.Zero;
            DrawAnimatedSprite(spriteBatch, _speakersSprite, -10f + spo.X - scrollX * 1.8f, spo.Y - scrollY * 1.8f, 1.05f);
        }

        spriteBatch.End();

        // === MULTIPLY blend batch 2: foreground blur (original: after speakers, darkens everything) ===
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: _multiplyBlend, transformMatrix: _zoomMatrix);

        // 10. Foreground blur (original: (-125, 170), blend MULTIPLY, scrollFactor 1, NO intro tween)
        if (_fgBlurTex != null)
            spriteBatch.Draw(_fgBlurTex, new Vector2(-125f - scrollX, 170f - scrollY), Color.White);

        spriteBatch.End();

        // === HUD Normal batch: panel background (camHUD, no zoom) ===
        // Original draw order: chooseDark → chooseDipshit → glow → icons → cursor → trails → nametag
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // 12. chooseDipshit platform (original: 426, -13, scrollFactor 0)
        if (_chooseDipshitTex != null)
            spriteBatch.Draw(_chooseDipshitTex, new Vector2(426, -13 + _platformSlideY), Color.White);

        spriteBatch.End();

        // === HUD Additive batch: dipshit glow effects (camHUD, no zoom) ===
        // Drawn AFTER panel so glow appears on top of the dark background
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive);

        // 13. Dipshit blur glow (original: 419, -65, blend ADD, scrollFactor 0)
        DrawAnimatedSprite(spriteBatch, _chooseBlurSprite, 419, -65 + _blurSlideY, 1f);

        // 14. Dipshit backing glow (original: 423, -17, blend ADD, scrollFactor 0)
        DrawAnimatedSprite(spriteBatch, _chooseBackingSprite, 423, -17 + _backingSlideY, 1f);

        spriteBatch.End();

        // === HUD Normal batch: icons (camHUD, no zoom) ===
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // 15. 3×3 Icon grid
        DrawIconGrid(spriteBatch);

        spriteBatch.End();

        // === HUD Additive batch: cursor trails (original: SCREEN blend, Additive approximation) ===
        DrawSelectorTrails(spriteBatch);

        // === HUD Normal batch: cursor, nametag, fade (camHUD, no zoom) ===
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // 16. Selector main cursor (on top of icons, matching original add order)
        DrawSelectorMain(spriteBatch);

        // 17. Nametag (original: midpoint 1008,100, scale 0.77, scrollFactor 0 — drawn last in HUD)
        DrawNametag(spriteBatch);

        // Fade overlay
        if (_fadeAlpha > 0)
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, W, H), Color.Black * _fadeAlpha);

        spriteBatch.End();

        }

    private void DrawAnimatedSprite(SpriteBatch sb, AnimatedSprite sprite, float x, float y, float scale)
    {
        if (sprite == null) return;
        sprite.Position = new Vector2(x, y);
        sprite.Scale = new Vector2(scale, scale);
        sprite.Draw(sb);
    }

    /// <summary>
    /// Draw a character using per-part Matrix transforms from RawCompositeData.
    /// Self-contained: reads animation state from CharAnim, NOT AnimatedSprite.
    /// Each body part gets its own SpriteBatch Begin/End with a full affine transform matrix,
    /// identical to CompositeDebugScene's proven MATRIX mode rendering.
    /// Must be called OUTSIDE of an active SpriteBatch batch.
    /// Uses RasterizerState.CullNone because flipped characters (a&lt;0) produce matrices with
    /// negative determinant that reverse triangle winding, which default culling would hide.
    /// </summary>
    private void DrawCharacterComposite(SpriteBatch sb,
        Dictionary<string, CharAnim> anims, Dictionary<string, SpriteSheet> sheets,
        string charId, float offsetX, float offsetY)
    {
        if (!anims.TryGetValue(charId, out var anim) || anim == null) return;
        if (string.IsNullOrEmpty(anim.CurrentAnim)) return;
        if (!sheets.TryGetValue(charId, out var sheet) || sheet?.Texture == null) return;
        if (!sheet.RawCompositeData.TryGetValue(anim.CurrentAnim, out var rawFrames) || rawFrames.Count == 0) return;

        var atlas = sheet.Texture;
        int idx = anim.FrameIndex % rawFrames.Count;
        var parts = rawFrames[idx];

        foreach (var (frame, a, b, c, d, tx, ty) in parts)
        {
            float adjTx = tx + offsetX;
            float adjTy = ty + offsetY;

            Matrix transformMatrix;
            if (frame.Rotated)
            {
                float sw = frame.SourceRect.Width;
                transformMatrix = new Matrix(
                    -c, -d, 0, 0,
                     a,  b, 0, 0,
                     0,  0, 1, 0,
                    c * sw + adjTx, d * sw + adjTy, 0, 1
                );
            }
            else
            {
                transformMatrix = new Matrix(
                    a, b, 0, 0,
                    c, d, 0, 0,
                    0, 0, 1, 0,
                    adjTx, adjTy, 0, 1
                );
            }

            // CullNone is critical: flipped characters (a<0, e.g. Pico) produce matrices with
            // negative determinant, reversing triangle winding. Default CullCounterClockwise
            // culls these reversed triangles, making the character invisible.
            sb.Begin(SpriteSortMode.Deferred, _compositeBlend, rasterizerState: RasterizerState.CullNone, transformMatrix: transformMatrix);
            sb.Draw(atlas, Vector2.Zero, frame.SourceRect, Color.White);
            sb.End();
        }
    }

    private void DrawIconGrid(SpriteBatch sb)
    {
        float halfCellX = GRID_X_SPREAD / 2f;
        float halfCellY = GRID_Y_SPREAD / 2f;

        for (int i = 0; i < 9; i++)
        {
            int gx = i % 3;
            int gy = i / 3;

            // Original: member.x = posX * grpXSpread + grpIcons.x
            float iconX = GRID_X + gx * GRID_X_SPREAD;
            float iconY = GRID_Y + gy * GRID_Y_SPREAD + _iconSlideY;

            // Center of grid cell
            float centerX = iconX + halfCellX;
            float centerY = iconY + halfCellY;

            if (_availableChars.TryGetValue(i, out var charId))
            {
                // Draw character icon at native size (original FNF: no scale normalization).
                // BF icon is 104×88, Pico is 98×76 — fits naturally in 107×127 grid cells.
                if (_iconSprites.TryGetValue(charId, out var iconSpr) && iconSpr.Sheet != null)
                {
                    // Update icon sprite only when animating (non-finished)
                    if (!iconSpr.Finished)
                        iconSpr.Update(1f / 60f);

                    var frame = iconSpr.GetCurrentFrame();
                    float fw = (frame?.FrameWidth > 0 ? frame.FrameWidth : frame?.SourceRect.Width) ?? 86;
                    float fh = (frame?.FrameHeight > 0 ? frame.FrameHeight : frame?.SourceRect.Height) ?? 86;
                    bool isSelected = (i == GetCurrentSelected());
                    float iconScale = isSelected ? 1.3f : 1.0f;
                    iconSpr.Position = new Vector2(centerX - fw * iconScale / 2f, centerY - fh * iconScale / 2f);
                    iconSpr.Scale = new Vector2(iconScale, iconScale);
                    iconSpr.Draw(sb);
                }
                else
                {
                    Color c = charId == "bf" ? new Color(49, 162, 253) : new Color(190, 80, 60);
                    int fallback = 80;
                    sb.Draw(Assets.Pixel, new Rectangle((int)(centerX - fallback / 2f), (int)(centerY - fallback / 2f), fallback, fallback), c);
                }
            }
            else
            {
                // Locked slot — draw lock icon at native size with no tint (original: all green padlocks).
                bool isLockSelected = (i == GetCurrentSelected());
                float selMult = isLockSelected ? 1.3f : 1.0f;
                if (_lockedIconTex != null)
                {
                    float drawW = _lockedIconTex.Width * selMult;
                    float drawH = _lockedIconTex.Height * selMult;
                    sb.Draw(_lockedIconTex, new Vector2(centerX - drawW / 2f, centerY - drawH / 2f),
                        null, Color.White, 0f, Vector2.Zero, new Vector2(selMult, selMult), SpriteEffects.None, 0f);
                }
                else
                {
                    float fallbackSize = 70f * selMult;
                    sb.Draw(Assets.Pixel, new Rectangle(
                        (int)(centerX - fallbackSize / 2f), (int)(centerY - fallbackSize / 2f),
                        (int)fallbackSize, (int)fallbackSize), new Color(100, 200, 100) * 0.5f);
                }
            }
        }
    }

    private void DrawSelectorTrails(SpriteBatch spriteBatch)
    {
        if (_selectorTex == null || _selectorConfirmed) return;

        // Original cursor has scrollFactor(0,0) on camHUD — follows icon group position during intro/exit
        Vector2 lightPos = _cursorLightBlue;
        Vector2 darkPos = _cursorDarkBlue;

        // Additive blend approximates SCREEN for cursor trails on dark backgrounds
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive);
        // Dark blue trail (original: #3C74F7, SCREEN blend, decay 0.404)
        spriteBatch.Draw(_selectorTex, darkPos, new Color(60, 116, 247) * (0.6f * _cursorAlpha));
        // Light blue trail (original: #3EBBFF, SCREEN blend, decay 0.202)
        spriteBatch.Draw(_selectorTex, lightPos, new Color(62, 187, 255) * (0.7f * _cursorAlpha));
        spriteBatch.End();
    }

    private void DrawSelectorMain(SpriteBatch sb)
    {
        // Original cursor on camHUD — follows icon group position during intro/exit
        Vector2 mainPos = _cursorMain;

        if (_selectorConfirmed)
        {
            // Confirmed: animated confirm cursor (original: main.x-2, main.y-4)
            if (_confirmSelectorSprite != null)
            {
                _confirmSelectorSprite.Position = new Vector2(mainPos.X - 2, mainPos.Y - 4);
                _confirmSelectorSprite.Scale = Vector2.One;
                _confirmSelectorSprite.Tint = Color.White * _cursorAlpha;
                _confirmSelectorSprite.Draw(sb);
            }
            else if (_selectorTex != null)
            {
                sb.Draw(_selectorTex, mainPos, new Color(80, 255, 80) * _cursorAlpha);
            }
        }
        else if (_selectorDenied)
        {
            // Denied: animated denied cursor (original: main.x-2, main.y-4)
            if (_deniedSelectorSprite != null)
            {
                _deniedSelectorSprite.Position = new Vector2(mainPos.X - 2, mainPos.Y - 4);
                _deniedSelectorSprite.Scale = Vector2.One;
                _deniedSelectorSprite.Tint = Color.White * _cursorAlpha;
                _deniedSelectorSprite.Draw(sb);
            }
            else if (_selectorTex != null)
            {
                // Fallback: red-tinted cursor when denied sprite is unavailable
                sb.Draw(_selectorTex, mainPos, new Color(255, 60, 60) * _cursorAlpha);
            }
        }
        else
        {
            // Main cursor: yellow tint (original: FlxColor.interpolate(0xFFFF00, 0xFFCC00, pingPong(frame,8)/8))
            int pp = _animFrame % 16;
            float pulse = (pp < 8 ? pp : 16 - pp) / 8f;
            var cursorColor = Color.Lerp(new Color(255, 255, 0), new Color(255, 204, 0), pulse) * _cursorAlpha;
            if (_selectorTex != null)
                sb.Draw(_selectorTex, mainPos, cursorColor);
            else
            {
                int bw = 3, rw = 124, rh = 112;
                sb.Draw(Assets.Pixel, new Rectangle((int)mainPos.X, (int)mainPos.Y, rw, bw), cursorColor);
                sb.Draw(Assets.Pixel, new Rectangle((int)mainPos.X, (int)(mainPos.Y + rh - bw), rw, bw), cursorColor);
                sb.Draw(Assets.Pixel, new Rectangle((int)mainPos.X, (int)mainPos.Y, bw, rh), cursorColor);
                sb.Draw(Assets.Pixel, new Rectangle((int)(mainPos.X + rw - bw), (int)mainPos.Y, bw, rh), cursorColor);
            }
        }
    }

    private void DrawNametag(SpriteBatch sb)
    {
        // Show locked nametag based on grid position (original: nametag updates per-slot, not per-curChar)
        int sel = GetCurrentSelected();
        string displayChar = _availableChars.ContainsKey(sel) ? _curChar : "locked";

        // Detect character switch and trigger slide-in animation
        // Original: nametag.x += nametag.width, alpha=0, tween x back + alpha 0→1, 0.4s expoOut
        if (displayChar != _nametagLastChar && _nametagLastChar != "")
            _nametagSwitchTimer = 0.4f;
        _nametagLastChar = displayChar;

        // Slide-in from right + alpha fade on character switch (original: 0.4s expoOut)
        float switchEase = 0f; // 1 at start (full offset right), 0 at end
        float switchAlpha = 1f;
        if (_nametagSwitchTimer > 0)
        {
            float t = 1f - _nametagSwitchTimer / 0.4f;
            switchEase = 1f - ExpoOut(t);
            switchAlpha = ExpoOut(t);
        }

        if (_titleTextures.TryGetValue(displayChar, out var titleTex))
        {
            // Original Nametag: midpointX=1008, midpointY=100, scale=0.77
            // Flixel centerOrigin + updatePosition: visual center stays at (midpointX, midpointY)
            float scale = 0.77f;
            float switchOffsetX = titleTex.Width * scale * switchEase;
            Vector2 origin = new Vector2(titleTex.Width / 2f, titleTex.Height / 2f);
            Vector2 position = new Vector2(1008f + switchOffsetX, 100f + _nametagSlideY);
            sb.Draw(titleTex, position, null, Color.White * switchAlpha, 0f, origin, scale, SpriteEffects.None, 0f);
        }
        else
        {
            // Fallback text at nametag position
            var font = Assets.GetFont(24);
            if (font != null)
            {
                string name = displayChar == "locked" ? "???" :
                    System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayChar);
                var sz = font.MeasureString(name);
                float switchOffsetX = sz.X * switchEase;
                float nx = 1008f - sz.X / 2f + switchOffsetX;
                float ny = 100f - sz.Y / 2f + _nametagSlideY;
                font.DrawText(sb, name, new Vector2(nx, ny), Color.White * switchAlpha);
            }
        }
    }

    /// <summary>
    /// Smooth interpolation matching original FNF's smoothLerpPrecision.
    /// target + (current - target) * exp(-dt / duration)
    /// </summary>
    private static float SmoothLerp(float current, float target, float dt, float duration)
    {
        return target + (current - target) * MathF.Exp(-dt / duration);
    }

    /// <summary>
    /// Exponential ease-out (original: FlxEase.expoOut).
    /// </summary>
    private static float ExpoOut(float t)
    {
        return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
    }

    /// <summary>
    /// Back ease-in (original: FlxEase.backIn). Overshoots slightly before moving.
    /// </summary>
    private static float BackIn(float t)
    {
        const float s = 1.70158f;
        return t * t * ((s + 1f) * t - s);
    }

    /// <summary>
    /// Self-contained animation state for character composites.
    /// Bypasses AnimatedSprite/SpriteSheet rendering pipeline entirely.
    /// Reads frame count from SpriteSheet.RawCompositeData; rendering reads parts directly.
    /// </summary>
    private sealed class CharAnim
    {
        public string CurrentAnim { get; private set; }
        public int FrameIndex { get; private set; }
        public int FrameCount { get; private set; }
        public bool Finished { get; private set; }
        public Action OnFinish { get; set; }

        private float _timer;
        private bool _looping;
        private float _frameRate = 24f;

        /// <summary>
        /// Play an animation. frameRate overrides the default 24fps (use higher values
        /// for Enter/Exit to make character switching feel snappier).
        /// </summary>
        public void Play(SpriteSheet sheet, string animName, bool loop, bool force = true, float frameRate = 24f)
        {
            if (!force && CurrentAnim == animName) return;
            if (sheet?.RawCompositeData == null ||
                !sheet.RawCompositeData.TryGetValue(animName, out var frames) ||
                frames.Count == 0)
                return;
            CurrentAnim = animName;
            FrameCount = frames.Count;
            FrameIndex = 0;
            _timer = 0;
            _looping = loop;
            _frameRate = Math.Max(1f, frameRate);
            Finished = false;
        }

        public void Update(float dt)
        {
            if (Finished || FrameCount <= 0) return;
            _timer += dt;
            float dur = 1f / _frameRate;
            while (_timer >= dur)
            {
                _timer -= dur;
                FrameIndex++;
                if (FrameIndex >= FrameCount)
                {
                    if (_looping)
                    {
                        FrameIndex = 0;
                    }
                    else
                    {
                        FrameIndex = FrameCount - 1;
                        Finished = true;
                        _timer = 0;
                        OnFinish?.Invoke();
                        break;
                    }
                }
            }
        }
    }
}
