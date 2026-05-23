using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using System.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Scoring rank enum matching original FNF ScoringRank from Scoring.hx.
/// Rank thresholds from Constants.hx:
///   PERFECT_GOLD = all sicks (tallies.sick == tallies.totalNotes)
///   PERFECT      = completion == 1.0
///   EXCELLENT    = completion >= 0.90
///   GREAT        = completion >= 0.80
///   GOOD         = completion >= 0.60
///   SHIT         = below 0.60
/// </summary>
public enum ScoringRank
{
    PERFECT_GOLD,
    PERFECT,
    EXCELLENT,
    GREAT,
    GOOD,
    SHIT
}

/// <summary>
/// Results screen — faithful port of the original FNF ResultState.hx.
///
/// Sequence (all timings match original at 24fps frame references):
///   - Gradient BG (0xFFFECC5C ? 0xFFFDC05C, 90°) + bgFlash overlay
///   - Black top bar slides down at 3/24s over 7/24s (quartOut)
///   - "RESULTS" sparrow anim at 6/24s (right side, x = width - 1480, y = -10)
///   - Sound system sparrow anim at 8/24s (left side, x = -15, y = -180)
///   - Ratings popin sparrow at 21/24s (x = -135, y = 135)
///   - Score popin sparrow at 36/24s (x = -180, y = 515)
///   - Score digits animate + tally counters start at 37/24s
///     Each tally counter: visible at (0.3*index)+1.20s, tweens curNumber?target over 0.5s (quartOut)
///   - Music starts at rank.getMusicDelay() (intro + loop)
///   - Clear percent counter starts at 37/24s, tweens over 58/24s (quartOut)
///     Starts from max(0, target-36), plays scrollMenu tick per increment
///     On complete: confirmMenu sound, flash white 0.4s, then fade alpha over 0.5s
///   - Character animations at rank.getBFDelay()
///   - Rank text backdrop (FlxBackdrop) at rank.getFlashDelay()
///     12 horizontal rows (alternating velocity ±7) + 1 vertical column (velocity -80)
///     bgFlash alpha=1 ? fade over 14/24s
///   - Small clear percent + difficulty + song name scroll in top bar after rank sequence
///   - Highscore "NEW" at rank.getHighscoreDelay()
///   - PAUSE/ACCEPT ? transition to FreeplayState
/// </summary>
public class ResultsScene : Scene
{
    // === Score data (matches ResultsStateParams.scoreData) ===
    private readonly int _score;
    private readonly int _maxCombo;
    private readonly int _misses;
    private readonly string _songName;
    private readonly string _difficultyId;
    private readonly bool _isNewHighscore;
    private readonly int _tallySick;
    private readonly int _tallyGood;
    private readonly int _tallyBad;
    private readonly int _tallyShit;
    private readonly int _tallyMissed;
    private readonly int _totalNotesHit;
    private readonly int _totalNotes;

    // === Computed ===
    private ScoringRank _rank;
    private int _clearPercentTarget;

    // === Master timer ===
    private float _timer;

    // === Spritesheet assets loaded from Content/resultScreen/ ===
    private SpriteSheet _tallieNumberSheet;         // tallieNumber.png — tally counter digits
    private SpriteSheet _scoreDigitalSheet;         // score-digital-numbers.png — score digit anims
    private SpriteSheet _clearPercentLeftSheet;     // clearPercent/clearPercentNumberLeft.png
    private SpriteSheet _clearPercentRightSheet;    // clearPercent/clearPercentNumberRight.png
    private SpriteSheet _clearPercentSmallSheet;    // clearPercent/clearPercentNumberSmall.png
    private Texture2D _clearPercentTextTex;         // clearPercent/clearPercentText.png
    private Texture2D _clearPercentTextSmallTex;    // clearPercent/clearPercentTextSmall.png
    private Texture2D _rankHorTex;                  // rankText/rankScroll{RANK}.png
    private Texture2D _rankVerTex;                  // rankText/rankText{RANK}.png
    private Texture2D _topBarBlackTex;              // topBarBlack.png

    // === Gradient BG (cached texture) ===
    private Texture2D _gradientBG;
    private Texture2D _bgFlashTex;

    // === BG flash (original: FlxGradient 0xFFFFF1A6 ? 0xFFFFF1BE) ===
    private bool _bgFlashVisible;
    private float _bgFlashAlpha;

    // === Black top bar (original: BitmapUtil.createResultsBar, -3.8° rotation) ===
    //     height = ceil(width / 8.7) ? 148 for 1280 width
    //     slides from y = -height to y = 0, over 7/24s with 3/24s delay, quartOut
    private int _topBarHeight;
    private float _topBarY;

    // === "RESULTS" sparrow anim (x = width - 1480, y = -10, at 6/24s) ===
    private AnimatedSprite _resultsAnim;
    private bool _resultsVisible;

    // === Sound system sparrow anim (x = -15, y = -180, at 8/24s) ===
    private AnimatedSprite _soundSystemAnim;
    private bool _soundSystemVisible;

    // === Ratings popin sparrow (x = -135, y = 135, at 21/24s) ===
    private AnimatedSprite _ratingsPopin;
    private bool _ratingsVisible;

    // === Score popin sparrow (x = -180, y = 515, at 36/24s) ===
    private AnimatedSprite _scorePopin;
    private bool _scoreVisible;

    // === Tally counters (7 rows, staggered reveal) ===
    //     Each visible at (0.3*index)+1.20s, tween over 0.5s quartOut
    private struct TallyEntry
    {
        public string Label;
        public int TargetValue;
        public Color ValueColor;
        public int X, Y;
        public float CurrentValue;
        public bool Visible;
        public bool TweenStarted;
        public float TweenTimer;
    }
    private TallyEntry[] _tallies;

    // === Score digits (original: ResultScore at x=35, y=305, 10 digits) ===
    //     Shuffle animation: each digit shuffles for 41/24s then tweens to final over 23/24s
    private bool _scoreDigitsVisible;
    private float _scoreDigitTimer;
    private int[] _scoreTargetDigits;
    private float[] _scoreCurrentDigits;
    private bool[] _scoreDigitDone;
    private int _scoreDigitCount;
    private float[] _scoreDigitShuffleTimers;
    private bool[] _scoreDigitShuffling;

    // === Clear percent counter (center, starts at 37/24s) ===
    //     Position: (width/2 + 190, height/2 - 70)
    //     Tweens from max(0, target-36) ? target over 58/24s, quartOut
    private bool _clearPercentActive;
    private float _clearPercentTimer;
    private float _clearPercentCurrent;
    private bool _clearPercentDone;
    private float _clearPercentFlashTimer;
    private bool _clearPercentFlashing;
    private float _clearPercentFadeTimer;
    private bool _clearPercentFading;
    private float _clearPercentAlpha;

    // === Rank text backdrop (at getFlashDelay) ===
    //     12 horizontal FlxBackdrop rows, velocity x = ±7
    //     1 vertical FlxBackdrop column, velocity y = -80 (after 30/24s)
    private bool _rankTextVisible;
    private float _rankTextTimer;
    private float[] _rankRowOffsets;
    private float _rankVertOffset;
    private bool _rankVertScrolling;
    private float _rankVertScrollDelay;
    private float _rankVertFlickerTimer;  // FlxFlicker: 2/24*3 = 0.25s total, 2/24 period
    private bool _rankVertFlickerDone;

    // === BG flash fade duration (5/24 for tally start, 14/24 for rank display) ===
    private float _bgFlashFadeDuration;

    // === Small clear percent (shows in top bar after afterRankTallySequence) ===
    private bool _smallClearPercentVisible;
    private bool _smallClearPercentFlashing;
    private float _smallClearPercentFlashTimer;
    private float _smallClearPercentX;
    private float _smallClearPercentY;

    // === afterRankTallySequence at getBFDelay() ===
    private bool _afterRankSequenceStarted;
    private float _afterRankTimer;

    // === Song name + difficulty + clearPercentSmall scrolling in top bar ===
    //     Original: timerThenSongName(1.0, false) on create
    //     Song name at angle -4.4°, scrolls with speedOfTween
    //     After rank sequence: timerThenSongName(3.0, true) with auto-scroll
    private float _songNameX;
    private float _songNameY;
    private float _difficultyX;
    private float _difficultyY;
    private bool _songNameMoving;
    private float _songNameMoveDelay;
    private bool _songNameTweenedIn;
    private float _songNameTweenTimer;
    private bool _songNameAutoScroll; // true after first scroll cycle (timerThenSongName autoScroll param)
    private float _songNameScrollStartDelay; // delay within timerThenSongName before starting scroll
    private Texture2D _songNameFontTex;  // tardlingSpritesheet.png bitmap font
    private const float SONG_NAME_ANGLE = -4.4f; // degrees
    private const int FONT_CHAR_W = 49;
    private const int FONT_CHAR_H = 61;
    private const int FONT_LETTER_SPACING = -15;
    private static readonly string FONT_LETTERS = "AaBbCcDdEeFfGgHhiIJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz:1234567890().-";

    // === Speed of tween (original: starts at 0,0 ramps to target over 0.7s quadIn) ===
    private float _speedOfTweenTargetX;
    private float _speedOfTweenTargetY;
    private float _speedOfTweenCurrentX;
    private float _speedOfTweenCurrentY;
    private bool _speedTweenActive;
    private float _speedTweenTimer;
    private bool _speedTweenStartedForCurrentCycle;

    // === Highscore NEW (at getHighscoreDelay) ===
    private AnimatedSprite _highscoreNewAnim;
    private bool _highscoreNewVisible;

    // === Character result animation (BF) ===
    //     Original: loaded from playable character JSON (renderType=animateatlas)
    //     Positioned at offsets, loopFrame, zIndex
    private AnimatedSprite _characterResultAnim;
    private bool _characterResultVisible;
    private int _characterResultLoopFrame;

    // === Difficulty texture ref (for width calculation) ===
    private Texture2D _difficultyTex;

    // === Score digit glow state (after tween, glow fires immediately) ===
    private bool[] _scoreDigitGlow; // true = show glow frame (0001) instead of settled (0005)
    private bool[] _scoreDigitFirstAppear; // true = first digit display uses glow frame

    // === Score digit tween state (after shuffle, tween 0?finalDigit over 23/24s quadOut) ===
    private bool[] _scoreDigitTweening;
    private float[] _scoreDigitTweenTimers;

    // === Music volume fade on exit ===
    private bool _musicFading;
    private float _musicFadeTimer;

    // === Music delay ===
    private bool _musicStarted;

    // === Rank BG overlay (solid black, zIndex 99999, alpha 0 normally, used for exit) ===
    private float _rankBgAlpha;

    // === Exit transition ===
    private bool _exiting;
    private float _exitTimer;

    // === Input busy flag ===
    private bool _busy;

    // === Story mode flag (determines exit target: StoryModeScene vs FreeplayScene) ===
    private readonly bool _isStoryMode;

    // Fallback constructor
    public ResultsScene(int score, int maxCombo, int misses, string songName = "")
        : this(score, maxCombo, misses, songName, "normal", false, 0, 0, 0, 0, misses, 0, 0) { }

    public ResultsScene(int score, int maxCombo, int misses, string songName,
        int tallySick, int tallyGood, int tallyBad, int tallyShit,
        int tallyMissed, int totalNotesHit, int totalNotes)
        : this(score, maxCombo, misses, songName, "normal", false,
              tallySick, tallyGood, tallyBad, tallyShit, tallyMissed, totalNotesHit, totalNotes) { }

    public ResultsScene(int score, int maxCombo, int misses, string songName,
        string difficultyId, bool isNewHighscore,
        int tallySick, int tallyGood, int tallyBad, int tallyShit,
        int tallyMissed, int totalNotesHit, int totalNotes,
        bool isStoryMode = false)
    {
        _score = score;
        _maxCombo = maxCombo;
        _misses = misses;
        _songName = songName;
        _difficultyId = difficultyId ?? "normal";
        _isNewHighscore = isNewHighscore;
        _tallySick = tallySick;
        _tallyGood = tallyGood;
        _tallyBad = tallyBad;
        _tallyShit = tallyShit;
        _tallyMissed = tallyMissed;
        _totalNotesHit = totalNotesHit;
        _totalNotes = totalNotes;
        _isStoryMode = isStoryMode;
    }

    public override void Load()
    {
        // Calculate rank (original: Scoring.calculateRank)
        _rank = CalculateRank();

        // Clear percent (original: Scoring.tallyCompletion * 100, floored)
        float clearFloat = _totalNotes == 0 ? 0f
            : Math.Clamp(_totalNotesHit / (float)_totalNotes, 0f, 1f) * 100f;
        _clearPercentTarget = (int)Math.Floor(clearFloat);
        _clearPercentCurrent = Math.Max(0, _clearPercentTarget - 36);
        _clearPercentAlpha = 1f;

        // === Top bar ===
        // Original: width = ceil(FlxG.width * 1.011), height = ceil(width / 8.7)
        int topBarWidth = (int)Math.Ceiling(FNFGame.SCREEN_WIDTH * 1.011f);
        _topBarHeight = (int)Math.Ceiling(topBarWidth / 8.7f); // ~149 for 1280 width
        _topBarY = -_topBarHeight;

        // === Load spritesheet animations ===
        var resultsSheet = SpriteSheet.Load(Game, "resultScreen/results");
        if (resultsSheet != null)
        {
            _resultsAnim = new AnimatedSprite { Sheet = resultsSheet };
            _resultsAnim.Position = new Vector2(FNFGame.SCREEN_WIDTH - 1480, -10);
            _resultsAnim.PlayAnimation("results instance 1", loop: false);
        }

        var soundSystemSheet = SpriteSheet.Load(Game, "resultScreen/soundSystem");
        if (soundSystemSheet != null)
        {
            _soundSystemAnim = new AnimatedSprite { Sheet = soundSystemSheet };
            _soundSystemAnim.Position = new Vector2(-15, -180);
            _soundSystemAnim.PlayAnimation("sound system", loop: false);
        }

        var ratingsSheet = SpriteSheet.Load(Game, "resultScreen/ratingsPopin");
        if (ratingsSheet != null)
        {
            _ratingsPopin = new AnimatedSprite { Sheet = ratingsSheet };
            _ratingsPopin.Position = new Vector2(-135, 135);
            _ratingsPopin.PlayAnimation("Categories", loop: false);
        }

        var scoreSheet = SpriteSheet.Load(Game, "resultScreen/scorePopin");
        if (scoreSheet != null)
        {
            _scorePopin = new AnimatedSprite { Sheet = scoreSheet };
            _scorePopin.Position = new Vector2(-180, 515);
            _scorePopin.PlayAnimation("tally score", loop: false);
        }

        var highscoreSheet = SpriteSheet.Load(Game, "resultScreen/highscoreNew");
        if (highscoreSheet != null)
        {
            _highscoreNewAnim = new AnimatedSprite { Sheet = highscoreSheet };
            _highscoreNewAnim.Position = new Vector2(44, 557);
            // Note: setGraphicSize(width * 0.8) is commented out in original source
            _highscoreNewAnim.PlayAnimation("highscoreAnim0", loop: false);
            // Original: onFinish -> play("new", true, false, 16) — loop from frame 16
            _highscoreNewAnim.OnFinish = () =>
            {
                _highscoreNewAnim.PlayAnimationFromFrame("highscoreAnim0", 16, loop: false);
            };
        }

        // === Load number spritesheets ===
        _tallieNumberSheet = SpriteSheet.Load(Game, "resultScreen/tallieNumber");
        _scoreDigitalSheet = SpriteSheet.Load(Game, "resultScreen/score-digital-numbers");
        _clearPercentLeftSheet = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberLeft");
        _clearPercentRightSheet = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberRight");
        _clearPercentSmallSheet = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberSmall");
        _clearPercentTextTex = Assets.LoadTexture("resultScreen/clearPercent/clearPercentText");
        _clearPercentTextSmallTex = Assets.LoadTexture("resultScreen/clearPercent/clearPercentTextSmall");

        // === Load rank text backdrop textures ===
        _rankHorTex = Assets.LoadTexture(GetHorTextAsset());
        _rankVerTex = Assets.LoadTexture(GetVerTextAsset());

        // === Load top bar texture ===
        _topBarBlackTex = Assets.LoadTexture("resultScreen/topBarBlack");

        // === Load song name bitmap font ===
        _songNameFontTex = Assets.LoadTexture("resultScreen/tardlingSpritesheet");

        // === Load difficulty texture and store reference ===
        _difficultyTex = Assets.LoadTexture($"resultScreen/diff_{_difficultyId}");
        if (_difficultyTex == Assets.Pixel) _difficultyTex = null;

        // === Load character result animation (BF) ===
        LoadCharacterResultAnimation();

        // === Build tally counter entries (matches original positions exactly) ===
        // Original: hStuf starts at 50 for totalHit/maxCombo, then hStuf += 2 twice = 54 for judgements
        int hitComboX = 375;
        // Original: if totalNotesHit >= 1000, shift totalHit and maxCombo left by 30
        if (_totalNotesHit >= 1000) hitComboX -= 30;
        int extraYOffset = 7;
        _tallies = new TallyEntry[]
        {
            new() { Label = "TOTAL NOTES HIT", TargetValue = _totalNotesHit, ValueColor = Color.White,
                     X = hitComboX, Y = 50 * 3 },   // hStuf=50
            new() { Label = "MAX COMBO",       TargetValue = _maxCombo,       ValueColor = Color.White,
                     X = hitComboX, Y = 50 * 4 },   // hStuf=50
            new() { Label = "SICK",   TargetValue = _tallySick,   ValueColor = new Color(0x89, 0xE5, 0x9E),
                     X = 230, Y = (54 * 5) + extraYOffset },   // hStuf=54
            new() { Label = "GOOD",   TargetValue = _tallyGood,   ValueColor = new Color(0x89, 0xC9, 0xE5),
                     X = 210, Y = (54 * 6) + extraYOffset },
            new() { Label = "BAD",    TargetValue = _tallyBad,    ValueColor = new Color(0xE6, 0xCF, 0x8A),
                     X = 190, Y = (54 * 7) + extraYOffset },
            new() { Label = "SHIT",   TargetValue = _tallyShit,   ValueColor = new Color(0xE6, 0x8C, 0x8A),
                     X = 220, Y = (54 * 8) + extraYOffset },
            new() { Label = "MISSED", TargetValue = _tallyMissed, ValueColor = new Color(0xC6, 0x8A, 0xE6),
                     X = 260, Y = (54 * 9) + extraYOffset },
        };

        // === Score digits (10 digits like original ResultScore) ===
        _scoreDigitCount = 10;
        _scoreTargetDigits = new int[_scoreDigitCount];
        _scoreCurrentDigits = new float[_scoreDigitCount];
        _scoreDigitDone = new bool[_scoreDigitCount];
        _scoreDigitShuffleTimers = new float[_scoreDigitCount];
        _scoreDigitShuffling = new bool[_scoreDigitCount];
        _scoreDigitTweening = new bool[_scoreDigitCount];
        _scoreDigitTweenTimers = new float[_scoreDigitCount];
        _scoreDigitGlow = new bool[_scoreDigitCount];
        _scoreDigitFirstAppear = new bool[_scoreDigitCount];
        for (int i = 0; i < _scoreDigitCount; i++)
            _scoreDigitFirstAppear[i] = true;

        // Break score into digits (right-aligned, leading zeros become 10 = "disabled")
        string scoreStr = _score.ToString().PadLeft(_scoreDigitCount, ' ');
        for (int i = 0; i < _scoreDigitCount; i++)
        {
            if (scoreStr[i] == ' ')
                _scoreTargetDigits[i] = 10; // DISABLED/GONE
            else
                _scoreTargetDigits[i] = scoreStr[i] - '0';
        }

        // === Rank backdrop ===
        _rankRowOffsets = new float[12];

        // === Song name positioning ===
        // Original: difficulty at x=555, clearPercentSmall beside it, songName beside that
        // All start at y = -height (offscreen), tween down during timerThenSongName
        _difficultyX = 555;
        _difficultyY = -50;
        int initDiffW = _difficultyTex != null ? _difficultyTex.Width : 80;
        _smallClearPercentX = _difficultyX + initDiffW + 60;
        _smallClearPercentY = -30;
        _songNameX = _smallClearPercentX + 94; // original: songName.x = clearPercentSmall.x + 94
        _songNameY = -50;
        _songNameMoveDelay = 0f; // timerThenSongName(1.0, false) called on create — tweens start immediately
        _songNameAutoScroll = false; // first call: no auto-scroll
        _songNameScrollStartDelay = 1.0f; // timerLength=1.0 for speed ramp delay

        // Compute speed direction from song name angle (original: speedOfTween)
        float angleRad = SONG_NAME_ANGLE * MathF.PI / 180f;
        _speedOfTweenTargetX = -1.0f * MathF.Cos(angleRad);
        _speedOfTweenTargetY = -1.0f * MathF.Sin(angleRad);

        // === Preload sounds ===
        Audio.PreloadSound("scrollMenu");
        Audio.PreloadSound("confirmMenu");

        // === Stop any existing music ===
        Audio.StopMusic();
    }

    private void LoadCharacterResultAnimation()
    {
        // Load result animations based on rank and selected character
        string selectedChar = Engine.HighscoreManager.Data.SelectedCharacter ?? "bf";
        string charFolder = selectedChar == "pico" ? "results-pico" : "results-bf";
        
        string animPath = _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => $"resultScreen/{charFolder}/resultsPERFECT/bed",
            ScoringRank.EXCELLENT => $"resultScreen/{charFolder}/resultsEXCELLENT",
            ScoringRank.GREAT => $"resultScreen/{charFolder}/resultsGREAT/bf",
            ScoringRank.GOOD => $"resultScreen/{charFolder}/resultsGOOD/bf",
            ScoringRank.SHIT => $"resultScreen/{charFolder}/resultsSHIT",
            _ => null
        };
        
        // Try character-specific folder first, fall back to bf
        if (animPath != null && selectedChar == "pico")
        {
            bool picoExists = false;
            string resolved = Assets.ResolveDirectory(animPath);
            if (resolved != null)
                picoExists = File.Exists(Path.Combine(resolved, "spritemap1.png"));
            if (!picoExists)
                picoExists = Assets.ResolvePath(animPath + ".png") != null;
            if (!picoExists)
            {
                charFolder = "results-bf";
                animPath = _rank switch
                {
                    ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => $"resultScreen/{charFolder}/resultsPERFECT/bed",
                    ScoringRank.EXCELLENT => $"resultScreen/{charFolder}/resultsEXCELLENT",
                    ScoringRank.GREAT => $"resultScreen/{charFolder}/resultsGREAT/bf",
                    ScoringRank.GOOD => $"resultScreen/{charFolder}/resultsGOOD/bf",
                    ScoringRank.SHIT => $"resultScreen/{charFolder}/resultsSHIT",
                    _ => null
                };
            }
        }

        // Original offsets from bf.json player data
        Vector2 offset = _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => new Vector2(403, -305),
            ScoringRank.EXCELLENT => new Vector2(560.85f, -410.35f),
            ScoringRank.GREAT => new Vector2(655.3f, -247.95f),
            ScoringRank.GOOD => new Vector2(645.4f, -214.8f),
            ScoringRank.SHIT => new Vector2(570.5f, -390.5f),
            _ => Vector2.Zero
        };

        _characterResultLoopFrame = _rank switch
        {
            // Frame indices from Animation.json label data:
            // PERFECT: "LOOP START" at frame 137
            // EXCELLENT: loopFrame 29 from bf.json
            // GREAT: loopFrame 15 from bf.json
            // GOOD: loopFrame 14 from bf.json
            // SHIT: "Loop Start" at frame 149
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 137,
            ScoringRank.EXCELLENT => 29,
            ScoringRank.GREAT => 15,
            ScoringRank.GOOD => 14,
            ScoringRank.SHIT => 149,
            _ => 0
        };

        if (animPath == null) return;

        var sheet = SpriteSheet.Load(Game, animPath, preRenderComposites: true);
        if (sheet == null) return;

        _characterResultAnim = new AnimatedSprite
        {
            Sheet = sheet,
            Position = offset
        };

        float scale = _rank == ScoringRank.GREAT ? 0.93f : 1.0f;
        _characterResultAnim.Scale = new Vector2(scale, scale);

        // Find and play the first animation available
        string firstAnim = sheet.Animations.Keys.FirstOrDefault();
        if (firstAnim != null)
        {
            // Original: plays once (non-looping), then on finish, replays from loopFrame
            // This creates an infinite loop from loopFrame via OnFinish callback
            _characterResultAnim.PlayAnimation(firstAnim, loop: false);

            if (_characterResultLoopFrame > 0)
            {
                _characterResultAnim.OnFinish = () =>
                {
                    _characterResultAnim.PlayAnimationFromFrame(firstAnim, _characterResultLoopFrame, loop: false);
                };
            }
        }

        // Load GF for GREAT rank (original: separate sprite at zIndex 499, delay 0.25)
        if (_rank == ScoringRank.GREAT)
        {
            var gfSheet = SpriteSheet.Load(Game, $"resultScreen/{charFolder}/resultsGREAT/gf", preRenderComposites: true);
            if (gfSheet != null)
            {
                _gfResultAnim = new AnimatedSprite
                {
                    Sheet = gfSheet,
                    Position = new Vector2(563.364f, -123.186f),
                    Scale = new Vector2(0.93f, 0.93f)
                };
                string gfAnim = gfSheet.Animations.Keys.FirstOrDefault();
                if (gfAnim != null)
                {
                    _gfResultAnim.PlayAnimation(gfAnim, loop: false);
                    _gfResultAnim.OnFinish = () =>
                    {
                        _gfResultAnim.PlayAnimationFromFrame(gfAnim, 9, loop: false);
                    };
                }
            }
        }
        // Load GF for GOOD rank (sparrow spritesheet, delay 0.91, loopFrame 9)
        else if (_rank == ScoringRank.GOOD)
        {
            var gfSheet = SpriteSheet.Load(Game, $"resultScreen/{charFolder}/resultsGOOD/resultGirlfriendGOOD", preRenderComposites: true);
            if (gfSheet != null)
            {
                _gfResultAnim = new AnimatedSprite
                {
                    Sheet = gfSheet,
                    Position = new Vector2(629, 323)
                };
                string gfAnim = gfSheet.Animations.Keys.FirstOrDefault();
                if (gfAnim != null)
                {
                    _gfResultAnim.PlayAnimation(gfAnim, loop: false);
                    _gfResultAnim.OnFinish = () =>
                    {
                        _gfResultAnim.PlayAnimationFromFrame(gfAnim, 9, loop: false);
                    };
                }
            }
        }

        // Load hearts for PERFECT rank (delay 4.41, loopFrame 43)
        if (_rank == ScoringRank.PERFECT_GOLD || _rank == ScoringRank.PERFECT)
        {
            var heartsSheet = SpriteSheet.Load(Game, $"resultScreen/{charFolder}/resultsPERFECT/hearts", preRenderComposites: true);
            if (heartsSheet != null)
            {
                _heartsResultAnim = new AnimatedSprite
                {
                    Sheet = heartsSheet,
                    Position = new Vector2(630, 300)
                };
                string heartsAnim = heartsSheet.Animations.Keys.FirstOrDefault();
                if (heartsAnim != null)
                {
                    _heartsResultAnim.PlayAnimation(heartsAnim, loop: false);
                    _heartsResultAnim.OnFinish = () =>
                    {
                        _heartsResultAnim.PlayAnimationFromFrame(heartsAnim, 43, loop: false);
                    };
                }
            }
        }
    }

    // GF / secondary character animation
    private AnimatedSprite _gfResultAnim;
    private bool _gfResultVisible;

    // Hearts animation for PERFECT rank (delay 4.41, loopFrame 43)
    private AnimatedSprite _heartsResultAnim;
    private bool _heartsResultVisible;

    public override void Unload()
    {
        // Restore music volume in case it was faded during exit
        Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
        _gradientBG?.Dispose();
        _bgFlashTex?.Dispose();
        
        // Dispose all spritesheets (GPU resources)
        _resultsAnim?.Sheet?.Dispose();
        _soundSystemAnim?.Sheet?.Dispose();
        _ratingsPopin?.Sheet?.Dispose();
        _scorePopin?.Sheet?.Dispose();
        _highscoreNewAnim?.Sheet?.Dispose();
        _characterResultAnim?.Sheet?.Dispose();
        _gfResultAnim?.Sheet?.Dispose();
        _heartsResultAnim?.Sheet?.Dispose();
        _tallieNumberSheet?.Dispose();
        _scoreDigitalSheet?.Dispose();
        _clearPercentLeftSheet?.Dispose();
        _clearPercentRightSheet?.Dispose();
        _clearPercentSmallSheet?.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _timer += dt;

        // === BG flash fade (rate depends on context: 5/24s for tally start, 14/24s for rank display) ===
        if (_bgFlashVisible && _bgFlashAlpha > 0)
        {
            float fadeRate = _bgFlashFadeDuration > 0 ? 1f / _bgFlashFadeDuration : (24f / 5f);
            _bgFlashAlpha = Math.Max(0, _bgFlashAlpha - dt * fadeRate);
            if (_bgFlashAlpha <= 0) _bgFlashVisible = false;
        }

        // === Top bar slide (3/24s delay, then slide over 7/24s, quartOut) ===
        if (_timer >= 3f / 24f)
        {
            float slideElapsed = _timer - (3f / 24f);
            float slideDuration = 7f / 24f;
            float t = Math.Clamp(slideElapsed / slideDuration, 0f, 1f);
            t = QuartOut(t);
            _topBarY = -_topBarHeight + _topBarHeight * t;
        }

        // === "RESULTS" anim at 6/24s ===
        if (!_resultsVisible && _timer >= 6f / 24f)
        {
            _resultsVisible = true;
        }
        if (_resultsVisible && _resultsAnim != null)
            _resultsAnim.Update(dt);

        // === Sound system at 8/24s ===
        if (!_soundSystemVisible && _timer >= 8f / 24f)
            _soundSystemVisible = true;
        if (_soundSystemVisible && _soundSystemAnim != null)
            _soundSystemAnim.Update(dt);

        // === Ratings popin at 21/24s ===
        if (!_ratingsVisible && _timer >= 21f / 24f)
            _ratingsVisible = true;
        if (_ratingsVisible && _ratingsPopin != null)
            _ratingsPopin.Update(dt);

        // === Score popin at 36/24s ===
        if (!_scoreVisible && _timer >= 36f / 24f)
            _scoreVisible = true;
        if (_scoreVisible && _scorePopin != null)
            _scorePopin.Update(dt);

        // === Score digits + tally counters start at 37/24s ===
        if (_timer >= 37f / 24f)
        {
            // Show score digits
            if (!_scoreDigitsVisible)
            {
                _scoreDigitsVisible = true;
                _scoreDigitTimer = 0;
            }

            // Start clear percent tally sequence (startRankTallySequence)
            if (!_clearPercentActive)
            {
                _clearPercentActive = true;
                _clearPercentTimer = 0;
                // BG flash on start (original: bgFlash.visible = true, tween alpha?0 over 5/24)
                _bgFlashVisible = true;
                _bgFlashAlpha = 1f;
                _bgFlashFadeDuration = 5f / 24f;
            }
        }

        // === Tally counters: each visible at (0.3*index)+1.20s ===
        if (_tallies != null)
        {
            for (int i = 0; i < _tallies.Length; i++)
            {
                float revealTime = (0.3f * i) + 1.20f;
                if (!_tallies[i].Visible && _timer >= revealTime)
                {
                    _tallies[i].Visible = true;
                    _tallies[i].TweenStarted = true;
                    _tallies[i].TweenTimer = 0;
                }
                if (_tallies[i].TweenStarted)
                {
                    _tallies[i].TweenTimer += dt;
                    float tt = Math.Clamp(_tallies[i].TweenTimer / 0.5f, 0f, 1f);
                    tt = QuartOut(tt);
                    _tallies[i].CurrentValue = _tallies[i].TargetValue * tt;
                }
            }
        }

        // === Score digit shuffle animation ===
        if (_scoreDigitsVisible)
        {
            _scoreDigitTimer += dt;
            int scoreStart = 0;
            // Find first non-disabled digit
            for (int i = 0; i < _scoreDigitCount; i++)
            {
                if (_scoreTargetDigits[i] != 10) { scoreStart = i; break; }
                if (i == _scoreDigitCount - 1) scoreStart = _scoreDigitCount;
            }

            for (int i = scoreStart; i < _scoreDigitCount; i++)
            {
                float digitDelay = (i - 1) / 24f;
                if (_scoreDigitTimer >= digitDelay && !_scoreDigitShuffling[i] && !_scoreDigitDone[i]
                    && !_scoreDigitTweening[i])
                {
                    _scoreDigitShuffling[i] = true;
                    _scoreDigitShuffleTimers[i] = 0;
                }
                if (_scoreDigitShuffling[i])
                {
                    _scoreDigitShuffleTimers[i] += dt;
                    float shuffleDuration = 41f / 24f;
                    float shuffleInterval = 1f / 24f;
                    // Original: FlxTimer.start(interval, shuffleProgress, loops)
                    // Each tick increments digit by 1, wrapping 9->0
                    int tickCount = (int)(_scoreDigitShuffleTimers[i] / shuffleInterval);
                    _scoreCurrentDigits[i] = tickCount % 10;

                    if (_scoreDigitShuffleTimers[i] >= shuffleDuration)
                    {
                        // Start tween to final digit over 23/24s (quadOut)
                        _scoreDigitShuffling[i] = false;
                        _scoreDigitTweening[i] = true;
                        _scoreDigitTweenTimers[i] = 0;
                        _scoreCurrentDigits[i] = 0; // tween from 0 to finalDigit
                    }
                }
                // Score digit tween: 0 ? finalDigit over 23/24s quadOut
                if (_scoreDigitTweening[i])
                {
                    _scoreDigitTweenTimers[i] += dt;
                    float tweenDuration = 23f / 24f;
                    float tt = Math.Clamp(_scoreDigitTweenTimers[i] / tweenDuration, 0f, 1f);
                    tt = QuadOut(tt);
                    _scoreCurrentDigits[i] = _scoreTargetDigits[i] * tt;
                    int digitRounded = (int)Math.Floor(_scoreCurrentDigits[i]);
                    if (digitRounded > 9) digitRounded = 9;
                    if (digitRounded < 0) digitRounded = 0;

                    if (_scoreDigitTweenTimers[i] >= tweenDuration)
                    {
                        _scoreDigitTweening[i] = false;
                        _scoreDigitDone[i] = true;
                        _scoreCurrentDigits[i] = _scoreTargetDigits[i];
                        // Original: finalDelay = scoreStart - (i - 1)
                        // scoreStart is count of active digits (not index), so finalDelay
                        // is always negative or 0 for typical scores, meaning glow replays immediately.
                        // In HaxeFlixel, FlxTimer.start with negative duration fires immediately.
                        _scoreDigitGlow[i] = true;
                    }
                }
            }
        }

        // === Clear percent tween (58/24s duration, quartOut) ===
        if (_clearPercentActive && !_clearPercentDone)
        {
            _clearPercentTimer += dt;
            float duration = 58f / 24f;
            float t = Math.Clamp(_clearPercentTimer / duration, 0f, 1f);
            t = QuartOut(t);

            float startVal = Math.Max(0, _clearPercentTarget - 36);
            float newVal = startVal + (_clearPercentTarget - startVal) * t;
            int newRounded = (int)Math.Round(newVal);

            // Play tick sound on each increment (original: FunkinSound.playOnce, default volume 1.0)
            if (newRounded != (int)Math.Round(_clearPercentCurrent))
            {
                Audio.PlaySound("scrollMenu", 1.0f);
            }
            _clearPercentCurrent = newVal;

            if (_clearPercentTimer >= duration)
            {
                _clearPercentDone = true;
                _clearPercentCurrent = _clearPercentTarget;
                Audio.PlaySound("confirmMenu");

                // Flash white
                _clearPercentFlashing = true;
                _clearPercentFlashTimer = 0;
            }
        }

        // === Clear percent flash (0.4s white flash, then fade over 0.5s after 0.25s delay) ===
        if (_clearPercentFlashing)
        {
            _clearPercentFlashTimer += dt;
            if (_clearPercentFlashTimer >= 0.4f)
                _clearPercentFlashing = false;
        }
        if (_clearPercentDone && !_clearPercentFading)
        {
            // Original: new FlxTimer().start(0.25, ...) after confirmMenu
            // Use clearPercentFlashTimer as post-completion elapsed tracker
            if (_clearPercentFlashTimer >= 0.25f)
            {
                _clearPercentFading = true;
                _clearPercentFadeTimer = 0;
            }
        }
        if (_clearPercentFading)
        {
            _clearPercentFadeTimer += dt;
            float fadeDelay = 0.5f;
            float fadeDuration = 0.5f;
            if (_clearPercentFadeTimer >= fadeDelay)
            {
                float ft = Math.Clamp((_clearPercentFadeTimer - fadeDelay) / fadeDuration, 0f, 1f);
                ft = QuartOut(ft);
                _clearPercentAlpha = 1f - ft;
            }
        }

        // === Music at getMusicDelay() ===
        if (!_musicStarted && _timer >= GetMusicDelay())
        {
            _musicStarted = true;
            string musicPath = GetResultsMusicPath();
            // Original: plays intro first (music/path/path-intro), then chains to loop (music/path/path)
            string introPath = $"music/{musicPath}/{musicPath}-intro";
            string loopPath = $"music/{musicPath}/{musicPath}";
            string directPath = $"music/{musicPath}";
            // Try intro+loop chaining first
            Audio.PlayMusicWithIntro(introPath, loopPath);
            // If that didn't find anything, try direct path
            if (!Audio.MusicPlaying)
                Audio.PlayMusic(directPath, true);
        }

        // === Rank text backdrop at getFlashDelay() (displayRankText) ===
        if (!_rankTextVisible && _timer >= GetFlashDelay())
        {
            _rankTextVisible = true;
            _rankTextTimer = 0;
            _rankVertFlickerTimer = 0;
            _rankVertFlickerDone = false;
            // BG flash with 14/24s fade (original: FlxTween alpha?0 over 14/24)
            _bgFlashVisible = true;
            _bgFlashAlpha = 1f;
            _bgFlashFadeDuration = 14f / 24f;
            _rankVertScrollDelay = 30f / 24f;
        }
        if (_rankTextVisible)
        {
            _rankTextTimer += dt;
            // Vertical text flicker: FlxFlicker(rankTextVert, 2/24*3, 2/24, true)
            // Duration = 0.25s, period = 0.083s — toggles visibility on/off
            if (!_rankVertFlickerDone)
            {
                _rankVertFlickerTimer += dt;
                if (_rankVertFlickerTimer >= (2f / 24f * 3f))
                    _rankVertFlickerDone = true;
            }
            // Horizontal rows scroll at velocity ±7 pixels/second (FlxBackdrop velocity.x)
            for (int i = 0; i < 12; i++)
            {
                float vel = (i % 2 == 0) ? -7f : 7f;
                _rankRowOffsets[i] += vel * dt;
            }
            // Vertical scroll starts after 30/24s delay
            if (_rankVertScrollDelay > 0)
            {
                _rankVertScrollDelay -= dt;
            }
            else if (!_rankVertScrolling)
            {
                _rankVertScrolling = true;
            }
            if (_rankVertScrolling)
                _rankVertOffset -= 80f * dt;
        }

        // === Highscore NEW at getHighscoreDelay() ===
        if (!_highscoreNewVisible && _isNewHighscore && _timer >= GetHighscoreDelay())
        {
            _highscoreNewVisible = true;
        }
        if (_highscoreNewVisible && _highscoreNewAnim != null)
            _highscoreNewAnim.Update(dt);

        // === afterRankTallySequence at getBFDelay() ===
        if (!_afterRankSequenceStarted && _timer >= GetBFDelay())
        {
            _afterRankSequenceStarted = true;
            _afterRankTimer = 0;
            // showSmallClearPercent()
            _smallClearPercentVisible = true;
            _smallClearPercentFlashing = true;
            _smallClearPercentFlashTimer = 0;
        }
        if (_afterRankSequenceStarted)
        {
            _afterRankTimer += dt;
            // Small clear percent flash lasts 0.4s
            if (_smallClearPercentFlashing)
            {
                _smallClearPercentFlashTimer += dt;
                if (_smallClearPercentFlashTimer >= 0.4f)
                    _smallClearPercentFlashing = false;
            }
            // After 2.5s, start scrolling song stuff (original: showSmallClearPercent's timer)
            // Note: speed was already ramped to target by first timerThenSongName(1.0, false)
            if (_afterRankTimer >= 2.5f && !_songNameMoving)
            {
                _songNameMoving = true;
                // Do NOT reset speed here — it's already at target from initial timerThenSongName
            }
        }

        // === Song name tween into top bar (timerThenSongName) ===
        if (!_songNameTweenedIn && _timer >= _songNameMoveDelay)
        {
            _songNameTweenedIn = true;
            _songNameTweenTimer = 0;
            // The timerLength starts here. After it expires, speedOfTween starts ramping
            // and movingSongStuff = autoScroll
            _songNameScrollStartDelay = _songNameAutoScroll ? 3.0f : 1.0f;
        }
        if (_songNameTweenedIn)
        {
            _songNameTweenTimer += dt;
            // Difficulty tweens to y=122+(barHeight-148) over 0.5s, expoOut, startDelay 0.8
            float diffDelay = 0.8f;
            float diffTargetY = 122f + (_topBarHeight - 148);
            if (_songNameTweenTimer >= diffDelay)
            {
                float t = Math.Clamp((_songNameTweenTimer - diffDelay) / 0.5f, 0f, 1f);
                t = ExpoOut(t);
                _difficultyY = -50 + (diffTargetY + 50) * t;
            }
            // Small clear percent: x = difficulty.x + diffWidth + 60, tweens to y = 122-5+(barHeight-148)
            // Only update X from difficulty during initial tween-in, not while scrolling
            int diffWidth = _difficultyTex != null ? _difficultyTex.Width : 80;
            if (!_songNameMoving)
                _smallClearPercentX = _difficultyX + diffWidth + 60;
            if (_songNameTweenTimer >= 0.85f)
            {
                float t = Math.Clamp((_songNameTweenTimer - 0.85f) / 0.5f, 0f, 1f);
                t = ExpoOut(t);
                float cpTargetY = (122f - 5f) + (_topBarHeight - 148);
                _smallClearPercentY = -30 + (cpTargetY + 30) * t;
            }
            // Song name tweens to position over 0.5s, expoOut, startDelay 0.9
            // songName.x = clearPercentSmall.x + 94 (set once in original timerThenSongName)
            float nameDelay = 0.9f;
            int songNameWidth2 = string.IsNullOrEmpty(_songName) ? 200
                : _songName.Length * (FONT_CHAR_W + FONT_LETTER_SPACING);
            float fuckedupnumber = -(songNameWidth2 * 0.5f) * MathF.Sin(SONG_NAME_ANGLE * MathF.PI / 180f) - 10;
            float nameTargetY = diffTargetY - 25 - fuckedupnumber;
            // Only set songNameX from clearPercentSmall during initial tween-in, not while scrolling
            if (!_songNameMoving)
                _songNameX = _smallClearPercentX + 94;
            if (_songNameTweenTimer >= nameDelay)
            {
                float t = Math.Clamp((_songNameTweenTimer - nameDelay) / 0.5f, 0f, 1f);
                t = ExpoOut(t);
                _songNameY = -50 + (nameTargetY + 50) * t;
            }

            // After timerLength delay: start speed ramp (for all calls)
            // and enable scrolling only if autoScroll
            if (!_speedTweenStartedForCurrentCycle
                && _songNameTweenTimer >= _songNameScrollStartDelay)
            {
                _speedTweenStartedForCurrentCycle = true;
                _speedOfTweenCurrentX = 0;
                _speedOfTweenCurrentY = 0;
                _speedTweenActive = true;
                _speedTweenTimer = 0;

                if (_songNameAutoScroll)
                    _songNameMoving = true;
            }
        }

        // === Speed ramp-up tween (original: speedOfTween starts at 0, tweens to target over 0.7s quadIn) ===
        if (_speedTweenActive)
        {
            _speedTweenTimer += dt;
            float st = Math.Clamp(_speedTweenTimer / 0.7f, 0f, 1f);
            st = QuadIn(st);
            _speedOfTweenCurrentX = _speedOfTweenTargetX * st;
            _speedOfTweenCurrentY = _speedOfTweenTargetY * st;
        }

        // === Song name + difficulty + clearPercentSmall scrolling ===
        if (_songNameMoving)
        {
            float speedX = _speedOfTweenCurrentX * 60f * dt;
            float speedY = _speedOfTweenCurrentY * 60f * dt;
            _songNameX += speedX;
            _difficultyX += speedX;
            _smallClearPercentX += speedX;
            _songNameY += speedY;
            _difficultyY += speedY;
            _smallClearPercentY += speedY;

            // Estimate song name width for scroll reset check
            int songNameWidth = string.IsNullOrEmpty(_songName) ? 200
                : _songName.Length * (FONT_CHAR_W + FONT_LETTER_SPACING);
            if (_songNameX + songNameWidth < 100)
            {
                // timerThenSongName(3.0, true) — reset and loop
                _songNameMoving = false;
                _speedTweenActive = false;
                _speedTweenStartedForCurrentCycle = false;
                _difficultyX = 555;
                _difficultyY = -50;
                _songNameY = -50;
                _smallClearPercentY = -30;
                _songNameTweenedIn = false;
                _songNameTweenTimer = 0;
                _songNameAutoScroll = true; // subsequent loops auto-scroll
                _songNameMoveDelay = _timer; // start tween-in immediately
            }
        }

        // === Character result animation at getBFDelay() ===
        if (!_characterResultVisible && _characterResultAnim != null
            && _timer >= GetBFDelay())
        {
            _characterResultVisible = true;
        }
        if (_characterResultVisible && _characterResultAnim != null)
            _characterResultAnim.Update(dt);

        // GF result animation (for GREAT rank delay 0.25s, GOOD rank delay 0.91s)
        float gfDelay = (_rank == ScoringRank.GOOD) ? 0.91f : 0.25f;
        if (!_gfResultVisible && _gfResultAnim != null
            && _timer >= GetBFDelay() + gfDelay)
        {
            _gfResultVisible = true;
        }
        if (_gfResultVisible && _gfResultAnim != null)
            _gfResultAnim.Update(dt);

        // Hearts result animation (PERFECT rank, delay 4.41s after BF delay)
        if (!_heartsResultVisible && _heartsResultAnim != null
            && _timer >= GetBFDelay() + 4.41f)
        {
            _heartsResultVisible = true;
        }
        if (_heartsResultVisible && _heartsResultAnim != null)
            _heartsResultAnim.Update(dt);

        // === Music volume fade on exit ===
        if (_musicFading)
        {
            _musicFadeTimer += dt;
            float vol = Math.Max(0, 1f - _musicFadeTimer / 0.8f);
            Audio.MusicVolume = vol;
            if (vol <= 0)
            {
                _musicFading = false;
                Audio.StopMusic();
            }
        }

        // === Exit transition (fade to black then switch scene) ===
        if (_exiting)
        {
            _exitTimer += dt;
            _rankBgAlpha = Math.Clamp(_exitTimer / 0.8f, 0f, 1f);
            if (_exitTimer >= 1.0f)
            {
                _exiting = false; // Prevent calling ChangeScene every frame
                // Restore music volume before switching scene
                Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
                // Return to the correct scene based on how we got here
                if (_isStoryMode)
                    Game.Scenes.ChangeScene(new StoryModeScene());
                else
                    Game.Scenes.ChangeScene(new FreeplayScene());
            }
        }

        // === Input (original: controls.PAUSE_P || controls.ACCEPT_P) ===
        if ((Input.ConfirmPressed || Input.BackPressed) && !_busy)
        {
            _busy = true;
            _exiting = true;
            _exitTimer = 0;
            // Original: tween music volume?0 over 0.8s, pitch 3?0.5 warp
            _musicFading = true;
            _musicFadeTimer = 0;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        // Enable scissor test to clip all drawing to screen bounds
        var prevScissor = Game.GraphicsDevice.ScissorRectangle;
        Game.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        var rastState = new RasterizerState { ScissorTestEnable = true };
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, rasterizerState: rastState);

        // === 1. Background gradient (zIndex 10) ===
        DrawGradientBackground(spriteBatch);

        // === 2. BG flash overlay (zIndex 20) — guarded by flashing lights preference ===
        if (_bgFlashVisible && _bgFlashAlpha > 0 && Engine.HighscoreManager.Data.FlashingLights)
        {
            EnsureBgFlashTexture();
            spriteBatch.Draw(_bgFlashTex,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.White * _bgFlashAlpha);
        }

        // === 3. Rank text scrolling backdrop (zIndex 100) ===
        if (_rankTextVisible)
            DrawRankBackdrop(spriteBatch);

        // === 4. Clear percent counter (zIndex 450) ===
        if (_clearPercentActive && _clearPercentAlpha > 0.01f)
            DrawClearPercent(spriteBatch);

        // === 5. Character result animation (zIndex 499-501) ===
        if (_gfResultVisible && _gfResultAnim != null)
            _gfResultAnim.Draw(spriteBatch);
        if (_characterResultVisible && _characterResultAnim != null)
            _characterResultAnim.Draw(spriteBatch);
        else if (_characterResultVisible)
        {
            // Fallback: draw large rank text when character sprite isn't available
            var rankFont = Assets.GetFont(72);
            if (rankFont != null)
            {
                string rankText = GetRankDisplayText();
                Color rankColor = GetRankColor();
                var rankSize = rankFont.MeasureString(rankText);
                float rx = FNFGame.SCREEN_WIDTH / 2f + 100 - rankSize.X / 2;
                float ry = FNFGame.SCREEN_HEIGHT / 2f - rankSize.Y / 2 - 40;
                // Shadow
                rankFont.DrawText(spriteBatch, rankText, new Vector2(rx + 3, ry + 3), Color.Black * 0.5f);
                rankFont.DrawText(spriteBatch, rankText, new Vector2(rx, ry), rankColor);
            }
        }
        if (_heartsResultVisible && _heartsResultAnim != null)
            _heartsResultAnim.Draw(spriteBatch);

        // === 6. Rank vertical text (zIndex 990) ===
        if (_rankTextVisible)
            DrawRankVerticalText(spriteBatch);

        // === 6b. Song name, difficulty, small clear percent (zIndex 1000) ===
        DrawTopBarContents(spriteBatch);

        // === 7. Black top bar (zIndex 1010) ===
        DrawTopBar(spriteBatch);

        // === 8. Sound system (zIndex 1100) ===
        if (_soundSystemVisible && _soundSystemAnim != null)
            _soundSystemAnim.Draw(spriteBatch);

        // === 9. "RESULTS" anim (zIndex 1200) ===
        if (_resultsVisible)
            DrawResultsTitle(spriteBatch);

        // === 10. Ratings popin (zIndex 1200) ===
        if (_ratingsVisible)
            DrawRatingsPanel(spriteBatch);

        // === 11. Score popin + digits (zIndex 1200) ===
        if (_scoreVisible)
            DrawScore(spriteBatch);

        // === 12. Tally counters (zIndex 1200) ===
        DrawTallyCounters(spriteBatch);

        // === 13. Highscore NEW (zIndex 1200) ===
        if (_highscoreNewVisible && _highscoreNewAnim != null)
            _highscoreNewAnim.Draw(spriteBatch);

        // === 14. Rank BG overlay (zIndex 99999, used for exit transition) ===
        if (_rankBgAlpha > 0.001f)
        {
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.Black * _rankBgAlpha);
        }

        spriteBatch.End();
        // Restore previous scissor rectangle
        Game.GraphicsDevice.ScissorRectangle = prevScissor;
    }

    // ==========================================
    //  Draw helpers
    // ==========================================

    private void DrawGradientBackground(SpriteBatch spriteBatch)
    {
        if (_gradientBG == null)
        {
            Color topColor = new Color(0xFE, 0xCC, 0x5C);
            Color botColor = new Color(0xFD, 0xC0, 0x5C);
            _gradientBG = new Texture2D(Game.GraphicsDevice, 1, FNFGame.SCREEN_HEIGHT);
            var data = new Color[FNFGame.SCREEN_HEIGHT];
            for (int y = 0; y < FNFGame.SCREEN_HEIGHT; y++)
            {
                float t = (float)y / FNFGame.SCREEN_HEIGHT;
                data[y] = Color.Lerp(topColor, botColor, t);
            }
            _gradientBG.SetData(data);
        }
        spriteBatch.Draw(_gradientBG,
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.White);
    }

    private void EnsureBgFlashTexture()
    {
        if (_bgFlashTex == null)
        {
            // Original: FlxGradient [0xFFFFF1A6, 0xFFFFF1BE] 90°
            _bgFlashTex = new Texture2D(Game.GraphicsDevice, 1, FNFGame.SCREEN_HEIGHT);
            var data = new Color[FNFGame.SCREEN_HEIGHT];
            Color top = new Color(0xFF, 0xF1, 0xA6);
            Color bot = new Color(0xFF, 0xF1, 0xBE);
            for (int y = 0; y < FNFGame.SCREEN_HEIGHT; y++)
            {
                float t = (float)y / FNFGame.SCREEN_HEIGHT;
                data[y] = Color.Lerp(top, bot, t);
            }
            _bgFlashTex.SetData(data);
        }
    }

    private void DrawTopBar(SpriteBatch spriteBatch)
    {
        // Original: BitmapUtil.createResultsBar() — black rect rotated -3.8°
        // width = ceil(FlxG.width * 1.011), height = ceil(width / 8.7)
        // matrix.rotate(-3.8 * PI / 180), matrix.translate(-15, 0)
        int barY = (int)_topBarY;
        float topBarRot = -3.8f * MathF.PI / 180f;

        if (_topBarBlackTex != null && _topBarBlackTex != Assets.Pixel)
        {
            spriteBatch.Draw(_topBarBlackTex,
                new Vector2(-15, barY),
                null,
                Color.White,
                topBarRot,
                Vector2.Zero,
                new Vector2((FNFGame.SCREEN_WIDTH + 30f) / _topBarBlackTex.Width, (float)_topBarHeight / _topBarBlackTex.Height),
                SpriteEffects.None, 0f);
        }
        else
        {
            spriteBatch.Draw(Assets.Pixel,
                new Vector2(-15, barY),
                null,
                Color.Black,
                topBarRot,
                Vector2.Zero,
                new Vector2(FNFGame.SCREEN_WIDTH + 30, _topBarHeight),
                SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Draws song name, difficulty, and small clear percent at zIndex 1000
    /// (behind blackTopBar at 1010, but above rankVerticalText at 990).
    /// The black bar partially covers these elements due to its -3.8° rotation.
    /// </summary>
    private void DrawTopBarContents(SpriteBatch spriteBatch)
    {
        // Song name (original: FlxBitmapText from tardlingSpritesheet.png, angle -4.4°, zIndex 1000)
        if (!string.IsNullOrEmpty(_songName))
        {
            DrawSongNameBitmapFont(spriteBatch, _songName, _songNameX, _songNameY);
        }

        // Difficulty text (original: diff_{difficultyId} graphic, zIndex 1000)
        // Original: maskShaderDifficulty clips at swagMaskX = difficulty.x - 30
        if (_difficultyTex != null && _difficultyY > -100)
        {
            if (_difficultyX + _difficultyTex.Width > 525)
            {
                spriteBatch.Draw(_difficultyTex, new Vector2(_difficultyX, _difficultyY), Color.White);
            }
        }
        else
        {
            var diffFont = Assets.GetFont(20);
            if (diffFont != null && _difficultyY > -100 && _difficultyX > 525)
            {
                string diffText = _difficultyId.ToUpper();
                diffFont.DrawText(spriteBatch, diffText,
                    new Vector2(_difficultyX, _difficultyY), Color.White);
            }
        }

        // Small clear percent (original: ClearPercentCounter small=true, zIndex 1000)
        if (_smallClearPercentVisible && _smallClearPercentY > -100)
        {
            float cpBaseX = _smallClearPercentX;
            float cpBaseY = _smallClearPercentY;
            Color cpTint = Color.White;

            if (cpBaseX > 525)
            {
                if (_clearPercentTextSmallTex != null && _clearPercentTextSmallTex != Assets.Pixel)
                {
                    spriteBatch.Draw(_clearPercentTextSmallTex,
                        new Vector2(cpBaseX + 40, cpBaseY), cpTint);
                }

                var cpDigits = GetDigitArray(_clearPercentTarget);
                int cpDigitOffset = (cpDigits.Length == 1) ? 1 : (cpDigits.Length == 3) ? -1 : 0;

                for (int d = 0; d < cpDigits.Length; d++)
                {
                    int di = d + 1;
                    float xPos = (di - 1 + cpDigitOffset) * 32f + (-24);
                    float yPos = (di - 1 + cpDigitOffset) * (-4f);

                    if (_clearPercentSmallSheet != null)
                    {
                        string fn = $"number {cpDigits[d]} 0001";
                        var frame = _clearPercentSmallSheet.GetFrame(fn);
                        if (frame == null)
                        {
                            var frames = _clearPercentSmallSheet.GetAnimationFuzzy($"number {cpDigits[d]}");
                            if (frames != null && frames.Count > 0) frame = frames[0];
                        }
                        if (frame != null)
                        {
                            spriteBatch.Draw(_clearPercentSmallSheet.Texture,
                                new Vector2(cpBaseX + xPos + frame.Offset.X, cpBaseY + yPos + frame.Offset.Y),
                                frame.SourceRect, cpTint);
                            continue;
                        }
                    }

                    var cpFont = Assets.GetFont(18);
                    if (cpFont != null)
                        cpFont.DrawText(spriteBatch, cpDigits[d].ToString(),
                            new Vector2(cpBaseX + xPos, cpBaseY + yPos), cpTint);
                }
            }
        }
    }

    private void DrawResultsTitle(SpriteBatch spriteBatch)
    {
        // Original: x = FlxG.width - 1480, y = -10
        if (_resultsAnim != null)
        {
            _resultsAnim.Draw(spriteBatch);
            return;
        }

        // Fallback: text
        var font = Assets.GetFont(56);
        if (font != null)
        {
            font.DrawText(spriteBatch, "RESULTS",
                new Vector2(FNFGame.SCREEN_WIDTH - 400, 80), Color.White);
        }
    }

    private void DrawRatingsPanel(SpriteBatch spriteBatch)
    {
        // Original: ratingsPopin at (-135, 135)
        if (_ratingsPopin != null)
        {
            _ratingsPopin.Draw(spriteBatch);
            return;
        }

        // Fallback: draw dark panel with tally labels
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(0, 135, 350, 400),
            new Color(0, 0, 0, 120));

        var labelFont = Assets.GetFont(18);
        if (labelFont != null && _tallies != null)
        {
            for (int i = 0; i < _tallies.Length; i++)
            {
                if (!_tallies[i].Visible) continue;
                labelFont.DrawText(spriteBatch, _tallies[i].Label,
                    new Vector2(20, _tallies[i].Y), _tallies[i].ValueColor * 0.8f);
            }
        }
    }

    private void DrawTallyCounters(SpriteBatch spriteBatch)
    {
        if (_tallies == null) return;

        for (int i = 0; i < _tallies.Length; i++)
        {
            if (!_tallies[i].Visible) continue;

            int curVal = (int)Math.Round(_tallies[i].CurrentValue);
            DrawTallyNumber(spriteBatch, curVal, _tallies[i].TargetValue,
                _tallies[i].X, _tallies[i].Y, _tallies[i].ValueColor);
        }
    }

    private void DrawTallyNumber(SpriteBatch spriteBatch, int number, int maxNumber,
        int x, int y, Color tint)
    {
        // Original: TallyCounter uses tallieNumber spritesheet, 43px spacing per digit
        // Each digit frame named "{digit} small0000"
        var digits = GetDigitArray(number);

        if (_tallieNumberSheet != null)
        {
            for (int d = 0; d < digits.Length; d++)
            {
                // Original: animation.addByPrefix("0", "0 small", 24, false)
                // Frame naming in XML: "{digit} small0000", "{digit} small0001", etc.
                string frameName = $"{digits[d]} small0000";
                var frame = _tallieNumberSheet.GetFrame(frameName);
                if (frame == null)
                {
                    // Try fuzzy match (prefix "N small")
                    var frames = _tallieNumberSheet.GetAnimationFuzzy($"{digits[d]} small");
                    if (frames != null && frames.Count > 0) frame = frames[0];
                }
                if (frame != null)
                {
                    float dx = x + d * 43;
                    spriteBatch.Draw(_tallieNumberSheet.Texture,
                        new Vector2(dx + frame.Offset.X, y + frame.Offset.Y),
                        frame.SourceRect, tint);
                }
            }
        }
        else
        {
            // Font fallback
            var valueFont = Assets.GetFont(28);
            if (valueFont != null)
                valueFont.DrawText(spriteBatch, number.ToString(), new Vector2(x, y), tint);
        }
    }

    private static int[] GetDigitArray(int number)
    {
        if (number <= 0) return new[] { 0 };
        var digits = new List<int>();
        int temp = Math.Abs(number);
        while (temp > 0)
        {
            digits.Add(temp % 10);
            temp /= 10;
        }
        digits.Reverse();
        return digits.ToArray();
    }

    private static readonly string[] NumToString = {
        "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE",
        "SIX", "SEVEN", "EIGHT", "NINE", "DISABLED"
    };

    private void DrawScore(SpriteBatch spriteBatch)
    {
        // Score popin background
        if (_scorePopin != null)
            _scorePopin.Draw(spriteBatch);
        else if (_scoreDigitsVisible)
        {
            // Fallback: draw "SCORE" label and dark backdrop behind digits
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(20, 290, 680, 80),
                new Color(0, 0, 0, 100));
            var scoreLabel = Assets.GetFont(18);
            if (scoreLabel != null)
                scoreLabel.DrawText(spriteBatch, "SCORE", new Vector2(35, 292), Color.White * 0.6f);
        }

        if (!_scoreDigitsVisible) return;

        // Original: score-digital-numbers spritesheet, 65px per digit, x=35 y=305
        // Frame naming: "{NAME} DIGITAL0001" etc. We use the first frame (0001 = glow, 0005 = settled)
        for (int i = 0; i < _scoreDigitCount; i++)
        {
            int digit;
            if (_scoreDigitDone[i])
                digit = _scoreTargetDigits[i];
            else if (_scoreDigitTweening != null && _scoreDigitTweening[i])
                digit = Math.Clamp((int)Math.Floor(_scoreCurrentDigits[i]), 0, 9);
            else if (_scoreDigitShuffling[i])
                digit = ((int)_scoreCurrentDigits[i]) % 10;
            else
                digit = _scoreTargetDigits[i] == 10 ? 10 : 0;

            if (digit >= 10)
            {
                // DISABLED/GONE — draw the GONE frame (invisible placeholder)
                // Original: plays GONE animation which is a transparent frame
                // We just skip drawing since GONE is effectively invisible
                continue;
            }

            float dx = 35 + (65 * i);
            float dy = 305;

            if (_scoreDigitalSheet != null)
            {
                // Original: first set_digit plays from frame 0 (glow=true), subsequent from frame 4 (glow=false)
                // During shuffle: frame 4 (settled look, 0005)
                // After tween finalDelay: frame 0 (glow replay, 0001)
                // _scoreDigitGlow[i] = true means show glow frame
                // _scoreDigitFirstAppear[i] = true means first time showing this digit
                string name = NumToString[digit];
                bool showGlow;
                if (_scoreDigitFirstAppear != null && _scoreDigitFirstAppear[i])
                {
                    showGlow = true;
                    _scoreDigitFirstAppear[i] = false;
                }
                else
                {
                    showGlow = _scoreDigitGlow != null && _scoreDigitGlow[i];
                }
                string frameSuffix = showGlow ? "0001" : "0005";
                string frameName = $"{name} DIGITAL{frameSuffix}";
                var frame = _scoreDigitalSheet.GetFrame(frameName);
                if (frame == null)
                {
                    // Fall back to any frame for this digit
                    var frames = _scoreDigitalSheet.GetAnimationFuzzy($"{name} DIGITAL");
                    if (frames != null && frames.Count > 0)
                        frame = frames[^1]; // last frame = settled
                }
                if (frame != null)
                {
                    spriteBatch.Draw(_scoreDigitalSheet.Texture,
                        new Vector2(dx + frame.Offset.X, dy + frame.Offset.Y),
                        frame.SourceRect, Color.White);
                    continue;
                }
            }

            // Font fallback
            var font = Assets.GetFont(36);
            if (font != null)
                font.DrawText(spriteBatch, digit.ToString(), new Vector2(dx, dy), Color.White);
        }
    }

    private void DrawClearPercent(SpriteBatch spriteBatch)
    {
        // Original: ClearPercentCounter at (width/2 + 190, height/2 - 70)
        // Consists of: clearPercentText.png + digit sprites from clearPercentNumberLeft/Right
        int displayPercent = (int)Math.Round(_clearPercentCurrent);
        float baseX = FNFGame.SCREEN_WIDTH / 2f + 190;
        float baseY = FNFGame.SCREEN_HEIGHT / 2f - 70;
        Color tint = Color.White * _clearPercentAlpha;

        // Draw "%" text sprite first (at x=0, y=0 relative to group)
        // Original: PureColor shader makes it all white during flash
        if (_clearPercentTextTex != null && _clearPercentTextTex != Assets.Pixel)
        {
            Color textTint = _clearPercentFlashing ? Color.White * _clearPercentAlpha : tint;
            spriteBatch.Draw(_clearPercentTextTex, new Vector2(baseX, baseY), textTint);
        }
        else
        {
            // Fallback: draw "%" with font
            var pctFont = Assets.GetFont(48);
            if (pctFont != null)
            {
                Color pctTint = _clearPercentFlashing ? Color.White * _clearPercentAlpha : tint;
                pctFont.DrawText(spriteBatch, "%", new Vector2(baseX + 20, baseY + 10), pctTint);
            }
        }

        // Draw digit sprites
        var digits = GetDigitArray(displayPercent);
        // Original layout: digitSize=72, offset for 1-digit(+1), 3-digit(-1)
        int digitOffset = (digits.Length == 1) ? 1 : (digits.Length == 3) ? -1 : 0;

        for (int ind = 0; ind < digits.Length; ind++)
        {
            int digitIndex = ind + 1;
            float xPos = (digitIndex - 1 + digitOffset) * 72f;
            float yPos = 72; // digits are below the "%" text

            // Three digits = LLR variant pattern, otherwise index >= 1 is Right
            bool variant = (digits.Length == 3) ? (digitIndex >= 2) : (digitIndex >= 1);
            var sheet = variant ? _clearPercentRightSheet : _clearPercentLeftSheet;

            if (sheet != null)
            {
                string frameName = $"number {digits[ind]} 0001";
                var frame = sheet.GetFrame(frameName);
                if (frame == null)
                {
                    var frames = sheet.GetAnimationFuzzy($"number {digits[ind]}");
                    if (frames != null && frames.Count > 0) frame = frames[0];
                }
                if (frame != null)
                {
                    // Flash white effect: during flash, draw as pure white
                    Color digitTint = _clearPercentFlashing ? Color.White * _clearPercentAlpha : tint;
                    spriteBatch.Draw(sheet.Texture,
                        new Vector2(baseX + xPos + frame.Offset.X, baseY + yPos + frame.Offset.Y),
                        frame.SourceRect, digitTint);
                    continue;
                }
            }

            // Font fallback
            var font = Assets.GetFont(72);
            if (font != null)
            {
                Color color = _clearPercentFlashing ? Color.White * _clearPercentAlpha : tint;
                font.DrawText(spriteBatch, digits[ind].ToString(), new Vector2(baseX + xPos, baseY + yPos), color);
            }
        }
    }

    private void DrawRankBackdrop(SpriteBatch spriteBatch)
    {
        // Original: 12 horizontal FlxBackdrop rows using rankScroll{RANK}.png, gap=10, X axis
        // Each row at y = 50 + (135*i/2) + 10, alternating velocity.x ±7
        // These are on cameraScroll which has canvas.rotation = -3.8°
        bool hasHorTex = _rankHorTex != null && _rankHorTex != Assets.Pixel;
        float rotRad = -3.8f * MathF.PI / 180f;

        for (int i = 0; i < 12; i++)
        {
            float y = 50 + (135 * i / 2f) + 10;
            float baseX = FNFGame.SCREEN_WIDTH / 2f - 320 + _rankRowOffsets[i];

            if (hasHorTex)
            {
                int tileW = _rankHorTex.Width + 10; // original gap = 10
                // Proper modulo wrapping for negative values
                float wrappedX = ((baseX % tileW) + tileW) % tileW;
                for (float tx = wrappedX - tileW * 4; tx < FNFGame.SCREEN_WIDTH + tileW; tx += tileW)
                {
                    spriteBatch.Draw(_rankHorTex,
                        new Vector2(tx, y),
                        null,
                        Color.White,
                        rotRad,
                        Vector2.Zero,
                        Vector2.One,
                        SpriteEffects.None, 0f);
                }
            }
            else
            {
                string rankText = GetRankDisplayText();
                Color horColor = GetRankColor() * 0.6f;
                var font = Assets.GetFont(28);
                if (font == null) continue;
                var textSize = font.MeasureString(rankText);
                float tileWidth = textSize.X + 10;
                if (tileWidth < 1) tileWidth = 200;
                float wrappedX = ((baseX % tileWidth) + tileWidth) % tileWidth;
                for (float tx = wrappedX - tileWidth * 3; tx < FNFGame.SCREEN_WIDTH + tileWidth; tx += tileWidth)
                {
                    if (tx > FNFGame.SCREEN_WIDTH + 50 || tx < -tileWidth - 50) continue;
                    font.DrawText(spriteBatch, rankText, new Vector2(tx, y), horColor);
                }
            }
        }
    }

    private void DrawRankVerticalText(SpriteBatch spriteBatch)
    {
        if (!_rankTextVisible) return;

        // FlxFlicker effect: toggles visibility every 2/24s for 2/24*3 duration
        // FlxFlicker starts by toggling immediately (visible?false on first tick)
        if (!_rankVertFlickerDone)
        {
            float flickerPeriod = 2f / 24f;
            int flickerIndex = (int)(_rankVertFlickerTimer / flickerPeriod);
            if (flickerIndex % 2 == 0) return; // hidden during even phases (first toggle hides)
        }

        // Original: FlxBackdrop using rankText{RANK}.png, Y axis, gap = 30
        // x = width - 44, y starts at 100, velocity.y = -80
        // Clamp x so vertical text doesn't overflow right edge
        float x = FNFGame.SCREEN_WIDTH - 44;
        bool hasVerTex = _rankVerTex != null && _rankVerTex != Assets.Pixel;

        if (hasVerTex)
        {
            int tileH = _rankVerTex.Height + 30; // original gap = 30
            float baseY = 100 + _rankVertOffset;
            float wrappedY = ((baseY % tileH) + tileH) % tileH;
            for (float ty = wrappedY - tileH * 3; ty < FNFGame.SCREEN_HEIGHT + tileH; ty += tileH)
            {
                if (ty > FNFGame.SCREEN_HEIGHT + 50 || ty < -_rankVerTex.Height - 50) continue;
                // Clip texture to screen width so it doesn't overflow right edge
                int drawWidth = Math.Min(_rankVerTex.Width, Math.Max(0, FNFGame.SCREEN_WIDTH - (int)x));
                if (drawWidth <= 0) continue;
                spriteBatch.Draw(_rankVerTex,
                    new Vector2(x, ty),
                    new Rectangle(0, 0, drawWidth, _rankVerTex.Height),
                    Color.White);
            }
        }
        else
        {
            // Text fallback
            string rankText = GetRankDisplayText();
            Color vertColor = GetRankColor() * 0.8f;
            var font = Assets.GetFont(28);
            if (font == null) return;
            var textSize = font.MeasureString(rankText);
            float tileHeight = textSize.Y + 30;
            if (tileHeight < 1) tileHeight = 50;
            float baseY = 100 + _rankVertOffset;
            float wrappedY = ((baseY % tileHeight) + tileHeight) % tileHeight;
            for (float ty = wrappedY - tileHeight * 3; ty < FNFGame.SCREEN_HEIGHT + tileHeight; ty += tileHeight)
            {
                if (ty > FNFGame.SCREEN_HEIGHT + 20 || ty < -tileHeight - 20) continue;
                font.DrawText(spriteBatch, rankText, new Vector2(Math.Min(x, FNFGame.SCREEN_WIDTH - 10), ty), vertColor);
            }
        }
    }

    // ==========================================
    //  Scoring (matches original Scoring.hx)
    // ==========================================

    private ScoringRank CalculateRank()
    {
        if (_totalNotes == 0) return ScoringRank.SHIT;
        if (_tallySick == _totalNotes) return ScoringRank.PERFECT_GOLD;

        float completion = Math.Clamp(
            _totalNotesHit / (float)_totalNotes, 0f, 1f);

        // Original uses == for PERFECT threshold, >= for all others
        if (completion == 1.0f) return ScoringRank.PERFECT;
        if (completion >= 0.90f) return ScoringRank.EXCELLENT;
        if (completion >= 0.80f) return ScoringRank.GREAT;
        if (completion >= 0.60f) return ScoringRank.GOOD;
        return ScoringRank.SHIT;
    }

    // ==========================================
    //  Rank helper functions (from ScoringRank enum in Scoring.hx)
    // ==========================================

    private string GetResultsMusicPath()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => "resultsPERFECT",
            ScoringRank.EXCELLENT => "resultsEXCELLENT",
            ScoringRank.GREAT or ScoringRank.GOOD => "resultsNORMAL",
            ScoringRank.SHIT => "resultsSHIT",
            _ => "resultsNORMAL"
        };
    }

    private float GetMusicDelay()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 95f / 24f,
            ScoringRank.EXCELLENT => 0f,
            ScoringRank.GREAT => 5f / 24f,
            ScoringRank.GOOD => 3f / 24f,
            ScoringRank.SHIT => 2f / 24f,
            _ => 3.5f
        };
    }

    private float GetBFDelay()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 95f / 24f,
            ScoringRank.EXCELLENT => 97f / 24f,
            ScoringRank.GREAT => 95f / 24f,
            ScoringRank.GOOD => 95f / 24f,
            ScoringRank.SHIT => 95f / 24f,
            _ => 3.5f
        };
    }

    private float GetFlashDelay()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 129f / 24f,
            ScoringRank.EXCELLENT => 122f / 24f,
            ScoringRank.GREAT => 109f / 24f,
            ScoringRank.GOOD => 107f / 24f,
            ScoringRank.SHIT => 186f / 24f,
            _ => 3.5f
        };
    }

    private float GetHighscoreDelay()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 140f / 24f,
            ScoringRank.EXCELLENT => 140f / 24f,
            ScoringRank.GREAT => 129f / 24f,
            ScoringRank.GOOD => 127f / 24f,
            ScoringRank.SHIT => 207f / 24f,
            _ => 3.5f
        };
    }

    private string GetRankDisplayText()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD => "PERFECT!!",
            ScoringRank.PERFECT => "PERFECT",
            ScoringRank.EXCELLENT => "EXCELLENT",
            ScoringRank.GREAT => "GREAT",
            ScoringRank.GOOD => "GOOD",
            ScoringRank.SHIT => "LOSS",
            _ => "LOSS"
        };
    }

    private string GetHorTextAsset()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => "resultScreen/rankText/rankScrollPERFECT",
            ScoringRank.EXCELLENT => "resultScreen/rankText/rankScrollEXCELLENT",
            ScoringRank.GREAT => "resultScreen/rankText/rankScrollGREAT",
            ScoringRank.GOOD => "resultScreen/rankText/rankScrollGOOD",
            ScoringRank.SHIT => "resultScreen/rankText/rankScrollLOSS",
            _ => "resultScreen/rankText/rankScrollGOOD"
        };
    }

    private string GetVerTextAsset()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => "resultScreen/rankText/rankTextPERFECT",
            ScoringRank.EXCELLENT => "resultScreen/rankText/rankTextEXCELLENT",
            ScoringRank.GREAT => "resultScreen/rankText/rankTextGREAT",
            ScoringRank.GOOD => "resultScreen/rankText/rankTextGOOD",
            ScoringRank.SHIT => "resultScreen/rankText/rankTextLOSS",
            _ => "resultScreen/rankText/rankTextGOOD"
        };
    }

    private Color GetRankColor()
    {
        return _rank switch
        {
            ScoringRank.PERFECT_GOLD => new Color(0xFF, 0xB6, 0x19),
            ScoringRank.PERFECT => new Color(0xFF, 0x58, 0xB4),
            ScoringRank.EXCELLENT => new Color(0xFD, 0xCB, 0x42),
            ScoringRank.GREAT => new Color(0xEA, 0xF6, 0xFF),
            ScoringRank.GOOD => new Color(0xEF, 0x87, 0x64),
            ScoringRank.SHIT => new Color(0x60, 0x44, 0xFF),
            _ => Color.White
        };
    }

    // ==========================================
    //  Easing functions (match FlxEase)
    // ==========================================

    private static float QuartOut(float t)
    {
        t -= 1f;
        return -(t * t * t * t - 1f);
    }

    private static float ExpoOut(float t)
    {
        return (t >= 1f) ? 1f : -MathF.Pow(2f, -10f * t) + 1f;
    }

    private static float QuadOut(float t)
    {
        return -t * (t - 2f);
    }

    private static float QuadIn(float t)
    {
        return t * t;
    }

    // ==========================================
    //  Bitmap font rendering (tardlingSpritesheet.png)
    // ==========================================

    private void DrawSongNameBitmapFont(SpriteBatch spriteBatch, string text, float x, float y)
    {
        // Original: FlxBitmapFont.fromMonospace(tardlingSpritesheet, fontLetters, 49x61)
        // letterSpacing = -15, angle = -4.4°
        bool hasBitmapFont = _songNameFontTex != null && _songNameFontTex != Assets.Pixel;
        float rotRad = SONG_NAME_ANGLE * MathF.PI / 180f;

        if (hasBitmapFont)
        {
            // Calculate chars per row in the spritesheet
            int charsPerRow = _songNameFontTex.Width / FONT_CHAR_W;
            float dx = 0;
            foreach (char c in text)
            {
                int idx = FONT_LETTERS.IndexOf(c);
                if (idx < 0)
                {
                    // Space or unknown char — advance by char width
                    dx += FONT_CHAR_W + FONT_LETTER_SPACING;
                    continue;
                }
                int col = idx % charsPerRow;
                int row = idx / charsPerRow;
                var srcRect = new Rectangle(col * FONT_CHAR_W, row * FONT_CHAR_H, FONT_CHAR_W, FONT_CHAR_H);

                // Calculate rotated position for this character
                float cx = x + dx * MathF.Cos(rotRad);
                float cy = y + dx * MathF.Sin(rotRad);

                // Clip: don't draw if left of x=520 (original maskShader)
                if (cx + FONT_CHAR_W > 525)
                {
                    spriteBatch.Draw(_songNameFontTex,
                        new Vector2(cx, cy),
                        srcRect,
                        Color.White,
                        rotRad,
                        Vector2.Zero,
                        Vector2.One,
                        SpriteEffects.None, 0f);
                }
                dx += FONT_CHAR_W + FONT_LETTER_SPACING;
            }
        }
        else
        {
            // Font fallback
            var nameFont = Assets.GetFont(24);
            if (nameFont != null && x > -500)
            {
                string formatted = text.Replace("-", " ").Replace("_", " ").ToUpper();
                nameFont.DrawText(spriteBatch, formatted, new Vector2(x, y), Color.White);
            }
        }
    }
}
