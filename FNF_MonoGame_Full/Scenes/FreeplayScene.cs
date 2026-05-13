using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;
using FNF_MonoGame.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Modern FNF FreeplayState — DJ BF on left, song capsules on right,
/// difficulty stamps, pixel icons, score display, album art backing.
/// Matches the modern FNF engine freeplay design.
/// </summary>
public class FreeplayScene : Scene
{
    // ── Song data ──
    private List<FreeplaySong> _allSongs = new(); // master list (unfiltered)
    private List<FreeplaySong> _songs = new(); // active/filtered list used for display
    private int _selectedIndex;
    private float _scrollLerp;
    private int _difficultyIndex = 1; // 0=easy, 1=normal, 2=hard, 3=erect, 4=nightmare
    private static readonly string[] Difficulties = { "easy", "normal", "hard", "erect", "nightmare" };

    // ── State ──
    private enum Phase { Intro, Idle, Confirmed }
    private Phase _phase = Phase.Intro;
    private float _introTimer;
    private float _confirmTimer;
    private float _songPreviewDelay;
    private bool _songPreviewPlaying;
    private int _lastPreviewIndex = -1;
    private int _displayedScore;
    private float _lerpScore;
    private float _intendedCompletion;
    private float _lerpCompletion;

    // ── Highscore animation timer (original: plays once every 12-60s) ──
    private float _highscoreNextPlay;
    private static readonly Random _rng = new();

    // ── Persist selection across visits ──
    private static int _lastSelectedIndex;
    private static int _lastDifficultyIndex = 1;

    // ── Visual assets ──
    private Texture2D _pinkBack;
    private Texture2D _bgImage;
    private Texture2D _bgAngleMask;
    private Texture2D _diffEasy, _diffNormal, _diffHard, _diffErect, _diffNightmare;
    private SpriteSheet _diffNightmareSheet;
    private List<SpriteFrame> _diffNightmareFrames;
    private Texture2D _clearBox;
    private SpriteSheet _capsuleSheet;
    private List<SpriteFrame> _capsuleSelected;
    private List<SpriteFrame> _capsuleUnselected;
    private SpriteSheet _highscoreSheet;
    private AnimatedSprite _highscoreSprite;

    // ── Difficulty selector arrows ──
    private SpriteSheet _selectorSheet;
    private AnimatedSprite _selectorLeft;
    private AnimatedSprite _selectorRight;

    // ── Confirm glow effects ──
    private Texture2D _confirmGlow;
    private Texture2D _confirmGlow2;
    private Texture2D _confirmTextGlow;
    private Texture2D _cardGlow;
    private Texture2D _separator; // difficulty dot base
    private float _confirmGlowAlpha;
    private float _confirmGlow2Alpha;
    private float _confirmTextGlowAlpha;
    private float _cardGlowAlpha;
    private float _cardGlowScale = 1f;
    private float _cardGlowDuration = 0.45f; // original: introDone=0.45s, disappear=0.25s
    private int _confirmGlowPhase; // 0=inactive, 1=glow2 fading in, 2=glow flash+textGlow
    private float _confirmGlowTimer;

    // ── Pink color tween on confirm (original: 0xFFD0D5 → 0x171831 over 0.33s) ──
    private Color _pinkColorFrom;
    private Color _pinkColorTo;
    private float _pinkTweenTimer;
    private float _pinkTweenDuration;
    private bool _pinkTweening;

    // ── BG image dimming on confirm ──
    private float _bgDimTimer;
    private float _bgDimDuration;
    private Color _bgDimFrom;
    private Color _bgDimTo;
    private bool _bgDimming;
    private Color _bgDimColor = Color.White;
    private int _bgDimPhase; // 0=none, 1=first dim, 2=second dim

    // ── Confirm screen fade (original: funnyCam.fade(BLACK, 0.2s) after startDelay) ──
    private bool _confirmFading;
    private float _confirmFadeAlpha;

    // ── Backing text yeah (animated atlas on pink panel) ──
    private SpriteSheet _backingTextSheet;
    private AnimatedSprite _backingTextSprite;

    // ── Dot pulse animation ──
    private SpriteSheet _dotPulseSheet;
    private List<SpriteFrame> _dotPulseFrames;

    // ── Intro/phase state ──
    private bool _introDone; // true after DJ intro finishes
    private float _introRevealTimer;
    private bool _introUiRevealed;
    private Color _pinkColor = new(0xFF, 0xD4, 0xE9); // starts pink, becomes gold
    private float _bgRevealTint; // 0=black, 1=white for BG image color fade
    private bool _orangeBarsVisible; // orange bars hidden until introDone
    private bool _dotsVisible; // dots fade in after introDone

    // ── CharSelectHint alpha pulse (original: hintTimer += elapsed*2, alpha lerp 0.3..0.9) ──
    private float _hintAlphaTimer;

    // ── DJ character ──
    private SpriteSheet _djSheet;
    private AnimatedSprite _djSprite;
    private string _djIdleAnim;
    private string _djIntroAnim;
    private string _djConfirmAnim;

    // ── Pixel icon cache ──
    private Dictionary<string, SpriteSheet> _pixelIconSheets = new(StringComparer.OrdinalIgnoreCase);

    // ── Layout constants (1280x720 native, matching original FNF) ──
    private const float CAPSULE_SCALE = 0.8f;
    private const float CAPSULE_FRAME_W = 612f;
    private const float CAPSULE_FRAME_H = 132f;
    private const float CAPSULE_SPACING = CAPSULE_FRAME_H * CAPSULE_SCALE + 10f;
    private const float SONGS_X_OFFSET = 0f;
    private const float DJ_POS_MULTI = 0.44f;
    private const float SONGS_POS_MULTI = 0.75f;
    private const float DJ_X = 640f;
    private const float DJ_Y = 366f;
    private const float DJ_SCALE = 1f;
    private const float PINK_PANEL_W = 524f;  // pinkBack.png native width — visible on 16:9
    private const float BG_IMAGE_X = PINK_PANEL_W * 0.74f; // backingImage x offset in original
    private const float DIFF_X = 90f;
    private const float DIFF_Y = 80f;

    // In this MonoGame port, all Freeplay UI is already authored in the fixed 1280x720 layout space.
    // Keep cutout offset at 0 so we don't double-apply widescreen shift values from HaxeFlixel.
    private float CutoutWidth => 0f;
    private float DjBaseX => CutoutWidth * DJ_POS_MULTI;
    private float SongsBaseX => CutoutWidth * SONGS_POS_MULTI;

    // ── Intro animation state ──
    private float _pinkSlideX;
    private float _capsuleSlideX;
    private float _overhangY; // overhang slides in from top
    private float _hintSlideY; // charSelectHint slides in from bottom
    private float _hintSlideTweenTimer;
    private float _overhangTweenTimer;
    private float _diffSlideX; // difficulty sprite slides in from left
    private float _diffIntroTweenTimer;
    private bool _capsuleFlicker; // text flickers on confirm
    private float _flickerTimer;
    private float _capsuleBounceTimer; // staggered bounce-in elapsed time
    private const float CAPSULE_BOUNCE_DURATION = 0.6f;

    // ── Score digit atlas ──
    private SpriteSheet _digitSheet;
    private static readonly string[] DigitNames = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" };

    // ── Capsule detail assets ──
    private SpriteSheet _bigNumbersSheet;
    private SpriteSheet _smallNumbersSheet;
    private SpriteSheet _weekTypesSheet;
    private SpriteSheet _newTextSheet;
    private List<SpriteFrame> _newTextFrames;
    private Texture2D _bpmText;
    private Texture2D _difficultyText;

    // ── Letter sort ──
    private Texture2D _miniArrow;
    private SpriteSheet _sortedLettersSheet;
    private AnimatedSprite _sortedLettersSprite;
    private int _letterSortIndex = 2; // 0=fav, 1=#, 2=ALL, 3+=A-Z groups
    private static readonly string[] LetterSortLabels = { "#", "fav", "ALL", "A-B", "C-D", "E-H", "I-L", "M-N", "O-R", "S", "T", "U-Z" };

    // LetterSort "wiggle" animation (FNF_Official: doLetterChangeAnims)
    private float _letterWiggleTimer;
    private int _letterWiggleDir;

    // ── Album roll ──
    private SpriteSheet _albumRollSheet;
    private AnimatedSprite _albumRollSprite;
    private Texture2D _albumArtTexture;
    private SpriteSheet _albumTitleSheet; // volume1-text etc.
    private AnimatedSprite _albumTitleSprite;
    private SpriteSheet _freeplayStarsSheet;
    private AnimatedSprite _freeplayStarsSprite;
    private string _currentAlbumId;
    private bool _albumVisible;
    private bool _albumIntroPlayed;
    private int _albumDifficultyRating;
    private float _albumRevealTimer;
    private bool _albumTitleVisible;
    private bool _albumStarsVisible;
    private const float ALBUM_REVEAL_DELAY = 0.75f;

    // ── DJ afk timer ──
    private float _djAfkTimer;
    private string _djAfkAnim;
    private bool _djAfkPlaying;

    // ── Difficulty arrow push (original: scale 0.5, offset.y-=5, reset after 2/24s) ──
    private float _leftArrowPush;
    private float _rightArrowPush;
    private const float ARROW_PUSH_DURATION = 2f / 24f; // ~0.083s

    // ── Song preview fade-in/out (FNF_Official: FADE_IN_DURATION=0.5, FADE_OUT_DURATION=0.25, delay=0.25,
    // start volume=0.25, end volume=1.0, fade-out end volume=0.0) ──
    private float _previewFadeTimer;
    private bool _previewFadingIn;
    private bool _previewFadingOut;
    private float _previewFadeOutTimer;
    private string _pendingPreviewSong;
    private string _activePreviewSong;
    private int _activePreviewStartMs;
    private int _activePreviewEndMs;
    private const float PREVIEW_FADE_IN_DURATION = 0.5f;
    private const float PREVIEW_FADE_OUT_DURATION = 0.25f;
    private const float PREVIEW_START_VOLUME = 0.25f;
    private const float PREVIEW_TARGET_VOLUME = 1.0f;
    private const float PREVIEW_FADE_OUT_END_VOLUME = 0.0f;
    private const float PREVIEW_DELAY = 0.25f;

    // ── Hold/spam navigation (original: first press immediate, 0.9s hold then 0.07s repeat) ──
    private float _spamTimer;
    private bool _spamming;

    // ── Difficulty slide transition (old slides out, new slides in) ──
    private bool _diffTransitioning;
    private float _diffTransTimer;
    private float _diffOldX;
    private float _diffNewX;
    private float _diffOldTarget;
    private float _diffNewTarget;
    private int _diffOldIndex;
    private Texture2D _diffOldTex;
    private const float DIFF_TRANS_DURATION = 0.2f;

    // ── Difficulty alpha flash (original: arrow moveShitDown whiteShader 1/24s) ──
    private float _diffAlphaFlashTimer;

    // ── Difficulty y-offset bounce (original: offset.y -= 5, resets after 1/24s) ──
    private float _diffYBounceTimer;

    // ── Exit animation state (per-element exitMovers matching original goBack) ──
    private bool _exiting;
    private float _exitTimer;
    private const float EXIT_DURATION = 0.5f; // longest element tween = DJ at 0.5s
    private float _exitDjOffset;       // DJ slides left
    private float _exitPinkOffset;     // pink panel slides left
    private float _exitBgOffset;       // BG/overlay slides right
    private float _exitDiffXOffset;    // difficulty + dots slide left (original: x:-300, speed:0.25)
    private float _exitArrowOffset;    // arrows slide left
    private float _exitScoreOffset;    // score area slides right
    private float _exitLetterYOffset;  // letter sort slides up
    private float _exitCapsuleOffset;  // capsules jump-out right
    private float _exitOverhangYOffset; // overhang slides up (original: y:-164, speed:0.2)
    private float _exitAlbumOffset;    // album roll slides right (original: x:width, speed:0.4)

    // ── Music fade-in after exit (original: fadeIn(4.0, 0.0, 1.0)) ──
    private float _menuFadeTimer;
    private bool _menuFading;

    // ── Favorites (persisted via HighscoreManager) ──
    private SpriteSheet _favHeartSheet;
    private List<SpriteFrame> _favHeartFrames;

    // ── Background layers (darkback, yellow bg piece) ──
    private Texture2D _darkBack;
    private Texture2D _yellowBgPiece;

    // ── Pico freeplay backing card layers ──
    private bool _isPicoTheme;
    private Texture2D _picoBlueBar;
    private Texture2D _picoMiddleLoop;
    private Texture2D _picoLowerLoop;
    private Texture2D _picoGlow;
    private SpriteSheet _picoTopLoopSheet;

    private static readonly string[] PicoTopLoopAnims =
    {
        "rocket launcher info",
        "rifle info",
        "sniper info",
        "uzi info",
        "base"
    };

    private static readonly string[] CanonicalPicoSongs =
    {
        "bopeebo", "fresh", "dadbattle", "spookeez", "south",
        "pico", "philly-nice", "blammed", "cocoa", "eggnog",
        "senpai", "roses", "ugh", "guns", "stress"
    };

    // ── Rank badges ──
    private SpriteSheet _rankBadgesSheet;

    public override void Load()
    {
        _isPicoTheme = GetSelectedCharacterVariationId().Equals("pico", StringComparison.OrdinalIgnoreCase);

        LoadSongList();

        _selectedIndex = Math.Clamp(_lastSelectedIndex, 0, Math.Max(0, _songs.Count - 1));
        _difficultyIndex = Math.Clamp(_lastDifficultyIndex, 0, Difficulties.Length - 1);
        _scrollLerp = _selectedIndex;

        // ── Load textures ──
        _pinkBack = Assets.LoadTexture("menus/freeplay/pinkBack.png");
        _bgImage = Assets.LoadTexture(_isPicoTheme
            ? "menus/freeplay/freeplayBGweek1-pico.png"
            : "menus/freeplay/freeplayBGweek1-bf.png");
        _diffEasy = Assets.LoadTexture("menus/freeplay/freeplayeasy.png");
        _diffNormal = Assets.LoadTexture("menus/freeplay/freeplaynormal.png");
        _diffHard = Assets.LoadTexture("menus/freeplay/freeplayhard.png");
        _diffErect = Assets.LoadTexture("menus/freeplay/freeplayerect.png");
        _diffNightmare = Assets.LoadTexture("menus/freeplay/freeplaynightmare.png");
        _diffNightmareSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplaynightmare");
        if (_diffNightmareSheet != null)
            _diffNightmareFrames = _diffNightmareSheet.GetAnimation("idle");
        _clearBox = Assets.LoadTexture("menus/freeplay/clearBox.png");
        _confirmGlow = Assets.LoadTexture("menus/freeplay/confirmGlow.png");
        _confirmGlow2 = Assets.LoadTexture("menus/freeplay/confirmGlow2.png");
        _confirmTextGlow = Assets.LoadTexture("menus/freeplay/glowingText.png");
        _cardGlow = Assets.LoadTexture("menus/freeplay/cardGlow.png");
        _separator = Assets.LoadTexture("menus/freeplay/seperator.png");

        // ── Capsule spritesheet (Sparrow XML) ──
        _capsuleSheet = SpriteSheet.Load(Game, _isPicoTheme
            ? "menus/freeplay/freeplayCapsule/capsule/freeplayCapsule_pico"
            : "menus/freeplay/freeplayCapsule/capsule/freeplayCapsule");
        if (_capsuleSheet != null)
        {
            _capsuleSelected = _capsuleSheet.GetAnimation("mp3 capsule w backing");
            _capsuleUnselected = _capsuleSheet.GetAnimation("mp3 capsule w backing NOT SELECTED");
        }

        // ── Highscore label sprite (original: plays once on random 12-50s timer, then 20-60s) ──
        _highscoreSheet = SpriteSheet.Load(Game, "menus/freeplay/highscore");
        if (_highscoreSheet != null)
        {
            _highscoreSprite = new AnimatedSprite { Sheet = _highscoreSheet };
            _highscoreNextPlay = _rng.Next(12, 51);
        }

        // ── Difficulty selector arrows (freeplaySelector sparrow atlas) ──
        _selectorSheet = SpriteSheet.Load(Game, _isPicoTheme
            ? "menus/freeplay/freeplaySelector/freeplaySelector_pico"
            : "menus/freeplay/freeplaySelector");
        if (_selectorSheet != null)
        {
            _selectorLeft = new AnimatedSprite { Sheet = _selectorSheet };
            _selectorLeft.PlayAnimation("arrow pointer loop", loop: true);
            _selectorRight = new AnimatedSprite { Sheet = _selectorSheet };
            _selectorRight.PlayAnimation("arrow pointer loop", loop: true);
        }

        // ── DJ BF (animateatlas composite) ──
        LoadDJ();

        // ── Backing text yeah (animated atlas on pink panel) ──
        _backingTextSheet = SpriteSheet.Load(Game, "menus/freeplay/backing-text-yeah",
            preRenderComposites: true);
        if (_backingTextSheet != null)
        {
            _backingTextSprite = new AnimatedSprite { Sheet = _backingTextSheet };
        }

        // ── DotPulse sparrow atlas for difficulty dot animation ──
        _dotPulseSheet = SpriteSheet.Load(Game, "menus/freeplay/dotPulse");
        if (_dotPulseSheet != null)
            _dotPulseFrames = _dotPulseSheet.GetAnimation("pulse");

        // ── Score digit atlas (digital numbers) ──
        _digitSheet = SpriteSheet.Load(Game, "resultScreen/score-digital-numbers");

        // ── Capsule detail assets ──
        _bigNumbersSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/bignumbers");
        _smallNumbersSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/smallnumbers");
        _weekTypesSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/weektypes");
        _newTextSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/new");
        if (_newTextSheet != null)
            _newTextFrames = _newTextSheet.GetAnimation("NEW notif");
        _bpmText = Assets.LoadTexture("menus/freeplay/freeplayCapsule/bpmtext.png");
        _difficultyText = Assets.LoadTexture("menus/freeplay/freeplayCapsule/difficultytext.png");

        // ── Letter sort (miniArrow + sortedLetters atlas) ──
        _miniArrow = Assets.LoadTexture("menus/freeplay/miniArrow.png");
        _sortedLettersSheet = SpriteSheet.Load(Game, "menus/freeplay/sortedLetters",
            preRenderComposites: true);
        if (_sortedLettersSheet != null)
        {
            _sortedLettersSprite = new AnimatedSprite { Sheet = _sortedLettersSheet };
        }

        // ── Album roll (animate atlas) ──
        _albumRollSheet = SpriteSheet.Load(Game, "menus/freeplay/albumRoll/freeplayAlbum",
            preRenderComposites: true);
        if (_albumRollSheet != null)
        {
            _albumRollSprite = new AnimatedSprite { Sheet = _albumRollSheet };
        }
        _albumTitleSheet = SpriteSheet.Load(Game, "menus/freeplay/albumRoll/volume1-text");
        if (_albumTitleSheet != null)
        {
            _albumTitleSprite = new AnimatedSprite { Sheet = _albumTitleSheet };
        }
        _albumArtTexture = Assets.LoadTexture("menus/freeplay/albumRoll/volume1.png");
        _freeplayStarsSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayStars",
            preRenderComposites: true);
        if (_freeplayStarsSheet != null)
        {
            _freeplayStarsSprite = new AnimatedSprite { Sheet = _freeplayStarsSheet };
        }

        // ── Fav heart (Sparrow atlas) ──
        _favHeartSheet = SpriteSheet.Load(Game, "menus/freeplay/favHeart");
        if (_favHeartSheet != null)
            _favHeartFrames = _favHeartSheet.GetAnimation("fav heart") ?? _favHeartSheet.GetAnimation("heart");

        // ── Background layers ──
        _darkBack = Assets.LoadTexture("menus/freeplay/darkback.png");
        _yellowBgPiece = Assets.LoadTexture("menus/freeplay/yellow bg piece.png");

        if (_isPicoTheme)
        {
            _picoBlueBar = Assets.LoadTexture("menus/freeplay/backingCards/pico/blueBar.png");
            _picoMiddleLoop = Assets.LoadTexture("menus/freeplay/backingCards/pico/middleLoop.png");
            _picoLowerLoop = Assets.LoadTexture("menus/freeplay/backingCards/pico/lowerLoop.png");
            _picoGlow = Assets.LoadTexture("menus/freeplay/backingCards/pico/glow.png");
            _picoTopLoopSheet = SpriteSheet.Load(Game, "menus/freeplay/backingCards/pico/topLoop");
        }

        // ── Rank badges ──
        _rankBadgesSheet = SpriteSheet.Load(Game, "menus/freeplay/rankbadges");

        // ── Precompute angled mask overlay (runtime-generated) ──
        _bgAngleMask?.Dispose();
        _bgAngleMask = CreateFreeplayAngleMask(Game.GraphicsDevice);

        // ── Insert Random capsule at position 0 (original behavior) ──
        _allSongs.Insert(0, new FreeplaySong
        {
            Name = null,
            DisplayName = "Random",
            OpponentChar = null,
            WeekId = null,
            Difficulties = new[] { "easy", "normal", "hard", "erect", "nightmare" },
            IsRandom = true
        });

        // ── Set filtered view (default letter filter + selected difficulty) ──
        _songs = ApplyDifficultyFilter(_allSongs);

        // ── Preload pixel icons for visible songs ──
        foreach (var song in _allSongs)
        {
            if (!song.IsRandom)
                EnsurePixelIcon(song.OpponentChar);
        }

        // ── Initialize state ──
        _phase = Phase.Intro;
        _introTimer = 0;
        _pinkSlideX = -PINK_PANEL_W; // starts offscreen left, slides to 0
        _capsuleSlideX = FNFGame.SCREEN_WIDTH;
        _overhangY = -164f; // starts offscreen above
        _hintSlideY = 100f; // starts 100px below final position
        _hintSlideTweenTimer = 0f;
        _overhangTweenTimer = 0f;
        _diffSlideX = -300f; // starts offscreen left
        _diffIntroTweenTimer = 0f;
        _capsuleFlicker = false;
        _flickerTimer = 0f;
        _capsuleBounceTimer = 0f;
        _songPreviewDelay = 0;
        _songPreviewPlaying = false;
        _lastPreviewIndex = -1;
        _activePreviewSong = null;
        _activePreviewStartMs = 0;
        _activePreviewEndMs = 0;
        _introDone = false;
        _pinkColor = new Color(0xFF, 0xD4, 0xE9); // starts pink
        _bgRevealTint = 0f; // BG starts black (hidden)
        _orangeBarsVisible = false;
        _confirmGlowAlpha = 0f;
        _confirmGlow2Alpha = 0f;
        _confirmTextGlowAlpha = 0f;
        _confirmGlowPhase = 0;
        _confirmGlowTimer = 0f;
        _cardGlowAlpha = 0f;
        _cardGlowScale = 1f;
        _pinkTweening = false;
        _bgDimming = false;
        _bgDimPhase = 0;
        _bgDimColor = Color.White;
        _dotsVisible = false;
        _albumVisible = false;
        _albumIntroPlayed = false;
        _albumDifficultyRating = 0;
        _albumRevealTimer = 0f;
        _albumTitleVisible = false;
        _albumStarsVisible = false;
        _djAfkTimer = 0f;
        _djAfkPlaying = false;
        _spamTimer = 0f;
        _spamming = false;
        _diffTransitioning = false;
        _exiting = false;
        _exitTimer = 0f;
        _exitDjOffset = 0f;
        _exitPinkOffset = 0f;
        _exitBgOffset = 0f;
        _exitDiffXOffset = 0f;
        _exitArrowOffset = 0f;
        _exitScoreOffset = 0f;
        _exitLetterYOffset = 0f;
        _exitCapsuleOffset = 0f;
        _exitOverhangYOffset = 0f;
        _exitAlbumOffset = 0f;
        _menuFading = false;
        _letterSortIndex = 2; // ALL

        if (_songs.Count > 0)
        {
            UpdateSelection();
            UpdateAlbumForSelection();
            SwitchBackingImage();
        }

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    public override void Unload()
    {
        _lastSelectedIndex = _selectedIndex;
        _lastDifficultyIndex = _difficultyIndex;
        Audio.StopMusic();

        _djSheet?.Dispose();
        _djSheet = null;
        _djSprite = null;
        _capsuleSheet?.Dispose();
        _capsuleSheet = null;
        _highscoreSheet?.Dispose();
        _highscoreSheet = null;
        _selectorSheet?.Dispose();
        _selectorSheet = null;
        _selectorLeft = null;
        _selectorRight = null;
        _diffNightmareSheet?.Dispose();
        _diffNightmareSheet = null;
        _diffNightmareFrames = null;
        _backingTextSheet?.Dispose();
        _backingTextSheet = null;
        _backingTextSprite = null;
        _dotPulseSheet?.Dispose();
        _dotPulseSheet = null;
        _dotPulseFrames = null;
        _digitSheet?.Dispose();
        _digitSheet = null;
        _bigNumbersSheet?.Dispose();
        _bigNumbersSheet = null;
        _smallNumbersSheet?.Dispose();
        _smallNumbersSheet = null;
        _weekTypesSheet?.Dispose();
        _weekTypesSheet = null;
        _newTextSheet?.Dispose();
        _newTextSheet = null;
        _newTextFrames = null;
        _sortedLettersSheet?.Dispose();
        _sortedLettersSheet = null;
        _sortedLettersSprite = null;
        _albumRollSheet?.Dispose();
        _albumRollSheet = null;
        _albumRollSprite = null;
        _albumTitleSheet?.Dispose();
        _albumTitleSheet = null;
        _albumTitleSprite = null;
        _freeplayStarsSheet?.Dispose();
        _freeplayStarsSheet = null;
        _freeplayStarsSprite = null;
        _favHeartSheet?.Dispose();
        _favHeartSheet = null;
        _favHeartFrames = null;
        _rankBadgesSheet?.Dispose();
        _rankBadgesSheet = null;
        _picoTopLoopSheet?.Dispose();
        _picoTopLoopSheet = null;

        foreach (var kvp in _pixelIconSheets)
            kvp.Value?.Dispose();
        _pixelIconSheets.Clear();

        _bgAngleMask?.Dispose();
        _bgAngleMask = null;
    }

    // ═══════════════════════════════════════════════════════
    //  UPDATE
    // ═══════════════════════════════════════════════════════

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Smooth scroll
        _scrollLerp += (_selectedIndex - _scrollLerp) * Math.Min(1f, dt * 12f);

        // Lerp score display (original: smoothLerpPrecision(lerpScore, intendedScore, dt, 0.2) with precision=1/100)
        {
            float factor = MathF.Pow(0.01f, dt / 0.2f);
            _lerpScore = _displayedScore + (_lerpScore - _displayedScore) * factor;
            if (MathF.Abs(_lerpScore - _displayedScore) <= 1f) _lerpScore = _displayedScore;
        }
        // Lerp completion display (original: smoothLerpPrecision(lerpCompletion, intendedCompletion, dt, 0.5))
        {
            float factor = MathF.Pow(0.01f, dt / 0.5f);
            _lerpCompletion = _intendedCompletion + (_lerpCompletion - _intendedCompletion) * factor;
            if (MathF.Abs(_lerpCompletion - _intendedCompletion) <= 0.01f) _lerpCompletion = _intendedCompletion;
        }

        // DJ animation update
        _djSprite?.Update(dt);
        _highscoreSprite?.Update(dt);
        _selectorLeft?.Update(dt);
        _selectorRight?.Update(dt);
        _backingTextSprite?.Update(dt);
        _albumRollSprite?.Update(dt);
        _albumTitleSprite?.Update(dt);

        if (_albumRevealTimer > 0f)
        {
            _albumRevealTimer = Math.Max(0f, _albumRevealTimer - dt);
            if (_albumRevealTimer <= 0f)
            {
                _albumTitleVisible = true;
                _albumStarsVisible = true;
                if (_albumTitleSprite != null)
                {
                    _albumTitleSprite.OnFinish = () =>
                    {
                        _albumTitleSprite.OnFinish = null;
                        PlayAlbumTitleState("idle", loop: true, force: true);
                    };
                    PlayAlbumTitleState("switch", loop: false, force: true);
                }
            }
        }

        // Arrow push timers countdown
        if (_leftArrowPush > 0) _leftArrowPush = Math.Max(0, _leftArrowPush - dt);
        if (_rightArrowPush > 0) _rightArrowPush = Math.Max(0, _rightArrowPush - dt);

        // Difficulty alpha flash countdown (original: 1/24s flash on change)
        if (_diffAlphaFlashTimer > 0) _diffAlphaFlashTimer = Math.Max(0, _diffAlphaFlashTimer - dt);
        if (_diffYBounceTimer > 0) _diffYBounceTimer = Math.Max(0, _diffYBounceTimer - dt);
        if (_letterWiggleTimer > 0) _letterWiggleTimer = Math.Max(0, _letterWiggleTimer - dt);

        // Song preview fade-out (FNF_Official: fade out old preview before starting new)
        if (_previewFadingOut)
        {
            _previewFadeOutTimer += dt;
            float t = Math.Min(1f, _previewFadeOutTimer / PREVIEW_FADE_OUT_DURATION);
            Audio.MusicVolume = MathHelper.Lerp(Audio.MusicVolume, PREVIEW_FADE_OUT_END_VOLUME, t);
            if (t >= 1f)
            {
                _previewFadingOut = false;
                Audio.StopMusic();

                var next = _pendingPreviewSong;
                _pendingPreviewSong = null;
                if (next != null)
                    StartPreview(next);
            }
        }

        // Song preview fade-in
        if (_previewFadingIn)
        {
            _previewFadeTimer += dt;
            float t = Math.Min(1f, _previewFadeTimer / PREVIEW_FADE_IN_DURATION);
            Audio.MusicVolume = MathHelper.Lerp(PREVIEW_START_VOLUME, PREVIEW_TARGET_VOLUME, t);
            if (t >= 1f) _previewFadingIn = false;
        }

        // Preview loop segment using authored metadata (previewStart/previewEnd)
        if (_phase == Phase.Idle
            && _songPreviewPlaying
            && !_previewFadingIn
            && !_previewFadingOut
            && !string.IsNullOrEmpty(_activePreviewSong)
            && _activePreviewEndMs > _activePreviewStartMs
            && Audio.MusicPlaying
            && Audio.MusicPosition >= _activePreviewEndMs)
        {
            _pendingPreviewSong = _activePreviewSong;
            BeginPreviewFadeOut();
        }

        // Difficulty slide transition (old slides out, new slides in over 0.2s circInOut)
        if (_diffTransitioning)
        {
            _diffTransTimer += dt;
            float t = Math.Min(1f, _diffTransTimer / DIFF_TRANS_DURATION);
            // circInOut easing
            float eased = t < 0.5f
                ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f
                : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;
            _diffOldX = MathHelper.Lerp(_diffSlideX, _diffOldTarget, eased);
            _diffNewX = MathHelper.Lerp(_diffNewX > _diffSlideX ? 500f : -320f, DIFF_X, eased);
            if (t >= 1f)
            {
                _diffTransitioning = false;
                _diffOldTex = null;
                _diffSlideX = DIFF_X;
            }
        }

        // Exit animation — per-element exitMovers (original: goBack with FlxTween expoIn per element)
        if (_exiting)
        {
            _exitTimer += dt;

            // expoIn easing helper: t → pow(2, 10*(t-1)) for t>0, else 0
            static float ExpoIn(float t) => t <= 0 ? 0 : t >= 1 ? 1 : MathF.Pow(2f, 10f * (t - 1f));

            // DJ: target -dj.width*1.6 (~-1024), speed 0.5s
            float djT = ExpoIn(Math.Min(1f, _exitTimer / 0.5f));
            _exitDjOffset = djT * -(FNFGame.SCREEN_WIDTH * 0.8f);

            // Pink panel: target -PINK_PANEL_W, speed 0.4s (original: BackingCard exitMover speed:0.4)
            float pinkT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));
            _exitPinkOffset = pinkT * -PINK_PANEL_W;

            // BG / overlay: target FlxG.width*1.5, speed 0.4s
            float bgT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));
            _exitBgOffset = bgT * (FNFGame.SCREEN_WIDTH * 1.5f);

            // Difficulty + dots: target x-300, speed 0.25s (original: grpDifficulties exitMover x:-300 speed:0.25)
            float diffT = ExpoIn(Math.Min(1f, _exitTimer / 0.25f));
            _exitDiffXOffset = diffT * -300f;

            // Arrows: target -width*2, speed 0.26s
            float arrowT = ExpoIn(Math.Min(1f, _exitTimer / 0.26f));
            _exitArrowOffset = arrowT * -(FNFGame.SCREEN_WIDTH * 0.4f);

            // Score/highscore/clearBox: target FlxG.width, speed 0.3s
            float scoreT = ExpoIn(Math.Min(1f, _exitTimer / 0.3f));
            _exitScoreOffset = scoreT * FNFGame.SCREEN_WIDTH;

            // LetterSort: target y-100, speed 0.3s
            float letterT = ExpoIn(Math.Min(1f, _exitTimer / 0.3f));
            _exitLetterYOffset = letterT * -100f;

            // Overhang group: target y -164, speed 0.2s (original: y:-overhangStuff.height speed:0.2)
            float ohT = ExpoIn(Math.Min(1f, _exitTimer / 0.2f));
            _exitOverhangYOffset = ohT * -164f;

            // Album roll: target x FlxG.width, speed 0.4s (original: albumRoll exitMover x:FlxG.width speed:0.4)
            float albumT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));
            _exitAlbumOffset = albumT * FNFGame.SCREEN_WIDTH;

            // Capsules: slide right (original: doJumpOut, x → 1.2*screenWidth)
            float capsT = ExpoIn(Math.Min(1f, _exitTimer / 0.35f));
            _exitCapsuleOffset = capsT * (FNFGame.SCREEN_WIDTH * 1.2f);

            if (_exitTimer >= EXIT_DURATION)
            {
                _previewFadingIn = false;
                _previewFadingOut = false;
                Audio.MusicVolume = 1f;
                Audio.StopMusic();
                // Restore freakyMenu with fade-in (original: fadeIn(4.0, 0.0, 1.0))
                Audio.PlayMusic("music/freakyMenu", true);
                Audio.MusicVolume = 0f;
                _menuFadeTimer = 0f;
                _menuFading = true;
                Game.Scenes.ChangeScene(new MainMenuScene());
                return;
            }
        }

        // Music fade-in after returning to main menu (original: fadeIn(4.0, 0.0, 1.0))
        if (_menuFading)
        {
            _menuFadeTimer += dt;
            float t = Math.Min(1f, _menuFadeTimer / 4f);
            Audio.MusicVolume = t;
            if (t >= 1f) _menuFading = false;
        }

        // CardGlow fade out (original: introDone=0.45s sineOut, disappear=0.25s sineOut)
        if (_cardGlowAlpha > 0)
        {
            _cardGlowAlpha = Math.Max(0, _cardGlowAlpha - dt / _cardGlowDuration);
            _cardGlowScale = 1f + (1f - _cardGlowAlpha) * 0.2f; // 1.0 -> 1.2
        }

        // BG image color reveal (original: expoOut from black to white in 0.6s)
        if (_introDone && _bgRevealTint < 1f)
        {
            _bgRevealTint = Math.Min(1f, _bgRevealTint + dt / 0.6f);
        }

        // Confirm glow phased sequence (matches original BackingCard.confirm)
        // Phase 1: confirmGlow2 alpha 0→0.5 over 0.33s
        // Phase 2: confirmGlow2=0.6, confirmGlow=1, confirmTextGlow=1; then glow→0, textGlow→0.4 over 0.5s
        if (_confirmGlowPhase == 1)
        {
            _confirmGlowTimer += dt;
            float t = Math.Min(1f, _confirmGlowTimer / 0.33f);
            _confirmGlow2Alpha = t * 0.5f;
            if (t >= 1f)
            {
                _confirmGlowPhase = 2;
                _confirmGlowTimer = 0f;
                _confirmGlow2Alpha = 0.6f;
                _confirmGlowAlpha = 1f;
                _confirmTextGlowAlpha = 1f;
                // Start second BG dim phase
                _bgDimPhase = 2;
                _bgDimFrom = new Color(0xCD, 0xCD, 0xCD);
                _bgDimTo = new Color(0x55, 0x55, 0x55);
                _bgDimDuration = 2f;
                _bgDimTimer = 0f;
                _bgDimming = true;
            }
        }
        else if (_confirmGlowPhase == 2)
        {
            _confirmGlowTimer += dt;
            float t = Math.Min(1f, _confirmGlowTimer / 0.5f);
            _confirmGlowAlpha = 1f - t;
            _confirmTextGlowAlpha = 1f - t * 0.6f; // 1.0 → 0.4
        }

        // Pink color tween (smooth interpolation)
        if (_pinkTweening)
        {
            _pinkTweenTimer += dt;
            float t = Math.Min(1f, _pinkTweenTimer / _pinkTweenDuration);
            // quadOut easing: 1 - (1-t)^2
            float eased = 1f - (1f - t) * (1f - t);
            _pinkColor = Color.Lerp(_pinkColorFrom, _pinkColorTo, eased);
            if (t >= 1f) _pinkTweening = false;
        }

        // BG image dimming on confirm
        if (_bgDimming)
        {
            _bgDimTimer += dt;
            float t = Math.Min(1f, _bgDimTimer / _bgDimDuration);
            float eased = _bgDimPhase == 1 ? t : (1f - MathF.Pow(2f, -10f * t)); // linear for phase1, expoOut for phase2
            _bgDimColor = Color.Lerp(_bgDimFrom, _bgDimTo, eased);
            if (t >= 1f) _bgDimming = false;
        }

        // Highscore animation on random timer (original: 12-50s first, 20-60s repeat)
        if (_highscoreSprite != null && _highscoreSheet != null)
        {
            _highscoreNextPlay -= dt;
            if (_highscoreNextPlay <= 0)
            {
                _highscoreSprite.PlayAnimation("highscore small instance 1", loop: false, force: true);
                _highscoreNextPlay = _rng.Next(20, 61);
            }
        }

        // Overhang slides in from top (original: y=-164 tweens to y=-100 over 0.3s quartOut)
        if (_overhangTweenTimer < 0.3f)
        {
            _overhangTweenTimer = Math.Min(0.3f, _overhangTweenTimer + dt);
            float t = _overhangTweenTimer / 0.3f;
            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut
            _overhangY = MathHelper.Lerp(-164f, -100f, eased);
        }

        // Capsule bounce-in timer
        if (_capsuleBounceTimer < CAPSULE_BOUNCE_DURATION)
            _capsuleBounceTimer += dt;

        // CharSelectHint slides in from bottom (original: y starts 100px below, tweens over 0.8s quartOut)
        if (_introDone && _hintSlideTweenTimer < 0.8f)
        {
            _hintSlideTweenTimer = Math.Min(0.8f, _hintSlideTweenTimer + dt);
            float t = _hintSlideTweenTimer / 0.8f;
            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut
            _hintSlideY = MathHelper.Lerp(100f, 0f, eased);
        }

        // CharSelectHint alpha pulse (original: hintTimer += elapsed * 2)
        if (_introDone)
            _hintAlphaTimer += dt * 2f;

        // Intro UI reveal gate (FNF_Official: 1/24s then 1.5/24s)
        if (_introDone && !_introUiRevealed)
        {
            _introRevealTimer += dt;
            if (_introRevealTimer >= (2.5f / 24f))
            {
                _introUiRevealed = true;
                _dotsVisible = true;
            }
        }

        // Difficulty sprite slides in from left on introDone (original: x tweens to 90 over 0.6s quartOut)
        if (_introDone && _diffIntroTweenTimer < 0.6f)
        {
            _diffIntroTweenTimer = Math.Min(0.6f, _diffIntroTweenTimer + dt);
            float t = _diffIntroTweenTimer / 0.6f;
            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut
            _diffSlideX = MathHelper.Lerp(-300f, DIFF_X, eased);
        }

        // Capsule text flicker on confirm
        if (_capsuleFlicker)
        {
            _flickerTimer += dt;
        }

        switch (_phase)
        {
            case Phase.Intro:
                UpdateIntro(dt);
                break;
            case Phase.Idle:
                UpdateIdle(dt);
                break;
            case Phase.Confirmed:
                UpdateConfirmed(dt);
                break;
        }
    }

    private void UpdateIntro(float dt)
    {
        _introTimer += dt;

        // Pink panel slides in from left
        float targetPink = 0;
        _pinkSlideX += (targetPink - _pinkSlideX) * Math.Min(1f, dt * 6f);

        // Capsules slide in from right
        float targetCapsuleX = 0;
        _capsuleSlideX += (targetCapsuleX - _capsuleSlideX) * Math.Min(1f, dt * 5f);

        // Transition to idle when DJ intro finishes (OnDJIntroDone sets _introDone)
        // Also use a safety timeout so the user isn't stuck if DJ anim is missing
        if (_introDone || _introTimer > 2.5f)
        {
            _phase = Phase.Idle;
            _pinkSlideX = 0;
            _capsuleSlideX = 0;
            if (!_introDone) OnDJIntroDone(); // safety fallback
        }

        // Allow back during intro
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            _pendingPreviewSong = null;
            _previewFadingIn = false;
            _previewFadingOut = false;
            Audio.MusicVolume = 1f;
            Game.Scenes.ChangeScene(new MainMenuScene());
            return;
        }
    }

    private void UpdateIdle(float dt)
    {
        if (_exiting) return; // block input during exit animation

        if (_songs.Count == 0)
        {
            if (Input.BackPressed)
            {
                Audio.PlaySound("cancelMenu");
                _pendingPreviewSong = null;
                _previewFadingIn = false;
                _previewFadingOut = false;
                Audio.MusicVolume = 1f;
                Game.Scenes.ChangeScene(new MainMenuScene());
                return;
            }
            return;
        }

        // Hold/spam navigation (original: first press immediate, 0.9s hold → 0.07s repeat)
        bool upHeld = Input.UpHeld;
        bool downHeld = Input.DownHeld;
        if (upHeld || downHeld)
        {
            if (_spamming)
            {
                if (_spamTimer >= 0.07f)
                {
                    _spamTimer = 0;
                    ChangeSelectionNav(upHeld ? -1 : 1);
                }
            }
            else if (_spamTimer >= 0.9f)
            {
                _spamming = true;
                _spamTimer = 0;
            }
            else if (_spamTimer <= 0)
            {
                ChangeSelectionNav(upHeld ? -1 : 1);
            }
            _spamTimer += dt;
        }
        else
        {
            _spamming = false;
            _spamTimer = 0;
        }

        // Mouse wheel scrolling (original: FlxG.mouse.wheel)
        int wheel = Input.ScrollDelta;
        if (wheel != 0)
        {
            int steps = wheel > 0 ? -1 : 1;
            ChangeSelectionNav(steps);
        }

        // Difficulty switch (separate from letter sort controls to avoid double-trigger)
         bool diffLeftPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.A)
             || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Left)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.DPadLeft);
         bool diffRightPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.D)
             || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Right)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightShoulder)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightTrigger)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.DPadRight);
         if (diffLeftPressed)
         {
             ChangeDifficulty(-1);
             _djAfkTimer = 0; // reset AFK on difficulty change
         }
         if (diffRightPressed)
         {
             ChangeDifficulty(1);
             _djAfkTimer = 0; // reset AFK on difficulty change
         }

        // DJ afk animation (original: plays after ~30s of no input)
        if (_djSprite != null && !string.IsNullOrEmpty(_djAfkAnim))
        {
            _djAfkTimer += dt;
            if (_djAfkTimer >= 30f && !_djAfkPlaying)
            {
                _djSprite.PlayAnimation(_djAfkAnim, loop: false, force: true);
                _djSprite.OnFinish = () =>
                {
                    _djSprite.OnFinish = null;
                    if (!string.IsNullOrEmpty(_djIdleAnim))
                        _djSprite.PlayAnimation(_djIdleAnim, loop: true, force: true);
                    _djAfkPlaying = false;
                    _djAfkTimer = 0f;
                };
                _djAfkPlaying = true;
            }
        }

        // Home/End jump navigation (original: changeSelection jumps to first/last)
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Home) && _selectedIndex != 0)
        {
            _selectedIndex = 0;
            Audio.PlaySound("scrollMenu", 0.4f);
            OnSelectionChanged();
            _djAfkTimer = 0;
            _djAfkPlaying = false;
        }
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.End) && _selectedIndex != _songs.Count - 1)
        {
            _selectedIndex = _songs.Count - 1;
            Audio.PlaySound("scrollMenu", 0.4f);
            OnSelectionChanged();
            _djAfkTimer = 0;
            _djAfkPlaying = false;
        }

        // Letter sort cycling (FNF_Official: FREEPLAY_LEFT/FREEPLAY_RIGHT)
        // Use dedicated nav inputs so A/D (difficulty) does not also move letter sort.
        bool letterLeftPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Q);
        bool letterRightPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.E);

        if (letterLeftPressed)
        {
            _letterSortIndex = Math.Max(0, _letterSortIndex - 1);
            Audio.PlaySound("scrollMenu", 0.4f);
            ApplyLetterFilter();
            StartLetterWiggle(-1);
            _djAfkTimer = 0; // reset AFK on letter sort change
        }
        if (letterRightPressed)
        {
            _letterSortIndex = Math.Min(LetterSortLabels.Length - 1, _letterSortIndex + 1);
            Audio.PlaySound("scrollMenu", 0.4f);
            ApplyLetterFilter();
            StartLetterWiggle(1);
            _djAfkTimer = 0; // reset AFK on letter sort change
        }

        // TAB / controller SwitchCharButton -> Character Select (original: FREEPLAY_CHAR_SELECT, plays confirmMenu)
        if (Input.SwitchCharPressed && !_diffTransitioning)
        {
            Audio.PlaySound("confirmMenu");
            _pendingPreviewSong = null;
            _previewFadingIn = false;
            _previewFadingOut = false;
            Audio.MusicVolume = 1f;
            Game.Scenes.ChangeScene(new CharacterSelectScene());
            return;
        }

        // CTRL → Toggle favorite (original: controls.FREEPLAY_FAVORITE)
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.LeftControl)
            || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.RightControl))
        {
            if (_songs.Count > 0)
            {
                var currentSong = _songs[_selectedIndex];
                if (!currentSong.IsRandom && !string.IsNullOrEmpty(currentSong.Name))
                {
                    var favs = HighscoreManager.Data.FavoriteSongs;
                    bool wasFav = favs.Contains(currentSong.Name);
                    if (wasFav)
                        favs.Remove(currentSong.Name);
                    else
                        favs.Add(currentSong.Name);
                    HighscoreManager.SavePreferences();
                    Audio.PlaySound(wasFav ? "unfav" : "fav");
                    _djAfkTimer = 0; // reset AFK on favorite toggle
                }
            }
        }

        // Confirm
        if (Input.ConfirmPressed && !_diffTransitioning)
        {
            if (_songs.Count == 0)
                return;

            // If random capsule selected, pick a random real song
            var currentSong = _songs[_selectedIndex];
            if (currentSong.IsRandom)
            {
                var realSongs = _songs.Where(s => !s.IsRandom).ToList();
                if (realSongs.Count > 0)
                {
                    var picked = realSongs[_rng.Next(realSongs.Count)];
                    _selectedIndex = _songs.IndexOf(picked);
                    currentSong = _songs[_selectedIndex];
                    UpdateSelection();
                    UpdateAlbumForSelection();
                    SwitchBackingImage();
                }
                else
                {
                    Audio.PlaySound("cancelMenu");
                    return;
                }
            }

            _pendingPreviewSong = null;
            _previewFadingIn = false;
            _previewFadingOut = false;
            Audio.MusicVolume = 1f;

            _phase = Phase.Confirmed;
            _confirmTimer = 1.0f; // original: bf.json startDelay=1.0
            _confirmFading = false;
            _confirmFadeAlpha = 0f;
            Audio.PlaySound("confirmMenu");
            if (_djSprite != null && !string.IsNullOrEmpty(_djConfirmAnim))
                _djSprite.PlayAnimation(_djConfirmAnim, force: true, loop: false);

            // Capsule text flicker on confirm (original: songText.flickerText)
            _capsuleFlicker = true;
            _flickerTimer = 0f;

            // Original BackingCard.confirm(): pink color tween 0xFFD0D5 -> 0x171831 over 0.33s quadOut
            _pinkColorFrom = new Color(0xFF, 0xD0, 0xD5);
            _pinkColorTo = new Color(0x17, 0x18, 0x31);
            _pinkTweenTimer = 0f;
            _pinkTweenDuration = 0.33f;
            _pinkTweening = true;

            _orangeBarsVisible = false;

            // Start confirm glow sequence (phase 1: glow2 alpha 0->0.5 over 0.33s)
            _confirmGlowAlpha = 0f;
            _confirmGlow2Alpha = 0f;
            _confirmTextGlowAlpha = 0f;
            _confirmGlowPhase = 1;
            _confirmGlowTimer = 0f;

            // BG image dim phase 1: 0xA8A8A8 -> 0x646464 over 0.5s
            _bgDimPhase = 1;
            _bgDimFrom = new Color(0xA8, 0xA8, 0xA8);
            _bgDimTo = new Color(0x64, 0x64, 0x64);
            _bgDimDuration = 0.5f;
            _bgDimTimer = 0f;
            _bgDimming = true;

            // Play backingTextYeah animation on confirm
            if (_backingTextSprite != null && _backingTextSheet?.Animations != null)
            {
                foreach (var key in _backingTextSheet.Animations.Keys)
                {
                    _backingTextSprite.PlayAnimation(key, loop: false, force: true);
                    break;
                }
            }

            // Hide dots on confirm (original: fadeDots(false))
            _dotsVisible = false;
            return;
        }

        // Back — start exit animation (original: goBack() with exitMovers)
        if (Input.BackPressed && !_diffTransitioning)
        {
            Audio.PlaySound("cancelMenu");
            _pendingPreviewSong = null;
            _previewFadingIn = false;
            _previewFadingOut = false;
            Audio.MusicVolume = 1f;
            // Original: backingCard.disappear() — tween pink back to pink, replay cardGlow, hide orange
            _pinkColorFrom = new Color(0xFF, 0xD8, 0x63); // gold
            _pinkColorTo = new Color(0xFF, 0xD0, 0xD5);   // pink
            _pinkTweenTimer = 0f;
            _pinkTweenDuration = 0.25f;
            _pinkTweening = true;
            _orangeBarsVisible = false;
            _cardGlowAlpha = 1f;
            _cardGlowScale = 1f;
            _cardGlowDuration = 0.25f; // original: disappear uses 0.25s sineOut (vs 0.45s introDone)
            _dotsVisible = false;
            _exiting = true;
            _exitTimer = 0f;
            return;
        }

        // Song preview with delay (original: 0.25s delay)
        _songPreviewDelay += dt;
        if (_songPreviewDelay >= PREVIEW_DELAY && !_songPreviewPlaying && _lastPreviewIndex != _selectedIndex)
        {
            _lastPreviewIndex = _selectedIndex;
            _songPreviewPlaying = true;
            if (_songs.Count > 0)
                PlaySongPreview(_songs[_selectedIndex].Name);
        }
    }

    /// <summary>
    /// Move selection by delta (original: changeSelection). Resets afk timer and plays scroll sound.
    /// </summary>
    private void ChangeSelectionNav(int delta)
    {
        if (_songs.Count == 0) return; // defensive guard against empty list
        _selectedIndex = ((_selectedIndex + delta) % _songs.Count + _songs.Count) % _songs.Count;
        Audio.PlaySound("scrollMenu", 0.4f);
        OnSelectionChanged();
        _djAfkTimer = 0;
        _djAfkPlaying = false;
    }

    /// <summary>
    /// Change difficulty with slide-in/out animation (original: changeDiff with circInOut tween).
    /// </summary>
    private void ChangeDifficulty(int change)
    {
        int oldIdx = _difficultyIndex;
        int newIdx = ((_difficultyIndex + change) % Difficulties.Length + Difficulties.Length) % Difficulties.Length;
        if (newIdx == oldIdx)
            return;

        _difficultyIndex = newIdx;

        Audio.PlaySound("scrollMenu", 0.4f);
        _djAfkTimer = 0;
        _djAfkPlaying = false;

        // Push arrow animation
        if (change < 0) _leftArrowPush = ARROW_PUSH_DURATION;
        else _rightArrowPush = ARROW_PUSH_DURATION;

        // Difficulty alpha flash (original: 1/24s at half alpha)
        _diffAlphaFlashTimer = 1f / 24f;

        // Difficulty y-offset bounce (original: offset.y -= 5, resets after 1/24s)
        _diffYBounceTimer = 1f / 24f;

        // Start difficulty slide transition (old slides out, new slides in)
        _diffOldIndex = oldIdx;
        _diffOldTex = oldIdx switch
        {
            0 => _diffEasy,
            1 => _diffNormal,
            2 => _diffHard,
            3 => _diffErect,
            4 => _diffNightmare,
            _ => _diffNormal
        };
        _diffOldTarget = change > 0 ? -320f : 500f;
        _diffNewX = change > 0 ? 500f : -320f;
        _diffNewTarget = DIFF_X;
        _diffTransTimer = 0f;
        _diffTransitioning = true;
        _diffOldX = _diffSlideX;

        // Switching difficulty changes which songs are available in list.
        ApplyLetterFilter();
    }

    private void UpdateConfirmed(float dt)
    {
        _confirmTimer -= dt;
        if (_confirmTimer <= 0 && !_confirmFading)
        {
            // Original: after startDelay, funnyCam.fade(BLACK, 0.2s) then load PlayState
            _confirmFading = true;
            _confirmFadeAlpha = 0f;
        }
        if (_confirmFading)
        {
            _confirmFadeAlpha = Math.Min(1f, _confirmFadeAlpha + dt / 0.2f);
            if (_confirmFadeAlpha >= 1f)
            {
                if (_songs.Count > 0)
                {
                    var song = _songs[_selectedIndex];
                    string diff = GetCurrentDifficulty(song);
                    string playSongName = ResolveSongNameForSelection(song, diff);
                    _previewFadingIn = false;
                    _previewFadingOut = false;
                    Audio.MusicVolume = 1f;
                    Audio.StopMusic();
                    Game.Scenes.ChangeScene(new PlayScene(playSongName, diff));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  DRAW
    // ═══════════════════════════════════════════════════════

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // 1) Solid dark background
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
            new Color(14, 11, 33));

        // 2) Darkback layer behind DJ (original: darkBack below DJ)
        if (_introDone && _darkBack != null && _darkBack != Assets.Pixel)
        {
            float dbScale = (float)FNFGame.SCREEN_HEIGHT / _darkBack.Height;
            spriteBatch.Draw(_darkBack,
                new Vector2(_pinkSlideX + _exitPinkOffset, 0), null, Color.White * 0.5f,
                0f, Vector2.Zero, dbScale, SpriteEffects.None, 0f);
        }

        // 3) Character BG image (right side)
        DrawBGImage(spriteBatch);

        // 3.5) Angled mask overlay that defines the diagonal split (approx of AngleMask/blackOverlay)
        DrawBGAngleMask(spriteBatch);

        // 4) Yellow bg piece (original: behind capsules area)
        if (_introDone && _yellowBgPiece != null && _yellowBgPiece != Assets.Pixel)
        {
            spriteBatch.Draw(_yellowBgPiece,
                new Vector2(PINK_PANEL_W + _exitBgOffset, 0), null, Color.White,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }

        // 5) Pink backing card (left panel)
        DrawPinkBack(spriteBatch);

        // 5.5) Pico-specific backing card overlays (guns / RPG theme)
        DrawPicoBackingCardLayers(spriteBatch);

        // 6) Orange accent bar
        DrawOrangeBar(spriteBatch);

        // CardGlow (fades out after DJ intro)
        DrawCardGlow(spriteBatch);

        spriteBatch.End();

        // 5) Backing-card overlays + DJ BF — composites need AlphaBlend
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        // Backing text yeah belongs to backing card and should render behind DJ.
        DrawBackingTextYeah(spriteBatch);
        DrawDJ(spriteBatch);
        spriteBatch.End();

        // 6) Everything else (capsules, UI)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Song capsules
        if (_songs.Count > 0)
            DrawCapsules(spriteBatch);
        else
            DrawEmptyState(spriteBatch);

        // UI elements only visible after DJ intro (original behavior)
        // During exit, elements animate out via per-element offsets instead of hiding
        if (_introDone && _introUiRevealed)
        {
            // Difficulty selector + arrows + dots
            DrawDifficulty(spriteBatch);
            DrawDifficultyArrows(spriteBatch);
            DrawDifficultyDots(spriteBatch);

            // Letter sort bar below difficulty area
            DrawLetterSort(spriteBatch);
        }

        if (_introDone)
        {

            // Score display (highscore label + score + clearBox + completion)
            DrawScoreDisplay(spriteBatch);
        }

        spriteBatch.End();
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

        if (_introDone)
        {
            // Album roll (right side)
            DrawAlbumRoll(spriteBatch);
        }

        // Confirm glow effects (original: BlendMode.ADD)
        spriteBatch.End();
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive);
        DrawConfirmGlow(spriteBatch);
        spriteBatch.End();
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Top overhang bar (always visible, animates in from top)
        DrawOverhang(spriteBatch);

        // Character select hint at bottom
        DrawCharSelectHint(spriteBatch);

        // Confirm screen fade-to-black overlay (original: funnyCam.fade(BLACK, 0.2s))
        if (_confirmFading && _confirmFadeAlpha > 0)
        {
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.Black * _confirmFadeAlpha);
        }

        spriteBatch.End();
    }

    private void DrawBGImage(SpriteBatch sb)
    {
        if (_bgImage == null || _bgImage == Assets.Pixel) return;
        if (!_introDone) return; // Original: backingImage.visible = false until introDone
        // Original: color fades from 0xFF000000 to 0xFFFFFFFF (expoOut over 0.6s)
        byte tintVal = (byte)(Math.Min(1f, _bgRevealTint) * 255f);
        var revealColor = new Color(tintVal, tintVal, tintVal);
        // During confirm, BG dims via _bgDimColor
        Color tintColor = _phase == Phase.Confirmed
            ? new Color(
                (byte)(revealColor.R * _bgDimColor.R / 255),
                (byte)(revealColor.G * _bgDimColor.G / 255),
                (byte)(revealColor.B * _bgDimColor.B / 255))
            : revealColor;
        float scale = (float)FNFGame.SCREEN_HEIGHT / _bgImage.Height;
        // Keep in fixed 1280x720 authored layout space.
        float bgImageX = BG_IMAGE_X;

        sb.Draw(_bgImage,
            new Vector2(bgImageX + _exitBgOffset, 0), null, tintColor,
            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawPinkBack(SpriteBatch sb)
    {
        if (PINK_PANEL_W <= 0) return;
        float pinkX = _pinkSlideX + _exitPinkOffset;
        if (_pinkBack == null || _pinkBack == Assets.Pixel)
        {
            sb.Draw(Assets.Pixel,
                new Rectangle((int)pinkX, 0, (int)PINK_PANEL_W, FNFGame.SCREEN_HEIGHT),
                _pinkColor);
            return;
        }
        float scaleX = PINK_PANEL_W / _pinkBack.Width;
        float scaleY = (float)FNFGame.SCREEN_HEIGHT / _pinkBack.Height;
        float s = Math.Max(scaleX, scaleY);
        sb.Draw(_pinkBack,
            new Vector2(pinkX, 0), null,
            _pinkColor,
            0f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    private void DrawBGAngleMask(SpriteBatch sb)
    {
        // Keep disabled: the backing image art already contains the intended split/mask.
        // Drawing an extra runtime mask over-darkens Freeplay and causes the screenshot bug.
        return;
    }

    private static Texture2D CreateFreeplayAngleMask(GraphicsDevice gd)
    {
        int w = FNFGame.SCREEN_WIDTH;
        int h = FNFGame.SCREEN_HEIGHT;

        var tex = new Texture2D(gd, w, h, false, SurfaceFormat.Color);
        var data = new Color[w * h];

        float baseX = w * 0.55f;
        float slope = 0.35f;
        float midY = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float cutX = baseX - (y - midY) * slope;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float a = x < cutX - 2 ? 1f : x > cutX + 2 ? 0f : 1f - (x - (cutX - 2)) / 4f;
                byte alpha = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
                data[row + x] = new Color((byte)0, (byte)0, (byte)0, alpha);
            }
        }

        tex.SetData(data);
        return tex;
    }

    private void DrawEmptyState(SpriteBatch sb)
    {
        var font = Assets.GetFont(24);
        if (font == null) return;

        const string text = "No songs found";
        var sz = font.MeasureString(text);
        float x = (FNFGame.SCREEN_WIDTH - sz.X) / 2f;
        float y = (FNFGame.SCREEN_HEIGHT - sz.Y) / 2f;
        font.DrawText(sb, text, new Vector2(x, y), Color.White);
    }

    private void DrawOrangeBar(SpriteBatch sb)
    {
        if (PINK_PANEL_W <= 0 || !_orangeBarsVisible) return;
        float pinkX = _pinkSlideX + _exitPinkOffset;
        int barY = 440;
        int barH = 75;

        if (_isPicoTheme && _picoBlueBar != null && _picoBlueBar != Assets.Pixel)
        {
            float sx = PINK_PANEL_W / _picoBlueBar.Width;
            float sy = (float)barH / _picoBlueBar.Height;
            sb.Draw(_picoBlueBar,
                new Vector2(pinkX, barY),
                null,
                Color.White,
                0f,
                Vector2.Zero,
                new Vector2(sx, sy),
                SpriteEffects.None,
                0f);
        }
        else
        {
            // Original: orangeBackShit at (84, 440) width=pinkBack.width, height=75
            float barX = pinkX + 84f;
            sb.Draw(Assets.Pixel,
                new Rectangle((int)barX, barY, (int)(PINK_PANEL_W - 84f), barH),
                new Color(254, 218, 0));
            // alsoOrangeLOL at (0, 440) width=100
            sb.Draw(Assets.Pixel,
                new Rectangle((int)pinkX, barY, 100, barH),
                new Color(255, 212, 0));
        }

        var font = Assets.GetFont(24);
        if (font != null)
            font.DrawText(sb, GetSelectedCharacterDisplayName(), new Vector2(pinkX + 14f, barY + 18f), Color.Black);
    }

    private void DrawPicoBackingCardLayers(SpriteBatch sb)
    {
        if (!_isPicoTheme || !_introDone) return;

        DrawPicoPanelTexture(sb, _picoMiddleLoop, 145f, 1f);
        DrawPicoPanelTexture(sb, _picoLowerLoop, FNFGame.SCREEN_HEIGHT, 1f, alignBottom: true);

        if (_picoTopLoopSheet?.Texture != null)
        {
            var frame = GetPicoTopLoopFrame();
            if (frame != null)
            {
                float pinkX = _pinkSlideX + _exitPinkOffset;
                float scale = PINK_PANEL_W / frame.SourceRect.Width;
                sb.Draw(_picoTopLoopSheet.Texture,
                    new Vector2(pinkX, 0f) + frame.Offset * scale,
                    frame.SourceRect,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    0f);
            }
        }

        DrawPicoPanelTexture(sb, _picoGlow, 0f, 0.8f);
    }

    private SpriteFrame GetPicoTopLoopFrame()
    {
        if (_picoTopLoopSheet == null)
            return null;

        string animName = PicoTopLoopAnims[Math.Abs(_selectedIndex) % PicoTopLoopAnims.Length];
        var frames = _picoTopLoopSheet.GetAnimation(animName);
        if (frames == null || frames.Count == 0)
            frames = _picoTopLoopSheet.GetAnimation("base");
        if (frames == null || frames.Count == 0)
            return null;

        int frameIndex = (int)((uint)Environment.TickCount / 83u % (uint)frames.Count);
        return frames[frameIndex];
    }

    private void DrawPicoPanelTexture(SpriteBatch sb, Texture2D texture, float y, float alpha, bool alignBottom = false)
    {
        if (texture == null || texture == Assets.Pixel)
            return;

        float pinkX = _pinkSlideX + _exitPinkOffset;
        float scale = PINK_PANEL_W / texture.Width;
        float drawY = alignBottom ? y - texture.Height * scale : y;

        sb.Draw(texture,
            new Vector2(pinkX, drawY),
            null,
            Color.White * alpha,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);
    }

    private void DrawCardGlow(SpriteBatch sb)
    {
        if (_cardGlow == null || _cardGlow == Assets.Pixel || _cardGlowAlpha <= 0) return;
        // Original: cardGlow at (-30, -30), fades alpha 1->0, scale 1->1.2
        float s = _cardGlowScale;
        sb.Draw(_cardGlow,
            new Vector2(-30f, -30f), null,
            Color.White * _cardGlowAlpha,
            0f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    private void DrawDJ(SpriteBatch sb)
    {
        if (_djSprite == null) return;
        _djSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);
        _djSprite.Position = new Vector2(DjBaseX + DJ_X + _exitDjOffset, DJ_Y);
        _djSprite.Scale = new Vector2(DJ_SCALE, DJ_SCALE);
        _djSprite.Draw(sb);
    }

    private void DrawBackingTextYeah(SpriteBatch sb)
    {
        if (_backingTextSprite == null) return;
        _backingTextSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);
        // Original: position at (CUTOUT_WIDTH * DJ_POS_MULTI - 320, 120)
        float textX = PINK_PANEL_W * 0.74f - 320f + _exitPinkOffset;
        _backingTextSprite.Position = new Vector2(textX, 120f);
        _backingTextSprite.Scale = Vector2.One;
        _backingTextSprite.Draw(sb);
    }

    private void DrawCapsules(SpriteBatch sb)
    {
        if (_capsuleSheet?.Texture == null) return;

        List<Action> additiveItems = new List<Action>();

        int visibleRange = 8;
        for (int offset = -3; offset <= visibleRange; offset++)
        {
            int songIdx = _selectedIndex + offset;
            if (songIdx < 0 || songIdx >= _songs.Count) continue;

            // Position calculation (matches original intendedX/intendedY + intendedY offsets)
            float relIdx = songIdx - _scrollLerp;
            int capsuleIndex = songIdx - _selectedIndex;
            float yOffset = 0f;
            if (capsuleIndex < 0) yOffset += 50f;
            else if (capsuleIndex > 4) yOffset -= 10f;
            float capsuleY = relIdx * CAPSULE_SPACING + 120f - yOffset;
            // Capsules above selection shift up by 100 (original: if index < curSelected: y -= 100)
            if (songIdx < _selectedIndex) capsuleY -= 100f;
            float capsuleX = 270f + 60f * MathF.Sin(capsuleIndex) + SongsBaseX + SONGS_X_OFFSET + _capsuleSlideX + _exitCapsuleOffset;

            // Staggered bounce-in: each capsule starts 100px below and bounces up with delay
            if (_capsuleBounceTimer < CAPSULE_BOUNCE_DURATION)
            {
                float stagger = songIdx * 0.04f; // 40ms stagger per capsule
                float bounceT = Math.Clamp((_capsuleBounceTimer - stagger) / (CAPSULE_BOUNCE_DURATION - stagger), 0f, 1f);
                // elasticOut approximation: overshoot then settle
                float eased = bounceT <= 0 ? 0 : bounceT >= 1 ? 1
                    : MathF.Pow(2f, -10f * bounceT) * MathF.Sin((bounceT * 10f - 0.75f) * (2f * MathF.PI / 3f)) + 1f;
                capsuleY += (1f - eased) * 100f;
            }

            // Skip off-screen
            if (capsuleY < -CAPSULE_FRAME_H * CAPSULE_SCALE - 20 || capsuleY > FNFGame.SCREEN_HEIGHT + 50) continue;

            bool isSelected = songIdx == _selectedIndex;
            // Original: capsule.offset.x = selected ? 0 : -5 (unselected capsules shift left)
            if (!isSelected) capsuleX -= 5f;
            var frames = isSelected ? _capsuleSelected : _capsuleUnselected;
            if (frames == null || frames.Count == 0) continue;

            // Animate capsule (loop the animation frames at 24fps)
            int frameIdx = (int)((uint)Environment.TickCount / 41u % (uint)frames.Count);
            var frame = frames[frameIdx];
            var song = _songs[songIdx];
            float alpha = isSelected ? 1f : 0.6f;
            // Original: initRandom() sets alpha=0.5, hides songText/favIcon/ranking
            if (song.IsRandom)
            {
                alpha = 0.5f;
                // Original: initRandom() y = intendedY(0) + 10
                capsuleY += 10f;
            }

            // Original: grayscaleShader.setAmount(isSelected ? 0 : 0.8) — desaturate unselected capsules
            // Simulate 80% grayscale by lerping color toward gray
            Color capsuleTint = isSelected
                ? Color.White * alpha
                : Color.Lerp(Color.White, new Color(128, 128, 128), 0.8f) * alpha;

            // Draw capsule sprite
            Vector2 pos = new Vector2(capsuleX, capsuleY);
            Vector2 scale = new Vector2(CAPSULE_SCALE, CAPSULE_SCALE);
            Vector2 drawOffset = frame.Offset;
            sb.Draw(_capsuleSheet.Texture,
                pos + drawOffset * scale,
                frame.SourceRect, capsuleTint,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

            if (song.IsRandom)
            {
                // Original: songText.visible=false, no icon/ranking/fav drawn
                continue;
            }

            // Draw song name text on capsule (with flicker on confirm)
            bool flickerHide = _capsuleFlicker && isSelected && ((int)(_flickerTimer * 20f) % 2 == 1);
            if (!flickerHide)
            {
                DrawCapsuleText(sb, song, pos, isSelected, alpha);
            }

            // Draw pixel icon on capsule (original: plays 'confirm' anim on song confirm)
            DrawPixelIcon(sb, song, pos, alpha, _phase == Phase.Confirmed && isSelected);

            // Draw capsule detail info (BPM, difficulty rating, week type, NEW badge)
            DrawCapsuleDetails(sb, song, pos, alpha);

             // Draw fav heart if song is favorited (original: favIcon at x=405 default, x=370 if rank visible)
            if (HighscoreManager.Data.FavoriteSongs.Contains(song.Name) && _favHeartFrames != null && _favHeartFrames.Count > 0
                && _favHeartSheet?.Texture != null)
            {
                int fi = (int)((uint)Environment.TickCount / 83u % (uint)_favHeartFrames.Count);
                var hf = _favHeartFrames[fi];
                string favDiff = GetCurrentDifficulty(song);
                bool hasRank = !string.IsNullOrEmpty(HighscoreManager.GetRank(song.Name, favDiff));
                float favX = hasRank ? 370f : 405f;
                additiveItems.Add(() =>
                {
                    sb.Draw(_favHeartSheet.Texture,
                        pos + new Vector2(favX, 40f) + hf.Offset,
                        hf.SourceRect, Color.White * alpha,
                        0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
                });
            }

            // Draw rank badge (original: ranking at (555, 40))
            Action drawRank = GetRankBadgeAction(sb, song, pos, alpha);
            if (drawRank != null)
                additiveItems.Add(drawRank);
        }

        if (additiveItems.Count > 0)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive);
            foreach (var action in additiveItems) action();
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        }
    }

    private void DrawCapsuleText(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, bool selected, float alpha)
    {
        string displayName = song.DisplayName ?? song.Name;
        // Original SongMenuItem.hx: CapsuleText at (capsule.width * 0.26, 45) = (159.12, 45),
        // font size Std.int(40 * realScaled) = 32.
        var font = Assets.GetFont(32);
        if (font != null)
        {
            float textX = capsulePos.X + 159f;
            float textY = capsulePos.Y + 45f;
            Color textColor = Color.White;
            string clipped = ClipText(font, displayName, 290);
            font.DrawText(sb, clipped, new Vector2(textX, textY), textColor * alpha);
        }
    }

    private void DrawPixelIcon(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha, bool isConfirmed)
    {
        var sheet = EnsurePixelIcon(song.OpponentChar);
        if (sheet?.Texture == null) return;

        // Original: on confirm, pixelIcon.animation.play('confirm'), onFinish → 'confirm-hold'
        if (isConfirmed)
        {
            var confirmFrames = sheet.GetAnimation("confirm");
            if (confirmFrames != null && confirmFrames.Count > 0)
            {
                int fi = (int)(_flickerTimer * 24f);
                if (fi < confirmFrames.Count)
                {
                    DrawPixelIconFrame(sb, sheet.Texture, confirmFrames[fi], capsulePos, alpha, song.OpponentChar);
                    return;
                }
                var holdFrames = sheet.GetAnimation("confirm-hold");
                if (holdFrames != null && holdFrames.Count > 0)
                {
                    int hi = (int)((uint)Environment.TickCount / 41u % (uint)holdFrames.Count);
                    DrawPixelIconFrame(sb, sheet.Texture, holdFrames[hi], capsulePos, alpha, song.OpponentChar);
                    return;
                }
                DrawPixelIconFrame(sb, sheet.Texture, confirmFrames[^1], capsulePos, alpha, song.OpponentChar);
                return;
            }
        }

        var idleFrames = sheet.GetAnimation("idle");
        if (idleFrames == null || idleFrames.Count == 0)
        {
            if (sheet.Frames.Count == 0) return;
            var firstFrame = sheet.Frames.Values.First();
            DrawPixelIconFrame(sb, sheet.Texture, firstFrame, capsulePos, alpha, song.OpponentChar);
            return;
        }

        DrawPixelIconFrame(sb, sheet.Texture, idleFrames[0], capsulePos, alpha, song.OpponentChar);
    }

    private void DrawPixelIconFrame(SpriteBatch sb, Texture2D tex, SpriteFrame frame,
        Vector2 capsulePos, float alpha, string characterId)
    {
        // Original SongMenuItem.hx: PixelatedIcon at (160, 35) relative to the capsule group,
        // scale = 2. FlxSprite.origin only affects the scale/rotation PIVOT, not the draw
        // position - the sprite's top-left stays at (160, 35). We previously subtracted
        // origin*2 which shoved the icon ~100px too far left.
        float iconX = capsulePos.X + 160f;
        float iconY = capsulePos.Y + 35f;
        float iconScale = 2f;

        sb.Draw(tex,
            new Vector2(iconX, iconY) + frame.Offset * iconScale,
            frame.SourceRect, Color.White * alpha,
            0f, Vector2.Zero, iconScale, SpriteEffects.None, 0f);
    }

    private static float GetPixelIconOriginX(string characterId)
    {
        if (string.Equals(characterId, "parents-christmas", StringComparison.OrdinalIgnoreCase))
            return 140f;

        if (string.Equals(characterId, "sserafim-kazuha", StringComparison.OrdinalIgnoreCase))
            return 195f;

        return 100f;
    }

    private void DrawCapsuleDetails(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha)
    {
        // Original SongMenuItem: all child sprites use absolute local positions within FlxSpriteGroup,
        // NOT affected by the capsule graphic's scale (realScaled=0.8). Draw at 1:1 coords.
        const float detailYOffset = -28f;

        // BPM text label at (144, 87), scale 0.9
        if (_bpmText != null && _bpmText != Assets.Pixel)
        {
            sb.Draw(_bpmText,
                capsulePos + new Vector2(144f, 87f + detailYOffset),
                null, Color.White * alpha, 0f, Vector2.Zero,
                0.9f, SpriteEffects.None, 0f);
        }

        // BPM numbers (3 small digits) at (191+i*11, 88.5) — original updateBPM positions
        if (_smallNumbersSheet?.Texture != null)
        {
            int bpm = song.Bpm;
            int d0 = bpm < 100 ? 0 : (bpm / 100) % 10;
            int d1 = bpm < 10 ? 0 : (bpm / 10) % 10;
            int d2 = bpm % 10;
            float d0x = d0 == 1 ? 186f : 191f;
            DrawSmallNumber(sb, d0, capsulePos + new Vector2(d0x, 88.5f + detailYOffset), alpha);
            DrawSmallNumber(sb, d1, capsulePos + new Vector2(202f, 88.5f + detailYOffset), alpha);
            DrawSmallNumber(sb, d2, capsulePos + new Vector2(213f, 88.5f + detailYOffset), alpha);
        }

        // Difficulty text label at (414, 87), scale 0.9
        if (_difficultyText != null && _difficultyText != Assets.Pixel)
        {
            sb.Draw(_difficultyText,
                capsulePos + new Vector2(414f, 87f + detailYOffset),
                null, Color.White * alpha, 0f, Vector2.Zero,
                0.9f, SpriteEffects.None, 0f);
        }

        // Difficulty rating numbers (2 big digits) at (466+i*30, 32)
        if (_bigNumbersSheet?.Texture != null)
        {
            string currentDiff = GetCurrentDifficulty(song);
            int rating = 0;
            if (song.Ratings != null && song.Ratings.TryGetValue(currentDiff, out int r))
                rating = Math.Clamp(r, 0, 20);
            int tens = rating >= 10 ? rating / 10 : 0;
            int ones = rating % 10;
            DrawBigNumber(sb, tens, capsulePos + new Vector2(466f, 32f + detailYOffset), alpha);
            DrawBigNumber(sb, ones, capsulePos + new Vector2(496f, 32f + detailYOffset), alpha);
        }

        // NEW badge at (454, 9) (original: shows when song has no score)
        if (_newTextFrames != null && _newTextFrames.Count > 0 && _newTextSheet?.Texture != null)
        {
            string nd = GetCurrentDifficulty(song);
            int existingScore = HighscoreManager.GetScore(song.Name, nd);
            if (existingScore <= 0)
            {
                int ni = (int)((uint)Environment.TickCount / 83u % (uint)_newTextFrames.Count);
                var nf = _newTextFrames[ni];
                sb.Draw(_newTextSheet.Texture,
                    capsulePos + new Vector2(454f, 9f + detailYOffset) + nf.Offset * 0.9f,
                    nf.SourceRect, Color.White * alpha, 0f, Vector2.Zero,
                    0.9f, SpriteEffects.None, 0f);
            }
        }

        // Week type (WEEK/WEEKEND) at (291, 87)
        if (_weekTypesSheet?.Texture != null && song.WeekNum != 0)
        {
            string weekAnim = song.WeekNum > 0 ? "WEEK text instance 1" : "WEEKEND text instance 1";
            var weekFrames = _weekTypesSheet.GetAnimation(weekAnim);
            if (weekFrames != null && weekFrames.Count > 0)
            {
                var wf = weekFrames[0];
                sb.Draw(_weekTypesSheet.Texture,
                    capsulePos + new Vector2(291f, 87f + detailYOffset) + wf.Offset * 0.9f,
                    wf.SourceRect, Color.White * alpha, 0f, Vector2.Zero,
                    0.9f, SpriteEffects.None, 0f);
            }

            // Week number at (355, 88.5) — weekend entries shift x by -35 (original: checkWeek)
            if (_smallNumbersSheet?.Texture != null)
            {
                int wn = Math.Abs(song.WeekNum);
                float weekNumX = 355f;
                if (song.WeekNum < 0) weekNumX -= 35f;
                DrawSmallNumber(sb, wn % 10, capsulePos + new Vector2(weekNumX, 88.5f + detailYOffset), alpha);
            }
        }
    }

    private void DrawSmallNumber(SpriteBatch sb, int digit, Vector2 pos, float alpha)
    {
        if (_smallNumbersSheet?.Texture == null || digit < 0 || digit > 9) return;
        string frameName = DigitNames[digit] + "0000";
        if (_smallNumbersSheet.Frames.TryGetValue(frameName, out var frame))
        {
            float checkX = digit == 1 ? -4f : (digit == 3 ? -1f : 0f);
            sb.Draw(_smallNumbersSheet.Texture,
                pos + new Vector2(checkX * 0.9f, 0f) + frame.Offset * 0.9f,
                frame.SourceRect, Color.White * alpha, 0f, Vector2.Zero,
                0.9f, SpriteEffects.None, 0f);
        }
    }

    private void DrawBigNumber(SpriteBatch sb, int digit, Vector2 pos, float alpha)
    {
        if (_bigNumbersSheet?.Texture == null || digit < 0 || digit > 9) return;
        string frameName = DigitNames[digit] + "0000";
        if (_bigNumbersSheet.Frames.TryGetValue(frameName, out var frame))
        {
            float checkX = digit == 1 ? -4f : (digit == 3 ? -1f : 0f);
            sb.Draw(_bigNumbersSheet.Texture,
                pos + new Vector2(checkX * 0.9f, 0f) + frame.Offset * 0.9f,
                frame.SourceRect, Color.White * alpha, 0f, Vector2.Zero,
                0.9f, SpriteEffects.None, 0f);
        }
    }

    private Action GetRankBadgeAction(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha)
    {
        if (_rankBadgesSheet?.Texture == null) return null;

        string diff = GetCurrentDifficulty(song);
        string rank = HighscoreManager.GetRank(song.Name, diff);
        if (string.IsNullOrEmpty(rank)) return null;

        // Map rank string to atlas animation prefix
        string prefix = rank.ToUpperInvariant() switch
        {
            "PERFECT_GOLD" or "PERFECT GOLD" => "PERFECT rank GOLD",
            "PERFECT" => "PERFECT rank",
            "EXCELLENT" => "EXCELLENT rank",
            "GREAT" => "GREAT rank",
            "GOOD" => "GOOD rank",
            "LOSS" => "LOSS rank",
            _ => null
        };
        if (prefix == null) return null;

        var frames = _rankBadgesSheet.GetAnimation(prefix);
        if (frames == null || frames.Count == 0) return null;

        int fi = (int)((uint)Environment.TickCount / 83u % (uint)frames.Count);
        var frame = frames[fi];

        // Original: ranking at (420, 41) relative to capsule, scale 0.9 (from SongMenuItem.hx)
        float rankScale = 0.9f;
        float rankAlpha = Math.Max(alpha, 0.7f); // original: rank badge alpha min 0.7 for unselected
        // Original: ranking.color = isSelected ? 0xFFFFFFFF : 0xFFAAAAAA
        Color rankColor = alpha >= 1f ? Color.White : new Color(0xAA, 0xAA, 0xAA);
        Vector2 pos = capsulePos + new Vector2(420f, 41f);

        return () =>
        {
            sb.Draw(_rankBadgesSheet.Texture,
                pos + frame.Offset * rankScale,
                frame.SourceRect, rankColor * rankAlpha,
                0f, Vector2.Zero, rankScale, SpriteEffects.None, 0f);
        };
    }

    private void DrawDifficulty(SpriteBatch sb)
    {
        float y = DIFF_Y;
        // Original: offset.y -= 5 for 1/24s on difficulty change (bounce up)
        float yBounce = _diffYBounceTimer > 0 ? -5f : 0f;

        // During transition: draw old difficulty sliding out
        if (_diffTransitioning)
        {
            DrawDifficultyStamp(sb, _diffOldIndex,
                _diffOldX + DjBaseX + _exitDiffXOffset,
                y + yBounce,
                1f,
                animateNightmare: false);
        }

        float x = (_diffTransitioning ? _diffNewX : _diffSlideX) + DjBaseX + _exitDiffXOffset;
        // Original: alpha flashes to 0.5 for 1/24s on difficulty change
        float diffAlpha = _diffAlphaFlashTimer > 0 ? 0.5f : 1f;
        DrawDifficultyStamp(sb, _difficultyIndex, x, y + yBounce, diffAlpha, animateNightmare: true);
    }

    private void DrawDifficultyStamp(SpriteBatch sb, int difficultyIndex, float x, float y, float alpha, bool animateNightmare)
    {
        if (difficultyIndex == 4 && _diffNightmareFrames != null && _diffNightmareFrames.Count > 0 && _diffNightmareSheet?.Texture != null)
        {
            int frameIndex = animateNightmare
                ? (int)((uint)Environment.TickCount / 41u % (uint)_diffNightmareFrames.Count)
                : 0;
            var frame = _diffNightmareFrames[Math.Clamp(frameIndex, 0, _diffNightmareFrames.Count - 1)];
            sb.Draw(_diffNightmareSheet.Texture,
                new Vector2(x, y) + frame.Offset,
                frame.SourceRect,
                Color.White * alpha,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0f);
            return;
        }

        var tex = difficultyIndex switch
        {
            0 => _diffEasy,
            1 => _diffNormal,
            2 => _diffHard,
            3 => _diffErect,
            4 => _diffNightmare,
            _ => _diffNormal
        };
        if (tex == null || tex == Assets.Pixel)
            return;

        sb.Draw(tex,
            new Vector2(x, y),
            null,
            Color.White * alpha,
            0f,
            Vector2.Zero,
            1f,
            SpriteEffects.None,
            0f);
    }

    private void DrawDifficultyArrows(SpriteBatch sb)
    {
        if (_phase != Phase.Idle) return;
        float y = DIFF_Y - 10f; // original: grpDifficulties.y - 10
        float exitX = _exitArrowOffset + _exitDiffXOffset; // arrows share difficulty X exit
        float baseX = DjBaseX;

        // Left arrow at x=20 (not flipped) — push: scale 0.5, offset.y -= 5
        if (_selectorLeft != null)
        {
            _selectorLeft.EnsureCurrentFrameRendered(Game.GraphicsDevice);
            bool leftPush = _leftArrowPush > 0;
            _selectorLeft.Position = new Vector2(baseX + 20f + exitX, leftPush ? y - 5f : y);
            _selectorLeft.Scale = leftPush ? new Vector2(0.5f) : Vector2.One;
            _selectorLeft.Tint = leftPush ? Color.White : Color.White;
            _selectorLeft.Draw(sb);
        }

        // Right arrow at x=325 (flipped horizontally) — push: scale 0.5, offset.y -= 5
        if (_selectorRight != null)
        {
            _selectorRight.EnsureCurrentFrameRendered(Game.GraphicsDevice);
            bool rightPush = _rightArrowPush > 0;
            _selectorRight.Position = new Vector2(baseX + 325f + exitX, rightPush ? y - 5f : y);
            _selectorRight.Scale = rightPush ? new Vector2(0.5f) : Vector2.One;
            _selectorRight.Effects = SpriteEffects.FlipHorizontally;
            _selectorRight.Draw(sb);
        }
    }

    private void DrawDifficultyDots(SpriteBatch sb)
    {
        if (_separator == null || _separator == Assets.Pixel) return;
        if (!_dotsVisible) return;
        if (_songs.Count == 0) return;

        var song = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];
        var songDifficulties = GetSongDifficulties(song);
        int selectedSongDiff = GetSongDifficultyIndex(song);

        // Original refreshDots: distance=30, groupOffset=14.7, shiftAmt=(distance*count)/2
        // difficultyDots.x = 260 - 14.7*(count-1); dot.x = difficultyDots.x + 30*i - shiftAmt
        int dotCount = Math.Max(1, songDifficulties.Length);
        float distance = 30f;
        float groupOffsetPer = 14.7f;
        float dotsGroupX = 260f - groupOffsetPer * (dotCount - 1);
        float shiftAmt = (distance * dotCount) / 2f;
        float baseY = 170f;
        for (int i = 0; i < dotCount; i++)
        {
            float dotX = DjBaseX + (dotsGroupX + distance * i) - shiftAmt + _exitDiffXOffset;
            bool selected = i == selectedSongDiff;
            // Original: deselected=0xFF484848, selected=0xFFFAFAFA
            Color dotColor = selected ? new Color(0xFA, 0xFA, 0xFA) : new Color(0x48, 0x48, 0x48);
            sb.Draw(_separator,
                new Vector2(dotX, baseY), null, dotColor,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

            // Draw dotPulse animation overlay for selected dot
            if (selected && _dotPulseFrames != null && _dotPulseFrames.Count > 0 && _dotPulseSheet?.Texture != null)
            {
                int frameIdx = (int)((DateTime.Now.Millisecond / 83.3f) % _dotPulseFrames.Count);
                var pFrame = _dotPulseFrames[frameIdx];
                float pulseX = dotX + (_separator.Width / 2f) - (pFrame.SourceRect.Width / 2f);
                float pulseY = baseY + (_separator.Height / 2f) - (pFrame.SourceRect.Height / 2f);
                sb.Draw(_dotPulseSheet.Texture,
                    new Vector2(pulseX, pulseY) + pFrame.Offset, pFrame.SourceRect,
                    dotColor * 0.6f,
                    0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
            }
        }
    }

    private void DrawConfirmGlow(SpriteBatch sb)
    {
        if (_phase != Phase.Confirmed) return;
        // Original: confirmGlow2 at (CUTOUT_WIDTH * DJ_POS_MULTI - 30, 240)
        float glowX = PINK_PANEL_W * 0.74f - 30f;
        if (_confirmGlow2 != null && _confirmGlow2 != Assets.Pixel && _confirmGlow2Alpha > 0)
        {
            sb.Draw(_confirmGlow2,
                new Vector2(glowX, 240f), null,
                Color.White * _confirmGlow2Alpha,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
        if (_confirmGlow != null && _confirmGlow != Assets.Pixel && _confirmGlowAlpha > 0)
        {
            sb.Draw(_confirmGlow,
                new Vector2(glowX, 240f), null,
                Color.White * _confirmGlowAlpha,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
        // Original: confirmTextGlow at (CUTOUT_WIDTH * DJ_POS_MULTI - 8, 115)
        if (_confirmTextGlow != null && _confirmTextGlow != Assets.Pixel && _confirmTextGlowAlpha > 0)
        {
            float textGlowX = PINK_PANEL_W * 0.74f - 8f;
            sb.Draw(_confirmTextGlow,
                new Vector2(textGlowX, 115f), null,
                Color.White * _confirmTextGlowAlpha,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
    }

    private void DrawCharSelectHint(SpriteBatch sb)
    {
        if (!_introDone) return;
        // Original: charSelectHint at center bottom, font "5by7" size 32, color 0xFF5F5F5F
        // Alpha pulses via sine wave (original: targetAmt = (sin(hintTimer)+1)/2, alpha = lerp(0.3, 0.9, targetAmt))
        var font = Assets.GetFont(32);
        if (font == null) return;
        float targetAmt = (MathF.Sin(_hintAlphaTimer) + 1f) / 2f;
        float hintAlpha = MathHelper.Lerp(0.3f, 0.9f, targetAmt);
        var hintCol = new Color(0x5F, 0x5F, 0x5F) * hintAlpha;
        float y = FNFGame.SCREEN_HEIGHT - 50f + _hintSlideY;

        // Show controller button sprite when last-used device was a controller, otherwise TAB text
        bool useController = Input.GamePadConnected && Input.LastDevice == InputManager.InputDevice.Controller;
        string spriteName = useController ? OptionsScene.GetButtonSpriteName(Input.SwitchCharButton) : null;
        Texture2D btnTex = spriteName != null ? Assets.LoadTexture($"game/ui/controller/{spriteName}.png") : null;

        if (useController && btnTex != null && btnTex != Assets.Pixel)
        {
            string pre = "Press ";
            string post = " to change characters";
            var preSz = font.MeasureString(pre);
            var postSz = font.MeasureString(post);
            int iconSize = 40;
            int gap = 6;
            float totalW = preSz.X + gap + iconSize + gap + postSz.X;
            float x = (FNFGame.SCREEN_WIDTH - totalW) / 2f;
            font.DrawText(sb, pre, new Vector2(x, y), hintCol);
            float iconX = x + preSz.X + gap;
            float iconY = y + (preSz.Y - iconSize) / 2f;
            sb.Draw(btnTex, new Rectangle((int)iconX, (int)iconY, iconSize, iconSize), Color.White * hintAlpha);
            font.DrawText(sb, post, new Vector2(iconX + iconSize + gap, y), hintCol);
        }
        else
        {
            string label = useController ? OptionsScene.FormatButtonName(Input.SwitchCharButton) : "TAB";
            string hint = $"Press [ {label} ] to change characters";
            var sz = font.MeasureString(hint);
            float x = (FNFGame.SCREEN_WIDTH - sz.X) / 2f;
            font.DrawText(sb, hint, new Vector2(x, y), hintCol);
        }
    }

    private void DrawLetterSort(SpriteBatch sb)
    {
        if (_phase != Phase.Idle) return;
        if (_sortedLettersSheet?.Texture == null) return;
        // Original: LetterSort at ((CUTOUT_WIDTH * SONGS_POS_MULTI) + 400, 75)
        float baseX = SongsBaseX + 400f;
        float baseY = 75f + _exitLetterYOffset;
        float slotWidth = 80f;

        float xOffset = GetLetterWiggleOffset();

        // Draw left miniArrow at (-20, 15) in group (flipped)
        if (_miniArrow != null && _miniArrow != Assets.Pixel)
        {
            sb.Draw(_miniArrow,
                new Vector2(baseX - 20f + xOffset, baseY + 15f), null, Color.White,
                0f, Vector2.Zero, 1f, SpriteEffects.FlipHorizontally, 0f);
        }

        // Draw 5 letter slots centered on _letterSortIndex using the atlas (matches FNF_Official)
        for (int i = 0; i < 5; i++)
        {
            int labelIdx = _letterSortIndex - 2 + i;
            if (labelIdx < 0 || labelIdx >= LetterSortLabels.Length) continue;

            float slotX = baseX + 50f + i * slotWidth + xOffset;
            float slotY = baseY;

            float darkness = Math.Max(Math.Abs(i - 2) / 6f, 0.01f);
            float scale = i == 2 ? 1f : 0.8f;
            byte c = (byte)(255 * (1f - darkness));
            Color col = new Color(c, c, c);

            string label = LetterSortLabels[labelIdx];
            string animName = label switch
            {
                "#" => "#",
                "fav" => "fav",
                "ALL" => "ALL",
                _ => label
            };

            var frames = _sortedLettersSheet.GetAnimation(animName);
            SpriteFrame frame;
            if (frames != null && frames.Count > 0)
            {
                frame = frames[0];
            }
            else if (_sortedLettersSheet.Frames.TryGetValue(animName, out var singleFrame))
            {
                frame = singleFrame;
            }
            else
            {
                continue;
            }

            // In the original, each FreeplayLetter is offset by +50,+50; approximate by centering in our 80x50 slot.
            float drawX = slotX + (slotWidth - frame.SourceRect.Width * scale) * 0.5f;
            float drawY = slotY + (50f - frame.SourceRect.Height * scale) * 0.5f;

            sb.Draw(_sortedLettersSheet.Texture,
                new Vector2(drawX, drawY) + frame.Offset * scale,
                frame.SourceRect,
                col,
                0f,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                0f);

            // Draw separator between slots at (i*80+60, 20) in group (except last)
            if (i < 4 && _separator != null && _separator != Assets.Pixel)
            {
                sb.Draw(_separator,
                    new Vector2(baseX + 60f + i * 80f + xOffset, baseY + 20f), null, col,
                    0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
            }
        }

        // Draw right miniArrow at (380, 15) in group
        if (_miniArrow != null && _miniArrow != Assets.Pixel)
        {
            sb.Draw(_miniArrow,
                new Vector2(baseX + 380f + xOffset, baseY + 15f), null, Color.White,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }

    }

    private void StartLetterWiggle(int dir)
    {
        _letterWiggleDir = Math.Sign(dir);
        if (_letterWiggleDir == 0) _letterWiggleDir = 1;
        _letterWiggleTimer = 3f / 24f;
    }

    private float GetLetterWiggleOffset()
    {
        if (_letterWiggleTimer <= 0f)
            return 0f;

        float elapsed = (3f / 24f) - _letterWiggleTimer;
        float frame = elapsed * 24f;
        float v = frame switch
        {
            < 1f => -10f,
            < 2f => -22f,
            < 3f => 2f,
            _ => 0f
        };

        return v * _letterWiggleDir;
    }


    private void DrawAlbumRoll(SpriteBatch sb)
    {
        if (!_albumVisible) return;
        // Original: AlbumRoll at (FlxG.width - 360, 220)
        float albumX = FNFGame.SCREEN_WIDTH - 360f + _exitAlbumOffset;
        float albumY = 220f;

        if (_albumArtTexture != null && _albumArtTexture != Assets.Pixel)
        {
            sb.Draw(_albumArtTexture,
                new Vector2(albumX, albumY),
                Color.White);
        }

        if (_albumRollSprite?.Sheet?.Texture != null)
        {
            _albumRollSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);
            _albumRollSprite.Position = new Vector2(albumX, albumY);
            _albumRollSprite.Scale = Vector2.One;
            _albumRollSprite.Draw(sb);
        }

        // Album title (e.g. "Vol. 1") at (FlxG.width - 330, 209)
        if (_albumTitleVisible && _albumTitleSprite?.Sheet?.Texture != null)
        {
            _albumTitleSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);
            _albumTitleSprite.Position = new Vector2(FNFGame.SCREEN_WIDTH - 330f + _exitAlbumOffset, 209f);
            _albumTitleSprite.Scale = Vector2.One;
            _albumTitleSprite.Draw(sb);
        }

        if (_albumStarsVisible && _freeplayStarsSprite?.Sheet?.Texture != null)
        {
            _freeplayStarsSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);
            _freeplayStarsSprite.Position = new Vector2(FNFGame.SCREEN_WIDTH - 330f + _exitAlbumOffset, 209f);
            _freeplayStarsSprite.Scale = Vector2.One;
            _freeplayStarsSprite.Draw(sb);
        }
    }

    private void DrawScoreDisplay(SpriteBatch sb)
    {
        // Original: FreeplayScore at (FlxG.width - 353, 60), highscore at (FlxG.width - 420, 70)
        float highscoreX = FNFGame.SCREEN_WIDTH - 420f + _exitScoreOffset;
        float highscoreY = 70f;
        float scoreX = FNFGame.SCREEN_WIDTH - 353f + _exitScoreOffset;
        float scoreY = 60f;

        if (_highscoreSprite?.Sheet?.Texture != null)
        {
            _highscoreSprite.Position = new Vector2(highscoreX, highscoreY);
            _highscoreSprite.Scale = Vector2.One;
            _highscoreSprite.Draw(sb);
        }

        int displayScore = (int)_lerpScore;
        string scoreStr = displayScore.ToString("D7");

        // Draw score using digit atlas sprites (original: FreeplayScore, 7 digits, 45px spacing, 0.4x scale)
        if (_digitSheet?.Texture != null && _digitSheet.Frames != null)
        {
            float digitScale = 0.4f;
            float digitSpacing = 45f * digitScale; // ~18px per digit
            for (int i = 0; i < scoreStr.Length; i++)
            {
                int digit = scoreStr[i] - '0';
                if (digit < 0 || digit > 9) continue;
                string frameName = DigitNames[digit] + " DIGITAL0005"; // settled frame (last)
                if (_digitSheet.Frames.TryGetValue(frameName, out var frame))
                {
                    // Original FreeplayScore: digit "1" has offset.x -= 15
                    float digitOffsetX = digit == 1 ? -15f * digitScale : 0f;
                    sb.Draw(_digitSheet.Texture,
                        new Vector2(scoreX + i * digitSpacing + digitOffsetX, scoreY) + frame.Offset * digitScale,
                        frame.SourceRect, Color.White,
                        0f, Vector2.Zero, digitScale, SpriteEffects.None, 0f);
                }
            }
        }
        else
        {
            // Fallback: plain font if digit atlas not loaded
            var font = Assets.GetFont(40);
            if (font != null)
            {
                font.DrawText(sb, scoreStr, new Vector2(scoreX, scoreY + 10f), Color.White);
            }
        }

        // Original: clearBox at (FlxG.width - 115, 65)
        if (_clearBox != null && _clearBox != Assets.Pixel)
        {
            sb.Draw(_clearBox,
                new Vector2(FNFGame.SCREEN_WIDTH - 115f + _exitScoreOffset, 65f), null, Color.White,
                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }

        // Original: txtCompletion at (FlxG.width - 95, 87), shows completion % (uses lerpCompletion)
        var clearFont = Assets.GetFont(32);
        if (clearFont != null)
        {
            int compVal = (int)(_lerpCompletion * 100f);
            string compText = compVal > 0 ? compVal.ToString() : "0";
            // Original: right-align by digit count (3→offset 10, 2→0, 1→-24)
            float compOffsetX = compText.Length switch { 3 => 10f, 2 => 0f, 1 => -24f, _ => 0f };
            clearFont.DrawText(sb, compText, new Vector2(FNFGame.SCREEN_WIDTH - 95f + compOffsetX + _exitScoreOffset, 87f), Color.White);
        }
    }

    private void DrawOverhang(SpriteBatch sb)
    {
        // Original: 164px sprite at y=-100, so 64px visible at top
        // _overhangY tweens from -164 to -100; visible portion = _overhangY + 164
        float overhangYExit = _overhangY + _exitOverhangYOffset;
        float visibleH = overhangYExit + 164f;
        if (visibleH <= 0) return;
        int overhangH = (int)visibleH;

        var font = Assets.GetFont(40);
        float barY = overhangH - 64f; // text area offset

        if (_introDone && font != null)
        {
            // Draw the arrow BEHIND the black bar area (only shows during exit tween)
            font.DrawText(sb, "<---", new Vector2(8, barY + 8), Color.White);
        }

        // Black overhang bar
        sb.Draw(Assets.Pixel,
            new Rectangle(0, (int)_exitOverhangYOffset, FNFGame.SCREEN_WIDTH, overhangH),
            Color.Black);

        if (font == null) return;

        if (_introDone)
        {
            // freeplayTxtBg (black rect behind FREEPLAY text)
            string freeplayText = "FREEPLAY";
            var textSize = font.MeasureString(freeplayText);
            sb.Draw(Assets.Pixel,
                new Rectangle(0, (int)(barY + _exitOverhangYOffset), (int)(textSize.X + 24), (int)(textSize.Y + 24)),
                Color.Black);
            // FREEPLAY text on top
            font.DrawText(sb, freeplayText, new Vector2(8, barY + 8 + _exitOverhangYOffset), Color.White);

            // ostName right-aligned, 'OFFICIAL OST'
            string ostText = "OFFICIAL OST";
            var ostSize = font.MeasureString(ostText);
            float ostX = FNFGame.SCREEN_WIDTH - 8f - ostSize.X;
            font.DrawText(sb, ostText, new Vector2(ostX, barY + 8 + _exitOverhangYOffset), Color.White);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════

    private void LoadDJ()
    {
        string[] djFilter;
        string sheetPath;
        if (_isPicoTheme)
        {
            djFilter = new[]
            {
                "pico dj intro", "Pico DJ", "Pico DJ confirm",
                "Pico DJ cheer", "Pico DJ loss", "Pico DJ afk"
            };
            sheetPath = "menus/freeplay/freeplay-pico";
        }
        else
        {
            djFilter = new[]
            {
                "boyfriend dj intro", "Boyfriend DJ", "Boyfriend DJ confirm",
                "Boyfriend DJ fist pump", "Boyfriend DJ loss reaction 1", "bf dj afk"
            };
            sheetPath = "menus/freeplay/freeplay-boyfriend";
        }

        _djSheet = SpriteSheet.Load(Game, sheetPath,
            preRenderComposites: true, preRenderFilter: djFilter);
        if (_djSheet == null) return;

        _djSprite = new AnimatedSprite { Sheet = _djSheet };

        if (_isPicoTheme)
        {
            _djIntroAnim = FindDJAnim("pico dj intro");
            _djIdleAnim = FindDJAnim("Pico DJ");
            _djConfirmAnim = FindDJAnim("Pico DJ confirm");
            _djAfkAnim = FindDJAnim("Pico DJ afk");
        }
        else
        {
            _djIntroAnim = FindDJAnim("boyfriend dj intro");
            _djIdleAnim = FindDJAnim("Boyfriend DJ");
            _djConfirmAnim = FindDJAnim("Boyfriend DJ confirm");
            _djAfkAnim = FindDJAnim("bf dj afk");
        }

        if (!string.IsNullOrEmpty(_djIntroAnim))
        {
            _djSprite.PlayAnimation(_djIntroAnim, loop: false);
            _djSprite.OnFinish = () =>
            {
                if (!string.IsNullOrEmpty(_djIdleAnim))
                    _djSprite.PlayAnimation(_djIdleAnim, loop: true);
                OnDJIntroDone();
            };
        }
        else if (!string.IsNullOrEmpty(_djIdleAnim))
        {
            _djSprite.PlayAnimation(_djIdleAnim, loop: true);
            OnDJIntroDone();
        }
    }

    private void OnDJIntroDone()
    {
        _introDone = true;
        _introRevealTimer = 0f;
        _introUiRevealed = false;
        _hintSlideTweenTimer = 0f;
        // Original: pinkBack.color = 0xFFFFD863 (gold)
        _pinkColor = _isPicoTheme
            ? new Color(0x61, 0x95, 0xFF)
            : new Color(0xFF, 0xD8, 0x63);
        // Original: orangeBackShit.visible = true, alsoOrangeLOL.visible = true
        _orangeBarsVisible = true;
        // Original: cardGlow.visible = true, alpha fades to 0 with scale 1.2 (0.45s sineOut)
        _cardGlowAlpha = 1f;
        _cardGlowScale = 1f;
        _cardGlowDuration = 0.45f;
        // Original: backingImage color fades from black to white over 0.6s
        // We'll lerp _bgRevealTint from 0 to 1 in Update
        // Original: fadeDots(true) — show dots after intro (after slight delay)
        _dotsVisible = false;
        // Album roll: visible + play intro
        _albumVisible = true;
        StartAlbumIntro();
    }

    private string FindDJAnim(string name)
    {
        if (_djSheet == null) return null;
        if (_djSheet.Animations.ContainsKey(name)) return name;
        foreach (var k in _djSheet.Animations.Keys)
        {
            if (k.Equals(name, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        foreach (var k in _djSheet.Animations.Keys)
        {
            if (k.Contains(name, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        return null;
    }

    private SpriteSheet EnsurePixelIcon(string charName)
    {
        if (string.IsNullOrEmpty(charName)) charName = "dad";

        if (_pixelIconSheets.TryGetValue(charName, out var existing))
            return existing;

        string iconName = MapCharToPixelIcon(charName);
        var sheet = SpriteSheet.Load(Game, $"menus/freeplay/icons/{iconName}pixel");
        _pixelIconSheets[charName] = sheet;
        return sheet;
    }

    private static string MapCharToPixelIcon(string charName)
    {
        return charName.ToLowerInvariant() switch
        {
            "gf" or "gf-christmas" or "gf-car" or "gf-pixel" or "gf-tankmen" => "gf",
            "dad" or "dad-christmas" => "dad",
            "spooky" or "spooky-dark" => "spooky",
            "pico" or "pico-player" or "pico-blazin" or "pico-christmas" => "pico",
            "mom" or "mom-car" => "mom",
            "parents-christmas" => "parents-christmas",
            "senpai" or "senpai-angry" => "senpai",
            "spirit" => "spirit",
            "tankman" => "tankman",
            "bf" or "bf-pixel" or "bf-christmas" or "bf-car" or "bf-holding-gf" => "bf",
            "bf-pixel-dead" => "bf",
            "monster" or "monster-christmas" => "monster",
            "darnell" or "darnell-blazin" => "darnell",
            "nene" or "nene-christmas" => "bf",
            "sserafim-kazuha" => "sserafim-kazuha",
            _ => "dad"
        };
    }

    private static string ClipText(SpriteFontBase font, string text, float maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string sub = text[..len] + "...";
            if (font.MeasureString(sub).X <= maxWidth)
                return sub;
        }
        return text[..1] + "...";
    }

    private void OnSelectionChanged()
    {
        // FNF_Official: fade out preview audio before starting the next preview.
        _pendingPreviewSong = null;
        _activePreviewSong = null;
        _activePreviewStartMs = 0;
        _activePreviewEndMs = 0;
        BeginPreviewFadeOut();
        _songPreviewDelay = 0;
        _songPreviewPlaying = false;
        UpdateSelection();
        UpdateAlbumForSelection();
        SwitchBackingImage();
    }

    private void UpdateAlbumForSelection()
    {
        string albumId = null;
        if (_songs.Count > 0)
        {
            var song = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];
            if (song != null && !song.IsRandom && !string.IsNullOrWhiteSpace(song.Album))
                albumId = song.Album;
            else if (song != null && !song.IsRandom)
                albumId = "volume1";
        }

        if (albumId == null)
        {
            _currentAlbumId = null;
            _albumVisible = false;
            _albumTitleVisible = false;
            _albumStarsVisible = false;
            _albumArtTexture = null;
            _albumRevealTimer = 0f;
            return;
        }

        _albumVisible = true;

        bool albumChanged = !string.Equals(_currentAlbumId, albumId, StringComparison.OrdinalIgnoreCase);
        if (!albumChanged)
            return;

        _currentAlbumId = albumId;

        _albumTitleSheet?.Dispose();
        _albumTitleSheet = SpriteSheet.Load(Game, $"menus/freeplay/albumRoll/{albumId}-text")
            ?? SpriteSheet.Load(Game, "menus/freeplay/albumRoll/volume1-text");
        _albumArtTexture = Assets.LoadTexture($"menus/freeplay/albumRoll/{albumId}.png");
        if (_albumArtTexture == null || _albumArtTexture == Assets.Pixel)
            _albumArtTexture = Assets.LoadTexture("menus/freeplay/albumRoll/volume1.png");

        if (_albumTitleSheet != null)
        {
            _albumTitleSprite = new AnimatedSprite { Sheet = _albumTitleSheet };
            PlayAlbumTitleState("idle", loop: true, force: true);
        }

        RefreshAlbumStarsFrame();

        if (!_introDone)
            return;

        if (!_albumIntroPlayed)
        {
            StartAlbumIntro();
            return;
        }

        PlayAlbumTransition("switch");
    }

    private void StartAlbumIntro()
    {
        if (_albumRollSprite == null)
            return;

        _albumVisible = true;
        _albumIntroPlayed = true;
        _albumTitleVisible = false;
        _albumStarsVisible = false;
        _albumRevealTimer = ALBUM_REVEAL_DELAY;

        _albumRollSprite.OnFinish = () =>
        {
            _albumRollSprite.OnFinish = null;
            PlayAlbumRollState("idle", loop: true, force: true);
        };
        PlayAlbumRollState("intro", loop: false, force: true);
    }

    private void PlayAlbumTransition(string transitionState)
    {
        if (_albumRollSprite == null)
            return;

        _albumRollSprite.OnFinish = () =>
        {
            _albumRollSprite.OnFinish = null;
            PlayAlbumRollState("idle", loop: true, force: true);
        };

        PlayAlbumRollState(transitionState, loop: false, force: true);

        if (_albumTitleSprite != null)
        {
            _albumTitleVisible = true;
            _albumTitleSprite.OnFinish = () =>
            {
                _albumTitleSprite.OnFinish = null;
                PlayAlbumTitleState("idle", loop: true, force: true);
            };
            PlayAlbumTitleState("switch", loop: false, force: true);
        }

        _albumStarsVisible = true;
        RefreshAlbumStarsFrame();
    }

    private void RefreshAlbumStarsFrame()
    {
        if (_freeplayStarsSprite?.Sheet?.Animations == null)
            return;

        string key = FindAlbumAnimationKey(_freeplayStarsSprite.Sheet, "stars");
        if (key == null)
            return;

        var frames = _freeplayStarsSprite.Sheet.GetAnimation(key);
        if (frames == null || frames.Count == 0)
        {
            _freeplayStarsSprite.PlayAnimation(key, force: true, loop: false);
            return;
        }

        int frame = Math.Clamp((int)MathF.Round((_albumDifficultyRating / 20f) * (frames.Count - 1)), 0, frames.Count - 1);
        _freeplayStarsSprite.PlayAnimationFromFrame(key, frame, loop: false);
    }

    private void PlayAlbumRollState(string state, bool loop, bool force)
    {
        if (_albumRollSprite?.Sheet?.Animations == null)
            return;

        string key = FindAlbumAnimationKey(_albumRollSprite.Sheet, state);
        if (key != null)
            _albumRollSprite.PlayAnimation(key, loop: loop, force: force);
    }

    private void PlayAlbumTitleState(string state, bool loop, bool force)
    {
        if (_albumTitleSprite?.Sheet?.Animations == null)
            return;

        string key = FindAlbumAnimationKey(_albumTitleSprite.Sheet, state);
        if (key != null)
            _albumTitleSprite.PlayAnimation(key, loop: loop, force: force);
    }

    private string FindAlbumAnimationKey(SpriteSheet sheet, string state)
    {
        if (sheet?.Animations == null || sheet.Animations.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(_currentAlbumId))
        {
            foreach (var key in sheet.Animations.Keys)
            {
                if (key.Contains(_currentAlbumId, StringComparison.OrdinalIgnoreCase)
                    && key.Contains(state, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
        }

        foreach (var key in sheet.Animations.Keys)
        {
            if (key.Contains(state, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        if (!string.IsNullOrEmpty(_currentAlbumId))
        {
            foreach (var key in sheet.Animations.Keys)
            {
                if (key.Contains(_currentAlbumId, StringComparison.OrdinalIgnoreCase))
                    return key;
            }
        }

        foreach (var key in sheet.Animations.Keys)
            return key;

        return null;
    }

    /// <summary>
    /// Switches the BG image to match the current song's week/character (original: switchBackingImage).
    /// Pattern: freeplayBG{levelId}-{characterId}.png with fallback to freeplayBGweek1-bf.png.
    /// </summary>
    private void SwitchBackingImage()
    {
        if (_songs.Count == 0) return;
        var song = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];
        if (song.IsRandom) return;

        string weekId = song.WeekId ?? "week1";
        string selectedVariation = GetSelectedCharacterVariationId();
        string charId = selectedVariation;
        string bgKey = $"menus/freeplay/freeplayBG{weekId}-{charId}.png";
        var newBg = Assets.LoadTexture(bgKey);
        if (newBg != null && newBg != Assets.Pixel)
        {
            _bgImage = newBg;
            return;
        }

        string fallback = selectedVariation.Equals("pico", StringComparison.OrdinalIgnoreCase)
            ? "menus/freeplay/freeplayBGweek1-pico.png"
            : "menus/freeplay/freeplayBGweek1-bf.png";
        newBg = Assets.LoadTexture(fallback);
        if (newBg != null && newBg != Assets.Pixel)
            _bgImage = newBg;
        else
            _bgImage = Assets.LoadTexture("menus/freeplay/freeplayBGweek1-bf.png");
    }

    /// <summary>
    /// Rebuilds the filtered song list based on the current letter sort index.
    /// </summary>
    private void ApplyLetterFilter()
    {
        // Preserve selection like FNF_Official: keep current song if still in list (or if it was Random).
        string prevSongName = null;
        if (_songs.Count > 0)
        {
            var prev = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];
            if (prev != null && !prev.IsRandom)
                prevSongName = prev.Name;
        }

        string label = LetterSortLabels[_letterSortIndex];

        if (label == "ALL")
        {
            _songs = new List<FreeplaySong>(_allSongs);
        }
        else if (label == "fav")
        {
            var favs = HighscoreManager.Data.FavoriteSongs;
            _songs = _allSongs.Where(s => s.IsRandom || (!string.IsNullOrEmpty(s.Name) && favs.Contains(s.Name))).ToList();
        }
        else if (label == "#")
        {
            _songs = _allSongs.Where(s => s.IsRandom || (!string.IsNullOrEmpty(s.DisplayName) && !char.IsLetter(s.DisplayName[0]))).ToList();
        }
        else
        {
            // Letter range filter (e.g. "A-B", "C-D")
            char lo = label[0];
            char hi = label.Length >= 3 ? label[2] : lo;
            _songs = _allSongs.Where(s =>
            {
                if (s.IsRandom) return true;
                if (string.IsNullOrEmpty(s.DisplayName)) return false;
                char first = char.ToUpperInvariant(s.DisplayName[0]);
                return first >= lo && first <= hi;
            }).ToList();
        }

        _songs = ApplyDifficultyFilter(_songs);

        // Always keep Random capsule at index 0
        var randomCapsule = _songs.FirstOrDefault(s => s.IsRandom);
        if (randomCapsule != null)
        {
            _songs.Remove(randomCapsule);
            _songs.Insert(0, randomCapsule);
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));
        _scrollLerp = _selectedIndex;

        if (prevSongName == null)
        {
            // Random was selected; keep it.
            _selectedIndex = 0;
        }
        else
        {
            int idx = _songs.FindIndex(s => !s.IsRandom && string.Equals(s.Name, prevSongName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _selectedIndex = idx;
            }
            else if (_songs.Count > 1)
            {
                // Jump to first real song (avoid landing on Random)
                _selectedIndex = 1;
            }
            else
            {
                _selectedIndex = 0;
            }
        }

        _scrollLerp = _selectedIndex;
        OnSelectionChanged();
    }

    private List<FreeplaySong> ApplyDifficultyFilter(IEnumerable<FreeplaySong> songs)
    {
        string currentDifficulty = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
        return songs
            .Where(song => song != null
                && (song.IsRandom || SongMatchesSelectedCharacter(song))
                && (song.IsRandom
                    || SongHasDifficulty(song, currentDifficulty)
                    || HasDifficultyVariant(song, currentDifficulty)))
            .ToList();
    }

    private bool SongMatchesSelectedCharacter(FreeplaySong song)
    {
        if (song == null || song.IsRandom)
            return true;

        string selectedVariation = GetSelectedCharacterVariationId();
        bool isPicoDefaultSong = IsPicoCharacterId(song.PlayerCharacter);

        if (selectedVariation.Equals("pico", StringComparison.OrdinalIgnoreCase))
        {
            return isPicoDefaultSong
                || SongHasCharacterVariation(song, "pico");
        }

        return !isPicoDefaultSong
            || SongHasCharacterVariation(song, "bf");
    }

    private static bool IsPicoCharacterId(string characterId)
        => !string.IsNullOrWhiteSpace(characterId)
            && characterId.Contains("pico", StringComparison.OrdinalIgnoreCase);

    private static string GetSongIdentityKey(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            return string.Empty;

        return songName
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private bool SongHasCharacterVariation(FreeplaySong song, string characterVariation)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Name) || string.IsNullOrWhiteSpace(characterVariation))
            return false;

        string variationId = characterVariation.Trim().ToLowerInvariant();
        if (song.Name.EndsWith($"_{variationId}", StringComparison.OrdinalIgnoreCase))
            return true;

        if (song.SongVariations != null
            && song.SongVariations.Any(v => v.Equals(variationId, StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (string baseName in GetSongFolderCandidates(song.Name))
        {
            string variantName = $"{baseName}_{variationId}";
            if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null
                || Assets.ResolvePath($"songs/{variantName}/charts/chart.json") != null)
            {
                return true;
            }

            if (HasSongInstVariation(baseName, variationId))
                return true;
        }

        return false;
    }

    private bool HasSongInstVariation(string songName, string variationId)
    {
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(variationId))
            return false;

        return Assets.ResolvePath($"songs/{songName}/Inst-{variationId}.ogg") != null
            || Assets.ResolvePath($"songs/{songName}/tracks/Inst-{variationId}.ogg") != null;
    }

    private bool HasDifficultyVariant(FreeplaySong song, string difficulty)
    {
        if (song == null)
            return false;

        if (song.SongVariations != null
            && song.SongVariations.Any(v => v.Equals(difficulty, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase)
            && song.SongVariations != null
            && song.SongVariations.Any(v => v.Equals("erect", StringComparison.OrdinalIgnoreCase)))
            return true;

        return HasDifficultyVariant(song.Name, difficulty);
    }

    private static bool SongHasDifficulty(FreeplaySong song, string difficulty)
    {
        if (song?.Difficulties == null || song.Difficulties.Length == 0)
            return false;

        return song.Difficulties.Any(d => d.Equals(difficulty, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasDifficultyVariant(string songName, string difficulty)
    {
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(difficulty))
            return false;

        if (!difficulty.Equals("erect", StringComparison.OrdinalIgnoreCase)
            && !difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase))
            return false;

        if (songName.EndsWith($"_{difficulty}", StringComparison.OrdinalIgnoreCase))
            return true;

        if (difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase)
            && songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase))
            return true;

        if (songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)
            || songName.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase))
            return false;

        return TryResolveVariantName(songName, difficulty, out _);
    }

    private string ResolveSongNameForDifficulty(FreeplaySong song, string difficulty)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Name))
            return song?.Name;

        bool hasAnyVariantSuffix = song.Name.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)
            || song.Name.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase);

        if (!hasAnyVariantSuffix
            && !song.Name.EndsWith($"_{difficulty}", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveVariantName(song.Name, difficulty, out string variantName))
                return variantName;
        }

        return song.Name;
    }

    private string ResolveSongNameForSelection(FreeplaySong song, string difficulty)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Name))
            return song?.Name;

        string baseSongName = song.Name;
        string selectedVariation = GetSelectedCharacterVariationId();
        if (TryResolveCharacterVariantName(baseSongName, selectedVariation, out string characterSongName))
        {
            if (TryResolveVariantName(characterSongName, difficulty, out string characterDifficultyVariant))
                return characterDifficultyVariant;

            return characterSongName;
        }

        if (TryResolveVariantName(baseSongName, difficulty, out string difficultyVariant))
            return difficultyVariant;

        return baseSongName;
    }

    private bool TryResolveVariantName(string songName, string difficulty, out string resolvedSongName)
    {
        resolvedSongName = null;
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(difficulty))
            return false;

        foreach (string difficultyId in GetVariantSearchOrder(difficulty))
        {
            foreach (string baseName in GetSongFolderCandidates(songName))
            {
                string variantName = $"{baseName}_{difficultyId}";
                if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null
                    || Assets.ResolvePath($"songs/{variantName}/tracks/Inst-{difficultyId}.ogg") != null
                    || Assets.ResolvePath($"songs/{variantName}/tracks/Inst.ogg") != null)
                {
                    resolvedSongName = variantName;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveCharacterVariantName(string songName, string characterVariation, out string resolvedSongName)
    {
        resolvedSongName = null;
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(characterVariation))
            return false;

        string normalizedVariation = characterVariation.Trim().ToLowerInvariant();
        if (songName.EndsWith($"_{normalizedVariation}", StringComparison.OrdinalIgnoreCase))
        {
            resolvedSongName = songName;
            return true;
        }

        if (songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)
            || songName.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase))
            return false;

        // Pass 1: prefer real character-variant chart folders (e.g. *_pico)
        foreach (string baseName in GetSongFolderCandidates(songName))
        {
            string variantName = $"{baseName}_{normalizedVariation}";
            if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null
                || Assets.ResolvePath($"songs/{variantName}/charts/chart.json") != null)
            {
                resolvedSongName = variantName;
                return true;
            }
        }

        // Pass 2: fallback to base-song instrumental variation (Inst-{variation})
        foreach (string baseName in GetSongFolderCandidates(songName))
        {
            if (HasSongInstVariation(baseName, normalizedVariation))
            {
                resolvedSongName = baseName;
                return true;
            }
        }

        return false;
    }

    private static string GetSelectedCharacterVariationId()
    {
        string selectedCharacter = HighscoreManager.Data.SelectedCharacter?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(selectedCharacter))
            return "bf";

        if (selectedCharacter.Contains("pico", StringComparison.OrdinalIgnoreCase))
            return "pico";

        return "bf";
    }

    private static string GetSelectedCharacterDisplayName()
    {
        string variationId = GetSelectedCharacterVariationId();
        return variationId.ToUpperInvariant();
    }

    private static IEnumerable<string> GetVariantSearchOrder(string difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
            yield break;

        string normalized = difficulty.Trim().ToLowerInvariant();
        yield return normalized;

        if (normalized == "nightmare")
            yield return "erect";
    }

    private static IEnumerable<string> GetSongFolderCandidates(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
            }
        }

        AddCandidate(songName);
        AddCandidate(songName.Replace('-', '_'));
        AddCandidate(songName.Replace('_', '-'));
        AddCandidate(songName.Replace("-", "").Replace("_", ""));

        if (songName.Equals("dadbattle", StringComparison.OrdinalIgnoreCase))
            AddCandidate("dad_battle");

        foreach (var candidate in seen)
            yield return candidate;
    }

    private void UpdateSelection()
    {
        if (_songs.Count == 0) return;
        var song = _songs[_selectedIndex];
        SyncDifficultyToSong(song);
        if (song.IsRandom)
        {
            _displayedScore = 0;
            _intendedCompletion = 0f;
            _albumDifficultyRating = 0;
            RefreshAlbumStarsFrame();
            return;
        }
        string diff = GetCurrentDifficulty(song);
        _displayedScore = HighscoreManager.GetScore(song.Name, diff);
        _intendedCompletion = HighscoreManager.GetClearPercent(song.Name, diff);
        _albumDifficultyRating = 0;
        if (song.Ratings != null && song.Ratings.TryGetValue(diff, out int rating))
            _albumDifficultyRating = Math.Clamp(rating, 0, 20);
        RefreshAlbumStarsFrame();
    }

    private string GetCurrentDifficulty(FreeplaySong song)
    {
        string diff = Difficulties[_difficultyIndex];
        if (song != null && HasDifficultyVariant(song, diff))
            return diff;

        if (song.Difficulties != null && song.Difficulties.Length > 0)
        {
            foreach (string d in song.Difficulties)
            {
                if (d.Equals(diff, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return song.Difficulties[0];
        }
        return diff;
    }

    private string[] GetSongDifficulties(FreeplaySong song)
    {
        if (song?.Difficulties != null && song.Difficulties.Length > 0)
            return song.Difficulties;
        return Difficulties;
    }

    private int GetSongDifficultyIndex(FreeplaySong song)
    {
        var songDifficulties = GetSongDifficulties(song);
        string currentDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
        int idx = Array.FindIndex(songDifficulties, d => d.Equals(currentDiff, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }

    private void SyncDifficultyToSong(FreeplaySong song)
    {
        if (song == null || song.IsRandom)
            return;

        string currentDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
        if (SongHasDifficulty(song, currentDiff) || HasDifficultyVariant(song, currentDiff))
            return;

        var songDifficulties = GetSongDifficulties(song);
        if (songDifficulties.Length == 0)
            return;

        int idx = GetSongDifficultyIndex(song);
        string resolvedDiff = songDifficulties[Math.Clamp(idx, 0, songDifficulties.Length - 1)];
        int globalIdx = Array.FindIndex(Difficulties, d => d.Equals(resolvedDiff, StringComparison.OrdinalIgnoreCase));
        _difficultyIndex = globalIdx >= 0 ? globalIdx : 1;
    }

    private void PlaySongPreview(string songName)
    {
        if (!string.IsNullOrWhiteSpace(songName))
        {
            var selectedSong = _songs.FirstOrDefault(s => !s.IsRandom
                && string.Equals(s.Name, songName, StringComparison.OrdinalIgnoreCase));
            if (selectedSong != null)
            {
                string selectedDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
                songName = ResolveSongNameForSelection(selectedSong, selectedDiff);
            }
        }

        // Queue the preview; if music is currently playing, fade it out first.
        if (Audio.MusicPlaying)
        {
            _pendingPreviewSong = songName;
            BeginPreviewFadeOut();
            return;
        }

        StartPreview(songName);
    }

    private void BeginPreviewFadeOut()
    {
        if (!Audio.MusicPlaying)
            return;
        if (_previewFadingOut)
            return;

        _previewFadingIn = false;
        _previewFadingOut = true;
        _previewFadeOutTimer = 0f;
    }

    private void StartPreview(string songName)
    {
        int previewStartMs = 0;
        int previewEndMs = 0;
        string previewDifficulty = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
        if (!string.IsNullOrEmpty(songName))
        {
            var previewSong = _songs.FirstOrDefault(s => !s.IsRandom
                && string.Equals(s.Name, songName, StringComparison.OrdinalIgnoreCase));
            if (previewSong != null)
            {
                previewStartMs = Math.Max(0, previewSong.PreviewStartMs);
                previewEndMs = Math.Max(0, previewSong.PreviewEndMs);
            }
        }

        // Random capsule preview
        if (string.IsNullOrEmpty(songName))
        {
            Audio.PlayMusic("music/freeplayRandom", true);
            if (Audio.MusicPlaying)
            {
                Audio.MusicVolume = PREVIEW_START_VOLUME;
                _previewFadeTimer = 0f;
                _previewFadingIn = true;
            }
            _activePreviewSong = null;
            _activePreviewStartMs = 0;
            _activePreviewEndMs = 0;
            return;
        }

        // Song Inst preview (path fallbacks match existing project conventions)
        var instCandidates = new List<string>();
        string previewDiffId = (previewDifficulty ?? "normal").Trim().ToLowerInvariant();
        if (previewDiffId == "nightmare")
        {
            instCandidates.Add("Inst-nightmare");
            instCandidates.Add("Inst-erect");
        }
        else if (previewDiffId == "erect")
        {
            instCandidates.Add("Inst-erect");
        }
        instCandidates.Add("Inst");

        string noSep = songName.Replace("_", "").Replace("-", "");
        string hyphen = songName.Replace('_', '-');
        foreach (string inst in instCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Audio.MusicPlaying)
                break;

            Audio.PlayMusic($"songs/{songName}/{inst}", true, previewStartMs);
            if (Audio.MusicPlaying)
                break;

            Audio.PlayMusic($"songs/{noSep}/{inst}", true, previewStartMs);
            if (Audio.MusicPlaying)
                break;

            Audio.PlayMusic($"songs/{hyphen}/{inst}", true, previewStartMs);
        }

        if (Audio.MusicPlaying)
        {
            Audio.MusicVolume = PREVIEW_START_VOLUME;
            _previewFadeTimer = 0f;
            _previewFadingIn = true;
            _activePreviewSong = songName;
            _activePreviewStartMs = previewStartMs;
            _activePreviewEndMs = previewEndMs;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  SONG LIST LOADING
    // ═══════════════════════════════════════════════════════

    private void LoadSongList()
    {
        _allSongs.Clear();
        var addedSongKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        string levelsDir = Assets.ResolveDirectory("data/levels")
                        ?? Path.Combine(contentPath, "data", "levels");
        if (Directory.Exists(levelsDir))
        {
            string[] weekOrder = { "tutorial", "week1", "week2", "week3", "week4",
                                   "week5", "week6", "week7", "weekend1", "sserafim" };
            foreach (string weekName in weekOrder)
            {
                string jsonFile = Path.Combine(levelsDir, weekName + ".json");
                if (!File.Exists(jsonFile)) continue;

                try
                {
                    var level = JsonConvert.DeserializeObject<LevelJson>(File.ReadAllText(jsonFile));
                    if (level?.Songs == null) continue;

                    foreach (string songName in level.Songs)
                    {
                        string songKey = GetSongIdentityKey(songName);
                        if (addedSongKeys.Contains(songKey)) continue;

                        var fSong = BuildSong(songName, weekName, level.Difficulties);
                        if (fSong != null)
                        {
                            _allSongs.Add(fSong);
                            addedSongKeys.Add(songKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading level '{weekName}': {ex.Message}");
                }
            }
        }

        AutoIncludeMissingCharacterVariantSongs(addedSongKeys, "pico");
        EnsureCanonicalPicoSongs(addedSongKeys);

        Console.WriteLine($"Freeplay: loaded {_allSongs.Count} songs");
    }

    private void EnsureCanonicalPicoSongs(HashSet<string> addedSongKeys)
    {
        if (addedSongKeys == null)
            return;

        foreach (string canonicalSong in CanonicalPicoSongs)
        {
            string songKey = GetSongIdentityKey(canonicalSong);
            if (addedSongKeys.Contains(songKey))
                continue;

            string sourceSongName = null;
            foreach (string candidate in GetSongFolderCandidates(canonicalSong))
            {
                if (Assets.ResolvePath($"songs/{candidate}/charts/meta.json") != null
                    || Assets.ResolvePath($"songs/{candidate}/charts/chart.json") != null)
                {
                    sourceSongName = candidate;
                    break;
                }
            }

            if (sourceSongName == null)
                continue;

            var song = BuildSong(sourceSongName, null, null);
            if (song == null)
                continue;

            song.Name = canonicalSong;
            song.DisplayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(canonicalSong.Replace('-', ' ').Replace('_', ' '));
            song.WeekNum = ResolveWeekNum(canonicalSong, song.WeekId);

            _allSongs.Add(song);
            addedSongKeys.Add(songKey);
        }
    }

    private void AutoIncludeMissingCharacterVariantSongs(HashSet<string> addedSongKeys, string characterVariation)
    {
        if (addedSongKeys == null || string.IsNullOrWhiteSpace(characterVariation))
            return;

        string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        string songsDir = Assets.ResolveDirectory("songs")
                         ?? Path.Combine(contentPath, "songs");
        if (!Directory.Exists(songsDir))
            return;

        string variationId = characterVariation.Trim().ToLowerInvariant();
        string variationSuffix = "_" + variationId;

        foreach (string songDir in Directory.EnumerateDirectories(songsDir))
        {
            string folderName = Path.GetFileName(songDir);
            if (string.IsNullOrWhiteSpace(folderName)
                || !folderName.EndsWith(variationSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            string baseSongName = folderName[..^variationSuffix.Length];
            if (string.IsNullOrWhiteSpace(baseSongName))
                continue;

            string songKey = GetSongIdentityKey(baseSongName);
            if (addedSongKeys.Contains(songKey))
                continue;

            var song = BuildSong(folderName, null, null);
            if (song == null)
                continue;

            song.Name = baseSongName;
            song.DisplayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(baseSongName.Replace('-', ' ').Replace('_', ' '));
            song.WeekNum = ResolveWeekNum(baseSongName, song.WeekId);

            _allSongs.Add(song);
            addedSongKeys.Add(songKey);
        }
    }

    private FreeplaySong BuildSong(string songName, string weekId, List<string> weekDifficulties)
    {
        string opponentChar = "dad";
        string playerChar = "bf";
        string[] difficulties = null;
        int bpm = 100;
        Dictionary<string, int> ratings = null;
        string album = null;
        int previewStartMs = 0;
        int previewEndMs = 0;
        string[] songVariations = null;

        // Try multiple metadata paths (project layout varies)
        string metaPath = Assets.ResolvePath($"songs/{songName}/charts/meta.json")
                       ?? Assets.ResolvePath($"data/songs/{songName}/metadata.json")
                       ?? Assets.ResolvePath($"data/songs/{songName}/{songName}-metadata.json");
        if (metaPath != null && File.Exists(metaPath))
        {
            try
            {
                var meta = JObject.Parse(File.ReadAllText(metaPath));
                opponentChar = meta["playData"]?["characters"]?["opponent"]?.ToString() ?? "dad";
                playerChar = meta["playData"]?["characters"]?["player"]?.ToString() ?? "bf";
                var diffs = meta["playData"]?["difficulties"];
                if (diffs is JArray da)
                    difficulties = da.Select(d => d.ToString()).ToArray();
                var bpmVal = meta["songData"]?["bpm"] ?? meta["timeChanges"]?[0]?["bpm"];
                if (bpmVal != null)
                    bpm = (int)Math.Round((double)bpmVal);
                // Parse per-difficulty ratings
                var ratingsToken = meta["playData"]?["ratings"];
                if (ratingsToken is JObject ratingsObj)
                    ratings = ratingsObj.Properties().ToDictionary(p => p.Name, p => (int)p.Value);
                // Parse album id
                album = meta["playData"]?["album"]?.ToString();
                // Parse preview start (official metadata key: playData.previewStart)
                var previewStartToken = meta["playData"]?["previewStart"] ?? meta["previewStart"];
                if (previewStartToken != null && int.TryParse(previewStartToken.ToString(), out int parsedPreviewStart))
                    previewStartMs = Math.Max(0, parsedPreviewStart);
                var previewEndToken = meta["playData"]?["previewEnd"] ?? meta["previewEnd"];
                if (previewEndToken != null && int.TryParse(previewEndToken.ToString(), out int parsedPreviewEnd))
                    previewEndMs = Math.Max(0, parsedPreviewEnd);
                var variationsToken = meta["playData"]?["songVariations"] ?? meta["songVariations"];
                if (variationsToken is JArray variationArray)
                    songVariations = variationArray.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            }
            catch { }
        }

        if (difficulties == null && weekDifficulties != null && weekDifficulties.Count > 0)
            difficulties = weekDifficulties.ToArray();

        difficulties ??= new[] { "easy", "normal", "hard" };

        string displayName = songName.Replace("-", " ").Replace("_", " ");
        displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);

        int weekNum = ResolveWeekNum(songName, weekId);

        return new FreeplaySong
        {
            Name = songName,
            DisplayName = displayName,
            OpponentChar = opponentChar,
            PlayerCharacter = playerChar,
            WeekId = weekId,
            Difficulties = difficulties,
            Bpm = bpm,
            WeekNum = weekNum,
            Ratings = ratings,
            Album = album,
            PreviewStartMs = previewStartMs,
            PreviewEndMs = previewEndMs,
            SongVariations = songVariations
        };
    }

    private static int ResolveWeekNum(string songName, string weekId)
    {
        // Match original SongMenuItem.checkWeek()
        return songName?.ToLowerInvariant() switch
        {
            "bopeebo" or "fresh" or "dadbattle" => 1,
            "spookeez" or "south" or "monster" => 2,
            "pico" or "philly-nice" or "blammed" => 3,
            "satin-panties" or "high" or "milf" => 4,
            "cocoa" or "eggnog" or "winter-horrorland" => 5,
            "senpai" or "roses" or "thorns" => 6,
            "ugh" or "guns" or "stress" => 7,
            "darnell" or "lit-up" or "2hot" or "blazin" => -1, // weekend
            _ => weekId switch
            {
                "tutorial" => 0,
                "week1" => 1, "week2" => 2, "week3" => 3, "week4" => 4,
                "week5" => 5, "week6" => 6, "week7" => 7,
                "weekend1" => -1,
                _ => 0
            }
        };
    }
}

/// <summary>
/// Song entry for the freeplay song list.
/// </summary>
public class FreeplaySong
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string OpponentChar { get; set; }
    public string PlayerCharacter { get; set; }
    public string WeekId { get; set; }
    public string[] Difficulties { get; set; }
    public bool IsRandom { get; set; }
    public int Bpm { get; set; }
    public int WeekNum { get; set; } // positive=WEEK, negative=WEEKEND, 0=unknown
    public Dictionary<string, int> Ratings { get; set; } // per-difficulty ratings from metadata
    public string Album { get; set; } // album id (e.g. "volume1")
    public int PreviewStartMs { get; set; } // preview start in milliseconds from metadata
    public int PreviewEndMs { get; set; } // preview end in milliseconds from metadata
    public string[] SongVariations { get; set; } // metadata variants (e.g. erect, pico)
}
