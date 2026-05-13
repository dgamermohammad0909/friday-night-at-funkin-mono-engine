using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Title screen scene — faithful recreation of the original FNF intro sequence.
/// Beat 1:  "The" / "Funkin Crew Inc"
/// Beat 3:  "presents"
/// Beat 4:  clear
/// Beat 5:  "In association" / "with"
/// Beat 7:  "newgrounds" + NG logo
/// Beat 8:  clear
/// Beat 9:  random intro text line 1
/// Beat 11: random intro text line 2
/// Beat 12: clear
/// Beat 13: "Friday"
/// Beat 14: "Night"
/// Beat 15: "Funkin"
/// Beat 16: flash ? show title screen (logo + GF + "Press Enter")
/// </summary>
public class TitleScene : Scene
{
    // ?? Intro sequence state ??
    // BPM of the title screen music (freakyMenu.ogg). Hardcoded to match original FNF.
    // Modders: change this to match your custom title music BPM for correct beat sync.
    private const float MENU_BPM = 102f;
    private float _beatDuration;          // seconds per beat
    private double _songPosition;         // seconds since music started
    private int _lastBeat = -1;
    private bool _skippedIntro;
    private bool _transitioning;
    private float _transitionTimer;

    // Intro text lines currently visible
    private readonly List<string> _introLines = new();
    private bool _showNgSpr;

    // Random wacky intro text (from intro_messages.txt)
    private string _wackyLine1 = "";
    private string _wackyLine2 = "";

    // ?? Title screen assets ??
    private SpriteSheet _logoSheet;
    private SpriteSheet _gfSheet;
    private SpriteSheet _enterSheet;
    private Texture2D _ngLogo;

    // ?? Title screen animation state ??
    private int _gfFrame;
    private float _gfAnimTimer;
    private bool _gfDanceLeft;
    private int _logoFrame;
    private float _logoAnimTimer;
    private float _enterAnimTimer;
    private int _enterFrame;
    private bool _enterPressed;

    // Camera flash
    private float _flashAlpha;
    private float _flashDuration = 4f; // original: flash(WHITE, 4) for intro, flash(WHITE, 1) for confirm

    // ?? Static flag — only play intro once per session (like original) ??
    private static bool s_initialized;

    public override void Load()
    {
        _beatDuration = 60f / MENU_BPM;

        // Load title assets
        _logoSheet = SpriteSheet.Load(Game, "menus/title/logo");
        _gfSheet = SpriteSheet.Load(Game, "menus/title/gf");
        _enterSheet = SpriteSheet.Load(Game, "menus/title/enter");
        _ngLogo = Assets.LoadTexture("menus/title/newgrounds_logo.png");

        // Load random intro message
        LoadWackyText();

        // Play menu music
        Audio.PlayMusic("music/freakyMenu", true);

        // If we already saw the intro this session, skip straight to the title screen
        if (s_initialized)
        {
            _skippedIntro = true;
            _songPosition = 99;
        }
        else
        {
            s_initialized = true;
        }
    }

    private void LoadWackyText()
    {
        try
        {
            // Original FNF: data/introText.txt
            string path = Assets.ResolvePath("data/introText.txt")
                       ?? Assets.ResolvePath("intro_messages.txt")
                       ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "intro_messages.txt");
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && l.Contains("--"))
                    .ToArray();
                if (lines.Length > 0)
                {
                    var rng = new Random();
                    var parts = lines[rng.Next(lines.Length)].Split("--");
                    _wackyLine1 = parts[0].Trim();
                    _wackyLine2 = parts.Length > 1 ? parts[1].Trim() : "";
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(_wackyLine1))
        {
            _wackyLine1 = "shoutouts to tom fulp";
            _wackyLine2 = "lmao";
        }
    }

    public override void Unload()
    {
        _logoSheet?.Dispose();
        _gfSheet?.Dispose();
        _enterSheet?.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Track song position via audio manager
        _songPosition = Audio.MusicPosition / 1000.0; // ms ? s

        // Determine current beat
        int currentBeat = (int)(_songPosition / _beatDuration);

        // ?? Animate sprites (always running so they're ready when intro ends) ??
        _gfAnimTimer += delta;
        if (_gfAnimTimer >= 1f / 24f) { _gfAnimTimer = 0; _gfFrame++; }

        _logoAnimTimer += delta;
        if (_logoAnimTimer >= 1f / 24f) { _logoAnimTimer = 0; _logoFrame++; }

        _enterAnimTimer += delta;
        if (_enterAnimTimer >= 1f / 24f) { _enterAnimTimer = 0; _enterFrame++; }

        // Fade flash using current flash duration
        if (_flashAlpha > 0) _flashAlpha = Math.Max(0, _flashAlpha - delta / _flashDuration);

    // ?? Handle transitioning to main menu ??
        if (_transitioning)
        {
            _transitionTimer -= delta;
            if (_transitionTimer <= 0)
            {
                _transitioning = false; // Prevent calling ChangeScene every frame
                MoveToMainMenu();
            }

            // Allow spamming Enter to skip the transition delay
            if (Input.ConfirmPressed)
            {
                _transitioning = false;
                MoveToMainMenu();
            }
            return;
        }

        // ?? If still in intro, process beat-synced text ??
        if (!_skippedIntro)
        {
            if (currentBeat > _lastBeat)
            {
                for (int b = _lastBeat + 1; b <= currentBeat; b++)
                {
                    ProcessIntroBeat(b);
                }
            }
            _lastBeat = currentBeat;

            // Press Enter to skip intro
            if (Input.ConfirmPressed)
                SkipIntro();
            return;
        }

        // ?? Title screen is showing ??

        // GF dance on each beat (original: danceLeft/danceRight alternation + frame reset)
        if (currentBeat > _lastBeat)
        {
            _gfDanceLeft = !_gfDanceLeft;
            _gfFrame = 0; // Reset animation frame on dance direction switch
            _gfAnimTimer = 0;

            // Logo bump on each beat — restart logo anim
            _logoFrame = 0;
            _logoAnimTimer = 0;

            _lastBeat = currentBeat;
        }

        // Press Enter ? confirm
        if (Input.ConfirmPressed && !_transitioning)
        {
            _enterPressed = true;
            _enterFrame = 0;
            _enterAnimTimer = 0;
            Audio.PlaySound("confirmMenu");
            _flashAlpha = 1f;
            _flashDuration = 1f; // original: flash(WHITE, 1) on confirm
            _transitioning = true;
            _transitionTimer = 2f;
        }

        // Exit game on Escape from title
        if (Input.BackPressed)
        {
            Game.Exit();
        }
    }

    /// <summary>
    /// Process a single intro beat (matching original FNF TitleState.beatHit)
    /// </summary>
    private void ProcessIntroBeat(int beat)
    {
        switch (beat)
        {
            case 1:
                _introLines.Clear();
                _introLines.Add("The");
                _introLines.Add("Funkin Crew Inc");
                break;
            case 3:
                _introLines.Add("presents");
                break;
            case 4:
                _introLines.Clear();
                _showNgSpr = false;
                break;
            case 5:
                _introLines.Add("In association");
                _introLines.Add("with");
                break;
            case 7:
                _introLines.Add("newgrounds");
                _showNgSpr = true;
                break;
            case 8:
                _introLines.Clear();
                _showNgSpr = false;
                break;
            case 9:
                _introLines.Add(_wackyLine1);
                break;
            case 11:
                _introLines.Add(_wackyLine2);
                break;
            case 12:
                _introLines.Clear();
                break;
            case 13:
                _introLines.Add("Friday");
                break;
            case 14:
                _introLines.Add("Night");
                break;
            case 15:
                _introLines.Add("Funkin");
                break;
            case 16:
                SkipIntro();
                break;
        }
    }

    private void SkipIntro()
    {
        if (_skippedIntro) return;
        _skippedIntro = true;
        _introLines.Clear();
        _showNgSpr = false;
        _flashAlpha = 1f;
        _flashDuration = 4f; // original: flash(WHITE, 4) on intro skip
        _gfDanceLeft = false;
    }

    private void MoveToMainMenu()
    {
        Game.Scenes.ChangeScene(new MainMenuScene());
    }

    // ???????????????????????????????????????????????
    //  DRAWING
    // ???????????????????????????????????????????????

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Black background (always)
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
            Color.Black);

        if (_skippedIntro)
        {
            // ?? Title screen ??
            DrawGF(spriteBatch);
            DrawLogo(spriteBatch);
            DrawEnterPrompt(spriteBatch);
        }
        else
        {
            // ?? Intro sequence ??
            DrawIntroText(spriteBatch);

            if (_showNgSpr)
                DrawNGLogo(spriteBatch);
        }

        // Camera flash overlay
        if (_flashAlpha > 0)
        {
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.White * _flashAlpha);
        }

        spriteBatch.End();
    }

    // ?? Intro text (original uses Alphabet bold letters from alphabet_bold.png) ??
    // Original FNF: createCoolText positions at (FlxG.width * 0.5, FlxG.height * 0.45)
    // with offset = -280 + i * 60 per line
    private void DrawIntroText(SpriteBatch spriteBatch)
    {
        var alphabetFont = AlphabetFont.Bold;
        if (alphabetFont != null)
        {
            float centerY = FNFGame.SCREEN_HEIGHT * 0.45f;
            for (int i = 0; i < _introLines.Count; i++)
            {
                string line = _introLines[i];
                float scale = 0.7f;
                float y = centerY - 280 + i * 70;
                // Original: each letter offsets by charWidth with screenCenter(X)
                alphabetFont.DrawStringCentered(spriteBatch, line, y, Color.White, scale);
            }
        }
        else
        {
            var font = Assets.GetFont(36);
            if (font == null) return;
            float centerY = FNFGame.SCREEN_HEIGHT * 0.45f;
            for (int i = 0; i < _introLines.Count; i++)
            {
                string line = _introLines[i];
                var size = font.MeasureString(line);
                float x = (FNFGame.SCREEN_WIDTH - size.X) / 2f;
                float y = centerY - 280 + i * 60;
                font.DrawText(spriteBatch, line, new Vector2(x, y), Color.White);
            }
        }
    }

    // Newgrounds logo — original: setGraphicSize(width*0.8), screenCenter(), y = FlxG.height * 0.52
    private void DrawNGLogo(SpriteBatch spriteBatch)
    {
        if (_ngLogo == null || _ngLogo == Assets.Pixel) return;
        float scale = 0.8f;
        int w = (int)(_ngLogo.Width * scale);
        int h = (int)(_ngLogo.Height * scale);
        int x = (1280 - w) / 2;
        int y = (int)(720 * 0.52f);
        spriteBatch.Draw(_ngLogo, new Rectangle(x, y, w, h), Color.White);
    }

    // GF dancing — original: gfDance at (FlxG.width * 0.4, FlxG.height * 0.07), 1x scale
    private void DrawGF(SpriteBatch spriteBatch)
    {
        if (_gfSheet == null) return;
        var frames = GetGFFrames();
        if (frames == null || frames.Count == 0) return;
        var frame = frames[_gfFrame % frames.Count];
        // Original: 1x scale, position (FlxG.width * 0.4, FlxG.height * 0.07)
        int gfX = (int)(1280 * 0.4f);
        int gfY = (int)(720 * 0.07f);
        float offX = frame.Offset.X;
        float offY = frame.Offset.Y;
        spriteBatch.Draw(_gfSheet.Texture,
            new Vector2(gfX + offX, gfY + offY),
            frame.SourceRect, Color.White);
    }

    private List<SpriteFrame> GetGFFrames()
    {
        string danceAnim = _gfDanceLeft ? "danceLeft" : "danceRight";
        var frames = _gfSheet.GetAnimation(danceAnim);
        if (frames != null && frames.Count > 0) return frames;
        // Split full gfDance in half (original: danceLeft = 0-14, danceRight = 15-29)
        var fullAnim = _gfSheet.GetAnimation("gfDance")
                    ?? _gfSheet.Animations.Values.FirstOrDefault();
        if (fullAnim != null && fullAnim.Count >= 2)
        {
            int half = fullAnim.Count / 2;
            return _gfDanceLeft ? fullAnim.GetRange(0, half) : fullAnim.GetRange(half, fullAnim.Count - half);
        }
        return fullAnim;
    }

    // Logo bumpin — original: logoBumpin at (-150, -100), setGraphicSize(width * 0.4)
    private void DrawLogo(SpriteBatch spriteBatch)
    {
        if (_logoSheet == null) return;
        var frames = _logoSheet.GetAnimation("logo bumpin")
                  ?? _logoSheet.Animations.Values.FirstOrDefault();
        if (frames == null || frames.Count == 0) return;
        var frame = frames[_logoFrame % frames.Count];
        // Original: setGraphicSize(Std.int(width * 0.4)) where width = frameWidth (939)
        // Scale factor = targetWidth / sourceRect.Width per frame
        int fw = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
        float targetW = fw * 0.4f;
        float logoScale = targetW / frame.SourceRect.Width;
        float offX = frame.Offset.X * logoScale;
        float offY = frame.Offset.Y * logoScale;
        int w = (int)(frame.SourceRect.Width * logoScale);
        int h = (int)(frame.SourceRect.Height * logoScale);
        spriteBatch.Draw(_logoSheet.Texture,
            new Rectangle(-150 + (int)offX, -100 + (int)offY, w, h),
            frame.SourceRect, Color.White);
    }

    // "Press Enter to Begin" — original: titleEnter at (100, FlxG.height * 0.8), 1x scale
    private void DrawEnterPrompt(SpriteBatch spriteBatch)
    {
        if (_enterSheet == null) return;
        string animName = _enterPressed ? "ENTER PRESSED" : "Press Enter to Begin";
        var frames = _enterSheet.GetAnimation(animName);
        if (frames == null || frames.Count == 0)
            frames = _enterSheet.Animations.Values.FirstOrDefault();
        if (frames == null || frames.Count == 0) return;

        var frame = frames[_enterFrame % frames.Count];
        // Original: 1x scale, position (100, FlxG.height * 0.8)
        int enterX = 100;
        int enterY = (int)(720 * 0.8f);
        float offX = frame.Offset.X;
        float offY = frame.Offset.Y;
        spriteBatch.Draw(_enterSheet.Texture,
            new Vector2(enterX + offX, enterY + offY),
            frame.SourceRect, Color.White);
    }
}
