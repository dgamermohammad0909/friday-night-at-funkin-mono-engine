using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;
using Newtonsoft.Json;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Main gameplay scene - the rhythm game
/// Positions and values taken directly from FNF source code
/// </summary>
public class PlayScene : Scene
{
    private readonly string _songName;
    private readonly string _currentDifficulty;
    private readonly string _selectedCharacterVariation;
    private Chart _chart;
    private Conductor _conductor;
    private NoteField _noteField;
    private Character _boyfriend;
    private Character _opponent;
    private Character _girlfriend;
    
    // Scoring (PBOT1 system from original FNF)
    private int _score = 0;
    private int _combo = 0;
    private int _misses = 0;
    private int _maxCombo = 0;
    private string _lastRating = "";
    private float _ratingTimer = 0;
    
    // NPS tracking (notes per second)
    private int _nps;
    private int _npsCounter;
    private float _npsTimer;
    
    // Vocal resync timer (original FNF: resyncVocals checks every frame, we do 2x/s)
    private float _resyncTimer;
    
    // Tally tracking (matches original FNF Highscore.tallies)
    private int _tallySick = 0;
    private int _tallyGood = 0;
    private int _tallyBad = 0;
    private int _tallyShit = 0;
    private int _tallyMissed = 0;
    private int _totalNotesHit = 0;
    private int _totalNotes = 0;
    
    // Game state
    private bool _paused = false;
    private bool _gameOver = false;
    internal bool IsPaused => _paused;
    // Health: 0.0 (HEALTH_MIN) to 2.0 (HEALTH_MAX), starts at 1.0 (HEALTH_STARTING)
    private const float HEALTH_MAX = 2.0f;
    private const float HEALTH_MIN = 0.0f;
    private const float HEALTH_STARTING = 1.0f;
    private float _health = HEALTH_STARTING;
    private readonly Random _random = new();
    
    // Countdown state
    private bool _countdownActive = true;
    private float _countdownTimer = 0;
    private int _countdownStep = 0; // 0=3, 1=2, 2=1, 3=GO, 4=done
    private string _countdownText = "";
    private float _countdownDisplayTimer = 0;
    
    // Camera bop (original: cameraBopMultiplier lerps back to 1.0)
    private float _cameraBopMultiplier = 1.0f;
    private float _hudZoomAdd = 0f;
    private const float CAMERA_BOP_INTENSITY = 1.015f; // Constants.DEFAULT_BOP_INTENSITY
    private const float HUD_ZOOM_INTENSITY = 0.03f;    // hudCameraZoomIntensity = 0.015 * 2.0
    private const int CAMERA_ZOOM_RATE = 4;             // Constants.DEFAULT_ZOOM_RATE
    
    // Camera follow (original: camera follows whoever is singing)
    private float _cameraTargetX;
    private float _cameraTargetY;
    private float _cameraX;
    private float _cameraY;
    
    // Camera world-space system (original FNF: world coords centered on stage)
    // The game world is larger than the screen; we project world coords to screen
    // using a center point and zoom factor.
    private float _camWorldX = 660f; // camera world center X (follow target)
    private float _camWorldY = 450f; // camera world center Y
    private float _camTargetWorldX = 660f;
    private float _camTargetWorldY = 450f;
    
    // Icon bounce
    private float _iconBounceScale = 1.0f;
    
    // Hold note tracking
    private readonly bool[] _holdingNote = new bool[4];
    private readonly double[] _holdNoteEndTime = new double[4];
    private readonly Note[] _holdNoteRef = new Note[4]; // Reference to the active hold note
    
    // Health lerp (original: healthLerp for smooth bar animation)
    private float _healthLerp;
    
    // Pause menu (matches original PauseSubState exactly)
    private int _pauseSelection = 0;
    private string[] _pauseItems; // Built dynamically based on pause mode + practice mode state
    private float _pauseScrollLerp = 0f; // Smoothly follows _pauseSelection so long lists stay on-screen
    private float _pauseBgAlpha = 0f; // Tweens from 0 to 0.6, 0.8s quartOut (original)
    private static int _deathCounter = 0; // Persists across retries within same song session
    private static string _deathCounterSong; // Track which song the counter is for
    private bool _practiceMode = false;
    private bool _botplay = false;
    private int _globalOffset = 0; // Global audio offset in ms
    private bool _pauseDifficultyMode = false; // true = showing difficulty sub-menu
    private float _pauseMetadataAlpha = 0f; // Metadata fade-in (staggered, 1.8s quartOut)
    private float _pauseMenuAlpha = 0f; // Menu items fade-in
    private float _pauseTimer = 0f; // Time since pause opened
    
    // Dialogue system (M3 � basic conversation renderer)
    private bool _dialogueActive;
    private List<DialogueLine> _dialogueLines;
    private int _dialogueIndex;
    private string _dialogueCurrentText;
    private float _dialogueCharTimer;
    private int _dialogueCharIndex;
    private float _dialogueFadeAlpha;
    private bool _dialogueFadingIn = true;
    private string _dialogueSpeakerName;
    private Color _dialogueBoxColor = new Color(0x40, 0x30, 0x60, 0xE0);
    
    // Game over state machine (matches original GameOverSubState flow)
    private float _gameOverTimer = 0;
    private bool _gameOverMusicStarted = false;
    private bool _gameOverConfirmed = false;
    private float _gameOverFadeTimer = 0;
    private string _gameOverDeathAnim = "firstDeath"; // Current death animation state
    
    // Rating popup physics (matches original PopUpStuff: acceleration.y=550, velocity.y random)
    private float _ratingY;
    private float _ratingVelY;
    private float _ratingAlpha = 1f;
    private int _lastComboDisplay = 0;
    
    // Combo digit physics (original: each digit has its own velocity + acceleration)
    private float _comboY;
    private float _comboVelY;
    private float _comboAlpha = 1f;
    private float _comboTimer;
    
    // Note splash tracking
    private float[] _noteSplashTimer = new float[4];
    private int[] _noteSplashLane = { -1, -1, -1, -1 };
    
    // Receptor confirm flash timers (original: receptor plays confirm anim on note hit)
    // Player receptors flash on hit even after key release; opponent receptors flash on auto-hit
    private float[] _playerConfirmTimer = new float[4];
    private float[] _opponentConfirmTimer = new float[4];
    private const float RECEPTOR_CONFIRM_DURATION = 0.15f; // ~4 frames at 24fps
    
    // Cached HUD assets (avoid per-frame LoadTexture + string alloc)
    private Texture2D _playerIcon;
    private Texture2D _opponentIcon;
    private string _cachedPlayerSprite;
    private string _cachedOpponentSprite;
    
    // Note and receptor sprites
    private SpriteSheet _notesSheet;
    private SpriteSheet _receptorsSheet;
    private SpriteSheet _splashesSheet;
    
    // Pre-cached sprite frames (avoid string allocation + dictionary lookup per note per frame)
    private SpriteFrame[] _noteFrames = new SpriteFrame[4];       // [lane] -> note frame
    private SpriteFrame[] _sustainFrames = new SpriteFrame[4];    // [lane] -> sustain body frame
    private SpriteFrame[] _sustainEndFrames = new SpriteFrame[4]; // [lane] -> sustain end cap frame
    private List<SpriteFrame>[] _receptorStaticFrames = new List<SpriteFrame>[4];
    private List<SpriteFrame>[] _receptorConfirmFrames = new List<SpriteFrame>[4];
    private List<SpriteFrame>[] _receptorPressFrames = new List<SpriteFrame>[4];
    
    // Lane names for sprite lookup (order: Left, Down, Up, Right)
    private readonly string[] _laneNames = { "left", "down", "up", "right" };

    // Controller mode: true when last input was from controller (auto-switches mid-song)
    private bool UseControllerDisplay => Input.LastDevice == InputManager.InputDevice.Controller;

    // Controller button colors per lane � derived dynamically from actual button mapping
    // so sustain/splash/cover colors always match the displayed button sprites.
    private Color GetControllerLaneColor(int lane)
    {
        if (lane >= 0 && lane < 4)
        {
            var btn = Input.NoteFaceButtons[lane];
            var (color, _) = AssetManager.GetButtonInfo(btn);
            return color;
        }
        return Color.White;
    }

    // Original FNF exact constants: Note.swagWidth = 160 * 0.7 = 112
    private const int STRUMLINE_SIZE = 112;       // Note.swagWidth
    private const int NOTE_SPACING = 112;         // Note.swagWidth
    private const int STRUMLINE_X_OFFSET = 56;    // 0.5 * Note.swagWidth
    private const int STRUMLINE_Y_OFFSET = 50;    // Original: strumline Y = 50
    private const float SCROLL_SPEED = 1.0f;
    private const float PIXELS_PER_MS = 0.45f;
    
    // Downscroll / Middlescroll preferences (read from HighscoreManager on Load)
    private bool _downscroll;
    private bool _middlescroll;
    private bool _flashingLightsEnabled = true;
    private bool _cameraZoomEnabled = true;
    
    // Hold note cover sprites (visual glow effect while holding sustain notes)
    private SpriteSheet _holdCoverSheet;
    private float[] _holdCoverTimer = new float[4];
    
    // Story mode progression (play multiple songs in sequence)
    private readonly List<string> _weekSongs;
    private readonly int _weekSongIndex;
    private readonly string _weekId;
    private int _weekAccumulatedScore;
    
    // Event processing
    private int _nextEventIndex;

    public PlayScene(string songName, string difficulty = "normal")
        : this(songName, difficulty, null, 0, null, 0) { }
    
    /// <summary>
    /// Story mode constructor � plays songs in sequence from a week.
    /// </summary>
    public PlayScene(string songName, string difficulty, List<string> weekSongs, 
        int weekSongIndex, string weekId, int accumulatedScore)
    {
        _songName = songName;
        _currentDifficulty = difficulty;
        _weekSongs = weekSongs;
        _weekSongIndex = weekSongIndex;
        _weekId = weekId;
        _weekAccumulatedScore = accumulatedScore;
        // Character Select swap only applies in Freeplay; Story Mode weeks must always
        // use the week's intended characters regardless of the saved SelectedCharacter.
        _selectedCharacterVariation = string.IsNullOrEmpty(_weekId)
            ? GetSelectedCharacterVariationId()
            : "bf";
    }
    
    // Stage textures
    private Texture2D _stageBackdrop;
    private Texture2D _stageFront;
    private Texture2D _stageCurtains;
    private Texture2D _healthBarBG;

    private static readonly BlendState _phillyBlazinMultiplyBlend = new BlendState
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add
    };
    
    // Static stage props loaded from JSON (position, scale, scroll per prop)
    private struct StaticStageProp
    {
        public Texture2D Texture;
        public float X, Y;
        public float ScaleX, ScaleY;
        public float Alpha;
        public float ScrollX, ScrollY;
        public int ZIndex;
        public string Name;
        public string Blend;
        public Color? OverlayColor;
    }
    private List<StaticStageProp> _staticProps = new();
    private bool _hasJsonStage; // true when stage was loaded from JSON (skip legacy draw)
    
    // Default camera zoom from stage JSON (original: defaultCamZoom)
    private float _defaultCamZoom = 1.05f;
    
    
    // Pending character positions from stage JSON (applied after characters are loaded)
    private float[] _pendingBfPos;
    private float[] _pendingDadPos;
    private float[] _pendingGfPos;
    // Pending camera offsets from stage JSON (override character JSON offsets)
    private float[] _pendingBfCamOffsets;
    private float[] _pendingDadCamOffsets;
    private float[] _pendingGfCamOffsets;
    // Character z-indices from stage JSON (for proper interleaved draw order)
    private int _gfZIndex = 100;
    private int _dadZIndex = 200;
    private int _bfZIndex = 300;
    
    
    // Animated stage props (P1)
    private struct AnimatedStageProp
    {
        public AnimatedSprite Sprite;
        public int ZIndex;
        public float Alpha;
        public float ScrollX, ScrollY;
        public int DanceEvery; // dance on beat interval (0 = no dance)
        public int LastDanceBeat;
        public string Name; // prop name from stage JSON (e.g., "fastCar")
        public string[] DanceAnimNames; // animation names for dance alternation (e.g., ["danceLeft","danceRight"])
        public bool DanceToggle; // alternates between dance anims
    }
    private List<AnimatedStageProp> _animatedProps = new();
    private int _abotRigStartFrame = 1;
    private readonly float[] _abotVizDisplayLevels = new float[7];
    private float _abotVizUpdateTimer;
    private bool _hasAbotFallback;
    private float _abotBaseX;
    private float _abotBaseY;
    private float ABOT_MONITOR_CLIP_X = 210f;
    private float ABOT_MONITOR_CLIP_Y = 92f;
    private float ABOT_MONITOR_CLIP_W = 305f;
    private float ABOT_MONITOR_CLIP_H = 188f;
    private bool _abotDebugMode;
    private int _abotDebugSelection;
    private int _abotDebugStep = 1;
    private float _abotBodyOffsetX = -95f;
    private float _abotBodyOffsetY = 384f;
    private float _abotEyesOffsetX = 40f;
    private float _abotEyesOffsetY = 250f;
    private float _abotPupilOffsetX = 50f;
    private float _abotPupilOffsetY = 238f;
    private float _abotVizBaseOffsetX = 207f;
    private float _abotVizBaseOffsetY = 84f;
    private float _abotStereoOffsetX = 150f;
    private float _abotStereoOffsetY = 30f;
    private bool _abotUseContinuousAnim;
    private bool _abotRigHasRuntimeOrigin;
    private Vector2 _abotRigRuntimeOrigin;
    private bool _abotDebugFreezeAnim;
    private int _abotDebugFrame = 1;
    private bool _abotDebugFlipX;
    
    // FastCar scripted behavior (original limoRide.hxc)
    private bool _fastCarCanDrive = false;
    private float _fastCarVelocityX = 0;
    private int _fastCarPropIndex = -1; // index into _animatedProps (-1 if static or not limo stage)
    private int _fastCarStaticIndex = -1; // index into _staticProps
    
    // Spooky lightning flash (original SpookyState: random lightning on beat)
    private float _lightningFlashAlpha = 0f;
    private int _lightningBeatCooldown = 0; // beats to wait before next flash
    private int _halloweenBGPropIndex = -1; // index into _animatedProps
    
    // Philly train pass (original phillyTrain.hxc)
    private int _trainStaticIndex = -1; // index into _staticProps for train
    private float _trainX = 2000f; // current train X position
    private bool _trainMoving = false;
    private bool _trainStartedMoving = false;
    private float _trainCooldown = 0; // seconds before next eligible train
    private int _trainFrameTiming = 0; // frames since train started
    private bool _trainFinishing = false;

    private bool _isPhillyBlazinStage;
    private int _phillyBlazinSkyAdditiveIndex = -1;
    private int _phillyBlazinForegroundMultiplyIndex = -1;
    private int _phillyBlazinAdditionalLightenIndex = -1;
    private int _phillyBlazinLightningIndex = -1;
    private float _phillyBlazinLightningTimer = 0f;
    private float _phillyBlazinLightningFadeTimer = 0f;
    private float _phillyBlazinLightningShortFadeTimer = 0f;
    
    // Philly window lights (original: cycles 5 colors on beat)
    private int _phillyLightsIndex = -1; // index into _staticProps for win.png
    private int _phillyLightColor = 0; // current color index (0-4)
    private static readonly Color[] PHILLY_LIGHT_COLORS = {
        new Color(49, 162, 253),   // blue
        new Color(49, 253, 140),   // green
        new Color(251, 51, 245),   // pink
        new Color(253, 69, 49),    // red
        new Color(251, 166, 51)    // orange
    };
    
    // Countdown textures
    private Texture2D _readyTex;
    private Texture2D _setTex;
    private Texture2D _goTex;
    
    // Cached rating and combo textures (avoid per-frame LoadTexture)
    private Texture2D _ratingSickTex;
    private Texture2D _ratingGoodTex;
    private Texture2D _ratingBadTex;
    private Texture2D _ratingShitTex;
    private Texture2D[] _comboDigitTex = new Texture2D[10];
    
    // Combo strip fallback (combo.png = horizontal strip of 10 digits)
    private Texture2D _comboStripTex;
    private Rectangle[] _comboStripRects;
    
    // Animation frame counter
    private int _animFrame;
    private float _animTimer;
    private float _lastDelta; // Cache delta for use in ProcessNoteInput
    private float _holdScoreAccum; // Accumulates fractional hold bonus to avoid truncation

    private Matrix _worldZoomMatrix;
    private RasterizerState _worldClipRaster;
    private SamplerState _worldSampler = SamplerState.LinearClamp;
    private BlendState _worldBlendState = BlendState.AlphaBlend;

    // Phased loading (one heavy asset group per frame to prevent freezes)
    private bool _playLoading = true;
    private int _playLoadPhase;
    private string _playLoadStatus = "Loading...";
    private float _playLoadDotTimer;
    private string _resolvedNoteSkinPath;
    private string _resolvedStageFolder;

    public override void Load()
    {
        // Enable face button note input for gameplay
        Input.GameplayMode = true;

        // Load chart with chosen difficulty (lightweight: JSON parse only)
        _chart = Chart.Load(_songName, Assets, _currentDifficulty);

        // Initialize conductor (handles timing)
        _conductor = new Conductor(_chart.BPM);

        // Set BPM changes from chart data
        if (_chart.BPMChanges.Count > 0)
            _conductor.SetBPMChanges(_chart.BPMChanges);

        // Initialize note field
        _noteField = new NoteField(_chart, _conductor);

        // Cache paths for phased loading
        _resolvedNoteSkinPath = ResolveNoteSkinPath(_chart.NoteStyle);
        _resolvedStageFolder = ResolveStageFolder(_chart.Stage);

        // Read preferences from save data (lightweight)
        var saveData = HighscoreManager.Data;
        _downscroll = saveData.Downscroll;
        _middlescroll = saveData.Middlescroll;
        _globalOffset = saveData.GlobalOffset;
        _flashingLightsEnabled = saveData.FlashingLights;
        _cameraZoomEnabled = saveData.CameraZoom;

        // Start phased loading
        _playLoading = true;
        _playLoadPhase = 0;
        _playLoadStatus = "Loading notes...";
    }

    /// <summary>
    /// Process one loading phase per frame for PlayScene.
    /// </summary>
    private void ProcessPlayLoadPhase()
    {
        switch (_playLoadPhase)
        {
            case 0:
                _playLoadStatus = "Loading notes...";
                _notesSheet = SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/notes")
                           ?? SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/arrows")
                           ?? SpriteSheet.Load(Game, "game/skins/default/notes");
                _receptorsSheet = SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/receptors")
                               ?? SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/arrows")
                               ?? SpriteSheet.Load(Game, "game/skins/default/receptors");
                _splashesSheet = SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/splashes")
                              ?? SpriteSheet.Load(Game, "game/skins/default/splashes");
                _holdCoverSheet = SpriteSheet.Load(Game, $"{_resolvedNoteSkinPath}/holdCover")
                               ?? SpriteSheet.Load(Game, "game/skins/default/holdCover");
                CacheNoteFrames();
                break;
            case 1:
                _playLoadStatus = "Loading stage...";
                LoadStageTextures(_resolvedStageFolder);
                break;
            case 2:
                _playLoadStatus = "Loading UI...";
                _healthBarBG = Assets.LoadTexture("game/ui/healthBar.png");
                _readyTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/countdown/ready.png");
                if (_readyTex == Assets.Pixel) _readyTex = Assets.LoadTexture("game/skins/default/countdown/ready.png");
                _setTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/countdown/set.png");
                if (_setTex == Assets.Pixel) _setTex = Assets.LoadTexture("game/skins/default/countdown/set.png");
                _goTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/countdown/go.png");
                if (_goTex == Assets.Pixel) _goTex = Assets.LoadTexture("game/skins/default/countdown/go.png");
                break;
            case 3:
                _playLoadStatus = "Loading ratings...";
                _ratingSickTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/ratings/sick.png");
                if (_ratingSickTex == Assets.Pixel) _ratingSickTex = Assets.LoadTexture("game/skins/default/ratings/sick.png");
                _ratingGoodTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/ratings/good.png");
                if (_ratingGoodTex == Assets.Pixel) _ratingGoodTex = Assets.LoadTexture("game/skins/default/ratings/good.png");
                _ratingBadTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/ratings/bad.png");
                if (_ratingBadTex == Assets.Pixel) _ratingBadTex = Assets.LoadTexture("game/skins/default/ratings/bad.png");
                _ratingShitTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/ratings/shit.png");
                if (_ratingShitTex == Assets.Pixel) _ratingShitTex = Assets.LoadTexture("game/skins/default/ratings/shit.png");
                for (int d = 0; d < 10; d++)
                {
                    _comboDigitTex[d] = Assets.LoadTexture($"{_resolvedNoteSkinPath}/comboNumbers/num{d}.png");
                    if (_comboDigitTex[d] == Assets.Pixel)
                        _comboDigitTex[d] = Assets.LoadTexture($"game/skins/default/comboNumbers/num{d}.png");
                }
                bool anyMissing = false;
                for (int d = 0; d < 10; d++)
                    if (_comboDigitTex[d] == Assets.Pixel) { anyMissing = true; break; }
                if (anyMissing)
                {
                    _comboStripTex = Assets.LoadTexture($"{_resolvedNoteSkinPath}/combo.png");
                    if (_comboStripTex == Assets.Pixel)
                        _comboStripTex = Assets.LoadTexture("game/skins/default/combo.png");
                    if (_comboStripTex != null && _comboStripTex != Assets.Pixel)
                    {
                        int digitW = _comboStripTex.Width / 10;
                        int digitH = _comboStripTex.Height;
                        _comboStripRects = new Rectangle[10];
                        for (int d = 0; d < 10; d++)
                            _comboStripRects[d] = new Rectangle(d * digitW, 0, digitW, digitH);
                    }
                }
                break;
            case 4:
                _playLoadStatus = "Loading characters...";
                LoadCharacters();
                break;
            case 5:
                _playLoadStatus = "Starting...";
                PreloadGameplaySounds();
                LoadDialogue();
                FinishPlayLoading();
                return;
        }
        _playLoadPhase++;
    }

    /// <summary>
    /// Called when all PlayScene phased loading is complete.
    /// </summary>
    private void FinishPlayLoading()
    {
        if (_dialogueActive)
            _countdownActive = false;
        else
            _countdownActive = true;
        _countdownTimer = 0;
        _countdownStep = 0;
        _healthLerp = HEALTH_STARTING;
        BuildPauseMenuItems();

        if (_deathCounterSong != _songName)
        {
            _deathCounter = 0;
            _deathCounterSong = _songName;
        }

        float beatDuration = (float)_conductor.Crochet;
        Console.WriteLine($"Song: {_songName}, BPM: {_chart.BPM}, Beat: {beatDuration}s, Downscroll: {_downscroll}, Middlescroll: {_middlescroll}");
        _playLoading = false;
    }
    
    /// <summary>
    /// Load dialogue conversation if this song has one (M3).
    /// Dialogue JSON files are at data/dialogue/conversations/{songName}.json.
    /// </summary>
    private void LoadDialogue()
    {
        string convPath = Assets.ResolvePath($"data/dialogue/conversations/{_songName}.json");
        if (convPath == null) return;
        
        try
        {
            var conv = Newtonsoft.Json.JsonConvert.DeserializeObject<ConversationJson>(File.ReadAllText(convPath));
            if (conv?.Dialogue == null || conv.Dialogue.Count == 0) return;
            
            _dialogueLines = new List<DialogueLine>();
            foreach (var entry in conv.Dialogue)
            {
                string text = entry.Text != null ? string.Join(" ", entry.Text) : "";
                _dialogueLines.Add(new DialogueLine
                {
                    Speaker = entry.Speaker ?? "",
                    Text = text
                });
            }
            
            if (_dialogueLines.Count > 0)
            {
                _dialogueActive = true;
                _dialogueIndex = 0;
                _dialogueCharIndex = 0;
                _dialogueCharTimer = 0;
                _dialogueCurrentText = "";
                _dialogueSpeakerName = _dialogueLines[0].Speaker;
                _dialogueFadeAlpha = 0;
                _dialogueFadingIn = true;
            }
        }
        catch { }
    }
    
    /// <summary>
    /// Update dialogue text reveal and input handling (M3).
    /// </summary>
    private void UpdateDialogue(float dt)
    {
        // Fade in
        if (_dialogueFadingIn)
        {
            _dialogueFadeAlpha = Math.Min(1f, _dialogueFadeAlpha + dt * 2f);
            if (_dialogueFadeAlpha >= 1f) _dialogueFadingIn = false;
        }
        
        if (_dialogueLines == null || _dialogueIndex >= _dialogueLines.Count)
        {
            EndDialogue();
            return;
        }
        
        var line = _dialogueLines[_dialogueIndex];
        _dialogueSpeakerName = line.Speaker;
        
        // Type text character by character
        if (_dialogueCharIndex < line.Text.Length)
        {
            _dialogueCharTimer += dt;
            float charSpeed = 0.03f;
            while (_dialogueCharTimer >= charSpeed && _dialogueCharIndex < line.Text.Length)
            {
                _dialogueCharTimer -= charSpeed;
                _dialogueCharIndex++;
                _dialogueCurrentText = line.Text[.._dialogueCharIndex];
            }
        }
        
        // Advance on confirm
        if (Input.ConfirmPressed)
        {
            if (_dialogueCharIndex < line.Text.Length)
            {
                // Complete current line instantly
                _dialogueCharIndex = line.Text.Length;
                _dialogueCurrentText = line.Text;
            }
            else
            {
                // Next line
                _dialogueIndex++;
                if (_dialogueIndex >= _dialogueLines.Count)
                {
                    EndDialogue();
                }
                else
                {
                    _dialogueCharIndex = 0;
                    _dialogueCharTimer = 0;
                    _dialogueCurrentText = "";
                }
            }
        }
        
        // Skip dialogue on back button
        if (Input.BackPressed)
        {
            EndDialogue();
        }
    }
    
    private void EndDialogue()
    {
        _dialogueActive = false;
        _countdownActive = true;
        _countdownTimer = 0;
        _countdownStep = 0;
    }
    
    private void DrawDialogue(SpriteBatch spriteBatch)
    {
        if (!_dialogueActive) return;
        
        // Dim background
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
            Color.Black * 0.5f * _dialogueFadeAlpha);
        
        // Dialogue box at bottom
        int boxH = 180;
        int boxY = FNFGame.SCREEN_HEIGHT - boxH - 30;
        int boxX = 40;
        int boxW = FNFGame.SCREEN_WIDTH - 80;
        
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(boxX, boxY, boxW, boxH),
            _dialogueBoxColor * _dialogueFadeAlpha);
        
        // Border
        spriteBatch.Draw(Assets.Pixel, new Rectangle(boxX, boxY, boxW, 3), Color.White * 0.6f * _dialogueFadeAlpha);
        
        // Speaker name
        if (!string.IsNullOrEmpty(_dialogueSpeakerName))
        {
            var nameFont = Assets.GetFont(22);
            if (nameFont != null)
            {
                string displayName = _dialogueSpeakerName.Replace("-", " ").Replace("_", " ");
                displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);
                nameFont.DrawText(spriteBatch, displayName,
                    new Vector2(boxX + 20, boxY + 12), Color.Yellow * _dialogueFadeAlpha);
            }
        }
        
        // Dialogue text
        var textFont = Assets.GetFont(20);
        if (textFont != null && !string.IsNullOrEmpty(_dialogueCurrentText))
        {
            // Simple word wrap
            string wrapped = WrapText(textFont, _dialogueCurrentText, boxW - 40);
            textFont.DrawText(spriteBatch, wrapped,
                new Vector2(boxX + 20, boxY + 45), Color.White * _dialogueFadeAlpha);
        }
        
        // Continue hint
        if (_dialogueCharIndex >= (_dialogueLines?[Math.Min(_dialogueIndex, _dialogueLines.Count - 1)]?.Text?.Length ?? 0))
        {
            var hintFont = Assets.GetFont(14);
            if (hintFont != null)
            {
                hintFont.DrawText(spriteBatch, "Press ENTER to continue...",
                    new Vector2(boxX + boxW - 220, boxY + boxH - 25), Color.Gray * _dialogueFadeAlpha);
            }
        }
    }
    
    private static string WrapText(FontStashSharp.SpriteFontBase font, string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var words = text.Split(' ');
        var result = new System.Text.StringBuilder();
        string currentLine = "";
        
        foreach (var word in words)
        {
            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            var size = font.MeasureString(testLine);
            if (size.X > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                result.AppendLine(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }
        if (!string.IsNullOrEmpty(currentLine))
            result.Append(currentLine);
        
        return result.ToString();
    }
    
    /// <summary>
    /// Pre-load all sound effects used during gameplay to avoid
    /// hitches on first play.
    /// </summary>
    private void PreloadGameplaySounds()
    {
        // Miss sounds (3 variants)
        Audio.PreloadSound("missnote1");
        Audio.PreloadSound("missnote2");
        Audio.PreloadSound("missnote3");
        
        // Countdown sounds
        Audio.PreloadSound("game/skins/default/countdown/3");
        Audio.PreloadSound("game/skins/default/countdown/2");
        Audio.PreloadSound("game/skins/default/countdown/1");
        Audio.PreloadSound("game/skins/default/countdown/go");
        
        // Menu sounds
        Audio.PreloadSound("scrollMenu");
        Audio.PreloadSound("confirmMenu");
        
        // Stage-specific sounds
        string stageFolder = ResolveStageFolder(_chart.Stage);
        if (stageFolder == "spooky")
        {
            Audio.PreloadSound("game/stages/spooky/thunder_1");
            Audio.PreloadSound("game/stages/spooky/thunder_2");
        }
        if (stageFolder == "philly_train" || stageFolder == "philly_train_erect")
        {
            Audio.PreloadSound("game/stages/philly_train/train_passes");
        }
        
        // Game over sounds (with funkin.assets fallbacks)
        Audio.PreloadSound($"game/characters/{_cachedPlayerSprite}/gameover/on_death");
        Audio.PreloadSound($"game/characters/{_cachedPlayerSprite}/gameover/retry");
        Audio.PreloadSound("gameplay/gameover/fnf_loss_sfx");
    }
    
    private void LoadCharacters()
    {
        string playerName = _chart.PlayerCharacter ?? "bf";
        string opponentName = _chart.OpponentCharacter ?? "dad";
        string gfName = _chart.GirlfriendCharacter ?? "gf";

        // Ensure stage character slot data is available even if phased stage loading
        // couldn't populate pending arrays for this song variation.
        EnsurePendingStageCharacterData(ResolveStageFolder(_chart.Stage));

        if (_selectedCharacterVariation.Equals("pico", StringComparison.OrdinalIgnoreCase)
            && SongSupportsAudioVariation(_songName, "pico")
            && !playerName.Contains("pico", StringComparison.OrdinalIgnoreCase))
        {
            playerName = "pico-playable";
        }

        // Keep chart runtime character in sync with playable variation so voice loading
        // and other runtime systems use the same character as gameplay rendering.
        _chart.PlayerCharacter = playerName;
        
        // Map character variants to their sprite folders
        // e.g. "pico-playable" ? "pico" (same sprites, just playable flag)
        string playerSprite = ResolveCharacterSprite(playerName);
        string opponentSprite = ResolveCharacterSprite(opponentName);
        string gfSprite = ResolveCharacterSprite(gfName);
        
        // FNF character positions from original PlayState
        // Player on right, opponent on left, GF center-back on speakers
        // Scale is set by Character.LoadSprites from character JSON (default 1.0)
        _boyfriend = new Character(playerSprite, 800, 280);
        _boyfriend.LoadJsonData(Assets, playerName);
        _boyfriend.SetFlipForRole(true); // Player role
        _boyfriend.StepCrochet = (float)_conductor.StepCrochet;
        _boyfriend.LoadSprites(Game);
        
        _opponent = new Character(opponentSprite, 80, 230);
        _opponent.LoadJsonData(Assets, opponentName);
        _opponent.SetFlipForRole(false); // Opponent role
        _opponent.StepCrochet = (float)_conductor.StepCrochet;
        _opponent.LoadSprites(Game);
        
        // Only create separate GF if opponent is not GF
        if (opponentName != "gf" && opponentSprite != "gf")
        {
            _girlfriend = new Character(gfSprite, 370, 80);
            _girlfriend.LoadJsonData(Assets, gfName);
            _girlfriend.SetFlipForRole(false); // GF is not player
            _girlfriend.StepCrochet = (float)_conductor.StepCrochet;
            _girlfriend.LoadSprites(Game);
        }

        // Cache character icons � try multiple icon paths (original FNF: preload/images/icons/icon-{name}.png)
        _cachedPlayerSprite = ResolveCharacterSprite(playerName);
        _cachedOpponentSprite = ResolveCharacterSprite(opponentName);
        _playerIcon = LoadCharacterIcon(playerName, _cachedPlayerSprite);
        _opponentIcon = LoadCharacterIcon(opponentName, _cachedOpponentSprite);
        
        // Apply pending stage JSON character positions (saved during LoadStageTextures)
        // Original FNF (Stage.hx / BaseCharacter.hx):
        //   character.x = stagePos[0] - characterOrigin.x + globalOffsets[0]
        //   character.y = stagePos[1] - characterOrigin.y + globalOffsets[1]
        // Stage positions represent the character's FEET (bottom-center), not top-left.
        // characterOrigin = (width/2, height) for Sparrow, or composite registration point for animateatlas.
        // Note: globalOffsets (CharOffsets) are applied during Draw() for rendering; they're also
        // accounted for in GetMidpoint() for camera follow calculations.
        if (_pendingBfPos != null && _pendingBfPos.Length >= 2)
        {
            var bfOrigin = _boyfriend.GetCharacterOrigin();
            _boyfriend.Position = new Vector2(_pendingBfPos[0] - bfOrigin.X, _pendingBfPos[1] - bfOrigin.Y);
        }
        if (_pendingDadPos != null && _pendingDadPos.Length >= 2)
        {
            var dadOrigin = _opponent.GetCharacterOrigin();
            _opponent.Position = new Vector2(_pendingDadPos[0] - dadOrigin.X, _pendingDadPos[1] - dadOrigin.Y);
        }
        if (_pendingGfPos != null && _pendingGfPos.Length >= 2 && _girlfriend != null)
        {
            var gfOrigin = _girlfriend.GetCharacterOrigin();
            _girlfriend.Position = new Vector2(_pendingGfPos[0] - gfOrigin.X, _pendingGfPos[1] - gfOrigin.Y);
        }
        
        // Stage JSON cameraOffsets ADD to character JSON cameraOffsets (matches original Stage.hx)
        if (_pendingBfCamOffsets != null && _pendingBfCamOffsets.Length >= 2)
        {
            var charCam = _boyfriend.CameraOffsets;
            _boyfriend.CameraOffsets = new float[]
            {
                (charCam != null && charCam.Length >= 2 ? charCam[0] : 0f) + _pendingBfCamOffsets[0],
                (charCam != null && charCam.Length >= 2 ? charCam[1] : 0f) + _pendingBfCamOffsets[1]
            };
        }
        if (_pendingDadCamOffsets != null && _pendingDadCamOffsets.Length >= 2)
        {
            var charCam = _opponent.CameraOffsets;
            _opponent.CameraOffsets = new float[]
            {
                (charCam != null && charCam.Length >= 2 ? charCam[0] : 0f) + _pendingDadCamOffsets[0],
                (charCam != null && charCam.Length >= 2 ? charCam[1] : 0f) + _pendingDadCamOffsets[1]
            };
        }
        if (_pendingGfCamOffsets != null && _pendingGfCamOffsets.Length >= 2 && _girlfriend != null)
        {
            var charCam = _girlfriend.CameraOffsets;
            _girlfriend.CameraOffsets = new float[]
            {
                (charCam != null && charCam.Length >= 2 ? charCam[0] : 0f) + _pendingGfCamOffsets[0],
                (charCam != null && charCam.Length >= 2 ? charCam[1] : 0f) + _pendingGfCamOffsets[1]
            };
        }

        TryAddNeneSpeakers(gfName);
        
        // Initialize world-space camera to frame the stage
        if (_hasJsonStage)
        {
            // Stage JSON cameraOffsets already include the directional constants
            // (e.g., mainStage dad=[150,-100]). Apply them directly � do NOT add the
            // hardcoded +150/-100 on top, or the offset is doubled.
            var dadMid = _opponent.GetMidpoint();
            float camOffX = _opponent.CameraOffsets?.Length >= 2 ? _opponent.CameraOffsets[0] : 0f;
            float camOffY = _opponent.CameraOffsets?.Length >= 2 ? _opponent.CameraOffsets[1] : 0f;
            _camWorldX = dadMid.X + camOffX;
            _camWorldY = dadMid.Y + camOffY;
            _camTargetWorldX = _camWorldX;
            _camTargetWorldY = _camWorldY;
        }
        
        // Pre-fire the first FocusCamera event so the camera starts on the correct character
        // (events don't fire during countdown, but we want proper framing from the start)
        if (_chart.Events != null)
        {
            foreach (var ev in _chart.Events)
            {
                if (ev.Name == "FocusCamera" && ev.Time <= 0.5)
                {
                    // V2 convention: 0=Boyfriend, 1=Dad, 2=Girlfriend.
                    int initFocus = ParseFocusChar(ev.Value);
                    
                    if (initFocus == 0)
                    {
                        var mid = _boyfriend.GetMidpoint();
                        float px = _boyfriend.CameraOffsets?.Length >= 2 ? _boyfriend.CameraOffsets[0] : 0f;
                        float py = _boyfriend.CameraOffsets?.Length >= 2 ? _boyfriend.CameraOffsets[1] : 0f;
                        if (_hasJsonStage)
                        {
                            _camWorldX = _camTargetWorldX = mid.X + px;
                            _camWorldY = _camTargetWorldY = mid.Y + py;
                        }
                        else
                        {
                            _camWorldX = _camTargetWorldX = mid.X - 100 + px;
                            _camWorldY = _camTargetWorldY = mid.Y - 100 + py;
                        }
                    }
                    else if (initFocus == 2 && _girlfriend != null)
                    {
                        var mid = _girlfriend.GetMidpoint();
                        float gx = _girlfriend.CameraOffsets?.Length >= 2 ? _girlfriend.CameraOffsets[0] : 0f;
                        float gy = _girlfriend.CameraOffsets?.Length >= 2 ? _girlfriend.CameraOffsets[1] : 0f;
                        _camWorldX = _camTargetWorldX = mid.X + gx;
                        _camWorldY = _camTargetWorldY = mid.Y + gy;
                    }
                    else
                    {
                        var mid = _opponent.GetMidpoint();
                        float ox = _opponent.CameraOffsets?.Length >= 2 ? _opponent.CameraOffsets[0] : 0f;
                        float oy = _opponent.CameraOffsets?.Length >= 2 ? _opponent.CameraOffsets[1] : 0f;
                        if (_hasJsonStage)
                        {
                            _camWorldX = _camTargetWorldX = mid.X + ox;
                            _camWorldY = _camTargetWorldY = mid.Y + oy;
                        }
                        else
                        {
                            _camWorldX = _camTargetWorldX = mid.X + 150 + ox;
                            _camWorldY = _camTargetWorldY = mid.Y - 100 + oy;
                        }
                    }
                    break;
                }
            }
        }
        
        // Hook up beat callback for dancing + camera bop + icon bounce
        _conductor.OnBeat += (beat) =>
        {
            _boyfriend.Dance(beat);
            _opponent.Dance(beat);
            _girlfriend?.Dance(beat);
            
            // Animated stage props dance on beat (P1)
            for (int pi = 0; pi < _animatedProps.Count; pi++)
            {
                var prop = _animatedProps[pi];
                if (prop.DanceEvery > 0 && beat % prop.DanceEvery == 0)
                {
                    if (string.Equals(prop.Name, "neneSpeakerRig", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_abotUseContinuousAnim)
                        {
                            _animatedProps[pi] = prop;
                            continue;
                        }

                        string rigAnim = prop.DanceAnimNames?.Length > 0
                            ? prop.DanceAnimNames[0]
                            : prop.Sprite?.CurrentAnimation;
                        if (!string.IsNullOrEmpty(rigAnim))
                            prop.Sprite?.PlayAnimationFromFrame(rigAnim, _abotRigStartFrame, loop: false, loopFrame: _abotRigStartFrame);
                    }
                    else
                    if (prop.DanceAnimNames != null && prop.DanceAnimNames.Length >= 2)
                    {
                        // Alternate between dance animations (danceLeft/danceRight)
                        int idx = prop.DanceToggle ? 1 : 0;
                        string danceAnim = prop.DanceAnimNames[idx];
                        prop.Sprite?.PlayAnimation(danceAnim, force: true, loop: false);
                        prop.DanceToggle = !prop.DanceToggle;
                    }
                    else if (prop.Sprite?.Sheet != null)
                    {
                        string firstAnim = prop.Sprite.Sheet.Animations.Keys.FirstOrDefault();
                        if (firstAnim != null)
                            prop.Sprite.PlayAnimation(firstAnim, force: true, loop: true);
                    }
                    prop.LastDanceBeat = beat;
                    _animatedProps[pi] = prop;
                }
            }
            
            // FastCar: 10% chance each beat to zoom across (original limoRide.hxc)
            if (_fastCarCanDrive && _fastCarStaticIndex >= 0 && _random.Next(10) == 0)
            {
                _fastCarVelocityX = (_random.Next(170, 221) * 60f) * 3f; // velocity scaled for 60fps
                _fastCarCanDrive = false;
            }
            
            // Spooky lightning: random flash with cooldown (original: nextBeat + random(3,8))
            if (_halloweenBGPropIndex >= 0 && _lightningBeatCooldown <= 0 && _random.Next(10) == 0)
            {
                if (_flashingLightsEnabled) _lightningFlashAlpha = 1.0f;
                _lightningBeatCooldown = _random.Next(3, 9); // 3-8 beats cooldown
                // Play lightning animation on the BG prop
                var bgProp = _animatedProps[_halloweenBGPropIndex];
                bgProp.Sprite?.PlayAnimation("lightning", force: true, loop: false);
                _animatedProps[_halloweenBGPropIndex] = bgProp;
                // Play thunder sound
                Audio.PlaySound("game/stages/spooky/thunder_" + (_random.Next(2) + 1));
                // Flash GF
                _girlfriend?.PlayAnimation("scared", force: true);
                _boyfriend?.PlayAnimation("scared", force: true);
            }
            if (_lightningBeatCooldown > 0) _lightningBeatCooldown--;
            
            // Philly train: 10% chance each beat when cooldown expired (original phillyTrain.hxc)
            if (_trainStaticIndex >= 0 && !_trainMoving && _trainCooldown <= 0 && _random.Next(10) == 0)
            {
                _trainMoving = true;
                _trainStartedMoving = false;
                _trainFrameTiming = 0;
                _trainFinishing = false;
                Audio.PlaySound("game/stages/philly_train/train_passes");
            }
            if (_trainCooldown > 0) _trainCooldown -= (float)_conductor.Crochet;
            
            // Philly window lights: cycle colors every beat (original: curLight cycles 0-4)
            if (_phillyLightsIndex >= 0)
            {
                _phillyLightColor = (_phillyLightColor + 1) % PHILLY_LIGHT_COLORS.Length;
            }
            
            // Camera bop every CAMERA_ZOOM_RATE beats (original: every 4 beats)
            if (beat % CAMERA_ZOOM_RATE == 0 && _cameraZoomEnabled)
            {
                _cameraBopMultiplier = CAMERA_BOP_INTENSITY;
                _hudZoomAdd += HUD_ZOOM_INTENSITY;
            }
            
            // Icon bounce on every beat
            _iconBounceScale = 1.3f;
        };
    }

    /// <summary>
    /// Adds Nene's ABot speaker rig for Weekend 1 Blazin stage when Nene is the GF slot.
    /// Some stage packs omit this prop from JSON, which makes Nene float without speakers.
    /// </summary>
    private void TryAddNeneSpeakers(string gfName)
    {
        if (_girlfriend == null)
            return;

        string stageFolder = ResolveStageFolder(_chart.Stage);

        if (string.IsNullOrWhiteSpace(gfName))
            return;

        // Only the base `nene` character (and its Tankman variant) has the ABot speaker rig in the
        // original game. Other Nene variants (`nene-christmas`, `nene-pixel`, etc.) do not, and spawning
        // ABot on stages like `mallXmasErect` puts it in the middle of the scene where it doesn't belong.
        bool isAbotNene = gfName.Equals("nene", StringComparison.OrdinalIgnoreCase)
            || gfName.Equals("nene-tankmen", StringComparison.OrdinalIgnoreCase)
            || gfName.Equals("nene-speakers", StringComparison.OrdinalIgnoreCase);
        if (!isAbotNene)
            return;

        if (_animatedProps.Any(p => string.Equals(p.Name, "neneSpeakerRig", StringComparison.OrdinalIgnoreCase))
            || _staticProps.Any(p => string.Equals(p.Name, "neneSpeakerRig", StringComparison.OrdinalIgnoreCase)))
            return;

        float charOffX = _girlfriend.CharOffsets != null && _girlfriend.CharOffsets.Length >= 2 ? _girlfriend.CharOffsets[0] : 0f;
        float charOffY = _girlfriend.CharOffsets != null && _girlfriend.CharOffsets.Length >= 2 ? _girlfriend.CharOffsets[1] : 0f;
        float charScale = _girlfriend.Scale;

        // Mirror original nene.hxc placement:
        // abot.x = this.x - 95 - (-globalOffsets[0] * scale.x)
        // abot.y = this.y + 384 - (-globalOffsets[1] * scale.y)
        float abotX = _girlfriend.Position.X - 95f + (charOffX * charScale);
        float abotY = _girlfriend.Position.Y + 384f + (charOffY * charScale);
        int abotZ = _gfZIndex - 10;
        bool addedRig = false;

        // Match original nene.hxc ABotAtlasSprite exactly:
        // loadTextureAtlas("characters/abot/abotSystem", "shared")
        var systemBodySheet = SpriteSheet.Load(
            Game,
            "images/characters/abot/abotSystem",
            preRenderComposites: true,
            preRenderFilter: new[] { "Abot System", "Nene_assets", "default" },
            deferComposites: true,
            applyStageInstanceTransform: false,
            applyTRP: false);

        bool hasExplicitAbotSystem = systemBodySheet != null && (
            systemBodySheet.Animations.ContainsKey("Abot System")
            || systemBodySheet.RawCompositeData.ContainsKey("Abot System")
            || systemBodySheet.CompositeAnimations.ContainsKey("Abot System"));

        // If this atlas only exposes a generic/default timeline, fall back to the Nene-local
        // ABot source, which keeps the explicit "Abot System" timeline in original content.
        if (systemBodySheet != null && !hasExplicitAbotSystem)
        {
            var speakerSheet = SpriteSheet.Load(
                Game,
                "game/characters/nene/abot/speaker",
                preRenderComposites: true,
                preRenderFilter: new[] { "Abot System", "Nene_assets", "default" },
                deferComposites: true,
                applyStageInstanceTransform: false,
                applyTRP: false);

            if (speakerSheet != null)
            {
                systemBodySheet.Dispose();
                systemBodySheet = speakerSheet;
                hasExplicitAbotSystem = systemBodySheet.Animations.ContainsKey("Abot System")
                    || systemBodySheet.RawCompositeData.ContainsKey("Abot System")
                    || systemBodySheet.CompositeAnimations.ContainsKey("Abot System");
            }
        }
        if (systemBodySheet != null)
        {
            Console.WriteLine($"[ABOT] bodySheet keys anim=[{string.Join(",", systemBodySheet.Animations.Keys)}] raw=[{string.Join(",", systemBodySheet.RawCompositeData.Keys)}] comp=[{string.Join(",", systemBodySheet.CompositeAnimations.Keys)}]");

            string bodyAnim = systemBodySheet.Animations.Keys
                .FirstOrDefault(k => k.Equals("Abot System", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.Animations.Keys
                    .FirstOrDefault(k => k.Equals("Nene_assets", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.RawCompositeData.Keys
                    .FirstOrDefault(k => k.Equals("Abot System", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.RawCompositeData.Keys
                    .FirstOrDefault(k => k.Equals("Nene_assets", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.Animations.Keys
                    .FirstOrDefault(k => k.Equals("default", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.RawCompositeData.Keys
                    .FirstOrDefault(k => k.Equals("default", StringComparison.OrdinalIgnoreCase))
                ?? systemBodySheet.Animations.Keys.FirstOrDefault()
                ?? systemBodySheet.RawCompositeData.Keys.FirstOrDefault();

            if (!string.IsNullOrEmpty(bodyAnim))
            {
                // Strict original gameplay behavior: do NOT auto-apply local editor exports.
                // External offsets can desync per-tick ABot part transforms and cause drift.
                bool appliedEditedOffsets = false;
                if (appliedEditedOffsets)
                    Console.WriteLine($"[ABOT] applied editor offsets to anim='{bodyAnim}'");

                if (systemBodySheet.RawCompositeData.TryGetValue(bodyAnim, out var rawTicks))
                {
                    // Keep AnimateAtlas part list exactly as authored for strict original behavior.
                    // Match original nene.hxc: restart ABot body from frame 1 every beat.
                    _abotRigStartFrame = 1;

                    for (int ti = 0; ti < rawTicks.Count; ti++)
                    {
                        var partNames = rawTicks[ti]
                            .Select(p => p.Frame?.Name)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct()
                            .OrderBy(n => n)
                            .ToArray();
                        Console.WriteLine($"[ABOT] tick={ti}, parts={rawTicks[ti].Count}, names=[{string.Join(",", partNames)}]");
                    }

                    var rawParts = rawTicks
                        .SelectMany(t => t)
                        .Select(p => p.Frame?.Name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .OrderBy(n => n)
                        .ToArray();
                    Console.WriteLine($"[ABOT] rawAnim='{bodyAnim}', ticks={rawTicks.Count}, distinctParts=[{string.Join(",", rawParts)}], chosenStartFrame={_abotRigStartFrame}");

                    _abotRigRuntimeOrigin = ComputeRawCompositeOrigin(rawTicks, _abotRigStartFrame);
                    _abotRigHasRuntimeOrigin = true;
                    Console.WriteLine($"[ABOT] rawOrigin=({_abotRigRuntimeOrigin.X:0.0},{_abotRigRuntimeOrigin.Y:0.0})");

                    // Diagnostic: print bounding box per tick and the min-X/min-Y contributors
                    float gMinX = float.MaxValue, gMinY = float.MaxValue, gMaxX = float.MinValue, gMaxY = float.MinValue;
                    string gMinXName = "", gMinYName = "";
                    int gMinXTick = -1, gMinYTick = -1;
                    for (int t = 0; t < rawTicks.Count; t++)
                    {
                        foreach (var (f, a, b, c, d, tx, ty) in rawTicks[t])
                        {
                            float w = f.Rotated ? f.SourceRect.Height : f.SourceRect.Width;
                            float h = f.Rotated ? f.SourceRect.Width : f.SourceRect.Height;
                            float[] xs = { tx, a * w + tx, c * h + tx, a * w + c * h + tx };
                            float[] ys = { ty, b * w + ty, d * h + ty, b * w + d * h + ty };
                            foreach (var x in xs) { if (x < gMinX) { gMinX = x; gMinXName = f.Name; gMinXTick = t; } if (x > gMaxX) gMaxX = x; }
                            foreach (var y in ys) { if (y < gMinY) { gMinY = y; gMinYName = f.Name; gMinYTick = t; } if (y > gMaxY) gMaxY = y; }
                        }
                    }
                    Console.WriteLine($"[ABOT] BBox local=({gMinX:0.0},{gMinY:0.0})-({gMaxX:0.0},{gMaxY:0.0}) size=({gMaxX-gMinX:0.0}x{gMaxY-gMinY:0.0})");
                    Console.WriteLine($"[ABOT] MinX from part={gMinXName} tick={gMinXTick}, MinY from part={gMinYName} tick={gMinYTick}");

                    // World position of each part in start frame (so we can compare with pupil/viz/eyeWhites positions)
                    if (rawTicks.Count > _abotRigStartFrame)
                    {
                        var startFrameParts = rawTicks[_abotRigStartFrame];
                        Console.WriteLine($"[ABOT] === Parts in startFrame={_abotRigStartFrame} (world coords with bodyPos=({abotX + _abotRigRuntimeOrigin.X:0.0},{abotY + _abotRigRuntimeOrigin.Y:0.0})) ===");
                        float bx = abotX + _abotRigRuntimeOrigin.X;
                        float by = abotY + _abotRigRuntimeOrigin.Y;
                        foreach (var (f, a, _, _, d, tx, ty) in startFrameParts)
                        {
                            float w = f.Rotated ? f.SourceRect.Height : f.SourceRect.Width;
                            float h = f.Rotated ? f.SourceRect.Width : f.SourceRect.Height;
                            float wx0 = bx + tx;
                            float wy0 = by + ty;
                            float wx1 = bx + a * w + tx;
                            float wy1 = by + d * h + ty;
                            Console.WriteLine($"[ABOT]   part={f.Name} a={a:0.00} d={d:0.00} world=({Math.Min(wx0,wx1):0.0},{Math.Min(wy0,wy1):0.0})-({Math.Max(wx0,wx1):0.0},{Math.Max(wy0,wy1):0.0}) size=({w:0}x{h:0})");
                        }
                        Console.WriteLine($"[ABOT] Pupil expected at world ({abotX + 50:0.0},{abotY + 238:0.0}); EyeWhites at ({abotX + 40:0.0},{abotY + 250:0.0})");
                    }

                    // Runtime-composite playback still requires an animation entry.
                    if (!systemBodySheet.Animations.ContainsKey(bodyAnim))
                    {
                        var placeholders = new List<SpriteFrame>(rawTicks.Count);
                        for (int i = 0; i < rawTicks.Count; i++)
                            placeholders.Add(new SpriteFrame { Name = $"{bodyAnim}_{i}", SourceRect = Rectangle.Empty });
                        systemBodySheet.Animations[bodyAnim] = placeholders;
                    }
                }
                else if (systemBodySheet.CompositeAnimations.TryGetValue(bodyAnim, out var compFrames) && compFrames.Count > 0)
                {
                    _abotRigStartFrame = Math.Clamp(1, 0, compFrames.Count - 1);
                    _abotRigHasRuntimeOrigin = false;
                    _abotRigRuntimeOrigin = Vector2.Zero;
                    Console.WriteLine($"[ABOT] compAnim='{bodyAnim}', frames={compFrames.Count}, chosenStartFrame={_abotRigStartFrame}");
                }

                var systemBodySprite = new AnimatedSprite { Sheet = systemBodySheet };
                // Original nene.hxc: abot.anim.play("", true, false, 1) on each beat.
                // Start from frame 1 and don't loop so beat updates can re-trigger it.
                int bodyStartFrame = _abotRigStartFrame;
                if (systemBodySheet.Animations.TryGetValue(bodyAnim, out var bodyFrames) && bodyFrames.Count > 0)
                {
                    bodyStartFrame = Math.Clamp(bodyStartFrame, 0, bodyFrames.Count - 1);
                }
                else
                {
                    bodyStartFrame = 0;
                }
                systemBodySprite.PlayAnimationFromFrame(bodyAnim, bodyStartFrame, loop: _abotUseContinuousAnim, loopFrame: bodyStartFrame);
                var abotBasePos = new Vector2(abotX, abotY);
                var bodyOrigin = systemBodySprite.GetCompositeOrigin();
                systemBodySprite.Position = bodyOrigin.HasValue
                    ? abotBasePos + bodyOrigin.Value
                    : abotBasePos;
                systemBodySprite.Scale = new Vector2(1f, 1f);

                Console.WriteLine($"[ABOT] bodyAnim='{bodyAnim}', hasAbotSystem={hasExplicitAbotSystem}, runtime={systemBodySprite.IsRuntimeComposite()}, comp={systemBodySheet.CompositeAnimations.ContainsKey(bodyAnim)}, raw={systemBodySheet.RawCompositeData.ContainsKey(bodyAnim)}, pos=({systemBodySprite.Position.X:0.0},{systemBodySprite.Position.Y:0.0})");

                _animatedProps.Add(new AnimatedStageProp
                {
                    Sprite = systemBodySprite,
                    ZIndex = abotZ,
                    Alpha = 1f,
                    ScrollX = 1f,
                    ScrollY = 1f,
                    DanceEvery = 1,
                    LastDanceBeat = -1,
                    Name = "neneSpeakerRig",
                    DanceAnimNames = new[] { bodyAnim },
                    DanceToggle = false
                });
                addedRig = true;
            }
            else
            {
                systemBodySheet.Dispose();
            }
        }

        if (!addedRig)
        {
            Console.WriteLine("Nene speaker fallback: could not create ABot rig");
            return;
        }

        var eyesSheet = SpriteSheet.Load(
            Game,
            "images/characters/abot/systemEyes",
            preRenderComposites: true,
            preRenderFilter: new[] { "default" },
            deferComposites: false,
            applyStageInstanceTransform: false);

        if (eyesSheet != null)
        {
            string eyesAnim = eyesSheet.Animations.Keys
                .FirstOrDefault(k => k.Equals("default", StringComparison.OrdinalIgnoreCase))
                ?? eyesSheet.Animations.Keys
                    .FirstOrDefault(k => k.Equals("Nene_assets(1)", StringComparison.OrdinalIgnoreCase))
                ?? eyesSheet.Animations.Keys.FirstOrDefault();

            if (!string.IsNullOrEmpty(eyesAnim))
            {
                var eyesSprite = new AnimatedSprite { Sheet = eyesSheet };
                eyesSprite.PlayAnimationFromFrame(eyesAnim, 17, loop: true, loopFrame: 17);

                var pupilBasePos = new Vector2(abotX + 50f, abotY + 238f);
                eyesSprite.Position = pupilBasePos;
                eyesSprite.Scale = new Vector2(1f, 1f);

                Console.WriteLine($"[ABOT] pupilAnim='{eyesAnim}', runtime={eyesSprite.IsRuntimeComposite()}, comp={eyesSheet.CompositeAnimations.ContainsKey(eyesAnim)}, raw={eyesSheet.RawCompositeData.ContainsKey(eyesAnim)}, pos=({eyesSprite.Position.X:0.0},{eyesSprite.Position.Y:0.0})");

                _animatedProps.Add(new AnimatedStageProp
                {
                    Sprite = eyesSprite,
                    ZIndex = abotZ - 5,
                    Alpha = 1f,
                    ScrollX = 1f,
                    ScrollY = 1f,
                    DanceEvery = 0,
                    LastDanceBeat = -1,
                    Name = "neneSpeakerPupil"
                });
            }
            else
            {
                eyesSheet.Dispose();
            }
        }

        // Original nene.hxc uses a solid white mask behind ABot pupils.
        // Use a dedicated texture (not Assets.Pixel, which is treated as fullscreen color fill).
        var eyeWhitesTexture = new Texture2D(Game.GraphicsDevice, 160, 60);
        var eyeWhitesData = new Color[160 * 60];
        Array.Fill(eyeWhitesData, Color.White);
        eyeWhitesTexture.SetData(eyeWhitesData);
        _staticProps.Add(new StaticStageProp
        {
            Texture = eyeWhitesTexture,
            X = abotX + 40f,
            Y = abotY + 250f,
            ScaleX = 1f,
            ScaleY = 1f,
            Alpha = 1f,
            ScrollX = 1f,
            ScrollY = 1f,
            ZIndex = abotZ - 10,
            Name = "neneSpeakerEyeWhites"
        });

        var stereoBodyTex = Assets.LoadTexture("images/characters/abot/stereoBG")
            ?? Assets.LoadTexture("game/characters/nene/abot/stereoBG")
            ?? Assets.LoadTexture("images/characters/abot/stereoBG.png");
        if (hasExplicitAbotSystem && stereoBodyTex != null && stereoBodyTex != Assets.Pixel)
        {
            _staticProps.Add(new StaticStageProp
            {
                Texture = stereoBodyTex,
                X = abotX + 150f,
                Y = abotY + 30f,
                ScaleX = 1f,
                ScaleY = 1f,
                Alpha = 1f,
                ScrollX = 1f,
                ScrollY = 1f,
                ZIndex = abotZ - 8,
                Name = "neneSpeakerStereoBG"
            });
        }

        // Original ABotVis uses aBotViz with 7 bars and fixed relative offsets.
        var vizSheet = SpriteSheet.Load(Game, "images/characters/abot/aBotViz")
            ?? SpriteSheet.Load(Game, "game/characters/nene/abot/visualizer");
        if (vizSheet != null)
        {
            float vizBaseX = abotX + 207f;
            float vizBaseY = abotY + 84f;

            float[] vizOffsetsX = { 0f, 59f, 56f, 66f, 54f, 52f, 51f };
            float[] vizOffsetsY = { 0f, -8f, -3.5f, -0.4f, 0.5f, 4.7f, 7f };

            bool anyVizAdded = false;
            for (int barIndex = 1; barIndex <= 7; barIndex++)
            {
                string framePrefix = $"viz{barIndex}0";
                var barFrames = vizSheet.Frames.Values
                    .Where(f => f.Name.StartsWith(framePrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f =>
                    {
                        int i = f.Name.Length - 1;
                        while (i >= 0 && char.IsDigit(f.Name[i])) i--;
                        return int.TryParse(f.Name[(i + 1)..], out int n) ? n : 0;
                    })
                    .ToList();

                if (barFrames.Count == 0)
                    continue;

                string barAnim = $"neneVizBar{barIndex}";
                vizSheet.Animations[barAnim] = barFrames;

                var vizSprite = new AnimatedSprite { Sheet = vizSheet };
                vizSprite.PlayAnimationFromFrame(barAnim, 5, loop: false, loopFrame: 5);

                float addX = 0f;
                float addY = 0f;
                for (int i = 0; i < barIndex; i++)
                {
                    addX += vizOffsetsX[i];
                    addY += vizOffsetsY[i];
                }

                vizSprite.Position = new Vector2(vizBaseX + addX, vizBaseY + addY);
                vizSprite.Scale = new Vector2(1f, 1f);

                _animatedProps.Add(new AnimatedStageProp
                {
                    Sprite = vizSprite,
                    ZIndex = abotZ - 1,
                    Alpha = 0f,
                    ScrollX = 1f,
                    ScrollY = 1f,
                    DanceEvery = 0,
                    LastDanceBeat = -1,
                    Name = $"neneSpeakerViz{barIndex}",
                    DanceAnimNames = new[] { barAnim }
                });
                anyVizAdded = true;
                Console.WriteLine($"[ABOT] vizBar={barIndex}, frames={barFrames.Count}, z={abotZ - 1}, pos=({vizSprite.Position.X:0.0},{vizSprite.Position.Y:0.0})");
            }

            if (!anyVizAdded)
                vizSheet.Dispose();
        }

        _staticProps = _staticProps.OrderBy(p => p.ZIndex).ToList();
        _animatedProps = _animatedProps.OrderBy(p => p.ZIndex).ToList();
        _hasAbotFallback = true;
        _abotBaseX = abotX;
        _abotBaseY = abotY;
        Console.WriteLine($"Nene speaker fallback: added ABot rig at ({abotX:0.0}, {abotY:0.0}) stage='{_chart.Stage}' folder='{stageFolder}'");
    }

    private static Vector2 ComputeRawCompositeOrigin(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts, int frameIndex = -1)
    {
        float gMinX = float.MaxValue;
        float gMinY = float.MaxValue;

        // FlxAnimate/FlxSprite convention: sprite.x is the bounding-box top-left of the
        // CURRENT visible frame. Computing the min across all frames over-extends the
        // bounding box when animation frames have parts at different positions (e.g. the
        // ABot's chest alternates between two sprite variants with different local origins).
        // Use a single representative frame (defaults to frame 0 / idle) to match the
        // per-frame bounding box behavior.
        int startFrame = (frameIndex >= 0 && frameIndex < tickParts.Count) ? frameIndex : 0;
        int endFrame = startFrame + 1;

        for (int t = startFrame; t < endFrame; t++)
        {
            foreach (var (frame, a, b, c, d, tx, ty) in tickParts[t])
            {
                float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;

                float x0 = tx, y0 = ty;
                float x1 = a * w + tx, y1 = b * w + ty;
                float x2 = c * h + tx, y2 = d * h + ty;
                float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;

                gMinX = Math.Min(gMinX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
                gMinY = Math.Min(gMinY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
            }
        }

        return (gMinX < float.MaxValue) ? new Vector2(-gMinX, -gMinY) : Vector2.Zero;
    }

    /// <summary>
    /// Pre-cache all note and receptor sprite frames to avoid string allocation
    /// and dictionary lookups every frame in the draw loop.
    /// </summary>
    private void CacheNoteFrames()
    {
        for (int i = 0; i < 4; i++)
        {
            string lane = _laneNames[i];
            
            // Note head frames
            if (_notesSheet != null)
            {
                _notesSheet.Animations.TryGetValue($"{lane} note", out var noteFrameList);
                _noteFrames[i] = noteFrameList?.Count > 0 ? noteFrameList[0] : null;
                
                _notesSheet.Animations.TryGetValue($"{lane} sustain", out var sustainList);
                _sustainFrames[i] = sustainList?.Count > 0 ? sustainList[0] : null;
                
                _notesSheet.Animations.TryGetValue($"{lane} sustain end", out var sustainEndList);
                _sustainEndFrames[i] = sustainEndList?.Count > 0 ? sustainEndList[0] : null;
            }
            
            // Receptor frames
            if (_receptorsSheet != null)
            {
                _receptorsSheet.Animations.TryGetValue($"{lane} static", out var staticList);
                _receptorStaticFrames[i] = staticList;
                
                _receptorsSheet.Animations.TryGetValue($"{lane} confirm", out var confirmList);
                _receptorConfirmFrames[i] = confirmList;
                
                _receptorsSheet.Animations.TryGetValue($"{lane} press", out var pressList);
                _receptorPressFrames[i] = pressList;
            }
        }
    }
    
    private string ResolveCharacterSprite(string charName)
    {
        // Map character names to their sprite folder names
        // Only map when the variant genuinely reuses the base character's sprites
        string resolved = charName switch
        {
            "pico-playable" => "pico",
            "bf-car" => "bf",            // Car variant uses same bf sprites
            "gf-car" => "gf",            // Car variant uses same gf sprites
            "mom-car" => "mom",           // Car variant uses same mom sprites
            "parents-christmas" => "parents_christmas", // Folder uses underscore
            "bf-pixel" => "bf_pixel",     // Folder uses underscore
            _ => charName
        };
        
        // Check if the character folder actually exists (Content or funkin.assets)
        // Try exact charName first (e.g., "bf-christmas" has its own folder)
        if (Assets.ResolveDirectory($"game/characters/{charName}") != null)
            return charName;
        if (resolved != charName && Assets.ResolveDirectory($"game/characters/{resolved}") != null)
            return resolved;
        
        // Fallback: strip variant suffixes (-dark, -christmas, -pixel, etc.) to find base character
        if (charName.Contains('-'))
        {
            string baseName = charName[..charName.LastIndexOf('-')];
            if (Assets.ResolveDirectory($"game/characters/{baseName}") != null)
                return baseName;
        }
        
        return resolved;
    }


    /// <summary>
    /// Load character health bar icon from multiple paths (original: preload/images/icons/icon-{name}.png)
    /// </summary>
    private Texture2D LoadCharacterIcon(string charName, string resolvedSprite)
    {
        // Try original FNF path: images/icons/icon-{name}.png
        var tex = Assets.LoadTexture($"images/icons/icon-{charName}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        
        // Try resolved sprite name
        if (resolvedSprite != charName)
        {
            tex = Assets.LoadTexture($"images/icons/icon-{resolvedSprite}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        
        // Try character folder path
        tex = Assets.LoadTexture($"game/characters/{resolvedSprite}/icon.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        
        // Try base name (strip variant suffix: "bf-car" ? "bf")
        string baseName = charName.Contains('-') ? charName.Split('-')[0] : charName;
        if (baseName != charName)
        {
            tex = Assets.LoadTexture($"images/icons/icon-{baseName}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        
        // Fallback: generic face icon
        tex = Assets.LoadTexture("images/icons/icon-face.png");
        return tex ?? Assets.Pixel;
    }
    
    /// <summary>
    /// Resolve the note skin folder path from the chart's noteStyle.
    /// Maps noteStyle names to skin folders under Content/game/skins/.
    /// </summary>
    private string ResolveNoteSkinPath(string noteStyle)
    {
        if (string.IsNullOrEmpty(noteStyle) || noteStyle == "funkin")
            return "game/skins/default";
        
        // Check if a skin folder exists for this noteStyle
        string skinPath = $"game/skins/{noteStyle}";
        if (Assets.ResolveDirectory(skinPath) != null)
            return skinPath;
        
        // Map known noteStyle names to folders
        string mapped = noteStyle switch
        {
            "pixel" => "game/skins/pixel",
            "pixel-erect" => "game/skins/pixel",
            _ => "game/skins/default"
        };
        
        if (Assets.ResolveDirectory(mapped) != null)
            return mapped;
        
        return "game/skins/default";
    }
    
    /// <summary>
    /// Map chart stage names to actual stage folder names on disk.
    /// Chart metadata uses names like "mainStage", "spookyMansion", etc.
    /// </summary>
    private string ResolveStageFolder(string stageName)
    {
        string resolved = stageName switch
        {
            "mainStage" => "stage",
            "stage" => "stage",
            "mainStageErect" => "stage_erect",
            "stageErect" => "stage_erect",
            "stage_erect" => "stage_erect",
            "spookyMansion" => "spooky",
            "spookyMansionErect" => "spooky",
            "spooky" => "spooky",
            "phillyTrain" => "philly_train",
            "phillyTrainErect" => "philly_train_erect",
            "philly" => "philly_train",
            "philly-train" => "philly_train",
            "philly_train_erect" => "philly_train_erect",
            "limoRide" => "limo",
            "limoRideErect" => "limo_erect",
            "limo" => "limo",
            "limo_erect" => "limo_erect",
            "phillyStreets" => "philly_streets",
            "phillyStreetsErect" => "philly_streets",
            "philly_streets" => "philly_streets",
            "mallXmas" => "christmas",
            "mallXmasErect" => "christmas",
            "mallEvil" => "christmas",
            "school" => "school",
            "schoolEvil" => "school",
            "schoolErect" => "school",
            "schoolEvilErect" => "school",
            "tankmanBattlefield" => "tankman",
            "tankmanBattlefieldErect" => "tankman",
            "phillyBlazin" => "philly_blazin",
            // Asset path prefixes used in stage JSONs (e.g., "weeb/weebSky" splits to "weeb")
            "weeb" => "school",
            _ => stageName
        };
        
        // Verify the folder exists (Content or funkin.assets), fall back to "stage"
        if (Assets.ResolveDirectory($"game/stages/{resolved}") != null)
            return resolved;
        if (Assets.ResolveDirectory($"game/stages/{stageName}") != null)
            return stageName;
        
        Console.WriteLine($"Stage folder not found for '{stageName}', falling back to 'stage'");
        return "stage";
    }

    /// <summary>
    /// Build stage JSON name candidates for robust lookup across stage aliases.
    /// </summary>
    private List<string> GetStageDataNameCandidates(string stageName, string stageFolder)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(stageName))
        {
            candidates.Add(stageName);
            candidates.Add(stageName.Replace('-', '_'));
            candidates.Add(stageName.Replace('_', '-'));
        }

        if (!string.IsNullOrWhiteSpace(stageFolder))
        {
            candidates.Add(stageFolder);
            candidates.Add(stageFolder.Replace('-', '_'));

            string canonical = stageFolder switch
            {
                "stage" => "mainStage",
                "stage_erect" => "mainStageErect",
                "spooky" => "spookyMansion",
                "philly_train" => "phillyTrain",
                "philly_train_erect" => "phillyTrainErect",
                "limo" => "limoRide",
                "limo_erect" => "limoRideErect",
                "philly_streets" => "phillyStreets",
                "christmas" => "mallXmas",
                "school" => "school",
                "tankman" => "tankmanBattlefield",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(canonical))
                candidates.Add(canonical);
        }

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Ensure pending stage character slot data is available for BF/DAD/GF.
    /// This is a fallback path for cases where phased stage loading did not
    /// populate pending arrays before characters are initialized.
    /// </summary>
    private void EnsurePendingStageCharacterData(string stageFolder)
    {
        bool hasAnyPendingData = _pendingBfPos != null || _pendingDadPos != null || _pendingGfPos != null
                              || _pendingBfCamOffsets != null || _pendingDadCamOffsets != null || _pendingGfCamOffsets != null;
        if (hasAnyPendingData)
            return;

        StageJsonData stageJson = null;
        foreach (var candidate in GetStageDataNameCandidates(_chart.Stage, stageFolder))
        {
            stageJson = Assets.LoadJson<StageJsonData>($"data/stages/{candidate}");
            if (stageJson != null)
                break;
        }

        if (stageJson?.Characters == null)
            return;

        if (stageJson.Characters.Bf?.Position != null)
            _pendingBfPos = stageJson.Characters.Bf.Position;
        if (stageJson.Characters.Dad?.Position != null)
            _pendingDadPos = stageJson.Characters.Dad.Position;
        if (stageJson.Characters.Gf?.Position != null)
            _pendingGfPos = stageJson.Characters.Gf.Position;

        _pendingBfCamOffsets = stageJson.Characters.Bf?.CameraOffsets;
        _pendingDadCamOffsets = stageJson.Characters.Dad?.CameraOffsets;
        _pendingGfCamOffsets = stageJson.Characters.Gf?.CameraOffsets;

        if (stageJson.Characters.Gf != null) _gfZIndex = stageJson.Characters.Gf.ZIndex;
        if (stageJson.Characters.Dad != null) _dadZIndex = stageJson.Characters.Dad.ZIndex;
        if (stageJson.Characters.Bf != null) _bfZIndex = stageJson.Characters.Bf.ZIndex;
    }
    
    /// <summary>
    /// Load stage textures from JSON data if available, otherwise fall back to resolved folder.
    /// Parses Content/data/stages/{stageName}.json for prop definitions.
    /// </summary>
    private void LoadStageTextures(string stageFolder)
    {
        string basePath = $"game/stages/{stageFolder}";
        Console.WriteLine($"Loading stage: {stageFolder}");
        
        // Try loading stage JSON (original uses data/stages/{stageName}.json)
        // Use robust candidate lookup so all stage aliases still get character positions.
        StageJsonData stageJson = null;
        string loadedStageDataName = null;
        foreach (var candidate in GetStageDataNameCandidates(_chart.Stage, stageFolder))
        {
            stageJson = Assets.LoadJson<StageJsonData>($"data/stages/{candidate}");
            if (stageJson != null)
            {
                loadedStageDataName = candidate;
                break;
            }
        }

        if (stageJson != null)
        {
            _isPhillyBlazinStage = string.Equals(stageFolder, "philly_blazin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_chart.Stage, "phillyBlazin", StringComparison.OrdinalIgnoreCase);
            int propCount = stageJson.Props?.Count ?? 0;
            Console.WriteLine($"Loaded stage JSON for '{loadedStageDataName}' with {propCount} props");
            _hasJsonStage = true;
            _stageDirectory = stageJson.Directory;
            
            // Apply camera zoom from stage JSON (original: defaultCamZoom)
            _defaultCamZoom = stageJson.CameraZoom;
            
            var sortedProps = (stageJson.Props ?? new List<StageProp>()).OrderBy(p => p.ZIndex).ToList();
            
            foreach (var prop in sortedProps)
            {
                if (string.IsNullOrEmpty(prop.AssetPath)) continue;
                
                // Handle animated props separately
                if (prop.Animations != null && prop.Animations.Count > 0)
                {
                    // Skip color-hex props (they have no spritesheet)
                    if (prop.AssetPath.StartsWith("#")) continue;
                    
                    string stageDir = ResolveStageFolder(_chart.Stage);
                    
                    // Stage JSON assetPaths may include a folder prefix (e.g., "limo/bgLimo").
                    // Strip the prefix if it matches the stage folder to avoid double-path.
                    // Preserves subpaths (e.g., "christmas/erect/bottomBop" -> "erect/bottomBop")
                    string propPath = prop.AssetPath;
                    if (propPath.Contains('/'))
                    {
                        string[] parts = propPath.Split('/');
                        string prefix = ResolveStageFolder(parts[0]);
                        if (prefix == stageDir)
                            propPath = string.Join('/', parts[1..]);
                        // Erect stages use "erect/" prefix � strip it
                        else if (parts[0] == "erect")
                            propPath = string.Join('/', parts[1..]);
                    }
                    
                    var sheet = SpriteSheet.Load(Game, $"game/stages/{stageDir}/{propPath}");
                    
                    // Try funkin.assets week directory (e.g., week2/images/halloween_bg)
                    if (sheet == null && !string.IsNullOrEmpty(_stageDirectory))
                        sheet = SpriteSheet.Load(Game, $"{_stageDirectory}/images/{prop.AssetPath}");
                    
                    // Try cross-stage path
                    if (sheet == null && prop.AssetPath.Contains('/'))
                    {
                        string[] parts = prop.AssetPath.Split('/');
                        string subFolder = ResolveStageFolder(parts[0]);
                        string fileName = parts[^1];
                        sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/{fileName}");
                        if (sheet == null)
                            sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/erect/{fileName}");
                        // "philly/erect/bgFreaks" -> try philly_train_erect/bgFreaks
                        if (sheet == null && parts.Length >= 3 && parts[1] == "erect")
                            sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}_erect/{fileName}");
                    }
                    if (sheet == null)
                        sheet = SpriteSheet.Load(Game, $"game/stages/{stageDir}/erect/{prop.AssetPath}");
                    
                    // Try shared images
                    if (sheet == null)
                        sheet = SpriteSheet.Load(Game, $"images/{prop.AssetPath}");
                    
                    if (sheet == null) continue;
                    
                    // Register frame-index sub-animations from stage JSON
                    foreach (var anim in prop.Animations)
                    {
                        string prefix = anim.Prefix ?? anim.Name ?? "";
                        string animName = anim.Name ?? prefix;
                        if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(animName)) continue;
                        
                        // Find the full animation by prefix
                        var fullFrames = sheet.GetAnimationFuzzy(prefix);
                        if (fullFrames == null || fullFrames.Count == 0) continue;
                        
                        if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                        {
                            // Create sub-animation from specific frame indices
                            var subFrames = new List<SpriteFrame>();
                            foreach (int idx in anim.FrameIndices)
                            {
                                if (idx >= 0 && idx < fullFrames.Count)
                                    subFrames.Add(fullFrames[idx]);
                            }
                            if (subFrames.Count > 0)
                                sheet.Animations[animName] = subFrames;
                        }
                        else if (animName != prefix && !sheet.Animations.ContainsKey(animName))
                        {
                            // Alias: register the full animation under the animation name
                            sheet.Animations[animName] = fullFrames;
                        }
                    }
                    
                    var animSprite = new AnimatedSprite { Sheet = sheet };
                    float px = prop.Position != null && prop.Position.Length >= 1 ? prop.Position[0] : 0;
                    float py = prop.Position != null && prop.Position.Length >= 2 ? prop.Position[1] : 0;
                    animSprite.Position = new Vector2(px, py);
                    
                    float sx = prop.Scale != null && prop.Scale.Length >= 1 ? prop.Scale[0] : 1f;
                    float sy = prop.Scale != null && prop.Scale.Length >= 2 ? prop.Scale[1] : sx;
                    animSprite.Scale = new Vector2(sx, sy);
                    
                    // Use startingAnimation if specified, otherwise first animation
                    string startAnim = prop.StartingAnimation;
                    if (string.IsNullOrEmpty(startAnim))
                        startAnim = prop.Animations[0].Name ?? prop.Animations[0].Prefix;
                    if (!string.IsNullOrEmpty(startAnim))
                    {
                        var frames = sheet.GetAnimationFuzzy(startAnim);
                        if (frames != null && frames.Count > 0)
                        {
                            string animKey = sheet.Animations.FirstOrDefault(k => k.Value == frames).Key ?? startAnim;
                            animSprite.PlayAnimation(animKey, loop: true);
                        }
                    }
                    
                    // Build dance animation name list for alternation
                    string[] danceAnims = null;
                    if (prop.DanceEvery > 0 && prop.Animations.Count >= 2)
                    {
                        danceAnims = prop.Animations.Select(a => a.Name ?? a.Prefix).ToArray();
                    }
                    
                    _animatedProps.Add(new AnimatedStageProp
                    {
                        Sprite = animSprite,
                        ZIndex = (int)prop.ZIndex,
                        Alpha = Math.Clamp(prop.Alpha, 0f, 1f),
                        ScrollX = prop.Scroll != null && prop.Scroll.Length >= 1 ? prop.Scroll[0] : 1f,
                        ScrollY = prop.Scroll != null && prop.Scroll.Length >= 2 ? prop.Scroll[1] : 1f,
                        DanceEvery = (int)prop.DanceEvery,
                        LastDanceBeat = -1,
                        Name = prop.Name ?? "",
                        DanceAnimNames = danceAnims,
                        DanceToggle = false
                    });
                    continue;
                }
                
                // Static prop � load texture with position/scale/scroll from JSON
                var tex = LoadStagePropTexture(prop.AssetPath);
                if (tex == null || tex == Assets.Pixel) continue;
                
                float spx = prop.Position != null && prop.Position.Length >= 1 ? prop.Position[0] : 0;
                float spy = prop.Position != null && prop.Position.Length >= 2 ? prop.Position[1] : 0;
                float ssx = prop.Scale != null && prop.Scale.Length >= 1 ? prop.Scale[0] : 1f;
                float ssy = prop.Scale != null && prop.Scale.Length >= 2 ? prop.Scale[1] : ssx;
                float scrX = prop.Scroll != null && prop.Scroll.Length >= 1 ? prop.Scroll[0] : 1f;
                float scrY = prop.Scroll != null && prop.Scroll.Length >= 2 ? prop.Scroll[1] : 1f;
                
                _staticProps.Add(new StaticStageProp
                {
                    Texture = tex,
                    X = spx, Y = spy,
                    ScaleX = ssx, ScaleY = ssy,
                    Alpha = Math.Clamp(prop.Alpha, 0f, 1f),
                    ScrollX = scrX, ScrollY = scrY,
                    ZIndex = (int)prop.ZIndex,
                    Name = prop.Name ?? prop.AssetPath,
                    Blend = prop.Blend,
                    OverlayColor = ParseStageColor(prop.Color)
                });
            }
            
            // Detect fastCar static prop for scripted movement (original limoRide.hxc)
            for (int si = 0; si < _staticProps.Count; si++)
            {
                if (_staticProps[si].Name == "fastCar")
                {
                    _fastCarStaticIndex = si;
                    _fastCarCanDrive = true;
                    break;
                }
            }
            
            // Detect halloweenBG animated prop for lightning flash (original spookyMansion)
            for (int ai = 0; ai < _animatedProps.Count; ai++)
            {
                if (_animatedProps[ai].Name == "halloweenBG")
                {
                    _halloweenBGPropIndex = ai;
                    break;
                }
            }
            
            // Detect train static prop for scripted movement (original phillyTrain.hxc)
            for (int si = 0; si < _staticProps.Count; si++)
            {
                if (_staticProps[si].Name == "train")
                {
                    _trainStaticIndex = si;
                    _trainX = _staticProps[si].X;
                    break;
                }
            }
            
            // Detect philly window lights prop for color cycling
            for (int si = 0; si < _staticProps.Count; si++)
            {
                if (_staticProps[si].Name == "lights")
                {
                    _phillyLightsIndex = si;
                    break;
                }
            }

            // Detect Philly Blazin stage props for lightning effects
            _isPhillyBlazinStage = string.Equals(_chart.Stage, "phillyBlazin", StringComparison.OrdinalIgnoreCase);
            _phillyBlazinSkyAdditiveIndex = _staticProps.FindIndex(p => p.Name == "skyAdditive");
            _phillyBlazinForegroundMultiplyIndex = _staticProps.FindIndex(p => p.Name == "foregroundMultiply");
            _phillyBlazinAdditionalLightenIndex = _staticProps.FindIndex(p => p.Name == "neneSpeakerStereoBG");
            _phillyBlazinLightningIndex = _animatedProps.FindIndex(p => p.Name == "lightning");
            if (_isPhillyBlazinStage)
            {
                _phillyBlazinLightningTimer = 3.0f;
                _phillyBlazinLightningFadeTimer = 0f;
                _phillyBlazinLightningShortFadeTimer = 0f;
                if (_phillyBlazinSkyAdditiveIndex >= 0)
                {
                    var sky = _staticProps[_phillyBlazinSkyAdditiveIndex];
                    sky.Alpha = 0f;
                    _staticProps[_phillyBlazinSkyAdditiveIndex] = sky;
                }
                if (_phillyBlazinForegroundMultiplyIndex >= 0)
                {
                    var fg = _staticProps[_phillyBlazinForegroundMultiplyIndex];
                    fg.Alpha = 0f;
                    _staticProps[_phillyBlazinForegroundMultiplyIndex] = fg;
                }
                if (_phillyBlazinAdditionalLightenIndex >= 0)
                {
                    var add = _staticProps[_phillyBlazinAdditionalLightenIndex];
                    add.Alpha = 0f;
                    _staticProps[_phillyBlazinAdditionalLightenIndex] = add;
                }
                if (_phillyBlazinLightningIndex >= 0)
                {
                    var lightning = _animatedProps[_phillyBlazinLightningIndex];
                    lightning.Alpha = 0f;
                    _animatedProps[_phillyBlazinLightningIndex] = lightning;
                }
            }
            
            // Save character positions and camera offsets from stage JSON
            // (applied after characters are loaded � stage offsets override character JSON)
            if (stageJson.Characters != null)
            {
                if (stageJson.Characters.Bf?.Position != null)
                    _pendingBfPos = stageJson.Characters.Bf.Position;
                if (stageJson.Characters.Dad?.Position != null)
                    _pendingDadPos = stageJson.Characters.Dad.Position;
                if (stageJson.Characters.Gf?.Position != null)
                    _pendingGfPos = stageJson.Characters.Gf.Position;
                // Stage JSON camera offsets ADD to character JSON offsets (original Stage.hx behavior)
                _pendingBfCamOffsets = stageJson.Characters.Bf?.CameraOffsets;
                _pendingDadCamOffsets = stageJson.Characters.Dad?.CameraOffsets;
                _pendingGfCamOffsets = stageJson.Characters.Gf?.CameraOffsets;
                // Save character z-indices for proper interleaved draw order
                if (stageJson.Characters.Gf != null) _gfZIndex = stageJson.Characters.Gf.ZIndex;
                if (stageJson.Characters.Dad != null) _dadZIndex = stageJson.Characters.Dad.ZIndex;
                if (stageJson.Characters.Bf != null) _bfZIndex = stageJson.Characters.Bf.ZIndex;
            }
            return;
        }
        
        // Fallback: hardcoded stage loading by folder
        _hasJsonStage = false;
        
        // For non-JSON stages, characters are in screen-space coordinates.
        // Set camera so WorldToScreen is approximately a passthrough.
        _defaultCamZoom = 1.0f;
        _camWorldX = FNFGame.SCREEN_WIDTH / 2f;
        _camWorldY = FNFGame.SCREEN_HEIGHT / 2f;
        _camTargetWorldX = _camWorldX;
        _camTargetWorldY = _camWorldY;
        
        switch (stageFolder)
        {
            case "spooky":
                _stageBackdrop = Assets.LoadTexture($"{basePath}/bg.png");
                _stageFront = null;
                _stageCurtains = null;
                break;
            case "philly_train":
            case "philly_train_erect":
                _stageBackdrop = Assets.LoadTexture($"{basePath}/sky.png");
                _stageFront = Assets.LoadTexture($"{basePath}/street.png");
                _stageCurtains = null;
                break;
            case "limo":
            case "limo_erect":
                _stageBackdrop = Assets.LoadTexture($"{basePath}/limoSunset.png");
                _stageFront = Assets.LoadTexture($"{basePath}/bgLimo.png");
                _stageCurtains = null;
                break;
            case "philly_streets":
                _stageBackdrop = Assets.LoadTexture($"{basePath}/phillyTraffic.png");
                _stageFront = null;
                _stageCurtains = null;
                break;
            case "stage_erect":
                _stageBackdrop = Assets.LoadTexture($"{basePath}/bg.png");
                _stageFront = null;
                _stageCurtains = null;
                break;
            default:
                // Default "stage" layout
                _stageBackdrop = Assets.LoadTexture($"{basePath}/backdrop.png");
                _stageFront = Assets.LoadTexture($"{basePath}/front.png");
                _stageCurtains = Assets.LoadTexture($"{basePath}/curtains.png");
                break;
        }
    }
    
    /// <summary>
    /// Try loading a stage prop texture from its assetPath.
    /// Stage JSON references assets like "stageback" which maps to game/stages/stage/stageback.png
    /// Also handles:
    ///   - Color hex codes (#RRGGBB) ? creates a 1x1 solid color texture
    ///   - Cross-stage references (e.g., "phillyStreets/phillySkyline")
    ///   - funkin.assets week directory (e.g., directory="week2" ? week2/images/{assetPath}.png)
    ///   - Erect subfolder fallbacks
    /// </summary>
    private string _stageDirectory; // Set from stage JSON "directory" field
    
    private Texture2D LoadStagePropTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        
        // Handle color hex codes (e.g., "#8E9191") � create a solid color texture
        if (assetPath.StartsWith("#") && (assetPath.Length == 7 || assetPath.Length == 9))
        {
            try
            {
                string hex = assetPath.TrimStart('#');
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex[2..4], 16);
                int b = Convert.ToInt32(hex[4..6], 16);
                var colorTex = new Texture2D(Game.GraphicsDevice, 1, 1);
                colorTex.SetData(new[] { new Color(r, g, b) });
                return colorTex;
            }
            catch { return null; }
        }
        
        string stageFolder = ResolveStageFolder(_chart.Stage);
        
        // Strip stage folder prefix from assetPath to avoid double-path
        // (e.g., "limo/limoSunset" with stageFolder "limo" -> just "limoSunset")
        // Preserves subpaths (e.g., "christmas/erect/bgWalls" -> "erect/bgWalls")
        string resolvedAsset = assetPath;
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string prefix = ResolveStageFolder(parts[0]);
            if (prefix == stageFolder)
                resolvedAsset = string.Join('/', parts[1..]);
            // Erect stages use "erect/" prefix in asset paths (e.g., "erect/bg", "erect/crowd")
            // Strip it � the files live directly in the stage folder, not an erect/ subfolder
            else if (parts[0] == "erect")
                resolvedAsset = string.Join('/', parts[1..]);
        }
        
        // Try: game/stages/{folder}/{asset} (Content directory)
        var tex = Assets.LoadTexture($"game/stages/{stageFolder}/{resolvedAsset}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        
        // Try: {weekDir}/images/{assetPath} (funkin.assets week directory from stage JSON)
        if (!string.IsNullOrEmpty(_stageDirectory))
        {
            tex = Assets.LoadTexture($"{_stageDirectory}/images/{assetPath}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        
        // If assetPath contains a subfolder (e.g., "phillyStreets/phillySkyline"),
        // resolve the subfolder name to the actual stage folder and search there
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string subStageFolder = ResolveStageFolder(parts[0]);
            string fileName = parts[^1];
            
            tex = Assets.LoadTexture($"game/stages/{subStageFolder}/{fileName}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
            
            tex = Assets.LoadTexture($"game/stages/{subStageFolder}/erect/{fileName}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
            
            tex = Assets.LoadTexture($"game/stages/{subStageFolder}/{string.Join('/', parts[1..])}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
            
            // "philly/erect/sky" -> try philly_train_erect/sky.png
            if (parts.Length >= 3 && parts[1] == "erect")
            {
                string erectFolder = subStageFolder + "_erect";
                tex = Assets.LoadTexture($"game/stages/{erectFolder}/{fileName}.png");
                if (tex != null && tex != Assets.Pixel) return tex;
            }
        }
        
        // Try: game/stages/{folder}/erect/{assetPath}
        tex = Assets.LoadTexture($"game/stages/{stageFolder}/erect/{assetPath}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        
        // Try: images/{assetPath} (shared images � stageback, stagefront, etc.)
        tex = Assets.LoadTexture($"images/{assetPath}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        
        return null;
    }

    private static Color? ParseStageColor(string colorValue)
    {
        if (string.IsNullOrWhiteSpace(colorValue))
            return null;

        if (!colorValue.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            return null;

        string hex = colorValue.TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
            return null;

        if (!int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out int r))
            return null;
        if (!int.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out int g))
            return null;
        if (!int.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out int b))
            return null;

        byte a = 255;
        if (hex.Length == 8 && !byte.TryParse(hex[6..8], System.Globalization.NumberStyles.HexNumber, null, out a))
            return null;

        return new Color(r, g, b, a);
    }
    
    private void StartSong()
    {
        // Determine inst path � handle variant songs (e.g. bopeebo_erect ? bopeebo/Inst-erect)
        string baseSong = _songName;
        string variant = null;
        
        string[] variantSuffixes = { "_erect", "_pico", "_bf" };
        foreach (var suffix in variantSuffixes)
        {
            if (_songName.EndsWith(suffix))
            {
                baseSong = _songName[..^suffix.Length];
                variant = suffix[1..]; // "erect", "pico", or "bf"
                break;
            }
        }

        if (string.IsNullOrEmpty(variant)
            && !string.IsNullOrWhiteSpace(_selectedCharacterVariation)
            && SongSupportsAudioVariation(baseSong, _selectedCharacterVariation))
        {
            variant = _selectedCharacterVariation;
        }
        
        // Build candidate inst filenames based on song variant and selected difficulty.
        var instFileCandidates = new List<string>();
        if (!string.IsNullOrEmpty(variant))
            instFileCandidates.Add($"Inst-{variant}");

        string difficultyId = (_currentDifficulty ?? "normal").Trim().ToLowerInvariant();
        if (difficultyId == "nightmare")
        {
            instFileCandidates.Add("Inst-nightmare");
            instFileCandidates.Add("Inst-erect");
        }
        else if (difficultyId == "erect")
        {
            instFileCandidates.Add("Inst-erect");
        }

        instFileCandidates.Add("Inst");
        instFileCandidates = instFileCandidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        // Try multiple audio folder names � some songs use hyphens, others underscores
        // e.g. chart folder "dad_battle" but audio in "dadbattle"
        string[] audioFolders = GetAudioFolderCandidates(baseSong);
        
        string instPath = null;
        string audioFolder = null;
        foreach (var folder in audioFolders)
        {
            foreach (var instFile in instFileCandidates)
            {
                instPath = $"songs/{folder}/{instFile}";
                if (AudioFileExists(instPath))
                {
                    audioFolder = folder;
                    break;
                }
            }

            if (audioFolder != null)
                break;
        }
        
        // Fallback � just use baseSong
        if (audioFolder == null)
        {
            audioFolder = baseSong;
            instPath = $"songs/{baseSong}/{instFileCandidates[0]}";
        }
        
        Console.WriteLine($"Starting song: {instPath}");
        Audio.PlayMusic(instPath, false);
        
        // Play voice tracks if they exist
        string playerChar = _chart.PlayerCharacter ?? "bf";
        string opponentChar = _chart.OpponentCharacter ?? "dad";
        var voiceSuffixes = new List<string>();
        if (!string.IsNullOrEmpty(variant))
            voiceSuffixes.Add($"-{variant}");
        if (difficultyId == "nightmare")
        {
            voiceSuffixes.Add("-nightmare");
            voiceSuffixes.Add("-erect");
        }
        else if (difficultyId == "erect")
        {
            voiceSuffixes.Add("-erect");
        }
        voiceSuffixes.Add("");
        voiceSuffixes = voiceSuffixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        // Build candidate voice names: full char name, then base name (strip variant suffix)
        // e.g. "bf-car" -> try "Voices-bf-car", then "Voices-bf"
        var playerVoiceNames = new List<string> { playerChar };
        if (playerChar.Contains('-'))
            playerVoiceNames.Add(playerChar.Split('-')[0]);
        
        var opponentVoiceNames = new List<string> { opponentChar };
        if (opponentChar.Contains('-'))
            opponentVoiceNames.Add(opponentChar.Split('-')[0]);
        
        // Player voices � try per-character first (with fallback names), then combined Voices
        foreach (var pName in playerVoiceNames)
        {
            foreach (var voiceSuffix in voiceSuffixes)
            {
                string voicePath = $"songs/{audioFolder}/Voices-{pName}{voiceSuffix}";
                Audio.PlayVoices(voicePath);
                if (Audio.HasPlayerVoice)
                    break;
            }

            if (Audio.HasPlayerVoice)
                break;
        }
        
        // If per-character player voice not found, try combined Voices file
        if (!Audio.HasPlayerVoice)
        {
            foreach (var voiceSuffix in voiceSuffixes)
            {
                string combinedPath = $"songs/{audioFolder}/Voices{voiceSuffix}";
                Audio.PlayVoices(combinedPath);
                if (Audio.HasPlayerVoice)
                    break;
            }
        }
        
        // Opponent voices � try per-character (with fallback names)
        foreach (var oName in opponentVoiceNames)
        {
            foreach (var voiceSuffix in voiceSuffixes)
            {
                string opponentVoicePath = $"songs/{audioFolder}/Voices-{oName}{voiceSuffix}";
                Audio.PlayOpponentVoices(opponentVoicePath);
                if (Audio.HasOpponentVoice)
                    break;
            }

            if (Audio.HasOpponentVoice)
                break;
        }
        
        // Sync conductor with audio
        _conductor.SetAudioSync(() => Audio.MusicPosition);
        _conductor.Start();
        
        _countdownActive = false;
    }
    
    /// <summary>
    /// Get candidate audio folder names for a song.
    /// Charts use underscores (dad_battle) but audio may use hyphens (dadbattle) or other forms.
    /// </summary>
    private string[] GetAudioFolderCandidates(string songName)
    {
        var candidates = new List<string> { songName };
        
        // Try replacing underscores with hyphens
        if (songName.Contains('_'))
            candidates.Add(songName.Replace('_', '-'));
        
        // Try removing underscores entirely
        if (songName.Contains('_'))
            candidates.Add(songName.Replace("_", ""));
        
        // Try replacing hyphens with underscores
        if (songName.Contains('-'))
            candidates.Add(songName.Replace('-', '_'));
        
        // Try removing hyphens entirely
        if (songName.Contains('-'))
            candidates.Add(songName.Replace("-", ""));
        
        return candidates.Distinct().ToArray();
    }
    
    /// <summary>
    /// Check if an audio file exists at the given content-relative path
    /// </summary>
    private bool AudioFileExists(string path)
    {
        if (Assets.ResolvePath(path + ".ogg") != null) return true;
        string dir = Path.GetDirectoryName(path) ?? "";
        string file = Path.GetFileName(path);
        if (Assets.ResolvePath(Path.Combine(dir, "tracks", file + ".ogg")) != null) return true;
        return false;
    }

    private bool SongSupportsAudioVariation(string songName, string variation)
    {
        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(variation))
            return false;

        string variationId = variation.Trim().ToLowerInvariant();
        if (songName.EndsWith($"_{variationId}", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (string candidate in GetAudioFolderCandidates(songName))
        {
            if (AudioFileExists($"songs/{candidate}/Inst-{variationId}"))
                return true;

            string variantSong = $"{candidate}_{variationId}";
            if (Assets.ResolvePath($"songs/{variantSong}/charts/meta.json") != null
                || Assets.ResolvePath($"songs/{variantSong}/charts/chart.json") != null)
            {
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
    
    public override void Unload()
    {
        // Restore menu navigation mode for face buttons
        Input.GameplayMode = false;
        
        Audio.StopMusic();
        Audio.StopVoices();
        
        // Dispose character composite spritesheets (GPU resources)
        _boyfriend?.Dispose();
        _opponent?.Dispose();
        _girlfriend?.Dispose();
        
        // Dispose note/receptor spritesheets (each owns a Texture2D)
        _notesSheet?.Dispose();
        _receptorsSheet?.Dispose();
        _splashesSheet?.Dispose();
        _holdCoverSheet?.Dispose();
        
        // Dispose animated stage prop sheets
        foreach (var prop in _animatedProps)
            prop.Sprite?.Sheet?.Dispose();
    }
    
    public override void Update(GameTime gameTime)
    {
        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _lastDelta = delta;

        // Phased loading: process one heavy asset group per frame
        if (_playLoading)
        {
            _playLoadDotTimer += delta;
            ProcessPlayLoadPhase();
            return;
        }

        // Animate receptor frames
        _animTimer += delta;
        if (_animTimer >= 1f / 24f)
        {
            _animTimer = 0;
            _animFrame++;
            if (_animFrame > 100000) _animFrame = 0;
        }
        
        // Dialogue update (M3)
        if (_dialogueActive)
        {
            UpdateDialogue(delta);
            return; // Don't process gameplay while dialogue is active
        }

        HandleAbotDebugInput();
        
        // Handle pause (original: controls.PAUSE_P � Escape, Enter, or Start)
        bool pauseKeyPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Escape) || 
            Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.Start);
        bool enterPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        bool justPaused = false;

        if (pauseKeyPressed || (!_paused && enterPressed))
        {
            if (_paused)
            {
                if (_pauseDifficultyMode)
                {
                    // In difficulty sub-menu: ESC goes back to standard menu (original behavior)
                    _pauseDifficultyMode = false;
                    _pauseSelection = 0;
                    BuildPauseMenuItems();
                    Audio.PlaySound("scrollMenu");
                }
                else
                {
                    // In standard menu: ESC/Start resumes (original: controls.PAUSE_P => resume)
                    _paused = false;
                    Input.GameplayMode = true; // Face buttons back to note input
                    if (!_countdownActive)
                    {
                        Audio.ResumeMusic();
                        _conductor.Resume();
                    }
                }
            }
            else if (!_gameOver)
            {
                _paused = true;
                justPaused = true;
                Input.GameplayMode = false; // Face buttons act as menu nav while paused
                _pauseSelection = 0;
                _pauseScrollLerp = 0f;
                _pauseBgAlpha = 0f;
                _pauseMetadataAlpha = 0f;
                _pauseMenuAlpha = 0f;
                _pauseTimer = 0f;
                _pauseDifficultyMode = false;
                BuildPauseMenuItems();
                if (!_countdownActive)
                {
                    Audio.PauseMusic();
                    _conductor.Pause();
                }
                // Original FNF plays pause music (PauseSubState -> FlxG.sound.playMusic)
                Audio.PlaySound("scrollMenu");
            }
        }

        if (_paused)
        {
            _pauseTimer += delta;

            // Fade in background (original: FlxTween alpha 0->0.6, 0.8s quartOut)
            float bgT = Math.Clamp(_pauseTimer / 0.8f, 0f, 1f);
            float quartOut = 1f - MathF.Pow(1f - bgT, 4f);
            _pauseBgAlpha = 0.6f * quartOut;

            // Metadata fade-in (staggered, original: delay 0.1 per item, duration 1.8s quartOut)
            _pauseMetadataAlpha = Math.Clamp(_pauseTimer / 1.8f, 0f, 1f);
            _pauseMenuAlpha = Math.Clamp((_pauseTimer - 0.1f) / 0.4f, 0f, 1f);

            // Smoothly scroll the list so the selected item stays on-screen when there are many items.
            _pauseScrollLerp += (_pauseSelection - _pauseScrollLerp) * Math.Min(1f, delta * 14f);

            // Skip input on the frame we just paused (Start triggers both pause AND confirm)
            if (justPaused) { return; }

            // Handle SHIFT+UP/DOWN for global offset adjustment (original feature)
            var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            bool shiftHeld = kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || 
                             kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
            if (shiftHeld)
            {
                if (Input.UpPressed) { _globalOffset++; HighscoreManager.Data.GlobalOffset = _globalOffset; HighscoreManager.SavePreferences(); }
                if (Input.DownPressed) { _globalOffset--; HighscoreManager.Data.GlobalOffset = _globalOffset; HighscoreManager.SavePreferences(); }
            }
            else
            {
                // Navigate pause menu (matches original PauseSubState)
                if (Input.UpPressed)
                {
                    _pauseSelection = (_pauseSelection - 1 + _pauseItems.Length) % _pauseItems.Length;
                    Audio.PlaySound("scrollMenu");
                }
                if (Input.DownPressed)
                {
                    _pauseSelection = (_pauseSelection + 1) % _pauseItems.Length;
                    Audio.PlaySound("scrollMenu");
                }
                if (Input.ConfirmPressed)
                {
                    ExecutePauseMenuItem(_pauseItems[_pauseSelection]);
                }
            }
            return;
        }
        
        if (_gameOver)
        {
            _gameOverTimer += delta;
            
            // After 1 second, allow input (original: FlxTimer.start(1, canInput = true))
            if (_gameOverTimer >= 1.0f)
            {
                // Start game over music after death animation (~2.5s) and play deathLoop
                if (!_gameOverMusicStarted && _gameOverTimer >= 2.5f)
                {
                    _gameOverMusicStarted = true;
                    _gameOverDeathAnim = "deathLoop";
                    _boyfriend.PlayAnimation("deathLoop");
                    Audio.PlayMusic($"game/characters/{_cachedPlayerSprite}/gameover/theme", true);
                    // Fallback: try funkin.assets shared game-over music
                    if (!Audio.MusicPlaying)
                        Audio.PlayMusic("music/gameplay/gameover/gameOver", true);
                }
                
                if (!_gameOverConfirmed)
                {
                    // Accept = confirm retry (original: confirmDeath)
                    if (Input.ConfirmPressed)
                    {
                        _gameOverConfirmed = true;
                        _gameOverFadeTimer = 0;
                        _gameOverDeathAnim = "deathConfirm";
                        _boyfriend.PlayAnimation("deathConfirm");
                        // Stop game-over loop music first
                        Audio.StopMusic();
                        // Play retry/confirm sound (original: gameOverEnd replaces gameOver loop)
                        if (Audio.HasCachedSound($"game/characters/{_cachedPlayerSprite}/gameover/retry"))
                            Audio.PlaySound($"game/characters/{_cachedPlayerSprite}/gameover/retry", 0.8f);
                        else
                            Audio.PlayMusic("music/gameplay/gameover/gameOverEnd", false);
                    }
                    // Back = exit to menu (original: goBack)
                    if (Input.BackPressed)
                    {
                        Audio.StopMusic();
                        if (_weekId != null)
                            Game.Scenes.ChangeScene(new StoryModeScene());
                        else
                            Game.Scenes.ChangeScene(new FreeplayScene());
                    }
                }
                else
                {
                    // Fade to black then restart (original: camera.fade -> needsReset)
                    _gameOverFadeTimer += delta;
                    if (_gameOverFadeTimer >= 1.5f)
                    {
                        if (_weekSongs != null)
                            Game.Scenes.ChangeScene(new PlayScene(
                                _songName, _currentDifficulty,
                                _weekSongs, _weekSongIndex, _weekId, _weekAccumulatedScore));
                        else
                            Game.Scenes.ChangeScene(new PlayScene(_songName, _currentDifficulty));
                    }
                }
            }
            // Update BF animation during game over (so death animation plays)
            _boyfriend.Update(delta);
            return;
        }
        
        // Handle countdown
        if (_countdownActive)
        {
            UpdateCountdown(delta);
            _boyfriend.Update(delta);
            _opponent.Update(delta);
            _girlfriend?.Update(delta);
            // Process events during countdown so SetCameraBop/FocusCamera at t<=0 still fire
            ProcessEvents();
            return;
        }
        
        // Update conductor timing (synced with audio)
        _conductor.Update(delta);
        
        // Keep character step crochet in sync with BPM changes
        float currentStepCrochet = (float)_conductor.StepCrochet;
        _boyfriend.StepCrochet = currentStepCrochet;
        _opponent.StepCrochet = currentStepCrochet;
        if (_girlfriend != null) _girlfriend.StepCrochet = currentStepCrochet;
        
        // Resync vocals if drifted > 20ms (original FNF: resyncVocals in PlayState)
        _resyncTimer += delta;
        if (_resyncTimer >= 0.5f) // check every 500ms to avoid excessive seeking
        {
            _resyncTimer = 0f;
            Audio.ResyncVoices();
        }
        
        // Camera bop decay (original: lerp cameraBopMultiplier back to 1.0)
        // Use MathF.Exp(ln(rate) * t) which is faster than Math.Pow for frame-rate-independent decay
        float bopDecay = MathF.Exp(-3.074f * delta); // ln(0.95) * 60 ? -3.074
        _cameraBopMultiplier = 1.0f + (_cameraBopMultiplier - 1.0f) * bopDecay;
        _hudZoomAdd *= bopDecay;
        
        // Camera follow (original: camera lerps toward whoever is singing)
        float camLerp = 1f - MathF.Exp(-4f * delta);
        
        // World-space camera follow
        _camWorldX += (_camTargetWorldX - _camWorldX) * camLerp;
        _camWorldY += (_camTargetWorldY - _camWorldY) * camLerp;
        
        // Legacy camera: derive from world camera offset for non-JSON stage parallax
        if (!_hasJsonStage)
        {
            _cameraTargetX = _camTargetWorldX - 1280 / 2f;
            _cameraTargetY = _camTargetWorldY - 720 / 2f;
        }
        _cameraX += (_cameraTargetX - _cameraX) * camLerp;
        _cameraY += (_cameraTargetY - _cameraY) * camLerp;
        
        // Icon bounce decay
        float iconDecay = MathF.Exp(-10.397f * delta); // ln(0.5) * 15 ? -10.397
        _iconBounceScale = 1.0f + (_iconBounceScale - 1.0f) * iconDecay;
        
        // NPS update (every second, swap counter)
        _npsTimer += delta;
        if (_npsTimer >= 1f)
        {
            _npsTimer -= 1f;
            _nps = _npsCounter;
            _npsCounter = 0;
        }
        
        // Health lerp (original: healthLerp = FlxMath.lerp(health, healthLerp, ...))
        float lerpDecay = MathF.Exp(-6.321f * delta); // ln(0.9) * 60 ? -6.321
        _healthLerp = _health + (_healthLerp - _health) * lerpDecay;
        
        // Update note splash timers
        for (int i = 0; i < 4; i++)
        {
            if (_noteSplashTimer[i] > 0)
                _noteSplashTimer[i] -= delta;
        }
        
        // Update receptor confirm flash timers (decay so receptors return to static after note hit)
        for (int i = 0; i < 4; i++)
        {
            if (_playerConfirmTimer[i] > 0)
                _playerConfirmTimer[i] -= delta;
            if (_opponentConfirmTimer[i] > 0)
                _opponentConfirmTimer[i] -= delta;
        }
        
        // Update rating display timer and physics
        if (_ratingTimer > 0)
        {
            _ratingTimer -= delta;
            _ratingVelY += 550f * delta; // acceleration.y = 550 (original)
            _ratingY += _ratingVelY * delta;
            // Fade out in last 0.2s of display (original: FlxTween alpha->0 with startDelay)
            if (_ratingTimer < 0.2f)
                _ratingAlpha = _ratingTimer / 0.2f;
            
            // Combo digit physics (same acceleration)
            _comboTimer += delta;
            _comboVelY += 550f * delta;
            _comboY += _comboVelY * delta;
            if (_ratingTimer < 0.2f)
                _comboAlpha = _ratingTimer / 0.2f;
        }
        
        // Update countdown display fade
        if (_countdownDisplayTimer > 0)
            _countdownDisplayTimer -= delta;
        
        // Process player input for notes
        ProcessNoteInput();
        
        // Update note field (check for missed notes)
        var missedNotes = _noteField.Update(delta);
        for (int mi = 0; mi < missedNotes.Count; mi++)
        {
            OnNoteMiss(missedNotes[mi].Lane);
        }
        _noteField.ReturnMissedNotes();
        
        // Process opponent notes (auto-hit for animations)
        var opponentNotes = _noteField.GetOpponentNotes(_conductor.SongPosition);
        for (int oi = 0; oi < opponentNotes.Count; oi++)
        {
            var oppNote = opponentNotes[oi];
            _opponent.Sing(oppNote.Lane);
            // Opponent receptor confirm flash (original: opponentStrumline.playConfirm)
            _opponentConfirmTimer[oppNote.Lane] = RECEPTOR_CONFIRM_DURATION;
            _noteField.RemoveNote(oppNote);
        }
        
        // Keep opponent singing during sustain notes (original: opponent holds sing anim
        // for the sustain duration, doesn't snap back to idle mid-hold)
        for (int i = 0; i < 4; i++)
        {
            var sustainNote = _noteField.GetActiveOpponentSustain(i, _conductor.SongPosition);
            if (sustainNote != null)
            {
                _opponent.Sing(i);
                _opponentConfirmTimer[i] = RECEPTOR_CONFIRM_DURATION;
            }
        }
        
        // Update characters
        _boyfriend.Update(delta);
        _opponent.Update(delta);
        _girlfriend?.Update(delta);
        
        // Update animated stage props (P1)
        for (int pi = 0; pi < _animatedProps.Count; pi++)
        {
            _animatedProps[pi].Sprite?.Update(delta);
        }

        UpdateAbotVisualizer(delta);

        UpdateAbotFallbackAnchors();
        
        // FastCar movement (original limoRide.hxc: velocity-based scrolling)
        if (_fastCarStaticIndex >= 0 && _fastCarVelocityX > 0)
        {
            var car = _staticProps[_fastCarStaticIndex];
            car.X += _fastCarVelocityX * delta;
            _staticProps[_fastCarStaticIndex] = car;
            
            // Reset after car passes off-screen right (world x > 4000)
            if (car.X > 4000)
            {
                car.X = -12600;
                car.Y = _random.Next(140, 251);
                _staticProps[_fastCarStaticIndex] = car;
                _fastCarVelocityX = 0;
                _fastCarCanDrive = true;
            }
        }
        
        // Spooky lightning flash fade (original: flashes white then fades out over ~0.5s)
        if (_lightningFlashAlpha > 0)
        {
            _lightningFlashAlpha -= delta * 3f; // fade over ~0.33s
            if (_lightningFlashAlpha < 0) _lightningFlashAlpha = 0;
        }

        UpdatePhillyBlazinLightning(delta);
        
        // Philly train movement (original: trainMoving logic from PhillyTrain.hx)
        if (_trainStaticIndex >= 0 && _trainMoving)
        {
            _trainFrameTiming++;
            
            // Train starts off-screen right (2000), accelerates left
            if (_trainFrameTiming > 12) // delay before movement starts
            {
                if (!_trainStartedMoving)
                {
                    _trainStartedMoving = true;
                    _girlfriend?.PlayAnimation("hairBlow", force: true);
                }
                
                _trainX -= 400f * delta * 60f; // ~400px/frame at 60fps (original speed)
                
                var train = _staticProps[_trainStaticIndex];
                train.X = _trainX;
                _staticProps[_trainStaticIndex] = train;
                
                // Train fully passed when it reaches far left
                if (_trainX < -2000)
                {
                    _trainFinishing = true;
                }
                
                if (_trainFinishing && _trainX < -4000)
                {
                    // Reset train
                    _trainMoving = false;
                    _trainX = 2000;
                    var t = _staticProps[_trainStaticIndex];
                    t.X = _trainX;
                    _staticProps[_trainStaticIndex] = t;
                    _trainCooldown = 8f; // ~8 seconds before next eligible train
                    _girlfriend?.PlayAnimation("hairFall", force: true);
                }
            }
        }
        
        // Process chart events (FocusCamera, ZoomCamera, SetCameraBop, PlayAnimation, etc.)
        ProcessEvents();
        
        // Check game over (original: health <= Constants.HEALTH_MIN && !isPracticeMode && !isPlayerDying)
        if (_health <= HEALTH_MIN && !_gameOver && !_practiceMode)
        {
            _gameOver = true;
            _gameOverTimer = 0;
            _gameOverMusicStarted = false;
            _gameOverConfirmed = false;
            _gameOverFadeTimer = 0;
            _gameOverDeathAnim = "firstDeath";
            _deathCounter++; // Increment blue balls counter
            Audio.StopMusic();
            Audio.StopVoices();
            // Load death spritesheet (original: separate character with death animations)
            _boyfriend.IsDead = true;
            _boyfriend.LoadDeathSprites(Game);
            // Play blue ball / death sound (original: playBlueBalledSFX -> fnf_loss_sfx)
            Audio.PlaySound($"game/characters/{_cachedPlayerSprite}/gameover/on_death", 0.8f);
            // Fallback: try funkin.assets path
            if (!Audio.HasCachedSound($"game/characters/{_cachedPlayerSprite}/gameover/on_death"))
                Audio.PlaySound("gameplay/gameover/fnf_loss_sfx", 0.8f);
            // Play firstDeath animation on BF
            _boyfriend.PlayAnimation("firstDeath");
            // Music starts later in Update after death animation finishes
        }
        
        // In practice mode, prevent health from staying at 0 (clamp to small value)
        if (_practiceMode && _health <= HEALTH_MIN)
        {
            _health = 0.01f;
        }
        
        // Check song end � use a generous buffer after the last note + check music stopped
        // Only transition to results when the song actually finished (not during game over)
        double lastNoteTime = _chart.Notes.Count > 0 ? _chart.Notes[^1].Time : 0;
        bool pastLastNote = _conductor.SongPosition > lastNoteTime + 2.0;
        bool musicDone = !Audio.MusicPlaying && _conductor.SongPosition > 3.0;
        // Fallback: if we're 5 seconds past the last note, force end even if music is still "playing"
        bool forceEnd = _conductor.SongPosition > lastNoteTime + 5.0 && _conductor.SongPosition > 3.0;
        if ((pastLastNote && musicDone || forceEnd) && !_gameOver)
        {
            // Stop voices immediately so they don't bleed into ResultsScene
            Audio.StopVoices();
            
            // Save score to disk (completion = totalNotesHit / totalNotes, matching Scoring.hx)
            float clearPct = _totalNotes == 0 ? 0f
                : Math.Clamp(_totalNotesHit / (float)_totalNotes, 0f, 1f) * 100f;
            string rank = _totalNotes == 0 ? "SHIT"
                : (_tallySick == _totalNotes) ? "PERFECT_GOLD"
                : (clearPct >= 100f) ? "PERFECT"
                : (clearPct >= 90f) ? "EXCELLENT"
                : (clearPct >= 80f) ? "GREAT"
                : (clearPct >= 60f) ? "GOOD" : "SHIT";
            bool isNewHighscore = HighscoreManager.SaveScore(
                _songName, _currentDifficulty, _score, _maxCombo, clearPct, rank);

            // Story mode: chain to next song if more remain
            if (_weekSongs != null && _weekSongIndex + 1 < _weekSongs.Count)
            {
                int nextIdx = _weekSongIndex + 1;
                Game.Scenes.ChangeScene(new PlayScene(
                    _weekSongs[nextIdx], _currentDifficulty,
                    _weekSongs, nextIdx, _weekId, _weekAccumulatedScore + _score));
                return;
            }
            
            // Story mode: all songs complete � save week score
            if (_weekId != null)
            {
                int totalScore = _weekAccumulatedScore + _score;
                HighscoreManager.SaveWeekScore(_weekId, _currentDifficulty, totalScore);
            }

            Game.Scenes.ChangeScene(new ResultsScene(_score, _maxCombo, _misses, _songName,
                _currentDifficulty, isNewHighscore,
                _tallySick, _tallyGood, _tallyBad, _tallyShit, _tallyMissed, _totalNotesHit, _totalNotes,
                isStoryMode: _weekId != null));
        }
    }
    
    private void UpdateCountdown(float delta)
    {
        float beatDuration = (float)_conductor.Crochet;
        _countdownTimer += delta;
        
        if (_countdownTimer >= beatDuration && _countdownStep <= 4)
        {
            _countdownTimer -= beatDuration;
            
            switch (_countdownStep)
            {
                case 0:
                    Audio.PlaySound("game/skins/default/countdown/3", 0.6f);
                    break;
                case 1:
                    _countdownText = "READY";
                    _countdownDisplayTimer = beatDuration;
                    Audio.PlaySound("game/skins/default/countdown/2", 0.6f);
                    break;
                case 2:
                    _countdownText = "SET";
                    _countdownDisplayTimer = beatDuration;
                    Audio.PlaySound("game/skins/default/countdown/1", 0.6f);
                    break;
                case 3:
                    _countdownText = "GO!";
                    _countdownDisplayTimer = beatDuration;
                    Audio.PlaySound("game/skins/default/countdown/go", 0.6f);
                    break;
                case 4:
                    StartSong();
                    break;
            }
            _countdownStep++;
        }
    }
    
    /// <summary>
    /// Robustly extract the `char` index from a FocusCamera event value.
    /// Handles bare numbers (long/int/double/JValue/string) and JObjects with a "char" key.
    /// Returns 0 (opponent) if the value cannot be interpreted � but logs the unparseable shape so
    /// chart authoring issues don't silently aim the camera at the wrong character.
    /// </summary>
    private static int ParseFocusChar(object value)
    {
        if (value == null) return 0;
        switch (value)
        {
            case int i: return i;
            case long l: return (int)l;
            case short s: return s;
            case byte b: return b;
            case double d: return (int)d;
            case float f: return (int)f;
            case bool boolean: return boolean ? 1 : 0;
            case string str:
                return int.TryParse(str, out var parsed) ? parsed : 0;
            case Newtonsoft.Json.Linq.JValue jv:
                try { return jv.ToObject<int>(); }
                catch { return 0; }
            case Newtonsoft.Json.Linq.JObject jo:
                var token = jo["char"] ?? jo["c"];
                if (token != null)
                {
                    try { return token.ToObject<int>(); }
                    catch { /* fallthrough */ }
                }
                return 0;
            default:
                Console.WriteLine($"FocusCamera: unrecognized value type {value.GetType().FullName}, defaulting to opponent");
                return 0;
        }
    }

    /// <summary>
    /// Process chart events that have reached their trigger time.
    /// Supports: FocusCamera, ZoomCamera, SetCameraBop, PlayAnimation
    /// </summary>
    private void ProcessEvents()
    {
        if (_chart.Events == null || _chart.Events.Count == 0) return;
        double songPos = _conductor.SongPosition;
        
        while (_nextEventIndex < _chart.Events.Count)
        {
            var ev = _chart.Events[_nextEventIndex];
            if (ev.Time > songPos) break;
            if (ev.Fired) { _nextEventIndex++; continue; }
            
            ev.Fired = true;
            _nextEventIndex++;
            
            switch (ev.Name)
            {
                case "FocusCamera":
                    // Value: { "char": 0=Boyfriend, 1=Dad, 2=Girlfriend } or just a bare int.
                    // Convention matches official FNF V2 FocusCameraSongEvent.hx.
                    // Be permissive about how Newtonsoft.Json surfaces numbers (long/int/double/JValue/string)
                    // because chart files in the wild use all of these forms.
                    int focusChar = ParseFocusChar(ev.Value);
                    
                    if (focusChar == 0)
                    {
                        // Stage JSON cameraOffsets already include directional constants;
                        // only add hardcoded -100/-100 for legacy (non-JSON) stages.
                        float pcx = _boyfriend.CameraOffsets != null && _boyfriend.CameraOffsets.Length >= 2
                            ? _boyfriend.CameraOffsets[0] : 0f;
                        float pcy = _boyfriend.CameraOffsets != null && _boyfriend.CameraOffsets.Length >= 2
                            ? _boyfriend.CameraOffsets[1] : 0f;
                        var bfMid = _boyfriend.GetMidpoint();
                        if (_hasJsonStage)
                        {
                            _camTargetWorldX = bfMid.X + pcx;
                            _camTargetWorldY = bfMid.Y + pcy;
                        }
                        else
                        {
                            _camTargetWorldX = bfMid.X - 100 + pcx;
                            _camTargetWorldY = bfMid.Y - 100 + pcy;
                        }
                    }
                    else if (focusChar == 2 && _girlfriend != null)
                    {
                        // GF: no hardcoded constants in either format
                        float gcx = _girlfriend.CameraOffsets != null && _girlfriend.CameraOffsets.Length >= 2
                            ? _girlfriend.CameraOffsets[0] : 0f;
                        float gcy = _girlfriend.CameraOffsets != null && _girlfriend.CameraOffsets.Length >= 2
                            ? _girlfriend.CameraOffsets[1] : 0f;
                        var gfMid = _girlfriend.GetMidpoint();
                        _camTargetWorldX = gfMid.X + gcx;
                        _camTargetWorldY = gfMid.Y + gcy;
                    }
                    else
                    {
                        // Stage JSON cameraOffsets already include directional constants;
                        // only add hardcoded +150/-100 for legacy (non-JSON) stages.
                        float ocx = _opponent.CameraOffsets != null && _opponent.CameraOffsets.Length >= 2
                            ? _opponent.CameraOffsets[0] : 0f;
                        float ocy = _opponent.CameraOffsets != null && _opponent.CameraOffsets.Length >= 2
                            ? _opponent.CameraOffsets[1] : 0f;
                        var dadMid = _opponent.GetMidpoint();
                        if (_hasJsonStage)
                        {
                            _camTargetWorldX = dadMid.X + ocx;
                            _camTargetWorldY = dadMid.Y + ocy;
                        }
                        else
                        {
                            _camTargetWorldX = dadMid.X + 150 + ocx;
                            _camTargetWorldY = dadMid.Y - 100 + ocy;
                        }
                    }
                    break;
                    
                case "ZoomCamera":
                    // Trigger a camera bop
                    _cameraBopMultiplier = CAMERA_BOP_INTENSITY;
                    _hudZoomAdd = HUD_ZOOM_INTENSITY;
                    break;
                    
                case "SetCameraBop":
                    // Value: { "intensity": float, "rate": int }
                    // We just trigger a bop effect
                    _cameraBopMultiplier = CAMERA_BOP_INTENSITY;
                    break;
                    
                case "PlayAnimation":
                    // Value: { "target": "boyfriend"/"dad"/"gf", "anim": "hey" }
                    if (ev.Value is Newtonsoft.Json.Linq.JObject animObj)
                    {
                        string target = animObj.Value<string>("target") ?? "";
                        string anim = animObj.Value<string>("anim") ?? "idle";
                        if (target == "boyfriend" || target == "bf")
                            _boyfriend.PlayAnimation(anim);
                        else if (target == "dad" || target == "opponent")
                            _opponent.PlayAnimation(anim);
                        else if (target == "gf" || target == "girlfriend")
                            _girlfriend?.PlayAnimation(anim);
                    }
                    break;
            }
        }
    }
    
    private void ProcessNoteInput()
    {
        // Apply global offset: positive offset = notes hit later, negative = earlier
        double adjustedPos = _conductor.SongPosition + (_globalOffset / 1000.0);
        bool ghostTapping = HighscoreManager.Data.GhostTapping;
        
        // Botplay: auto-hit all player notes at exact timing
        if (_botplay)
        {
            for (int lane = 0; lane < 4; lane++)
            {
                var note = _noteField.GetBotplayNote(lane, adjustedPos);
                if (note != null)
                {
                    OnNoteHit(lane, "SICK!!", 500);
                    _noteField.RemoveNote(note);
                    _boyfriend.Sing(lane);
                    if (note.SustainLength > 0)
                    {
                        _holdingNote[lane] = true;
                        _holdNoteEndTime[lane] = note.Time + note.SustainLength;
                        _holdNoteRef[lane] = note;
                    }
                }
                // Auto-complete hold notes
                if (_holdingNote[lane] && adjustedPos >= _holdNoteEndTime[lane])
                {
                    _holdingNote[lane] = false;
                    _holdCoverTimer[lane] = 0; // Reset glow cover on hold completion
                    if (_holdNoteRef[lane] != null)
                    {
                        _noteField.RemoveHoldNote(_holdNoteRef[lane]);
                        _holdNoteRef[lane] = null;
                    }
                }
                else if (_holdingNote[lane])
                {
                    _holdScoreAccum += 250f * _lastDelta;
                    int whole = (int)_holdScoreAccum;
                    if (whole > 0) { _score += whole; _holdScoreAccum -= whole; }
                    _health = Math.Clamp(_health + (6f / 100f * HEALTH_MAX * _lastDelta), HEALTH_MIN, HEALTH_MAX);
                }
            }
            return;
        }
        
        for (int lane = 0; lane < 4; lane++)
        {
            if (Input.NotePressed[lane])
            {
                var note = _noteField.GetHittableNote(lane, adjustedPos);
                
                if (note != null)
                {
                    float diff = Math.Abs((float)(note.Time - adjustedPos));
                    string rating = GetRating(diff);
                    int scoreValue = GetScoreValue(diff);
                    
                    OnNoteHit(lane, rating, scoreValue);
                    _noteField.RemoveNote(note);
                    _boyfriend.Sing(lane);
                    
                    // Start hold tracking if sustain note
                    if (note.SustainLength > 0)
                    {
                        _holdingNote[lane] = true;
                        _holdNoteEndTime[lane] = note.Time + note.SustainLength;
                        _holdNoteRef[lane] = note; // Keep reference to remove when done
                    }
                }
                else if (!ghostTapping)
                {
                    // Ghost miss: pressed key with no note nearby
                    // Original: HEALTH_GHOST_MISS_PENALTY = -4.0% of HEALTH_MAX, score -10
                    // Skipped when ghost tapping is enabled
                    OnGhostMiss(lane);
                }
            }
            
            // Hold note release check
            if (_holdingNote[lane] && Input.NoteReleased[lane])
            {
                // Check if released significantly early (original: HOLD_DROP_PENALTY_THRESHOLD_MS = 160)
                double remainingMs = (_holdNoteEndTime[lane] - adjustedPos) * 1000.0;
                if (remainingMs > 160.0)
                {
                    // Penalize for dropping hold (original: SCORE_HOLD_DROP_PENALTY_PER_SECOND = -125)
                    float droppedSec = (float)(remainingMs / 1000.0);
                    _score += (int)(-125f * droppedSec);
                    _combo = 0;
                }
                _holdingNote[lane] = false;
                if (_holdNoteRef[lane] != null)
                {
                    _noteField.RemoveHoldNote(_holdNoteRef[lane]);
                    _holdNoteRef[lane] = null;
                }
            }
            
            // Hold note bonus while held (original: +250 score/sec, +6% health/sec)
            if (_holdingNote[lane] && Input.NoteHeld[lane])
            {
                if (adjustedPos >= _holdNoteEndTime[lane])
                {
                    _holdingNote[lane] = false;
                    _holdCoverTimer[lane] = 0; // Reset glow cover on hold completion
                    // Remove the hold note visual when complete
                    if (_holdNoteRef[lane] != null)
                    {
                        _noteField.RemoveHoldNote(_holdNoteRef[lane]);
                        _holdNoteRef[lane] = null;
                    }
                }
                else
                {
                    _holdScoreAccum += 250f * _lastDelta; // SCORE_HOLD_BONUS_PER_SECOND
                    int whole = (int)_holdScoreAccum;
                    if (whole > 0) { _score += whole; _holdScoreAccum -= whole; }
                    _health = Math.Clamp(_health + (6f / 100f * HEALTH_MAX * _lastDelta), HEALTH_MIN, HEALTH_MAX);
                }
            }
        }
    }
    
    private string GetRating(float timeDiff)
    {
        // PBOT1 judgement thresholds (original FNF)
        if (timeDiff <= NoteField.KILLER_WINDOW) return "SICK!!"; // killer threshold gives sick display
        if (timeDiff <= NoteField.SICK_WINDOW) return "SICK!!";
        if (timeDiff <= NoteField.GOOD_WINDOW) return "GOOD!";
        if (timeDiff <= NoteField.BAD_WINDOW) return "BAD";
        return "SHIT";
    }
    
    /// <summary>
    /// PBOT1 sigmoid scoring from original FNF.
    /// Max score = 500, offset = 54.99ms, slope = 0.080, min = 9
    /// Perfect threshold (&lt;5ms) always gives max score.
    /// </summary>
    private int GetScoreValue(float absTiming)
    {
        const float MAX_SCORE = 500f;
        const float SCORING_OFFSET = 54.99f;
        const float SCORING_SLOPE = 0.080f;
        const float MIN_SCORE = 9.0f;
        const float PERFECT_THRESHOLD = 0.005f; // 5ms
        const float MISS_THRESHOLD = 0.160f;
        const int MISS_SCORE = -100;
        
        if (absTiming > MISS_THRESHOLD) return MISS_SCORE;
        if (absTiming < PERFECT_THRESHOLD) return (int)MAX_SCORE;
        
        float absMs = absTiming * 1000f;
        float factor = 1.0f - (1.0f / (1.0f + (float)Math.Exp(-SCORING_SLOPE * (absMs - SCORING_OFFSET))));
        return (int)(MAX_SCORE * factor + MIN_SCORE);
    }
    
    private void OnNoteHit(int lane, string rating, int scoreValue)
    {
        _score += scoreValue;
        _lastRating = rating;
        _ratingTimer = 0.5f;
        _totalNotesHit++;
        _totalNotes++;
        _npsCounter++;
        
        // Combo break on BAD and SHIT (original Constants: JUDGEMENT_BAD_COMBO_BREAK = true, JUDGEMENT_SHIT_COMBO_BREAK = true)
        bool isComboBreak = rating == "BAD" || rating == "SHIT";
        if (isComboBreak)
        {
            _combo = 0;
        }
        else
        {
            _combo++;
            _maxCombo = Math.Max(_maxCombo, _combo);
        }
        
        // Track tallies (matches original FNF Highscore.tallies)
        switch (rating)
        {
            case "SICK!!":
                _tallySick++;
                break;
            case "GOOD!":
                _tallyGood++;
                break;
            case "BAD":
                _tallyBad++;
                break;
            case "SHIT":
                _tallyShit++;
                break;
        }
        
        // Health bonuses (original FNF Constants, percentage of HEALTH_MAX=2.0)
        float healthChange = rating switch
        {
            "SICK!!" => 1.5f / 100f * HEALTH_MAX,   // +1.5%
            "GOOD!" => 0.75f / 100f * HEALTH_MAX,   // +0.75%
            "BAD" => 0f,                             // +0.0%
            "SHIT" => -1.0f / 100f * HEALTH_MAX,    // -1.0%
            _ => 0f
        };
        _health = Math.Clamp(_health + healthChange, HEALTH_MIN, HEALTH_MAX);
        
        // Restore player vocals on hit (original: vocals.playerVolume = playerVocalsVolume)
        Audio.SetPlayerVoiceVolume(1.0f);
        
        // Note splash on sick hit (original: playerStrumline.playNoteSplash)
        if (rating == "SICK!!")
        {
            _noteSplashTimer[lane] = 0.3f;
            _noteSplashLane[lane] = lane;
        }
        
        // Receptor confirm flash (original: playerStrumline.playConfirm on note hit)
        _playerConfirmTimer[lane] = RECEPTOR_CONFIRM_DURATION;
        
        // Rating popup physics (original PopUpStuff: y = camera.height * 0.45 - 60)
        _ratingY = FNFGame.SCREEN_HEIGHT * 0.45f - 60;
        _ratingVelY = -_random.Next(140, 176); // velocity.y -= FlxG.random.int(140, 175)
        _ratingAlpha = 1f;
        _lastComboDisplay = _combo;
        
        // Combo digit physics
        _comboY = FNFGame.SCREEN_HEIGHT * 0.44f;
        _comboVelY = -_random.Next(140, 171);
        _comboAlpha = 1f;
        _comboTimer = 0;
    }
    
    private void OnNoteMiss(int lane = -1)
    {
        _combo = 0;
        _misses++;
        _tallyMissed++;
        _totalNotes++;
        _score += -100; // PBOT1_MISS_SCORE
        // Health penalty: -4.0% of HEALTH_MAX (original FNF HEALTH_MISS_PENALTY)
        _health = Math.Clamp(_health - (4.0f / 100f * HEALTH_MAX), HEALTH_MIN, HEALTH_MAX);
        _lastRating = "MISS";
        _ratingTimer = 0.3f;
        _ratingY = FNFGame.SCREEN_HEIGHT * 0.45f - 60;
        _ratingVelY = -_random.Next(140, 176);
        _ratingAlpha = 1f;
        _lastComboDisplay = 0;
        
        // Mute player vocals on miss (original: vocals.playerVolume = 0)
        Audio.SetPlayerVoiceVolume(0f);
        
        // Play miss animation on BF
        if (lane >= 0)
            _boyfriend.Miss(lane);
        
        // Play random miss sound (FNF has 3 miss sounds)
        int missNum = _random.Next(1, 4);
        Audio.PlaySound($"missnote{missNum}", 0.3f);
    }
    
    /// <summary>
    /// Ghost miss: player pressed a key when no note was nearby.
    /// Original: ghostNoteMiss() with HEALTH_GHOST_MISS_PENALTY and -10 score.
    /// </summary>
    private void OnGhostMiss(int lane)
    {
        // Original: HEALTH_GHOST_MISS_PENALTY = -4.0% of HEALTH_MAX, scoreChange = -10
        _health = Math.Clamp(_health - (4.0f / 100f * HEALTH_MAX), HEALTH_MIN, HEALTH_MAX);
        _score -= 10;
        
        // Play miss sound at lower volume (original: FlxG.random.float(0.1, 0.2))
        int missNum = _random.Next(1, 4);
        Audio.PlaySound($"missnote{missNum}", 0.15f);
        
        // Play miss animation
        _boyfriend.Miss(lane);
    }
    
    public override void Draw(SpriteBatch spriteBatch)
    {
        // Loading screen
        if (_playLoading)
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(10, 10, 20));
            int dots = ((int)(_playLoadDotTimer * 3f)) % 4;
            string dotStr = new string('.', dots);
            string statusText = _playLoadStatus + dotStr;
            var font = Assets.GetFont(24);
            if (font != null)
            {
                float tw = font.MeasureString("LOADING").X;
                font.DrawText(spriteBatch, "LOADING", new Vector2((FNFGame.SCREEN_WIDTH - tw) / 2, FNFGame.SCREEN_HEIGHT / 2 - 40), Color.White);
                float sw = font.MeasureString(statusText).X;
                font.DrawText(spriteBatch, statusText, new Vector2((FNFGame.SCREEN_WIDTH - sw) / 2, FNFGame.SCREEN_HEIGHT / 2 + 10), Color.Gray);
            }
            float progress = Math.Clamp((float)_playLoadPhase / 6f, 0f, 1f);
            int barW = 400, barH = 8;
            int barX = (FNFGame.SCREEN_WIDTH - barW) / 2;
            int barY = FNFGame.SCREEN_HEIGHT / 2 + 60;
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX, barY, barW, barH), new Color(40, 40, 60));
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX, barY, (int)(barW * progress), barH), new Color(100, 200, 255));
            spriteBatch.End();
            return;
        }

        // During game over, only draw the game over overlay (original: separate substate)
        if (_gameOver)
        {
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
            DrawGameOverOverlay(spriteBatch);
            spriteBatch.End();
            return;
        }
        
        // === WORLD PASS: stage + characters with camera zoom ===
        float zoom = _defaultCamZoom * _cameraBopMultiplier;
        // HaxeFlixel camera: zoom scales from origin (0,0), NOT from screen center.
        // WorldToScreen computes pre-zoom positions where (0,0) = top-left of camera view.
        // The zoom matrix multiplies these positions by the zoom factor from origin.
        _worldZoomMatrix = Matrix.CreateScale(zoom, zoom, 1f);
        // Clip world rendering to screen bounds (original FNF: FlxCamera viewport clips)
        var prevScissor = Game.GraphicsDevice.ScissorRectangle;
        Game.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        _worldClipRaster = new RasterizerState { ScissorTestEnable = true };
        _worldSampler = SamplerState.LinearClamp;
        _worldBlendState = BlendState.AlphaBlend;
        spriteBatch.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
        
        // Draw stage background (offset by camera)
        DrawStage(spriteBatch);
        
        // Z-ordered rendering: interleave props and characters by their zIndex
        // Characters: GF=_gfZIndex(100), Dad=_dadZIndex(200), BF=_bfZIndex(300)
        // Props can be at any zIndex (e.g., limoDrive=150 between GF and Dad)
        if (_hasJsonStage)
        {
            // Collect all z-index thresholds where we need to draw characters
            // Draw order: props < gfZ, GF, props < dadZ, Dad, props < bfZ, BF, remaining props
            int[] charZList = { _gfZIndex, _dadZIndex, _bfZIndex };
            
            int lastZ = int.MinValue;
            for (int ci = 0; ci < 3; ci++)
            {
                int charZ = charZList[ci];
                
                // Draw all props with zIndex >= lastZ and < charZ
                DrawPropsInRange(spriteBatch, lastZ, charZ);
                
                // Draw the character
                switch (ci)
                {
                    case 0: // GF
                        if (_girlfriend != null)
                        {
                            var gfScreen = WorldToScreen(_girlfriend.Position.X, _girlfriend.Position.Y);
                            _girlfriend.Draw(spriteBatch, Assets, gfScreen.X, gfScreen.Y);
                        }
                        break;
                    case 1: // Dad
                    {
                        var oppScreen = WorldToScreen(_opponent.Position.X, _opponent.Position.Y);
                        _opponent.Draw(spriteBatch, Assets, oppScreen.X, oppScreen.Y);
                        break;
                    }
                    case 2: // BF
                    {
                        var bfScreen = WorldToScreen(_boyfriend.Position.X, _boyfriend.Position.Y);
                        _boyfriend.Draw(spriteBatch, Assets, bfScreen.X, bfScreen.Y);
                        break;
                    }
                }
                
                lastZ = charZ;
            }
            
            // Draw remaining props with zIndex >= BF's zIndex
            DrawPropsInRange(spriteBatch, lastZ, int.MaxValue);
        }
        else
        {
            // Legacy (non-JSON) stages: simple ordering
            if (_girlfriend != null)
            {
                var gfScreen = WorldToScreen(_girlfriend.Position.X, _girlfriend.Position.Y);
                _girlfriend.Draw(spriteBatch, Assets, gfScreen.X, gfScreen.Y);
            }
            {
                var oppScreen = WorldToScreen(_opponent.Position.X, _opponent.Position.Y);
                _opponent.Draw(spriteBatch, Assets, oppScreen.X, oppScreen.Y);
            }
            {
                var bfScreen = WorldToScreen(_boyfriend.Position.X, _boyfriend.Position.Y);
                _boyfriend.Draw(spriteBatch, Assets, bfScreen.X, bfScreen.Y);
            }
        }
        
        // Draw stage curtains on top of characters (legacy stages only)
        if (!_hasJsonStage)
            DrawStageCurtains(spriteBatch);
        
        // Spooky lightning flash overlay (white screen flash that fades out)
        if (_lightningFlashAlpha > 0)
        {
            float flashZoom = _defaultCamZoom * _cameraBopMultiplier;
            int fillW = (int)(FNFGame.SCREEN_WIDTH / flashZoom) + 2;
            int fillH = (int)(FNFGame.SCREEN_HEIGHT / flashZoom) + 2;
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, fillW, fillH),
                Color.White * (_lightningFlashAlpha * 0.6f));
        }

        spriteBatch.End();
        // Restore previous scissor rectangle after world pass
        Game.GraphicsDevice.ScissorRectangle = prevScissor;
        
        // === HUD PASS: note field, health bar, score (with subtle HUD zoom) ===
        float hudZoom = 1f + _hudZoomAdd;
        var hudMatrix = Matrix.CreateTranslation(-1280 / 2f, -720 / 2f, 0)
                      * Matrix.CreateScale(hudZoom, hudZoom, 1f)
                      * Matrix.CreateTranslation(1280 / 2f, 720 / 2f, 0);
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: hudMatrix);

        // Draw note field / strumline
        DrawNoteField(spriteBatch);
        
        // Draw HUD
        DrawHUD(spriteBatch);
        
        // Draw note splashes on sick hits
        DrawNoteSplashes(spriteBatch);
        
        // Draw hold note cover glow effect (M11)
        DrawHoldNoteCovers(spriteBatch);
        
        // Draw rating popup (physics now in Update)
        if (_ratingTimer > 0)
        {
            DrawRating(spriteBatch);
        }
        
        spriteBatch.End();
        
        // === OVERLAY PASS: countdown, pause, dialogue (no zoom) ===
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        
        // Draw countdown
        if (_countdownActive || _countdownDisplayTimer > 0)
        {
            DrawCountdown(spriteBatch);
        }
        
        // Draw pause overlay on top of everything
        if (_paused)
        {
            DrawPauseOverlay(spriteBatch);
        }
        
        // Dialogue overlay (M3)
        if (_dialogueActive)
        {
            DrawDialogue(spriteBatch);
        }
        
        spriteBatch.End();
    }
    
    private void DrawStage(SpriteBatch spriteBatch)
    {
        // JSON-loaded stage: all props (static + animated) are drawn by DrawPropsInRange
        // in z-order interleaved with characters. Nothing to do here.
        if (_hasJsonStage)
        {
            return;
        }
        
        // Legacy stage drawing (no JSON)
        if (_stageBackdrop != null && _stageBackdrop != Assets.Pixel)
        {
            // Scale to fill entire screen (cover, not contain)
            float scaleX = (float)FNFGame.SCREEN_WIDTH / _stageBackdrop.Width;
            float scaleY = (float)FNFGame.SCREEN_HEIGHT / _stageBackdrop.Height;
            float scale = Math.Max(scaleX, scaleY) * 1.05f; // Slight oversize to cover
            int width = (int)(_stageBackdrop.Width * scale);
            int height = (int)(_stageBackdrop.Height * scale);
            // Background parallax: reduced scroll factor (original: scrollFactor ~0.5 for BG)
            int x = (FNFGame.SCREEN_WIDTH - width) / 2 + (int)(_cameraX * 0.5f);
            int y = (FNFGame.SCREEN_HEIGHT - height) / 2 + (int)(_cameraY * 0.5f);
            spriteBatch.Draw(_stageBackdrop, new Rectangle(x, y, width, height), Color.White);
        }
        else
        {
            spriteBatch.Draw(Assets.Pixel, 
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), 
                new Color(40, 30, 50));
        }
        
        if (_stageFront != null && _stageFront != Assets.Pixel)
        {
            float scale = (float)FNFGame.SCREEN_WIDTH / _stageFront.Width * 1.1f;
            int width = (int)(_stageFront.Width * scale);
            int height = (int)(_stageFront.Height * scale);
            // Stage front parallax: closer to camera than backdrop
            int x = (FNFGame.SCREEN_WIDTH - width) / 2 + (int)(_cameraX * 0.8f);
            spriteBatch.Draw(_stageFront, 
                new Rectangle(x, FNFGame.SCREEN_HEIGHT - height - 10 + (int)(_cameraY * 0.8f), width, height), Color.White);
        }
    }
    
    private void DrawStageCurtains(SpriteBatch spriteBatch)
    {
        if (_stageCurtains != null && _stageCurtains != Assets.Pixel)
        {
            float scale = (float)FNFGame.SCREEN_HEIGHT / _stageCurtains.Height;
            int width = (int)(_stageCurtains.Width * scale);
            int height = (int)(_stageCurtains.Height * scale);
            // Curtains parallax: foreground layer, slight camera offset
            int x = (FNFGame.SCREEN_WIDTH - width) / 2 + (int)(_cameraX * 0.3f);
            spriteBatch.Draw(_stageCurtains, new Rectangle(x, (int)(_cameraY * 0.3f), width, height), Color.White);
        }
    }
    
    /// <summary>
    /// Draw a static stage prop at its JSON-defined position with parallax scroll.
    /// Original FNF: each prop has position, scale, and scrollFactor from stage JSON.
    /// The camera offset is multiplied by the prop's scroll factor for parallax depth.
    /// </summary>
    private void DrawStaticProp(SpriteBatch spriteBatch, StaticStageProp prop)
    {
        if (!string.IsNullOrWhiteSpace(prop.Blend))
        {
            var blendState = GetStageBlendState(prop.Blend);
            if (blendState != null)
            {
                DrawBlazinStaticProp(spriteBatch, prop, blendState);
                return;
            }
        }

        if (_isPhillyBlazinStage && prop.Name == "skyAdditive")
        {
            DrawBlazinStaticProp(spriteBatch, prop, BlendState.Additive);
            return;
        }

        if (_isPhillyBlazinStage && prop.Name == "foregroundMultiply")
        {
            DrawBlazinStaticProp(spriteBatch, prop, _phillyBlazinMultiplyBlend);
            return;
        }

        if (_isPhillyBlazinStage && prop.Name == "additionalLighten")
        {
            DrawBlazinStaticProp(spriteBatch, prop, BlendState.Additive);
            return;
        }

        // Use WorldToScreen for camera projection � zoom is applied by the SpriteBatch matrix
        var screenPos = WorldToScreen(prop.X, prop.Y, prop.ScrollX, prop.ScrollY);
        int w = (int)(prop.Texture.Width * prop.ScaleX);
        int h = (int)(prop.Texture.Height * prop.ScaleY);

        // Philly window lights: tint with cycling colors (original: curLightColor alpha blend)
        Color tint = Color.White;
        if (_phillyLightsIndex >= 0 && prop.Name == "lights")
        {
            tint = PHILLY_LIGHT_COLORS[_phillyLightColor];
        }

        if (prop.OverlayColor.HasValue)
        {
            var overlay = prop.OverlayColor.Value;
            tint = new Color(overlay.R, overlay.G, overlay.B, tint.A);
        }

        tint *= Math.Clamp(prop.Alpha, 0f, 1f);

        bool isAbotPart = !string.IsNullOrEmpty(prop.Name) && prop.Name.StartsWith("neneSpeaker", StringComparison.OrdinalIgnoreCase);
        SpriteEffects fx = (_abotDebugMode && _abotDebugFlipX && isAbotPart) ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        spriteBatch.Draw(
            prop.Texture,
            new Vector2(screenPos.X, screenPos.Y),
            null,
            tint,
            0f,
            Vector2.Zero,
            new Vector2(prop.ScaleX, prop.ScaleY),
            fx,
            0f);
    }

    private void DrawBlazinStaticProp(SpriteBatch spriteBatch, StaticStageProp prop, BlendState blendState)
    {
        spriteBatch.End();
        spriteBatch.Begin(samplerState: _worldSampler, blendState: blendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);

        var screenPos = WorldToScreen(prop.X, prop.Y, prop.ScrollX, prop.ScrollY);
        Color tint = Color.White * Math.Clamp(prop.Alpha, 0f, 1f);
        if (prop.OverlayColor.HasValue)
        {
            var overlay = prop.OverlayColor.Value;
            tint = new Color(overlay.R, overlay.G, overlay.B, tint.A);
        }
        spriteBatch.Draw(
            prop.Texture,
            new Vector2(screenPos.X, screenPos.Y),
            null,
            tint,
            0f,
            Vector2.Zero,
            new Vector2(prop.ScaleX, prop.ScaleY),
            SpriteEffects.None,
            0f);

        spriteBatch.End();
        spriteBatch.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }

    private BlendState GetStageBlendState(string blend)
    {
        if (string.IsNullOrWhiteSpace(blend))
            return null;

        return blend.Trim().ToLowerInvariant() switch
        {
            "add" or "additive" or "screen" => BlendState.Additive,
            "multiply" => _phillyBlazinMultiplyBlend,
            _ => null
        };
    }
    
    private void UpdateAbotVisualizer(float delta)
    {
        _abotVizUpdateTimer += delta;
        if (_abotVizUpdateTimer < (1f / 30f))
            return;
        _abotVizUpdateTimer = 0f;

        if (_abotDebugMode)
        {
            for (int pi = 0; pi < _animatedProps.Count; pi++)
            {
                var dbgProp = _animatedProps[pi];
                if (!string.IsNullOrEmpty(dbgProp.Name) && dbgProp.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase))
                {
                    dbgProp.Alpha = 1f;
                    _animatedProps[pi] = dbgProp;
                }
            }
            return;
        }

        float musicLevel = Audio.MusicPlaying ? Audio.MusicVisualizerLevel : 0f;

        for (int pi = 0; pi < _animatedProps.Count; pi++)
        {
            var prop = _animatedProps[pi];
            if (string.IsNullOrEmpty(prop.Name) || !prop.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(prop.Name["neneSpeakerViz".Length..], out int oneBasedIndex))
                continue;

            int barIndex = oneBasedIndex - 1;
            if (barIndex < 0 || barIndex >= _abotVizDisplayLevels.Length)
                continue;

            float weighted = Math.Clamp(musicLevel * (1.15f - barIndex * 0.08f), 0f, 1f);
            float current = _abotVizDisplayLevels[barIndex];
            float lerp = weighted > current ? 0.55f : 0.22f;
            current += (weighted - current) * lerp;
            _abotVizDisplayLevels[barIndex] = current;

            int animFrame = (int)MathF.Round(current * 6f);
            bool visible = animFrame > 0;

            animFrame = Math.Clamp(animFrame - 1, 0, 5);
            animFrame = Math.Abs(animFrame - 5);

            string animName = prop.DanceAnimNames?.Length > 0
                ? prop.DanceAnimNames[0]
                : prop.Sprite?.CurrentAnimation;

            if (!string.IsNullOrEmpty(animName) && prop.Sprite != null && prop.Sprite.FrameIndex != animFrame)
                prop.Sprite.PlayAnimationFromFrame(animName, animFrame, loop: false, loopFrame: animFrame);

            prop.Alpha = visible ? 1f : 0f;
            _animatedProps[pi] = prop;
        }
    }

    private void UpdateAbotFallbackAnchors()
    {
        if (!_hasAbotFallback)
            return;

        if (_girlfriend != null)
        {
            float charOffX = _girlfriend.CharOffsets != null && _girlfriend.CharOffsets.Length >= 2 ? _girlfriend.CharOffsets[0] : 0f;
            float charOffY = _girlfriend.CharOffsets != null && _girlfriend.CharOffsets.Length >= 2 ? _girlfriend.CharOffsets[1] : 0f;
            float charScale = _girlfriend.Scale;
            _abotBaseX = _girlfriend.Position.X + _abotBodyOffsetX + (charOffX * charScale);
            _abotBaseY = _girlfriend.Position.Y + _abotBodyOffsetY + (charOffY * charScale);
        }

        for (int pi = 0; pi < _animatedProps.Count; pi++)
        {
            var prop = _animatedProps[pi];
            if (prop.Sprite == null)
                continue;

            if (string.Equals(prop.Name, "neneSpeakerRig", StringComparison.OrdinalIgnoreCase))
            {
                var bodyBasePos = new Vector2(_abotBaseX, _abotBaseY);
                var bodyOrigin = prop.Sprite.GetCompositeOrigin();
                prop.Sprite.Position = bodyOrigin.HasValue
                    ? bodyBasePos + bodyOrigin.Value
                    : bodyBasePos;
                _animatedProps[pi] = prop;
                continue;
            }

            if (string.Equals(prop.Name, "neneSpeakerPupil", StringComparison.OrdinalIgnoreCase))
            {
                var pupilBasePos = new Vector2(_abotBaseX + 50f, _abotBaseY + 238f);
                pupilBasePos = new Vector2(_abotBaseX + _abotPupilOffsetX, _abotBaseY + _abotPupilOffsetY);
                prop.Sprite.Position = pupilBasePos;
                _animatedProps[pi] = prop;
                continue;
            }

            if (prop.Name != null && prop.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(prop.Name["neneSpeakerViz".Length..], out int barIndex))
                {
                    float[] vizOffsetsX = { 0f, 59f, 56f, 66f, 54f, 52f, 51f };
                    float[] vizOffsetsY = { 0f, -8f, -3.5f, -0.4f, 0.5f, 4.7f, 7f };

                    float addX = 0f;
                    float addY = 0f;
                    for (int i = 0; i < barIndex && i < vizOffsetsX.Length; i++)
                    {
                        addX += vizOffsetsX[i];
                        addY += vizOffsetsY[i];
                    }

                    prop.Sprite.Position = new Vector2(_abotBaseX + _abotVizBaseOffsetX + addX, _abotBaseY + _abotVizBaseOffsetY + addY);
                    _animatedProps[pi] = prop;
                }
            }
        }

        for (int si = 0; si < _staticProps.Count; si++)
        {
            var prop = _staticProps[si];
            if (string.Equals(prop.Name, "neneSpeakerEyeWhites", StringComparison.OrdinalIgnoreCase))
            {
                prop.X = _abotBaseX + _abotEyesOffsetX;
                prop.Y = _abotBaseY + _abotEyesOffsetY;
                _staticProps[si] = prop;
            }
            else if (string.Equals(prop.Name, "neneSpeakerStereoBG", StringComparison.OrdinalIgnoreCase))
            {
                prop.X = _abotBaseX + _abotStereoOffsetX;
                prop.Y = _abotBaseY + _abotStereoOffsetY;
                _staticProps[si] = prop;
            }
        }
    }

    /// <summary>
    /// Draw all animated and static props whose zIndex is in [minZ, maxZ).
    /// Used for proper z-interleaved rendering with characters.
    /// </summary>
    private void DrawPropsInRange(SpriteBatch spriteBatch, int minZ, int maxZ)
    {
        // Merge static and animated props by zIndex for correct layering.
        // Both lists are already sorted by zIndex from loading.
        int si = 0, ai = 0;
        while (si < _staticProps.Count || ai < _animatedProps.Count)
        {
            // Get next static prop in range (skip those outside)
            while (si < _staticProps.Count && (_staticProps[si].ZIndex < minZ || _staticProps[si].ZIndex >= maxZ))
                si++;
            // Get next animated prop in range
            while (ai < _animatedProps.Count && (_animatedProps[ai].ZIndex < minZ || _animatedProps[ai].ZIndex >= maxZ))
                ai++;
            
            bool hasStatic = si < _staticProps.Count && _staticProps[si].ZIndex >= minZ && _staticProps[si].ZIndex < maxZ;
            bool hasAnim = ai < _animatedProps.Count && _animatedProps[ai].ZIndex >= minZ && _animatedProps[ai].ZIndex < maxZ;
            
            if (!hasStatic && !hasAnim) break;
            
            if (hasStatic && (!hasAnim || _staticProps[si].ZIndex <= _animatedProps[ai].ZIndex))
            {
                DrawStaticProp(spriteBatch, _staticProps[si]);
                si++;
            }
            else if (hasAnim)
            {
                var prop = _animatedProps[ai];
                if (prop.Sprite != null)
                {
                    var origPos = prop.Sprite.Position;
                    var origTint = prop.Sprite.Tint;
                    var screenPos = WorldToScreen(origPos.X, origPos.Y, prop.ScrollX, prop.ScrollY);
                    prop.Sprite.Position = screenPos;
                    float alpha = Math.Clamp(prop.Alpha, 0f, 1f);
                    byte outA = (byte)Math.Clamp(origTint.A * alpha, 0f, 255f);
                    prop.Sprite.Tint = new Color(origTint.R, origTint.G, origTint.B, outA);

                    bool isAbotViz = !string.IsNullOrEmpty(prop.Name)
                        && prop.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase);

                    Rectangle prevScissor = default;
                    bool clipped = false;
                    if (isAbotViz && TryGetAbotMonitorScissorRect(out var monitorScissor))
                    {
                        prevScissor = Game.GraphicsDevice.ScissorRectangle;
                        var clip = Rectangle.Intersect(prevScissor, monitorScissor);
                        if (clip.Width > 0 && clip.Height > 0)
                        {
                            Game.GraphicsDevice.ScissorRectangle = clip;
                            clipped = true;
                        }
                    }

                    prop.Sprite.Draw(spriteBatch);

                    if (clipped)
                        Game.GraphicsDevice.ScissorRectangle = prevScissor;

                    prop.Sprite.Position = origPos;
                    prop.Sprite.Tint = origTint;
                }
                ai++;
            }
        }
    }

    private bool TryGetAbotMonitorScissorRect(out Rectangle rect)
    {
        rect = default;
        if (!_hasAbotFallback)
            return false;

        var topLeft = WorldToScreen(_abotBaseX + ABOT_MONITOR_CLIP_X, _abotBaseY + ABOT_MONITOR_CLIP_Y);

        int x = (int)MathF.Floor(topLeft.X);
        int y = (int)MathF.Floor(topLeft.Y);
        int w = (int)MathF.Ceiling(ABOT_MONITOR_CLIP_W);
        int h = (int)MathF.Ceiling(ABOT_MONITOR_CLIP_H);

        var screenRect = new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        var clipRect = Rectangle.Intersect(screenRect, new Rectangle(x, y, Math.Max(0, w), Math.Max(0, h)));
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return false;

        rect = clipRect;
        return true;
    }

    private void HandleAbotDebugInput()
    {
        bool toggleDebug = Input.IsPressed(Keys.F8)
            || Input.IsPressed(Keys.F6)
            || Input.IsPressed(Keys.OemTilde)
            || Input.IsGamePadPressed(Buttons.RightStick);

        if (toggleDebug)
        {
            _abotDebugMode = !_abotDebugMode;
            Console.WriteLine($"[ABOT-DEBUG] mode={(_abotDebugMode ? "ON" : "OFF")}");
        }

        if (!_abotDebugMode)
            return;

        if (Input.IsPressed(Keys.Tab) || Input.IsPressed(Keys.PageDown))
        {
            _abotDebugSelection = (_abotDebugSelection + 1) % 7;
            LogAbotDebugState("selection");
        }

        if (Input.IsPressed(Keys.PageUp))
        {
            _abotDebugSelection = (_abotDebugSelection - 1 + 7) % 7;
            LogAbotDebugState("selection");
        }

        if (Input.IsPressed(Keys.OemPlus) || Input.IsPressed(Keys.Add))
        {
            _abotDebugStep = _abotDebugStep == 1 ? 5 : (_abotDebugStep == 5 ? 10 : 1);
            LogAbotDebugState("step");
        }

        if (Input.IsPressed(Keys.F7))
        {
            _abotDebugFreezeAnim = !_abotDebugFreezeAnim;
            LogAbotDebugState(_abotDebugFreezeAnim ? "anim-freeze" : "anim-live");
        }

        if (Input.IsPressed(Keys.H))
        {
            _abotDebugFlipX = !_abotDebugFlipX;
            LogAbotDebugState(_abotDebugFlipX ? "flipX-on" : "flipX-off");
        }

        if (Input.IsPressed(Keys.OemComma))
        {
            _abotDebugFrame = Math.Max(0, _abotDebugFrame - 1);
            _abotRigStartFrame = _abotDebugFrame;
            LogAbotDebugState("frame-");
        }
        if (Input.IsPressed(Keys.OemPeriod))
        {
            _abotDebugFrame += 1;
            _abotRigStartFrame = _abotDebugFrame;
            LogAbotDebugState("frame+");
        }

        float dx = 0f, dy = 0f;
        if (Input.IsPressed(Keys.NumPad4) || Input.IsPressed(Keys.Left)) dx -= _abotDebugStep;
        if (Input.IsPressed(Keys.NumPad6) || Input.IsPressed(Keys.Right)) dx += _abotDebugStep;
        if (Input.IsPressed(Keys.NumPad8) || Input.IsPressed(Keys.Up)) dy -= _abotDebugStep;
        if (Input.IsPressed(Keys.NumPad2) || Input.IsPressed(Keys.Down)) dy += _abotDebugStep;

        if (dx != 0f || dy != 0f)
        {
            ApplyAbotDebugDelta(dx, dy);
            LogAbotDebugState("move");
        }
    }

    private void ApplyAbotDebugDelta(float dx, float dy)
    {
        switch (_abotDebugSelection)
        {
            case 0: _abotBodyOffsetX += dx; _abotBodyOffsetY += dy; break;
            case 1: _abotEyesOffsetX += dx; _abotEyesOffsetY += dy; break;
            case 2: _abotPupilOffsetX += dx; _abotPupilOffsetY += dy; break;
            case 3: _abotVizBaseOffsetX += dx; _abotVizBaseOffsetY += dy; break;
            case 4: _abotStereoOffsetX += dx; _abotStereoOffsetY += dy; break;
            case 5:
                ABOT_MONITOR_CLIP_X += dx;
                ABOT_MONITOR_CLIP_Y += dy;
                break;
            case 6:
                ABOT_MONITOR_CLIP_W = Math.Max(10f, ABOT_MONITOR_CLIP_W + dx);
                ABOT_MONITOR_CLIP_H = Math.Max(10f, ABOT_MONITOR_CLIP_H + dy);
                break;
        }
    }

    private void LogAbotDebugState(string reason)
    {
        Console.WriteLine($"[ABOT-DEBUG] {reason} sel={_abotDebugSelection} step={_abotDebugStep} freeze={_abotDebugFreezeAnim} flipX={_abotDebugFlipX} frame={_abotDebugFrame} start={_abotRigStartFrame} body=({_abotBodyOffsetX:0.##},{_abotBodyOffsetY:0.##}) eyes=({_abotEyesOffsetX:0.##},{_abotEyesOffsetY:0.##}) pupil=({_abotPupilOffsetX:0.##},{_abotPupilOffsetY:0.##}) viz=({_abotVizBaseOffsetX:0.##},{_abotVizBaseOffsetY:0.##}) stereo=({_abotStereoOffsetX:0.##},{_abotStereoOffsetY:0.##}) clip=({ABOT_MONITOR_CLIP_X:0.##},{ABOT_MONITOR_CLIP_Y:0.##},{ABOT_MONITOR_CLIP_W:0.##},{ABOT_MONITOR_CLIP_H:0.##})");
    }

    private void UpdatePhillyBlazinLightning(float delta)
    {
        if (!_isPhillyBlazinStage)
            return;

        _phillyBlazinLightningTimer -= delta;
        _phillyBlazinLightningFadeTimer = Math.Max(0f, _phillyBlazinLightningFadeTimer - delta);
        _phillyBlazinLightningShortFadeTimer = Math.Max(0f, _phillyBlazinLightningShortFadeTimer - delta);

        if (_phillyBlazinLightningTimer <= 0f)
        {
            _phillyBlazinLightningTimer = 7f + (float)_random.NextDouble() * 8f;
            _phillyBlazinLightningFadeTimer = 1.5f;
            _phillyBlazinLightningShortFadeTimer = 0.3f;

            if (_phillyBlazinLightningIndex >= 0)
            {
                var lightning = _animatedProps[_phillyBlazinLightningIndex];
                lightning.Alpha = 1f;
                if (lightning.Sprite?.Sheet != null)
                {
                    if (lightning.Sprite.Sheet.Animations.ContainsKey("strike"))
                        lightning.Sprite.PlayAnimation("strike", force: true, loop: false);
                    else
                    {
                        string anim = lightning.Sprite.Sheet.Animations.Keys.FirstOrDefault();
                        if (!string.IsNullOrEmpty(anim))
                            lightning.Sprite.PlayAnimation(anim, force: true, loop: false);
                    }
                }
                _animatedProps[_phillyBlazinLightningIndex] = lightning;
            }

            if (_phillyBlazinSkyAdditiveIndex >= 0)
            {
                var sky = _staticProps[_phillyBlazinSkyAdditiveIndex];
                sky.Alpha = 0.7f;
                _staticProps[_phillyBlazinSkyAdditiveIndex] = sky;
            }

            if (_phillyBlazinForegroundMultiplyIndex >= 0)
            {
                var fg = _staticProps[_phillyBlazinForegroundMultiplyIndex];
                fg.Alpha = 0.64f;
                _staticProps[_phillyBlazinForegroundMultiplyIndex] = fg;
            }

            if (_phillyBlazinAdditionalLightenIndex >= 0)
            {
                var add = _staticProps[_phillyBlazinAdditionalLightenIndex];
                add.Alpha = 0.3f;
                _staticProps[_phillyBlazinAdditionalLightenIndex] = add;
            }
        }

        if (_phillyBlazinSkyAdditiveIndex >= 0)
        {
            var sky = _staticProps[_phillyBlazinSkyAdditiveIndex];
            if (_phillyBlazinLightningFadeTimer <= 0f)
                sky.Alpha = 0f;
            _staticProps[_phillyBlazinSkyAdditiveIndex] = sky;
        }

        if (_phillyBlazinForegroundMultiplyIndex >= 0)
        {
            var fg = _staticProps[_phillyBlazinForegroundMultiplyIndex];
            if (_phillyBlazinLightningFadeTimer <= 0f)
                fg.Alpha = 0f;
            _staticProps[_phillyBlazinForegroundMultiplyIndex] = fg;
        }

        if (_phillyBlazinAdditionalLightenIndex >= 0)
        {
            var add = _staticProps[_phillyBlazinAdditionalLightenIndex];
            if (_phillyBlazinLightningShortFadeTimer <= 0f)
                add.Alpha = 0f;
            _staticProps[_phillyBlazinAdditionalLightenIndex] = add;
        }

        if (_phillyBlazinLightningIndex >= 0)
        {
            var lightning = _animatedProps[_phillyBlazinLightningIndex];
            if (_phillyBlazinLightningFadeTimer <= 0f)
                lightning.Alpha = 0f;
            _animatedProps[_phillyBlazinLightningIndex] = lightning;
        }
    }
    
    
    /// <summary>
    /// Convert a world-space position to screen-space using HaxeFlixel camera model.
    /// In HaxeFlixel: camera.scroll = top-left of view in world space.
    /// Object screen pos = worldPos - camera.scroll * scrollFactor.
    /// Camera.scroll = cameraTarget - screenSize / (2 * zoom).
    /// </summary>
    private Vector2 WorldToScreen(float worldX, float worldY, float scrollX = 1f, float scrollY = 1f)
    {
        float zoom = _defaultCamZoom * _cameraBopMultiplier;
        // Camera center in world space, scroll = top-left offset by zoom
        float camScrollX = _camWorldX - 1280 / (2f * zoom);
        float camScrollY = _camWorldY - 720 / (2f * zoom);
        // World position offset by camera scroll * parallax, zoom applied by SpriteBatch matrix
        float sx = worldX - camScrollX * scrollX;
        float sy = worldY - camScrollY * scrollY;
        return new Vector2(sx, sy);
    }
    
    private void DrawCountdown(SpriteBatch spriteBatch)
    {
        if (_countdownDisplayTimer <= 0) return;
        
        float beatDuration = (float)_conductor.Crochet;
        // Original: alpha tweens from 1 to 0 over one beat
        float alpha = Math.Clamp(_countdownDisplayTimer / beatDuration, 0f, 1f);
        
        Texture2D tex = _countdownText switch
        {
            "READY" => _readyTex,
            "SET" => _setTex,
            "GO!" => _goTex,
            _ => null
        };
        
        if (tex != null && tex != Assets.Pixel)
        {
            // Original: setGraphicSize(Std.int(width * 0.65)), screenCenter
            int w = (int)(tex.Width * 0.65f);
            int h = (int)(tex.Height * 0.65f);
            spriteBatch.Draw(tex, 
                new Rectangle(1280 / 2 - w / 2, 720 / 2 - h / 2, w, h), 
                Color.White * alpha);
        }
        else
        {
            // Fallback: use AlphabetFont bitmap letters (like original)
            var alphabetFont = AlphabetFont.Bold;
            if (alphabetFont != null)
            {
                float abScale = 1.0f;
                float w = alphabetFont.MeasureWidth(_countdownText, abScale);
                alphabetFont.DrawString(spriteBatch, _countdownText,
                    new Vector2(1280 / 2 - w / 2, 720 / 2 - alphabetFont.MaxHeight * abScale / 2),
                    Color.White * alpha, abScale);
            }
            else
            {
                var font = Assets.GetFont(48);
                if (font != null)
                {
                    var size = font.MeasureString(_countdownText);
                    font.DrawText(spriteBatch, _countdownText,
                        new Vector2(1280 / 2 - size.X / 2, 720 / 2 - size.Y / 2),
                        Color.White * alpha);
                }
            }
        }
    }
    
    private void DrawNoteField(SpriteBatch spriteBatch)
    {
        int strumY = _downscroll ? (FNFGame.SCREEN_HEIGHT - STRUMLINE_Y_OFFSET - STRUMLINE_SIZE) : STRUMLINE_Y_OFFSET;
        int noteSize = STRUMLINE_SIZE;
        int laneSpacing = NOTE_SPACING;
        
        int playerStartX, opponentStartX;
        if (_middlescroll)
        {
            // Middlescroll: player strumline centered, opponent hidden
            playerStartX = FNFGame.SCREEN_WIDTH / 2 - (4 * laneSpacing) / 2;
            opponentStartX = -9999; // offscreen
        }
        else
        {
            playerStartX = FNFGame.SCREEN_WIDTH / 2 + STRUMLINE_X_OFFSET;
            opponentStartX = STRUMLINE_X_OFFSET;
        }
        
        // Draw opponent strumline (left side) � hidden in middlescroll
        if (!_middlescroll)
        {
            for (int i = 0; i < 4; i++)
            {
                int x = opponentStartX + i * laneSpacing;
                DrawReceptor(spriteBatch, x, strumY, noteSize, i, false);
            }
        }
        
        // Draw player strumline (right side, or center in middlescroll)
        for (int i = 0; i < 4; i++)
        {
            int x = playerStartX + i * laneSpacing;
            DrawReceptor(spriteBatch, x, strumY, noteSize, i, true);
        }

        if (_countdownActive) return; // Don't draw notes during countdown
        
        // Pre-compute scroll factor (avoids 4 multiplications per note)
        // Downscroll: notes come from top, scroll down to strumline at bottom (negate offset)
        float scrollFactor = PIXELS_PER_MS * 1000 * SCROLL_SPEED * _chart.Speed;
        if (_downscroll) scrollFactor = -scrollFactor;
        double songPos = _conductor.SongPosition;
        
        // Draw falling notes
        var visibleNotes = _noteField.GetVisibleNotes(songPos);
        for (int ni = 0; ni < visibleNotes.Count; ni++)
        {
            var note = visibleNotes[ni];
            float timeDiff = (float)(note.Time - songPos);
            float yOffset = timeDiff * scrollFactor;
            
            bool isPlayerNote = note.IsPlayerNote;
            
            // In middlescroll, skip drawing opponent notes
            if (_middlescroll && !isPlayerNote) continue;
            
            int startX = isPlayerNote ? playerStartX : opponentStartX;
            int lane = note.Lane % 4;
            
            int x = startX + lane * laneSpacing;
            int y = strumY + (int)yOffset;
            
            // For hit hold notes: note head stays at strumline, sustain shrinks from top
            bool isActiveHold = note.IsHit && note.SustainLength > 0;
            if (isActiveHold)
            {
                y = strumY; // Pin note head to strumline
            }
            
            // Draw sustain/hold tail first (behind note head)
            if (note.SustainLength > 0)
            {
                float sustainPixels = (float)(note.SustainLength * Math.Abs(scrollFactor));
                int sustainWidth = noteSize / 4;
                int sustainEndHeight = noteSize / 4;
                int sustainTop, sustainBottom;
                
                if (_downscroll)
                {
                    // Downscroll: sustain extends UPWARD from note
                    sustainBottom = y + noteSize / 2;
                    sustainTop = y + noteSize / 2 - (int)sustainPixels;
                    
                    if (isActiveHold)
                    {
                        sustainBottom = strumY + noteSize / 2;
                        float remainingSustain = (float)((note.Time + note.SustainLength) - songPos);
                        remainingSustain = Math.Max(0, remainingSustain);
                        sustainTop = sustainBottom - (int)(remainingSustain * Math.Abs(scrollFactor));
                    }
                    else
                    {
                        if (sustainBottom > strumY + noteSize / 2)
                            sustainBottom = strumY + noteSize / 2;
                    }
                }
                else
                {
                    // Upscroll: sustain extends DOWNWARD from note
                    sustainTop = y + noteSize / 2;
                    sustainBottom = y + (int)sustainPixels;
                
                    if (isActiveHold)
                    {
                        sustainTop = strumY + noteSize / 2;
                        float remainingSustain = (float)((note.Time + note.SustainLength) - songPos);
                        remainingSustain = Math.Max(0, remainingSustain);
                        sustainBottom = sustainTop + (int)(remainingSustain * Math.Abs(scrollFactor));
                    }
                    else
                    {
                        if (sustainTop < strumY + noteSize / 2)
                            sustainTop = strumY + noteSize / 2;
                    }
                }
                
                if (sustainBottom > sustainTop && sustainBottom > 0 && sustainTop < FNFGame.SCREEN_HEIGHT)
                {
                    int sustainCenterX = x + noteSize / 2 - sustainWidth / 2;
                    
                    // Compute end cap position first so body range is correct
                    // In downscroll, end cap goes at top (far end); in upscroll at bottom
                    int endY;
                    SpriteEffects endFlip;
                    if (_downscroll)
                    {
                        endY = sustainTop;
                        sustainTop += sustainEndHeight; // body starts below end cap
                        endFlip = SpriteEffects.FlipVertically;
                    }
                    else
                    {
                        endY = sustainBottom - sustainEndHeight;
                        endFlip = SpriteEffects.None;
                    }
                    
                    // Draw sustain body using cached frame
                    int bodyBottom = _downscroll ? sustainBottom : endY;
                    if (bodyBottom > sustainTop)
                    {
                        // Controller mode: draw solid colored rectangles (tinting colored arrow sprites doesn't change hue)
                        if (UseControllerDisplay && lane >= 0 && lane < 4)
                        {
                            spriteBatch.Draw(Assets.Pixel, 
                                new Rectangle(sustainCenterX, sustainTop, sustainWidth, bodyBottom - sustainTop), 
                                GetControllerLaneColor(lane) * 0.85f);
                        }
                        else
                        {
                            var sFrame = _sustainFrames[lane];
                            if (sFrame != null && _notesSheet?.Texture != null)
                            {
                                spriteBatch.Draw(_notesSheet.Texture, 
                                    new Rectangle(sustainCenterX, sustainTop, sustainWidth, bodyBottom - sustainTop), 
                                    sFrame.SourceRect, Color.White);
                            }
                            else
                            {
                                Color laneColor = lane switch
                                {
                                    0 => Color.Purple,
                                    1 => Color.Cyan,
                                    2 => Color.LimeGreen,
                                    3 => Color.Red,
                                    _ => Color.Magenta
                                };
                                spriteBatch.Draw(Assets.Pixel, 
                                    new Rectangle(sustainCenterX, sustainTop, sustainWidth, bodyBottom - sustainTop), 
                                    laneColor * 0.6f);
                            }
                        }
                    }

                    // Draw sustain end cap
                    if (endY >= 0 && endY < FNFGame.SCREEN_HEIGHT)
                    {
                        // Controller mode: solid colored end cap (same color as body)
                        if (UseControllerDisplay && lane >= 0 && lane < 4)
                        {
                            spriteBatch.Draw(Assets.Pixel,
                                new Rectangle(sustainCenterX, endY, sustainWidth, sustainEndHeight),
                                GetControllerLaneColor(lane) * 0.85f);
                        }
                        else
                        {
                            var eFrame = _sustainEndFrames[lane];
                            if (eFrame != null && _notesSheet?.Texture != null)
                            {
                                spriteBatch.Draw(_notesSheet.Texture,
                                    new Rectangle(sustainCenterX, endY, sustainWidth, sustainEndHeight),
                                    eFrame.SourceRect, Color.White, 0f, Vector2.Zero, endFlip, 0f);
                            }
                        }
                    }
                }
            }
            
            // Draw note head � skip if hold note already hit (head consumed, only sustain shows)
            if (!isActiveHold && y > -noteSize && y < FNFGame.SCREEN_HEIGHT)
            {
                DrawNote(spriteBatch, x, y, noteSize, lane);
            }
        }
    }
    
    private void DrawReceptor(SpriteBatch spriteBatch, int x, int y, int size, int lane, bool isPlayer)
    {
        bool keyHeld = isPlayer && Input.NoteHeld[lane];
        // Show confirm animation if key is held OR confirm timer is active (note just hit)
        bool showConfirm = keyHeld 
            || (isPlayer && _playerConfirmTimer[lane] > 0)
            || (!isPlayer && _opponentConfirmTimer[lane] > 0);

        // Controller mode: draw Xbox button sprite centered in receptor area (medium size)
        if (isPlayer && UseControllerDisplay)
        {
            var btn = Input.NoteFaceButtons[lane];
            int btnSize = (int)(size * 0.75f); // medium size � smaller than arrow but not tiny
            int bx = x + (size - btnSize) / 2;
            int by = y + (size - btnSize) / 2;
            Assets.DrawButtonReceptor(spriteBatch, btn, bx, by, btnSize,
                pressed: keyHeld, confirm: showConfirm && !keyHeld);
            return;
        }

        if (_receptorsSheet?.Texture != null)
        {
            var frames = showConfirm ? _receptorConfirmFrames[lane] : _receptorStaticFrames[lane];
            if (frames != null && frames.Count > 0)
            {
                int frameIdx = showConfirm ? (_animFrame % frames.Count) : 0;
                var frame = frames[frameIdx];
                if (showConfirm)
                {
                    // Confirm frames are larger � scale to fill but center on receptor
                    // Original: confirm glow extends beyond the 112px receptor area
                    float confirmScale = (float)size / Math.Min(frame.SourceRect.Width, frame.SourceRect.Height);
                    int cw = (int)(frame.SourceRect.Width * confirmScale);
                    int ch = (int)(frame.SourceRect.Height * confirmScale);
                    int cx = x + size / 2 - cw / 2;
                    int cy = y + size / 2 - ch / 2;
                    spriteBatch.Draw(_receptorsSheet.Texture, new Rectangle(cx, cy, cw, ch), frame.SourceRect, Color.White);
                }
                else
                {
                    spriteBatch.Draw(_receptorsSheet.Texture, new Rectangle(x, y, size, size), frame.SourceRect, Color.White);
                }
                return;
            }
            // Fallback to press frames
            if (showConfirm)
            {
                var pressFrames = _receptorPressFrames[lane];
                if (pressFrames != null && pressFrames.Count > 0)
                {
                    spriteBatch.Draw(_receptorsSheet.Texture, new Rectangle(x, y, size, size), pressFrames[0].SourceRect, Color.White);
                    return;
                }
            }
        }

        // Fallback colored rectangles
        Color laneColor = lane switch
        {
            0 => Color.Purple,
            1 => Color.Cyan,
            2 => Color.LimeGreen,
            3 => Color.Red,
            _ => Color.Gray
        };
        float baseAlpha = isPlayer ? 0.6f : 0.3f;
        if (showConfirm) baseAlpha = isPlayer ? 1.0f : 0.7f;
        spriteBatch.Draw(Assets.Pixel, new Rectangle(x, y, size, size), laneColor * baseAlpha);
    }

    private void DrawNote(SpriteBatch spriteBatch, int x, int y, int size, int lane)
    {
        // Controller mode: draw Xbox button sprites instead of arrow notes
        if (UseControllerDisplay && lane >= 0 && lane < 4)
        {
            var btn = Input.NoteFaceButtons[lane];
            var btnTex = Assets.GetButtonSprite(btn);
            if (btnTex != null)
            {
                int btnSize = (int)(size * 0.75f); // match receptor medium size
                int bx = x + (size - btnSize) / 2;
                int by = y + (size - btnSize) / 2;
                spriteBatch.Draw(btnTex, new Rectangle(bx, by, btnSize, btnSize), Color.White);
                return;
            }
            // Fallback: colored circle
            var (btnColor, _) = AssetManager.GetButtonInfo(btn);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(x, y, size, size), btnColor);
            return;
        }

        var frame = _noteFrames[lane];
        if (frame != null && _notesSheet?.Texture != null)
        {
            spriteBatch.Draw(_notesSheet.Texture, new Rectangle(x, y, size, size), frame.SourceRect, Color.White);
        }
        else
        {
            Color laneColor = lane switch
            {
                0 => Color.Purple,
                1 => Color.Cyan,
                2 => Color.LimeGreen,
                3 => Color.Red,
                _ => Color.Magenta
            };
            spriteBatch.Draw(Assets.Pixel, new Rectangle(x, y, size, size), laneColor);
        }
    }
    
    private void DrawHUD(SpriteBatch spriteBatch)
    {
        // Original: health bar at bottom in upscroll, top area in downscroll
        int healthBarY = _downscroll 
            ? (int)(FNFGame.SCREEN_HEIGHT * 0.1f) 
            : (int)(FNFGame.SCREEN_HEIGHT * 0.9f);
        
        if (_healthBarBG != null && _healthBarBG != Assets.Pixel)
        {
            int bgWidth = _healthBarBG.Width;
            int bgHeight = _healthBarBG.Height;
            int bgX = (FNFGame.SCREEN_WIDTH - bgWidth) / 2;
            spriteBatch.Draw(_healthBarBG, new Rectangle(bgX, healthBarY, bgWidth, bgHeight), Color.White);
            
            int barWidth = bgWidth - 8;
            int barHeight = bgHeight - 8;
            int barX = bgX + 4;
            int barY = healthBarY + 4;
            
            // Character-specific colors (original FNF: opponent color on left, player on right)
            Color opponentColor = _opponent?.HealthBarColor ?? Color.Red;
            Color playerColor = _boyfriend?.HealthBarColor ?? new Color(0x31, 0xB0, 0xD1);
            
            // Opponent side (full bar)
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX, barY, barWidth, barHeight), opponentColor);
            
            // Player side (fills from right based on health)
            float healthNorm = _healthLerp / HEALTH_MAX;
            int healthWidth = (int)(barWidth * healthNorm);
            spriteBatch.Draw(Assets.Pixel, 
                new Rectangle(barX + barWidth - healthWidth, barY, healthWidth, barHeight), 
                playerColor);
        }
        else
        {
            int barWidth = 600;
            int barHeight = 18;
            int barX = (FNFGame.SCREEN_WIDTH - barWidth) / 2;
            
            Color opponentColor2 = _opponent?.HealthBarColor ?? Color.Red;
            Color playerColor2 = _boyfriend?.HealthBarColor ?? new Color(0x31, 0xB0, 0xD1);
            
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX - 4, healthBarY - 4, barWidth + 8, barHeight + 8), Color.Black);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX, healthBarY, barWidth, barHeight), opponentColor2);
            float healthNorm2 = _healthLerp / HEALTH_MAX;
            int healthWidth = (int)(barWidth * healthNorm2);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(barX + barWidth - healthWidth, healthBarY, healthWidth, barHeight), playerColor2);
        }
        
        // Character icons (use cached textures)
        var bfIcon = _playerIcon;
        var opponentIcon = _opponentIcon;
        
        // Icon bounce on beat (scale from center)
        int iconBaseSize = 150;
        int iconSize = (int)(iconBaseSize * _iconBounceScale);
        int iconAreaWidth = 550;
        int iconBaseX = (FNFGame.SCREEN_WIDTH - iconAreaWidth) / 2;
        int iconSizeOffset = (iconSize - iconBaseSize) / 2;
        int iconY = healthBarY - iconSize / 2 - 5;
        float healthDisp = _healthLerp / HEALTH_MAX;
        int dividerPos = (int)(iconAreaWidth * (1 - healthDisp));
        
        // Opponent icon (original: square icon frames, width = height per frame)
        if (opponentIcon != null && opponentIcon != Assets.Pixel)
        {
            int frameW = opponentIcon.Height; // Icons are square: frame width = texture height
            int numFrames = opponentIcon.Width / Math.Max(1, frameW);
            int iconFrame;
            if (numFrames >= 3)
                iconFrame = _health > 1.6f ? 1 : (_health < 0.4f ? 2 : 0); // opponent losing/winning/normal
            else if (numFrames >= 2)
                iconFrame = _health > 1.6f ? 1 : 0; // losing/normal
            else
                iconFrame = 0;
            int oppIconX = iconBaseX + dividerPos - iconSize - 26;
            
            if (numFrames >= 2)
            {
                var srcRect = new Rectangle(iconFrame * frameW, 0, frameW, frameW);
                spriteBatch.Draw(opponentIcon, new Rectangle(oppIconX, iconY, iconSize, iconSize), srcRect, Color.White);
            }
            else
            {
                spriteBatch.Draw(opponentIcon, new Rectangle(oppIconX, iconY, iconSize, iconSize), Color.White);
            }
        }
        
        // BF icon (flipped, original: square icon frames)
        if (bfIcon != null && bfIcon != Assets.Pixel)
        {
            int frameW = bfIcon.Height; // Icons are square: frame width = texture height
            int numFrames = bfIcon.Width / Math.Max(1, frameW);
            int iconFrame;
            if (numFrames >= 3)
                iconFrame = _health < 0.4f ? 1 : (_health > 1.6f ? 2 : 0); // BF losing/winning/normal
            else if (numFrames >= 2)
                iconFrame = _health < 0.4f ? 1 : 0; // losing/normal
            else
                iconFrame = 0;
            int bfIconX = iconBaseX + dividerPos + 26;
            
            if (numFrames >= 2)
            {
                var srcRect = new Rectangle(iconFrame * frameW, 0, frameW, frameW);
                spriteBatch.Draw(bfIcon, new Rectangle(bfIconX, iconY, iconSize, iconSize), srcRect, Color.White, 0, Vector2.Zero, SpriteEffects.FlipHorizontally, 0);
            }
            else
            {
                spriteBatch.Draw(bfIcon, new Rectangle(bfIconX, iconY, iconSize, iconSize), null, Color.White, 0, Vector2.Zero, SpriteEffects.FlipHorizontally, 0);
            }
        }
        
        // Score text (matches original: centered below health bar, shows score + misses + accuracy + grade)
        float accuracy = _totalNotes > 0 ? (_totalNotesHit / (float)_totalNotes) * 100f : 0f;
        string grade = accuracy >= 100f ? "S+" : accuracy >= 95f ? "S" : accuracy >= 90f ? "A" :
                       accuracy >= 80f ? "B" : accuracy >= 70f ? "C" : accuracy >= 60f ? "D" : "F";
        string scoreText = $"Score: {_score:N0}  |  Misses: {_misses}  |  Accuracy: {accuracy:F2}% [{grade}]  |  NPS: {_nps}";
        var font = Assets.GetFont(16);
        if (font != null)
        {
            var scoreSize = font.MeasureString(scoreText);
            float scoreX = (FNFGame.SCREEN_WIDTH - scoreSize.X) / 2f;
            float scoreY = healthBarY + 30;
            // Drop shadow for readability over health bar
            font.DrawText(spriteBatch, scoreText, 
                new Vector2(scoreX + 1, scoreY + 1), Color.Black * 0.5f);
            font.DrawText(spriteBatch, scoreText, 
                new Vector2(scoreX, scoreY), Color.White);
        }
        
        // Song progress time bar (original: TimeBar at top/bottom depending on scroll)
        {
            // Use audio duration if available, fallback to chart song length
            double audioLen = Audio.MusicLength;
            float songLen = audioLen > 1 ? (float)audioLen : (float)(_chart?.SongLength ?? 180);
            if (songLen > 0)
            {
                float progress = Math.Clamp((float)_conductor.SongPosition / songLen, 0f, 1f);
                int tbW = 400;
                int tbH = 10;
                int tbX = (FNFGame.SCREEN_WIDTH - tbW) / 2;
                // Position: in downscroll at bottom, in upscroll above the score text area
                int tbY = _downscroll ? FNFGame.SCREEN_HEIGHT - 22 : 6;
                // Background
                spriteBatch.Draw(Assets.Pixel, new Rectangle(tbX - 1, tbY - 1, tbW + 2, tbH + 2), Color.Black * 0.5f);
                spriteBatch.Draw(Assets.Pixel, new Rectangle(tbX, tbY, tbW, tbH), new Color(30, 30, 30) * 0.8f);
                // Fill
                spriteBatch.Draw(Assets.Pixel, new Rectangle(tbX, tbY, (int)(tbW * progress), tbH), Color.White * 0.8f);
                // Time text centered below/above bar
                var tbFont = Assets.GetFont(12);
                if (tbFont != null)
                {
                    int elapsedSec = Math.Max(0, (int)_conductor.SongPosition);
                    int totalSec = Math.Max(0, (int)songLen);
                    string timeStr = $"{elapsedSec / 60}:{elapsedSec % 60:D2} / {totalSec / 60}:{totalSec % 60:D2}";
                    var tsz = tbFont.MeasureString(timeStr);
                    int timeTextY = _downscroll ? tbY - (int)tsz.Y - 2 : tbY + tbH + 2;
                    tbFont.DrawText(spriteBatch, timeStr,
                        new Vector2(tbX + (tbW - tsz.X) / 2, timeTextY), Color.White * 0.6f);
                }
            }
        }
        
        // Botplay indicator (original: centered, below/above strumline)
        if (_botplay)
        {
            var botFont = Assets.GetFont(32);
            if (botFont != null)
            {
                var botSize = botFont.MeasureString("BOTPLAY");
                // Place between strumline and health bar
                int botY = _downscroll ? FNFGame.SCREEN_HEIGHT - 100 : 70;
                botFont.DrawText(spriteBatch, "BOTPLAY",
                    new Vector2((FNFGame.SCREEN_WIDTH - botSize.X) / 2f, botY),
                    Color.White * 0.6f);
            }
        }

        if (_abotDebugMode)
        {
            var dbgFont = Assets.GetFont(12);
            if (dbgFont != null)
            {
                string text = $"ABOT DEBUG [F8/F6/~ or RStick] sel:{_abotDebugSelection} step:{_abotDebugStep} freeze:{_abotDebugFreezeAnim} flipX:{_abotDebugFlipX} frame:{_abotDebugFrame} start:{_abotRigStartFrame}\nTAB/PageDown next | PageUp prev | +/- step | Arrows/Numpad move | F7 freeze anim | ,/. frame | H flipX\nbody({_abotBodyOffsetX:0},{_abotBodyOffsetY:0}) eyes({_abotEyesOffsetX:0},{_abotEyesOffsetY:0}) pupil({_abotPupilOffsetX:0},{_abotPupilOffsetY:0})\nviz({_abotVizBaseOffsetX:0},{_abotVizBaseOffsetY:0}) stereo({_abotStereoOffsetX:0},{_abotStereoOffsetY:0}) clip({ABOT_MONITOR_CLIP_X:0},{ABOT_MONITOR_CLIP_Y:0},{ABOT_MONITOR_CLIP_W:0},{ABOT_MONITOR_CLIP_H:0})";
                dbgFont.DrawText(spriteBatch, text, new Vector2(8, 8), Color.Yellow);
            }
        }
    }
    
    private void DrawRating(SpriteBatch spriteBatch)
    {
        float alpha = _ratingAlpha;
        
        // Rating position (original: FlxG.width * 0.474, ratingY has physics)
        float ratingX = FNFGame.SCREEN_WIDTH * 0.474f;
        float ratingY = _ratingY;
        
        // Scale: original setGraphicSize(Std.int(width * 0.7))
        float ratingScale = 0.7f;
        
        // Draw rating image using cached textures
        Texture2D ratingTex = _lastRating switch
        {
            "SICK!!" => _ratingSickTex,
            "GOOD!" => _ratingGoodTex,
            "BAD" => _ratingBadTex,
            "SHIT" => _ratingShitTex,
            _ => null
        };
        
        bool drewImage = false;
        if (ratingTex != null && ratingTex != Assets.Pixel)
        {
            int width = (int)(ratingTex.Width * ratingScale);
            int height = (int)(ratingTex.Height * ratingScale);
            spriteBatch.Draw(ratingTex, 
                new Rectangle((int)(ratingX - width / 2), (int)ratingY, width, height), 
                Color.White * alpha);
            drewImage = true;
        }
        
        if (!drewImage)
        {
            Color ratingColor = _lastRating switch
            {
                "SICK!!" => Color.Cyan,
                "GOOD!" => Color.LimeGreen,
                "BAD" => Color.Orange,
                "SHIT" => Color.Red,
                "MISS" => Color.DarkRed,
                _ => Color.White
            };
            DrawText(spriteBatch, _lastRating, 
                new Vector2(ratingX - 50, ratingY), ratingColor * alpha);
        }
        
        // Draw combo number below rating when combo >= 10 (original: if (combo >= 10) displayCombo)
        if (_lastComboDisplay >= 10 && _lastRating != "MISS")
        {
            DrawComboDigits(spriteBatch, _lastComboDisplay, ratingX, _comboY, _comboAlpha);
        }
    }
    
    // Reusable digit buffer to avoid per-frame List allocation
    private readonly int[] _digitBuffer = new int[10];
    
    /// <summary>
    /// Draw combo digits as individual number sprites (matches original PopUpStuff.displayCombo).
    /// Original separates the combo into individual digits and displays them.
    /// </summary>
    private void DrawComboDigits(SpriteBatch spriteBatch, int combo, float baseX, float y, float alpha)
    {
        // Separate into individual digits using reusable buffer
        int digitCount = 0;
        int temp = combo;
        while (temp > 0 && digitCount < _digitBuffer.Length)
        {
            _digitBuffer[digitCount++] = temp % 10;
            temp /= 10;
        }
        while (digitCount < 3) _digitBuffer[digitCount++] = 0;

        for (int di = 0; di < digitCount; di++)
        {
            int digit = _digitBuffer[di];
            // Guard against invalid digit value
            if (digit < 0 || digit >= 10)
                continue;

            int daLoop = di + 1;
            var numTex = _comboDigitTex[digit];
            float digitX = (FNFGame.SCREEN_WIDTH * 0.507f) - (36 * daLoop) - 65;
            float digitY = y; // Use physics-driven Y from caller (original: each digit falls with gravity)

            if (numTex != null && numTex != Assets.Pixel)
            {
                float scale = 0.5f;
                int w = (int)(numTex.Width * scale);
                int h = (int)(numTex.Height * scale);
                spriteBatch.Draw(numTex, new Rectangle((int)digitX, (int)digitY, w, h), Color.White * alpha);
            }
            else if (_comboStripTex != null && _comboStripRects != null && digit >= 0 && digit < _comboStripRects.Length)
            {
                // Fallback: draw from combo.png strip (with bounds check)
                float scale = 0.5f;
                var srcRect = _comboStripRects[digit];
                int w = (int)(srcRect.Width * scale);
                int h = (int)(srcRect.Height * scale);
                spriteBatch.Draw(_comboStripTex, new Rectangle((int)digitX, (int)digitY, w, h), srcRect, Color.White * alpha);
            }
            else
            {
                // Fallback: text
                var font = Assets.GetFont(32);
                if (font != null)
                    font.DrawText(spriteBatch, digit.ToString(), new Vector2(digitX, digitY), Color.White * alpha);
            }
        }
    }
    
    /// <summary>
    /// Draw note splash effects on sick hits (original: playerStrumline.playNoteSplash)
    /// </summary>
    private void DrawNoteSplashes(SpriteBatch spriteBatch)
    {
        int playerStartX = FNFGame.SCREEN_WIDTH / 2 + STRUMLINE_X_OFFSET;
        int strumY = STRUMLINE_Y_OFFSET;
        if (_downscroll) strumY = FNFGame.SCREEN_HEIGHT - STRUMLINE_Y_OFFSET - STRUMLINE_SIZE;
        if (_middlescroll) playerStartX = FNFGame.SCREEN_WIDTH / 2 - (4 * NOTE_SPACING) / 2;
        int noteSize = STRUMLINE_SIZE;
        
        for (int i = 0; i < 4; i++)
        {
            if (_noteSplashTimer[i] > 0)
            {
                int x = playerStartX + i * NOTE_SPACING;
                float splashAlpha = _noteSplashTimer[i] / 0.3f;
                float splashScale = 1.5f + (1f - splashAlpha) * 0.5f;
                int splashSize = (int)(noteSize * splashScale);
                int offset = (splashSize - noteSize) / 2;
                
                // Controller mode: use button colors for splash tint
                Color splashTint = Color.White;
                if (UseControllerDisplay && i >= 0 && i < 4)
                    splashTint = GetControllerLaneColor(i);

                // Try spritesheet splash frames
                if (_splashesSheet != null)
                {
                    string laneName = _laneNames[i];
                    var frames = _splashesSheet.GetAnimation($"{laneName} splash")
                              ?? _splashesSheet.GetAnimationFuzzy($"note splash {laneName}")
                              ?? _splashesSheet.GetAnimationFuzzy($"splash {laneName}");
                    if (frames != null && frames.Count > 0)
                    {
                        int frameIdx = (int)((1f - splashAlpha) * (frames.Count - 1));
                        frameIdx = Math.Clamp(frameIdx, 0, frames.Count - 1);
                        var frame = frames[frameIdx];
                        spriteBatch.Draw(_splashesSheet.Texture,
                            new Rectangle(x - offset, strumY - offset, splashSize, splashSize),
                            frame.SourceRect, splashTint * splashAlpha);
                        continue;
                    }
                }

                // Fallback: colored rectangle
                Color splashColor;
                if (UseControllerDisplay && i >= 0 && i < 4)
                    splashColor = GetControllerLaneColor(i);
                else
                    splashColor = i switch
                    {
                        0 => Color.Purple,
                        1 => Color.Cyan,
                        2 => Color.LimeGreen,
                        3 => Color.Red,
                        _ => Color.White
                    };

                spriteBatch.Draw(Assets.Pixel,
                    new Rectangle(x - offset, strumY - offset, splashSize, splashSize),
                    splashColor * splashAlpha * 0.4f);
            }
        }
    }
    
    /// <summary>
    /// Draw hold note cover glow effect while sustain notes are being held.
    /// Original: holdNoteCover sprite with start/hold/end animations per lane.
    /// </summary>
    private void DrawHoldNoteCovers(SpriteBatch spriteBatch)
    {
        int playerStartX = FNFGame.SCREEN_WIDTH / 2 + STRUMLINE_X_OFFSET;
        int strumY = STRUMLINE_Y_OFFSET;
        if (_downscroll) strumY = FNFGame.SCREEN_HEIGHT - STRUMLINE_Y_OFFSET - STRUMLINE_SIZE;
        if (_middlescroll) playerStartX = FNFGame.SCREEN_WIDTH / 2 - (4 * NOTE_SPACING) / 2;
        int noteSize = STRUMLINE_SIZE;
        
        for (int i = 0; i < 4; i++)
        {
            if (_holdingNote[i] && Input.NoteHeld[i])
            {
                _holdCoverTimer[i] += _lastDelta;
                int x = playerStartX + i * NOTE_SPACING;
                float coverAlpha = Math.Min(1f, _holdCoverTimer[i] / 0.1f);
                float coverScale = 1.3f;
                int coverSize = (int)(noteSize * coverScale);
                int offset = (coverSize - noteSize) / 2;
                
                if (_holdCoverSheet != null)
                {
                    string laneName = _laneNames[i];
                    var frames = _holdCoverSheet.GetAnimationFuzzy($"{laneName} hold")
                              ?? _holdCoverSheet.GetAnimationFuzzy($"hold {laneName}")
                              ?? _holdCoverSheet.GetAnimationFuzzy("loop");
                    if (frames != null && frames.Count > 0)
                    {
                        int frameIdx = ((int)(_holdCoverTimer[i] * 24)) % frames.Count;
                        var frame = frames[frameIdx];
                        spriteBatch.Draw(_holdCoverSheet.Texture,
                            new Rectangle(x - offset, strumY - offset, coverSize, coverSize),
                            frame.SourceRect, Color.White * coverAlpha);
                        continue;
                    }
                }
                
                // Fallback: colored glow rectangle (use controller button colors when active)
                Color glowColor;
                if (UseControllerDisplay && i >= 0 && i < 4)
                    glowColor = GetControllerLaneColor(i);
                else
                    glowColor = i switch
                    {
                        0 => Color.Purple,
                        1 => Color.Cyan,
                        2 => Color.LimeGreen,
                        3 => Color.Red,
                        _ => Color.White
                    };
                spriteBatch.Draw(Assets.Pixel,
                    new Rectangle(x - offset, strumY - offset, coverSize, coverSize),
                    glowColor * coverAlpha * 0.25f);
            }
            else
            {
                _holdCoverTimer[i] = 0;
            }
        }
    }
    
    /// <summary>
    /// Build pause menu items dynamically based on current state.
    /// Matches original PAUSE_MENU_ENTRIES_STANDARD / PAUSE_MENU_ENTRIES_DIFFICULTY.
    /// </summary>
    private void BuildPauseMenuItems()
    {
        if (_pauseDifficultyMode)
        {
            // Difficulty sub-menu (original: lists available difficulties + Back)
            var items = new List<string> { "Easy", "Normal", "Hard", "Erect", "Nightmare", "Back" };
            _pauseItems = items.ToArray();
        }
        else
        {
            // Standard pause menu
            var items = new List<string> { "Resume", "Restart Song", "Change Difficulty" };
            if (!_practiceMode)
                items.Add("Enable Practice Mode");
            items.Add(_botplay ? "Disable Botplay" : "Enable Botplay");
            items.Add("Exit to Menu");
            _pauseItems = items.ToArray();
        }
    }
    
    /// <summary>
    /// Execute the selected pause menu item by name.
    /// </summary>
    private void ExecutePauseMenuItem(string item)
    {
        switch (item)
        {
            case "Resume":
                _paused = false;
                Input.GameplayMode = true;
                if (!_countdownActive)
                {
                    Audio.ResumeMusic();
                    _conductor.Resume();
                }
                break;
            case "Restart Song":
                Audio.StopMusic();
                if (_weekSongs != null)
                    Game.Scenes.ChangeScene(new PlayScene(
                        _songName, _currentDifficulty,
                        _weekSongs, _weekSongIndex, _weekId, _weekAccumulatedScore));
                else
                    Game.Scenes.ChangeScene(new PlayScene(_songName, _currentDifficulty));
                break;
            case "Change Difficulty":
                _pauseDifficultyMode = true;
                _pauseSelection = 0;
                BuildPauseMenuItems();
                Audio.PlaySound("scrollMenu");
                break;
            case "Back":
                // Return to standard pause menu from difficulty sub-menu
                _pauseDifficultyMode = false;
                _pauseSelection = 0;
                BuildPauseMenuItems();
                Audio.PlaySound("scrollMenu");
                break;
            case "Easy":
            case "Normal":
            case "Hard":
            case "Erect":
            case "Nightmare":
                // Change difficulty = restart song with new difficulty
                // (original: changeDifficulty ? restartPlayState with new diff)
                Audio.StopMusic();
                if (_weekSongs != null)
                    Game.Scenes.ChangeScene(new PlayScene(
                        _songName, item.ToLowerInvariant(),
                        _weekSongs, _weekSongIndex, _weekId, _weekAccumulatedScore));
                else
                    Game.Scenes.ChangeScene(new PlayScene(_songName, item.ToLowerInvariant()));
                break;
            case "Enable Practice Mode":
                _practiceMode = true;
                BuildPauseMenuItems(); // Rebuild to remove this option
                _pauseSelection = Math.Min(_pauseSelection, _pauseItems.Length - 1);
                Audio.PlaySound("confirmMenu");
                break;
            case "Enable Botplay":
            case "Disable Botplay":
                _botplay = !_botplay;
                BuildPauseMenuItems();
                _pauseSelection = Math.Min(_pauseSelection, _pauseItems.Length - 1);
                Audio.PlaySound("confirmMenu");
                break;
            case "Exit to Menu":
                Audio.StopMusic();
                if (_weekId != null)
                    Game.Scenes.ChangeScene(new StoryModeScene());
                else
                    Game.Scenes.ChangeScene(new FreeplayScene());
                break;
        }
    }
    
    private void DrawPauseOverlay(SpriteBatch spriteBatch)
    {
        // === Semi-transparent black background (original: alpha 0->0.6, 0.8s quartOut) ===
        spriteBatch.Draw(Assets.Pixel, 
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), 
            Color.Black * _pauseBgAlpha);
        
        // === Metadata in top-right (original: vcr.ttf, 32px, right-aligned, staggered fade-in) ===
        var metaFont = Assets.GetFont(32);
        if (metaFont != null)
        {
            int metaX = FNFGame.SCREEN_WIDTH - 20;
            int metaY = 15;
            int lineH = 32;
            int lineIdx = 0;
            
            // Line 0: Song name (original: just the song name, no prefix)
            string songDisplay = (_chart?.SongName ?? _songName ?? "").Replace("-", " ").Replace("_", " ");
            songDisplay = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(songDisplay);
            float a0 = GetMetadataLineAlpha(lineIdx * 0.1f);
            DrawRightAligned(spriteBatch, metaFont, songDisplay, metaX, metaY + lineH * lineIdx, Color.White * a0);
            lineIdx++;
            
            // Line 1: Artist (original: "Artist: Kawai Sprite", cycles to Charter after 15s)
            string artistText = $"Artist: {_chart?.Artist ?? "Unknown"}";
            float a1 = GetMetadataLineAlpha(lineIdx * 0.1f);
            DrawRightAligned(spriteBatch, metaFont, artistText, metaX, metaY + lineH * lineIdx, Color.White * a1);
            lineIdx++;
            
            // Line 2: Difficulty (original: "Difficulty: Easy/Normal/Hard")
            string diffDisplay = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                _chart?.Difficulty ?? _currentDifficulty ?? "normal");
            string diffText = $"Difficulty: {diffDisplay}";
            float a2 = GetMetadataLineAlpha(lineIdx * 0.1f);
            DrawRightAligned(spriteBatch, metaFont, diffText, metaX, metaY + lineH * lineIdx, Color.White * a2);
            lineIdx++;
            
            // Line 3: Blue Balls (original: "{deathCounter} Blue Balls")
            string deathText = $"{_deathCounter} Blue Balls";
            float a3 = GetMetadataLineAlpha(lineIdx * 0.1f);
            DrawRightAligned(spriteBatch, metaFont, deathText, metaX, metaY + lineH * lineIdx, Color.White * a3);
            lineIdx++;
            
            // Line 4: PRACTICE MODE (only when enabled)
            if (_practiceMode)
            {
                float a4 = GetMetadataLineAlpha(lineIdx * 0.1f);
                DrawRightAligned(spriteBatch, metaFont, "PRACTICE MODE", metaX, metaY + lineH * lineIdx, Color.White * a4);
            }
        }
        
        // === Offset text bottom-right (original: vcr.ttf, 16px, right-aligned) ===
        var offsetFont = Assets.GetFont(16);
        if (offsetFont != null)
        {
            int offsetX = FNFGame.SCREEN_WIDTH - 20;
            float offsetAlpha = GetMetadataLineAlpha(0.5f);
            
            string offsetStr = $"Global Offset: {_globalOffset}ms";
            int offsetY = FNFGame.SCREEN_HEIGHT - 56;
            DrawRightAligned(spriteBatch, offsetFont, offsetStr, offsetX, offsetY, Color.White * offsetAlpha);
            DrawRightAligned(spriteBatch, offsetFont, "Hold SHIFT-UP/DOWN,", offsetX, offsetY + 16, Color.White * offsetAlpha);
            DrawRightAligned(spriteBatch, offsetFont, "to change the offset.", offsetX, offsetY + 32, Color.White * offsetAlpha);
        }
        
        // === Song time progress bar (P2) ===
        float timeAlpha = GetMetadataLineAlpha(0.3f);
        if (timeAlpha > 0.01f)
        {
            int barW = 320;
            int barH = 8;
            int barX = FNFGame.SCREEN_WIDTH - barW - 20;
            int barY = 155;
            double audioLen = Audio.MusicLength;
            float songLen = audioLen > 1 ? (float)audioLen : (float)(_chart?.SongLength ?? 180);
            if (songLen <= 0) songLen = 1;
            float progress = Math.Clamp((float)_conductor.SongPosition / songLen, 0f, 1f);
            
            // Background bar
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(barX, barY, barW, barH),
                Color.White * 0.2f * timeAlpha);
            // Fill bar
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(barX, barY, (int)(barW * progress), barH),
                Color.White * 0.8f * timeAlpha);
            
            // Time text
            var timeFont = Assets.GetFont(14);
            if (timeFont != null)
            {
                int elapsedSec = Math.Max(0, (int)_conductor.SongPosition);
                int totalSec = Math.Max(0, (int)songLen);
                string timeStr = $"{elapsedSec / 60}:{elapsedSec % 60:D2} / {totalSec / 60}:{totalSec % 60:D2}";
                DrawRightAligned(spriteBatch, timeFont, timeStr, FNFGame.SCREEN_WIDTH - 20, barY + barH + 4, Color.White * 0.6f * timeAlpha);
            }
        }
        
        // === Menu items (original: AtlasText BOLD, letter.width*=2, letter.height*=2) ===
        // Original: y = 70*i + 30, x starts at 0
        // AtlasFont.BOLD base size is ~42px, doubled = ~84px visual
        int menuItemSpacing = 120;
        int menuStartY = 180;
        // Keep the selected item anchored near menuStartY by shifting all rows by the smoothed selection offset.
        float scrollOffsetY = -menuItemSpacing * _pauseScrollLerp;
        var alphabetFont = AlphabetFont.Bold;
        if (alphabetFont != null)
        {
            for (int i = 0; i < _pauseItems.Length; i++)
            {
                bool selected = i == _pauseSelection;
                float alpha = (selected ? 1.0f : 0.6f) * _pauseMenuAlpha;
                float yPos = menuStartY + menuItemSpacing * i + scrollOffsetY;
                if (yPos < -menuItemSpacing || yPos > FNFGame.SCREEN_HEIGHT) continue;
                float abScale = 0.85f;
                alphabetFont.DrawString(spriteBatch, _pauseItems[i].ToUpper(),
                    new Vector2(20, yPos), Color.White * alpha, abScale);
            }
        }
        else
        {
            var menuFont = Assets.GetFont(82);
            if (menuFont != null)
            {
                for (int i = 0; i < _pauseItems.Length; i++)
                {
                    bool selected = i == _pauseSelection;
                    float alpha = (selected ? 1.0f : 0.6f) * _pauseMenuAlpha;
                    float yPos = menuStartY + menuItemSpacing * i + scrollOffsetY;
                    if (yPos < -menuItemSpacing || yPos > FNFGame.SCREEN_HEIGHT) continue;
                    menuFont.DrawText(spriteBatch, _pauseItems[i].ToUpper(),
                        new Vector2(20, yPos), Color.White * alpha);
                }
            }
        }
    }
    
    /// <summary>
    /// Get fade-in alpha for a metadata line with staggered delay (original: 1.8s quartOut per line).
    /// </summary>
    private float GetMetadataLineAlpha(float delay)
    {
        float t = Math.Clamp((_pauseTimer - delay) / 1.8f, 0f, 1f);
        return 1f - MathF.Pow(1f - t, 4f); // quartOut easing
    }
    
    /// <summary>
    /// Draw text right-aligned at the given X position.
    /// </summary>
    private void DrawRightAligned(SpriteBatch spriteBatch, FontStashSharp.SpriteFontBase font, 
        string text, int rightX, int y, Color color)
    {
        var size = font.MeasureString(text);
        font.DrawText(spriteBatch, text, new Vector2(rightX - size.X, y), color);
    }
    
    private void DrawGameOverOverlay(SpriteBatch spriteBatch)
    {
        // Original GameOverSubState: black BG (opaque), BF death animation visible on top
        // The stage is NOT visible � it's a solid black background with BF's death sprite
        float bgAlpha = Math.Clamp(_gameOverTimer * 2f, 0f, 1f);
        spriteBatch.Draw(Assets.Pixel, 
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), 
            Color.Black * bgAlpha);
        
        // Draw BF death animation (original: plays firstDeath -> deathLoop -> deathConfirm)
        if (bgAlpha > 0.5f && _boyfriend != null)
        {
            // Play firstDeath on first frame if not already set
            if (_gameOverDeathAnim == "firstDeath" && _gameOverTimer < 0.1f)
            {
                _boyfriend.PlayAnimation("firstDeath");
            }
            
            // Center BF on screen for game over
            float goX = FNFGame.SCREEN_WIDTH / 2f;
            float goY = FNFGame.SCREEN_HEIGHT / 2f;
            _boyfriend.Draw(spriteBatch, Assets, goX, goY);
        }
        
        // "RETRY" / "BACK" hint text (original: appears after 1s)
        if (_gameOverTimer >= 1.0f && !_gameOverConfirmed)
        {
            var hintFont = Assets.GetFont(16);
            if (hintFont != null)
            {
                string hint = "ENTER: Retry   ESC: Exit";
                var hSize = hintFont.MeasureString(hint);
                hintFont.DrawText(spriteBatch, hint,
                    new Vector2((FNFGame.SCREEN_WIDTH - hSize.X) / 2, FNFGame.SCREEN_HEIGHT - 40),
                    Color.White * 0.5f);
            }
        }
        
        // Fade to black on confirm (original: camera.fade(BLACK, 1, true) -> needsReset)
        if (_gameOverConfirmed)
        {
            float fadeAlpha = Math.Clamp(_gameOverFadeTimer / 1.0f, 0f, 1f);
            spriteBatch.Draw(Assets.Pixel, 
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), 
                Color.Black * fadeAlpha);
        }
    }
    
    private void DrawText(SpriteBatch spriteBatch, string text, Vector2 pos, Color color, int fontSize = 24)
    {
        var font = Assets.GetFont(fontSize);
        if (font != null)
        {
            font.DrawText(spriteBatch, text, pos, color);
        }
    }
}

/// <summary>
/// Stage JSON data model � matches Content/data/stages/*.json format.
/// </summary>
public class StageJsonData
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("directory")] public string Directory { get; set; }
    [JsonProperty("cameraZoom")] public float CameraZoom { get; set; } = 1.1f;
    [JsonProperty("version")] public string Version { get; set; }
    [JsonProperty("props")] public List<StageProp> Props { get; set; }
    [JsonProperty("characters")] public StageCharacters Characters { get; set; }
}

public class StageProp
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("assetPath")] public string AssetPath { get; set; }
    [JsonProperty("alpha")] public float Alpha { get; set; } = 1f;
    [JsonProperty("position")] public float[] Position { get; set; }
    [JsonProperty("scale")] public float[] Scale { get; set; }
    [JsonProperty("scroll")] public float[] Scroll { get; set; }
    [JsonProperty("zIndex")] public float ZIndex { get; set; }
    [JsonProperty("animType")] public string AnimType { get; set; }
    [JsonProperty("isPixel")] public bool IsPixel { get; set; }
    [JsonProperty("danceEvery")] public float DanceEvery { get; set; }
    [JsonProperty("startingAnimation")] public string StartingAnimation { get; set; }
    [JsonProperty("animations")] public List<StagePropAnim> Animations { get; set; }
        [JsonProperty("blend")] public string Blend { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
}

public class StagePropAnim
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("prefix")] public string Prefix { get; set; }
    [JsonProperty("frameRate")] public float FrameRate { get; set; } = 24;
    [JsonProperty("looped")] public bool Looped { get; set; } = true;
    [JsonProperty("flipX")] public bool FlipX { get; set; }
    [JsonProperty("flipY")] public bool FlipY { get; set; }
    [JsonProperty("offsets")] public float[] Offsets { get; set; }
    [JsonProperty("frameIndices")] public int[] FrameIndices { get; set; }
}

public class StageCharacters
{
    [JsonProperty("bf")] public StageCharPos Bf { get; set; }
    [JsonProperty("dad")] public StageCharPos Dad { get; set; }
    [JsonProperty("gf")] public StageCharPos Gf { get; set; }
}

public class StageCharPos
{
    [JsonProperty("position")] public float[] Position { get; set; }
    [JsonProperty("zIndex")] public int ZIndex { get; set; }
    [JsonProperty("cameraOffsets")] public float[] CameraOffsets { get; set; }
}

// Dialogue data classes (M3)
public class DialogueLine
{
    public string Speaker { get; set; }
    public string Text { get; set; }
}

public class ConversationJson
{
    [JsonProperty("dialogue")] public List<ConversationEntry> Dialogue { get; set; }
    [JsonProperty("music")] public ConversationMusic Music { get; set; }
}

public class ConversationEntry
{
    [JsonProperty("speaker")] public string Speaker { get; set; }
    [JsonProperty("text")] public List<string> Text { get; set; }
}

public class ConversationMusic
{
    [JsonProperty("asset")] public string Asset { get; set; }
    [JsonProperty("looped")] public bool Looped { get; set; }
}
