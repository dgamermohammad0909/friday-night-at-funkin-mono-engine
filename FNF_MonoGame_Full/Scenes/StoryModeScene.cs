using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Story Mode week-selection — faithful recreation of FNF StoryMenuState.
/// Yellow banner with animated character props, week title images in
/// the centre, track list bottom-left, difficulty selector bottom-right.
/// </summary>
public class StoryModeScene : Scene
{
    // ?? Week data ??
    private readonly List<WeekData> _weeks = new();
    private int _selectedIndex;
    private bool _confirmed;
    private float _confirmTimer;

    // ?? Difficulty ??
    private static readonly string[] DEFAULT_DIFFICULTIES = { "easy", "normal", "hard" };
    private int _difficultyIndex = 1;
    private string[] CurrentDifficulties => _weeks.Count > 0 ? _weeks[_selectedIndex].Difficulties : DEFAULT_DIFFICULTIES;

    // ?? Assets ??
    private readonly Dictionary<string, Texture2D> _weekBanners = new();
    private readonly Dictionary<string, Texture2D> _diffTextures = new();
    private readonly Dictionary<string, SpriteSheet> _propSheets = new();
    private SpriteSheet _arrowsSheet;
    private Texture2D _lockTex;

    // ?? Animation ??
    private int _animFrame;
    private float _animTimer;
    private float _scrollLerp;
    private float _leftArrowTimer;   // >0 = show leftConfirm
    private float _rightArrowTimer;  // >0 = show rightConfirm

    // ?? Prop dance beat tracking (for danceLeft/danceRight alternation) ??
    private int _danceBeat;
    private float _danceBeatTimer;
    private const float DANCE_BPM = 102f; // Original FNF story menu BPM
    // Per-prop resolved animation frames: key = "propName:animName"
    private readonly Dictionary<string, List<SpriteFrame>> _propAnimFrames = new();
    // Per-prop dance state: which dance side each prop is on (true=left)
    private readonly Dictionary<string, bool> _propDanceLeft = new();
    // Per-prop animation start frame (for play-once dance anims)
    private readonly Dictionary<string, int> _propAnimStart = new();
    // Per-prop current animation key (to detect changes)
    private readonly Dictionary<string, string> _propCurrentAnim = new();

    // ?? Constants from original ??
    private const int BANNER_HEIGHT = 400;
    private const int BANNER_TOP = 56;

    public override void Load()
    {
        LoadWeekData();

        // Load difficulty textures (from story_menu/difficulties)
        var allDiffs = new HashSet<string>(DEFAULT_DIFFICULTIES);
        foreach (var w in _weeks)
            foreach (var d in w.Difficulties)
                allDiffs.Add(d);
        foreach (var d in allDiffs)
            _diffTextures[d] = Assets.LoadTexture($"menus/story_menu/difficulties/{d}.png");

        // Load week title banners
        foreach (var w in _weeks)
            _weekBanners[w.Id] = Assets.LoadTexture($"menus/story_menu/weeks/{w.Id}.png");

        // Load character prop spritesheets for the banner
        // Collect all prop names used across all weeks
        var propNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _weeks)
            foreach (var pn in w.PropNames)
                propNames.Add(pn);
        foreach (var pn in propNames)
        {
            var sheet = SpriteSheet.Load(Game, $"menus/story_menu/characters/{pn}");
            sheet ??= SpriteSheet.Load(Game, $"menus/story_menu/props/{pn}");
            if (sheet != null) _propSheets[pn] = sheet;
        }

        _lockTex = Assets.LoadTexture("menus/story_menu/lock.png");

        // Pre-resolve prop animation frames from level JSON definitions
        ResolvePropAnimations();

        // Load difficulty selector arrows spritesheet
        _arrowsSheet = SpriteSheet.Load(Game, "menus/story_menu/arrows");

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    private void LoadWeekData()
    {
        string levelsDir = Assets.ResolveDirectory("data/levels")
                        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "levels");
        if (!Directory.Exists(levelsDir)) return;

        string[] order = { "tutorial", "week1", "week2", "week3", "week4", "week5", "week6", "week7", "weekend1", "sserafim" };

        foreach (var wid in order)
        {
            string jp = Path.Combine(levelsDir, wid + ".json");
            if (!File.Exists(jp)) continue;
            try
            {
                var d = JsonConvert.DeserializeObject<LevelJson>(File.ReadAllText(jp));
                if (d == null) continue;
                _weeks.Add(new WeekData
                {
                    Id = wid,
                    Name = d.Name ?? wid.ToUpper(),
                    Songs = d.Songs ?? new List<string>(),
                    BackgroundColor = ParseHex(d.Background ?? "#F9CF51"),
                    PropNames = ExtractPropNames(d),
                    PropData = ExtractPropData(d),
                    Difficulties = d.Difficulties != null && d.Difficulties.Count > 0
                        ? d.Difficulties.ToArray()
                        : new[] { "easy", "normal", "hard" }
                });
            }
            catch (Exception ex) { Console.WriteLine($"Week load error {wid}: {ex.Message}"); }
        }

        if (_weeks.Count == 0)
            _weeks.Add(new WeekData
            {
                Id = "tutorial", Name = "TEACHING TIME",
                Songs = new() { "tutorial" },
                BackgroundColor = new Color(249, 207, 81),
                PropNames = new() { "gf", "bf" }
            });
        
        // All weeks unlocked (no progression lock)
        for (int i = 0; i < _weeks.Count; i++)
        {
            _weeks[i].Locked = false;
        }
    }

    private List<string> ExtractPropNames(LevelJson lj)
    {
        var result = new List<string>();
        if (lj.Props != null)
        {
            foreach (var p in lj.Props)
            {
                string ap = p.AssetPath ?? "";
                string name = ap.Contains('/') ? ap[(ap.LastIndexOf('/') + 1)..] : ap;
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        return result;
    }

    private List<LevelPropJson> ExtractPropData(LevelJson lj)
    {
        return lj.Props ?? new List<LevelPropJson>();
    }

    private Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length >= 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return new Color(r, g, b);
        }
        return new Color(249, 207, 81);
    }

    public override void Unload()
    {
        foreach (var sheet in _propSheets.Values)
            sheet?.Dispose();
        _propSheets.Clear();
        _arrowsSheet?.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _animTimer += dt;
        if (_animTimer >= 1f / 24f) { _animTimer = 0; _animFrame++; }
        // Beat tracking for prop dance alternation (original FNF: 102 BPM menu music)
        _danceBeatTimer += dt;
        float beatLen = 60f / DANCE_BPM;
        if (_danceBeatTimer >= beatLen) { _danceBeatTimer -= beatLen; _danceBeat++; }
        // Original FNF StoryMenuState: lerpValue 0.16 per frame
        _scrollLerp += (_selectedIndex - _scrollLerp) * 10f * dt;

        if (_confirmed)
        {
            _confirmTimer -= dt;
            if (_confirmTimer <= 0)
            {
                _confirmed = false; // Prevent calling ChangeScene every frame
                var w = _weeks[_selectedIndex];
                if (w.Songs.Count > 0)
                {
                    Audio.StopMusic();
                    Game.Scenes.ChangeScene(new PlayScene(
                        w.Songs[0], CurrentDifficulties[_difficultyIndex],
                        w.Songs, 0, w.Id, 0));
                }
            }
            return;
        }

        if (_weeks.Count == 0)
        {
            if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(new MainMenuScene()); }
            return;
        }

        if (Input.UpPressed)   { _selectedIndex = (_selectedIndex - 1 + _weeks.Count) % _weeks.Count; _difficultyIndex = Math.Clamp(_difficultyIndex, 0, CurrentDifficulties.Length - 1); Audio.PlaySound("scrollMenu"); }
        if (Input.DownPressed)  { _selectedIndex = (_selectedIndex + 1) % _weeks.Count; _difficultyIndex = Math.Clamp(_difficultyIndex, 0, CurrentDifficulties.Length - 1); Audio.PlaySound("scrollMenu"); }
        if (Input.LeftPressed)  { _difficultyIndex = (_difficultyIndex - 1 + CurrentDifficulties.Length) % CurrentDifficulties.Length; _leftArrowTimer = 0.15f; Audio.PlaySound("scrollMenu"); }
        if (Input.RightPressed) { _difficultyIndex = (_difficultyIndex + 1) % CurrentDifficulties.Length; _rightArrowTimer = 0.15f; Audio.PlaySound("scrollMenu"); }
        if (_leftArrowTimer > 0) _leftArrowTimer -= dt;
        if (_rightArrowTimer > 0) _rightArrowTimer -= dt;

        if (Input.ConfirmPressed)
        {
            // Block confirm on locked weeks (original: plays locked sound, shows lock icon)
            if (_weeks[_selectedIndex].Locked)
            {
                Audio.PlaySound("scrollMenu");
            }
            else
            {
                Audio.PlaySound("confirmMenu");
                _confirmed = true;
                _confirmTimer = 1f;
            }
        }
        if (Input.BackPressed)    { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(new MainMenuScene()); }
    }

    // ———————————————————————————————————
    //  DRAWING — matches original FNF StoryMenuState Z-order:
    //  1. yellowBG  2. grpWeekCharacters  3. blackBarTop
    //  4. grpWeekText (ON TOP)  5. arrows+difficulty  6. scoreText+weekTitle  7. tracklist
    // ———————————————————————————————————
    public override void Draw(SpriteBatch sb)
    {
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Full-screen black background (original FNF base game)
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black);

        if (_weeks.Count == 0)
        {
            var noWeeksFont = Assets.GetFont(24);
            if (noWeeksFont != null)
                noWeeksFont.DrawText(sb, "No weeks found. Check your content directory.",
                    new Vector2(FNFGame.SCREEN_WIDTH / 2 - 260, FNFGame.SCREEN_HEIGHT / 2 - 12), Color.Gray);
            sb.End();
            return;
        }

        var cw = _weeks[_selectedIndex];

        // 1) Yellow/colored banner background
        sb.Draw(Assets.Pixel, new Rectangle(0, BANNER_TOP, FNFGame.SCREEN_WIDTH, BANNER_HEIGHT), cw.BackgroundColor);

        // 2) Character props on banner
        DrawProps(sb, cw);

        // 3) Black top bar (covers anything that bleeds above banner)
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, BANNER_TOP), Color.Black);

        // 4) Week items ON TOP of banner (original FNF: grpWeekText added after yellowBG)
        //    Items have black backgrounds so they cover the banner cleanly.
        DrawWeekTitles(sb);

        // 5) Difficulty arrows + sprite (on top of week items)
        DrawDifficulty(sb);

        // 6) Score text + week title (on top of everything in the top bar)
        var tf = Assets.GetFont(32);
        if (tf != null)
        {
            int weekScore = HighscoreManager.GetWeekScore(cw.Id, CurrentDifficulties[_difficultyIndex]);
            tf.DrawText(sb, $"WEEK SCORE:{weekScore}", new Vector2(10, 10), Color.White);
            tf.DrawText(sb, cw.Name.ToUpper(), new Vector2(FNFGame.SCREEN_WIDTH * 0.7f, 10), Color.White * 0.7f);
        }

        // Controller button prompts (bottom-right, shown when gamepad connected)
        if (Input.GamePadConnected && !_confirmed)
        {
            int promptSize = 40;
            int promptY = FNFGame.SCREEN_HEIGHT - 54;
            var hintFont = Assets.GetFont(18);

            int selectLabelW = hintFont != null ? (int)hintFont.MeasureString("Select").X : 40;
            int backLabelW = hintFont != null ? (int)hintFont.MeasureString("Back").X : 30;
            int gap = 8;
            int sectionGap = 28;
            int totalW = promptSize + gap + selectLabelW + sectionGap + promptSize + gap + backLabelW;
            int px = FNFGame.SCREEN_WIDTH - totalW - 24;

            // Confirm: circle + label
            Assets.DrawButtonPrompt(sb, Input.ConfirmButton, px, promptY, promptSize, 1f);
            if (hintFont != null)
                hintFont.DrawText(sb, "Select", new Vector2(px + promptSize + gap, promptY + (promptSize - 18) / 2), Color.White * 0.95f);

            // Cancel: circle + label
            int cancelX = px + promptSize + gap + selectLabelW + sectionGap;
            Assets.DrawButtonPrompt(sb, Input.CancelButton, cancelX, promptY, promptSize, 1f);
            if (hintFont != null)
                hintFont.DrawText(sb, "Back", new Vector2(cancelX + promptSize + gap, promptY + (promptSize - 18) / 2), Color.White * 0.95f);
        }

        // 7) Track list (bottom-left, on top of week items like original)
        DrawTrackList(sb, cw);

        // 8) Confirm flash overlay
        if (_confirmed)
        {
            float bannerFlash = ((int)(_confirmTimer * 10) % 2 == 0) ? 0.35f : 0f;
            sb.Draw(Assets.Pixel, new Rectangle(0, BANNER_TOP, FNFGame.SCREEN_WIDTH, BANNER_HEIGHT), Color.White * bannerFlash);
        }

        sb.End();
    }

    private void DrawProps(SpriteBatch sb, WeekData w)
    {
        // Original FNF StoryMenuState positioning:
        // x = (FlxG.width * 0.25) * (1 + i) - 150 + offsets[0]
        // y = 70 + offsets[1]
        // scale applied from JSON directly (setGraphicSize)
        int propCount = w.PropNames.Count;
        if (propCount == 0) return;

        for (int i = 0; i < propCount; i++)
        {
            string pName = w.PropNames[i];
            if (!_propSheets.TryGetValue(pName, out var sheet)) continue;

            // Determine which animation to play from the level JSON prop data
            List<SpriteFrame> frames = null;
            bool useConfirm = _confirmed && pName.Equals("bf", StringComparison.OrdinalIgnoreCase);
            bool isDance = false;
            string resolvedAnimKey = null;

            if (useConfirm)
            {
                _propAnimFrames.TryGetValue($"{pName}:confirm", out frames);
                resolvedAnimKey = $"{pName}:confirm";
            }

            if (frames == null && i < w.PropData.Count)
            {
                var pd = w.PropData[i];
                bool hasDance = _propAnimFrames.ContainsKey($"{pName}:danceLeft")
                             && _propAnimFrames.ContainsKey($"{pName}:danceRight");

                if (hasDance)
                {
                    isDance = true;
                    // Alternate dance on beat (original: danceEvery beats)
                    int interval = pd.DanceEvery > 0 ? pd.DanceEvery : 2;
                    if (_danceBeat % interval == 0)
                    {
                        string key = $"{pName}:danceState";
                        if (!_propDanceLeft.ContainsKey(key)) _propDanceLeft[key] = true;
                        // Toggle on new beats
                        int expectedBeat = _danceBeat / interval;
                        bool left = expectedBeat % 2 == 0;
                        _propDanceLeft[key] = left;
                    }
                    string dKey = $"{pName}:danceState";
                    bool danceLeft = _propDanceLeft.ContainsKey(dKey) && _propDanceLeft[dKey];
                    resolvedAnimKey = danceLeft ? $"{pName}:danceLeft" : $"{pName}:danceRight";
                    _propAnimFrames.TryGetValue(resolvedAnimKey, out frames);
                }
                else
                {
                    resolvedAnimKey = $"{pName}:idle";
                    _propAnimFrames.TryGetValue(resolvedAnimKey, out frames);
                }
            }

            // Fallback: use spritesheet "idle" or "confirm" directly
            if (frames == null)
            {
                if (useConfirm)
                    sheet.Animations.TryGetValue("confirm", out frames);
                if (frames == null)
                    sheet.Animations.TryGetValue("idle", out frames);
                frames ??= sheet.Animations.Values.FirstOrDefault();
                resolvedAnimKey ??= $"{pName}:fallback";
            }
            if (frames == null || frames.Count == 0) continue;

            // Track animation changes: reset start frame when animation switches
            // (e.g., danceLeft ? danceRight, or idle ? confirm)
            string propStateKey = $"{pName}:{i}";
            if (!_propCurrentAnim.TryGetValue(propStateKey, out var prevAnimKey) || prevAnimKey != resolvedAnimKey)
            {
                _propCurrentAnim[propStateKey] = resolvedAnimKey;
                _propAnimStart[propStateKey] = _animFrame;
            }

            // Calculate frame index:
            // Dance anims: play once and hold on last frame (matches original FNF)
            // Idle/other anims: loop continuously
            int animProgress = _animFrame - _propAnimStart.GetValueOrDefault(propStateKey);
            SpriteFrame frame;
            if (isDance)
                frame = frames[Math.Min(animProgress, frames.Count - 1)];
            else
                frame = frames[animProgress % frames.Count];

            float scale = 1.0f;
            float offsetX = 0, offsetY = 0;
            if (i < w.PropData.Count)
            {
                var pd = w.PropData[i];
                if (pd.Scale > 0) scale = pd.Scale;
                if (pd.Offsets != null && pd.Offsets.Count >= 2)
                {
                    offsetX = pd.Offsets[0];
                    offsetY = pd.Offsets[1];
                }
            }

            // Use logical (untrimmed) frame size when available (Sparrow atlas trimming)
            // For rotated frames without FrameWidth/Height, swap source rect dimensions
            int logicalW = frame.FrameWidth > 0 ? frame.FrameWidth
                : (frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width);
            int logicalH = frame.FrameHeight > 0 ? frame.FrameHeight
                : (frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height);

            // Apply JSON scale to logical frame dimensions
            float finalScale = scale;

            // Safety clamp: if prop is taller than the available area (banner + overflow
            // into gray zone), scale it down proportionally. Original FNF allows props to
            // extend below the banner into the gray area, so we use a generous limit.
            float maxHeight = BANNER_HEIGHT * 1.3f;
            float scaledH = logicalH * finalScale;
            if (scaledH > maxHeight)
                finalScale *= maxHeight / scaledH;

            // Original FNF positioning formula:
            // x = (screenWidth * 0.25) * (1 + propIndex) - 150 + offsets[0]
            // y = 70 + offsets[1]   (~BANNER_TOP + 14)
            // Offsets are raw pixel values, NOT scaled by fitScale.
            int x = (int)((FNFGame.SCREEN_WIDTH * 0.25f) * (1 + i) - 150f + offsetX);
            int y = (int)(BANNER_TOP + 14 + offsetY);

            // Original FNF: BF faces left (toward opponent). Spritesheet has BF facing right, so flip.
            var effects = pName.Equals("bf", StringComparison.OrdinalIgnoreCase)
                ? SpriteEffects.FlipHorizontally
                : SpriteEffects.None;

            if (frame.Rotated)
            {
                // Rotated Sparrow frames: stored 90° clockwise in atlas.
                // Draw with -90° rotation, origin at (sourceWidth, 0) to unrotate.
                Vector2 offset = frame.Offset;
                bool wantFlipH = (effects & SpriteEffects.FlipHorizontally) != 0;
                if (wantFlipH && frame.FrameWidth > 0)
                    offset = new Vector2(frame.FrameWidth - frame.SourceRect.Height - offset.X, offset.Y);

                float drawX = x + offset.X * finalScale;
                float drawY = y + offset.Y * finalScale;
                var rotOrigin = new Vector2(frame.SourceRect.Width, 0);
                var rotEffects = SpriteEffects.None;
                if (wantFlipH)
                    rotEffects = SpriteEffects.FlipVertically;

                sb.Draw(sheet.Texture,
                    new Vector2(drawX, drawY),
                    frame.SourceRect,
                    Color.White,
                    -MathF.PI / 2f,
                    rotOrigin,
                    new Vector2(finalScale, finalScale),
                    rotEffects,
                    0f);
            }
            else
            {
                // Visible content rect (trimmed) scaled proportionally
                int drawW = (int)(frame.SourceRect.Width * finalScale);
                int drawH = (int)(frame.SourceRect.Height * finalScale);

                // Apply frame offset (trimmed whitespace) so the visible content
                // is positioned correctly within the logical frame area.
                Vector2 offset = frame.Offset;
                // Mirror X offset when flipped horizontally (matches AnimatedSprite.Draw)
                if ((effects & SpriteEffects.FlipHorizontally) != 0 && frame.FrameWidth > 0)
                    offset = new Vector2(frame.FrameWidth - frame.SourceRect.Width - offset.X, offset.Y);

                int drawX = x + (int)(offset.X * finalScale);
                int drawY = y + (int)(offset.Y * finalScale);

                sb.Draw(sheet.Texture, new Rectangle(drawX, drawY, drawW, drawH), frame.SourceRect, Color.White, 0f, Vector2.Zero, effects, 0f);
            }
        }
    }

    /// <summary>
    /// Pre-resolve prop animation frame lists from level JSON animation definitions.
    /// Creates sub-frame lists keyed as "propName:animName" using frameIndices when specified.
    /// </summary>
    private void ResolvePropAnimations()
    {
        _propAnimFrames.Clear();
        foreach (var w in _weeks)
        {
            for (int i = 0; i < w.PropNames.Count && i < w.PropData.Count; i++)
            {
                string pName = w.PropNames[i];
                var pd = w.PropData[i];
                if (pd.Animations == null || pd.Animations.Count == 0) continue;
                if (!_propSheets.TryGetValue(pName, out var sheet)) continue;

                foreach (var anim in pd.Animations)
                {
                    if (string.IsNullOrEmpty(anim.Name)) continue;
                    string key = $"{pName}:{anim.Name}";
                    if (_propAnimFrames.ContainsKey(key)) continue;

                    // Find parent animation in spritesheet by prefix
                    List<SpriteFrame> parentFrames = null;
                    if (!string.IsNullOrEmpty(anim.Prefix))
                        parentFrames = sheet.GetAnimationFuzzy(anim.Prefix);
                    parentFrames ??= sheet.Animations.Values.FirstOrDefault();
                    if (parentFrames == null || parentFrames.Count == 0) continue;

                    if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                    {
                        // Build sub-animation from specific frame indices
                        var subFrames = new List<SpriteFrame>();
                        foreach (int idx in anim.FrameIndices)
                        {
                            if (idx >= 0 && idx < parentFrames.Count)
                                subFrames.Add(parentFrames[idx]);
                        }
                        if (subFrames.Count > 0)
                            _propAnimFrames[key] = subFrames;
                    }
                    else
                    {
                        _propAnimFrames[key] = parentFrames;
                    }
                }
            }
        }
    }

    private void DrawWeekTitles(SpriteBatch sb)
    {
        // Original FNF MenuItem.update():
        // y = (targetY * 120) + 480 + (FlxG.height * 0.10)
        // At 720p: base = 480 + 72 = 552
        int topY = 480 + (int)(FNFGame.SCREEN_HEIGHT * 0.10f);
        int rowH = 120;

        int bannerBottom = BANNER_TOP + BANNER_HEIGHT;

        for (int i = 0; i < _weeks.Count; i++)
        {
            float relPos = i - _scrollLerp;
            int y = topY + (int)(relPos * rowH);
            // Cull items fully off-screen or fully inside the banner area
            if (y + rowH < 0 || y > FNFGame.SCREEN_HEIGHT) continue;
            // Skip items entirely above (inside) the banner — they scroll behind it
            if (y + rowH <= bannerBottom) continue;

            bool sel = i == _selectedIndex;
            float alpha = sel ? 1f : 0.6f;

            // On confirm: non-selected items disappear, selected flickers (original behavior)
            if (_confirmed)
            {
                if (sel)
                    alpha = ((int)(_confirmTimer * 10) % 2 == 0) ? 1f : 0.6f;
                else
                    alpha = 0f;
            }

            // Per-item background — only drawn below the yellow banner.
            // Original FNF: items are transparent PNGs over a full-screen black bg.
            // The yellow banner shows through when items scroll into it.
            if (y + rowH > bannerBottom)
            {
                int blackY = Math.Max(y, bannerBottom);
                int blackH = y + rowH - blackY;
                sb.Draw(Assets.Pixel, new Rectangle(0, blackY, FNFGame.SCREEN_WIDTH, blackH),
                    Color.Black);
            }

            // Original FNF: selected item has a flashingBG that flashes white on confirm
            if (_confirmed && sel)
            {
                int flashY = Math.Max(y, bannerBottom);
                int flashH = y + rowH - flashY;
                if (flashH > 0)
                {
                    float flash = ((int)(_confirmTimer * 10) % 2 == 0) ? 0.3f : 0f;
                    sb.Draw(Assets.Pixel, new Rectangle(0, flashY, FNFGame.SCREEN_WIDTH, flashH),
                        Color.Cyan * flash);
                }
            }

            // Only draw week title image/text below the banner bottom edge.
            // Items that partially overlap the banner are clipped by not drawing
            // the image when its center is above the banner — matches original FNF
            // where items scroll behind the yellow banner + character props.
            int imgCenterY = y + rowH / 2;
            if (imgCenterY < bannerBottom) continue;

            if (_weekBanners.TryGetValue(_weeks[i].Id, out var banner) && banner != Assets.Pixel)
            {
                int bw = banner.Width;
                int bh = banner.Height;
                // Center horizontally on screen (original FNF: screenCenter(X))
                int itemX = (FNFGame.SCREEN_WIDTH - bw) / 2;
                int imgY = y + (rowH - bh) / 2;
                sb.Draw(banner, new Rectangle(itemX, imgY, bw, bh), Color.White * alpha);

                if (_weeks[i].Locked && _lockTex != null && _lockTex != Assets.Pixel)
                {
                    int lockSize = 50;
                    int lx = itemX + bw - lockSize - 8;
                    sb.Draw(_lockTex, new Rectangle(lx, imgY + 4, lockSize, lockSize), Color.White * alpha);
                }
            }
            else
            {
                // Fallback text rendering centered
                var alphabetFont = AlphabetFont.Bold;
                if (alphabetFont != null)
                {
                    float sc = sel ? 0.55f : 0.45f;
                    alphabetFont.DrawStringCentered(sb, _weeks[i].Name, y + rowH / 2 - 20, Color.White * alpha, sc);
                }
                else
                {
                    var f = Assets.GetFont(sel ? 28 : 22);
                    if (f != null)
                    {
                        var s = f.MeasureString(_weeks[i].Name);
                        f.DrawText(sb, _weeks[i].Name, new Vector2((FNFGame.SCREEN_WIDTH - s.X) / 2, y + rowH / 2 - s.Y / 2), Color.White * alpha);
                    }
                }
            }
        }
    }

    private void DrawTrackList(SpriteBatch sb, WeekData w)
    {
        // Original FNF: single FlxText, vcr.ttf size 32, center-aligned,
        // ALL text same pink/purple color (0xE557E8), UPPERCASE.
        // y = yellowBG.x(0) + yellowBG.height(400) + 100 = 500
        // Content: "TRACKS\n\nSONGNAME\nSONGNAME" (double newline after header)
        int cx = (int)(FNFGame.SCREEN_WIDTH * 0.05f) + 80;
        int y = BANNER_HEIGHT + 100;
        Color trackColor = new Color(0xE5, 0x57, 0xE8);

        var font = Assets.GetFont(32);
        if (font != null)
        {
            var ts = font.MeasureString("TRACKS");
            font.DrawText(sb, "TRACKS", new Vector2(cx - ts.X / 2, y), trackColor);

            // Double newline gap then song names (all same color + size)
            for (int i = 0; i < w.Songs.Count; i++)
            {
                string n = FormatSongName(w.Songs[i]).ToUpper();
                var ns = font.MeasureString(n);
                font.DrawText(sb, n, new Vector2(cx - ns.X / 2, y + 70 + i * 38), trackColor);
            }
        }
    }

    private string FormatSongName(string n)
    {
        var w = n.Replace("-", " ").Replace("_", " ").Split(' ');
        return string.Join(" ", w.Select(x => x.Length > 0 ? char.ToUpper(x[0]) + (x.Length > 1 ? x[1..] : "") : ""));
    }

    private void DrawDifficulty(SpriteBatch sb)
    {
        // Difficulty arrows to the RIGHT of the centered week banner.
        // Original FNF: leftArrow.x = weekItem.x + weekItem.width + 10
        int weekBannerWidth = 400;
        if (_weeks.Count > 0 && _weekBanners.TryGetValue(_weeks[_selectedIndex].Id, out var selBanner) && selBanner != Assets.Pixel)
            weekBannerWidth = selBanner.Width;
        int weekRightEdge = (FNFGame.SCREEN_WIDTH + weekBannerWidth) / 2;

        string diff = CurrentDifficulties[_difficultyIndex];
        // Y follows selected week item
        int topY = 480 + (int)(FNFGame.SCREEN_HEIGHT * 0.10f);
        float selRelPos = _selectedIndex - _scrollLerp;
        int baseY = topY + (int)(selRelPos * 120) + 10;

        if (_arrowsSheet != null)
        {
            string leftAnim = _leftArrowTimer > 0 ? "leftConfirm" : "leftIdle";
            string rightAnim = _rightArrowTimer > 0 ? "rightConfirm" : "rightIdle";
            var leftFrames = _arrowsSheet.GetAnimation(leftAnim) ?? _arrowsSheet.GetAnimation("leftIdle");
            var rightFrames = _arrowsSheet.GetAnimation(rightAnim) ?? _arrowsSheet.GetAnimation("rightIdle");

            // Scale arrows + difficulty to 0.9x — newer engine assets are slightly larger
            float uiScale = 0.9f;
            int laW = (int)((leftFrames?[0]?.SourceRect.Width ?? 48) * uiScale);
            int laH = (int)((leftFrames?[0]?.SourceRect.Height ?? 85) * uiScale);
            int raW = (int)((rightFrames?[0]?.SourceRect.Width ?? 47) * uiScale);
            int raH = (int)((rightFrames?[0]?.SourceRect.Height ?? 85) * uiScale);

            // Difficulty sprite image
            bool hasDiffImg = _diffTextures.TryGetValue(diff, out var diffTex)
                           && diffTex != null && diffTex != Assets.Pixel;
            // Scale difficulty image: newer engine images are ~2x the original base game size
            int diffW = hasDiffImg ? (int)(diffTex.Width * 0.8f) : 120;
            int diffH = hasDiffImg ? (int)(diffTex.Height * 0.8f) : 48;

            int gap = 10;
            int totalDiffW = laW + gap + diffW + gap + raW;

            // Center the difficulty section in the space to the right of the banner
            int availableRight = FNFGame.SCREEN_WIDTH - weekRightEdge;
            int laX = weekRightEdge + (availableRight - totalDiffW) / 2;
            // Clamp so it doesn't overlap the banner or go off screen
            laX = Math.Max(weekRightEdge + 5, Math.Min(laX, FNFGame.SCREEN_WIDTH - totalDiffW - 5));

            // Left arrow
            if (leftFrames != null && leftFrames.Count > 0)
                sb.Draw(_arrowsSheet.Texture, new Rectangle(laX, baseY, laW, laH),
                    leftFrames[0].SourceRect, Color.White);

            // Difficulty image/text centered vertically with arrows
            int diffX = laX + laW + gap;
            int diffY = baseY + (laH - diffH) / 2;
            if (hasDiffImg)
            {
                sb.Draw(diffTex, new Rectangle(diffX, diffY, diffW, diffH), Color.White);
            }
            else
            {
                var diffFont = Assets.GetFont(20);
                if (diffFont != null)
                {
                    string diffText = diff.ToUpper();
                    var ts = diffFont.MeasureString(diffText);
                    diffFont.DrawText(sb, diffText, new Vector2(diffX, baseY + (laH - ts.Y) / 2), Color.White);
                    diffW = (int)ts.X;
                }
            }

            // Right arrow
            int raX = diffX + diffW + gap;
            if (rightFrames != null && rightFrames.Count > 0)
                sb.Draw(_arrowsSheet.Texture, new Rectangle(raX, baseY, raW, raH),
                    rightFrames[0].SourceRect, Color.White);
        }
    }
}

// ?? JSON models ??
public class LevelJson
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("songs")] public List<string> Songs { get; set; }
    [JsonProperty("background")] public string Background { get; set; }
    [JsonProperty("titleAsset")] public string TitleAsset { get; set; }
    [JsonProperty("props")] public List<LevelPropJson> Props { get; set; }
    [JsonProperty("difficulties")] public List<string> Difficulties { get; set; }
}

public class LevelPropJson
{
    [JsonProperty("assetPath")] public string AssetPath { get; set; }
    [JsonProperty("scale")] public float Scale { get; set; } = 1f;
    [JsonProperty("offsets")] public List<float> Offsets { get; set; }
    [JsonProperty("animations")] public List<LevelPropAnimJson> Animations { get; set; }
    [JsonProperty("danceEvery")] public int DanceEvery { get; set; }
}

public class LevelPropAnimJson
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("prefix")] public string Prefix { get; set; }
    [JsonProperty("frameRate")] public int FrameRate { get; set; } = 24;
    [JsonProperty("frameIndices")] public int[] FrameIndices { get; set; }
}

public class WeekData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Songs { get; set; } = new();
    public Color BackgroundColor { get; set; }
    public List<string> PropNames { get; set; } = new();
    public List<LevelPropJson> PropData { get; set; } = new();
    public string[] Difficulties { get; set; } = { "easy", "normal", "hard" };
    public bool Locked { get; set; }
}
