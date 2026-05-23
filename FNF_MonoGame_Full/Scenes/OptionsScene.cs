using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options scene � matches original FNF OptionsState with Preferences, Controls, Offsets.
/// Uses checkerboard background and AlphabetFont for menu items.
/// </summary>
public class OptionsScene : Scene
{
    // Main categories
    private readonly string[] _categories = { "Preferences", "Controls", "Offset", "Save Data", "Back" };
    private int _categoryIndex;
    private float _categoryScrollLerp;
    private float _alphabetBob; // global timer for selected-item character Y-bob
    private bool _inSubMenu;

    // Preferences items (original FNF: PreferencesMenu + GameplayChangersSubState)
    private readonly (string Name, string Description)[] _prefItems =
    {
        ("Naughtyness", "When enabled, raunchy content (such as swearing, etc.) is displayed."),
        ("Downscroll", "When enabled, notes move downwards toward the strumline at the bottom of the screen."),
        ("Flashing Lights", "When disabled, flashing effects are dampened. Useful for people with photosensitive epilepsy."),
        ("Camera Zoom", "When enabled, the camera bounces during songs."),
        ("Pause on Unfocus", "When enabled, the game automatically pauses when losing focus."),
        ("Music Volume", "Adjust background music volume."),
        ("SFX Volume", "Adjust sound effects volume."),
        ("Back", string.Empty)
    };
    private int _prefIndex;
    private float _prefScrollLerp;

    // Controls display
    private bool _inControls;
    private bool _inSaveData;
    private int _controlIndex;
    private readonly string[] _controlNames = {
        "Left", "Down", "Up", "Right", "Accept", "Back", "Pause", "Back to Menu"
    };
    private bool _waitingForKey;
    private int _rebindTarget = -1; // index into NoteKeysAlt (0-3)
    private bool _controlOnTab; // true = tab bar focused, Left/Right switches tabs
    private float _selFlash; // selection highlight flash timer

    // Controller bindings tab
    private int _controlTab; // 0=Keyboard, 1=Controller
    private int _gpBindIndex; // selected row in controller bindings
    private int _gpBindCol;   // 0=DPad, 1=Face, 2=Trigger
    private bool _waitingForButton;
    private readonly string[] _gpBindNames = { "Left", "Down", "Up", "Right" };
    private readonly string[] _gpBindSlots = { "D-Pad", "Face", "Bumper/Trigger" };

    // Controller button sprites (Kenney Input Prompts)
    private readonly Dictionary<string, Texture2D> _buttonSprites = new();

    // Offset calibration
    private bool _inOffset;
    private bool _pendingOffsetsMusic;
    private float _offsetBeatTimer;
    private float _offsetBeatBPM = 120f; // calibration BPM
    private float _offsetBeatVisual; // visual indicator pulse (0-1)

    // Checkerboard scroll
    private float _checkerScrollY;

    // Background texture (original uses same menuBG as main menu)
    private Texture2D _bgTex;

    // Spritesheet assets
    private SpriteSheet _checkboxSheet;
    private int _checkAnimFrame;
    private float _checkAnimTimer;

    public override void Load()
    {
        // Load values from save data
        var data = HighscoreManager.Data;

        // Load background (same as main menu)
        _bgTex = Assets.LoadTexture("menus/main_menu/background.png");

        // Load checkbox spritesheet for toggle options
        _checkboxSheet = SpriteSheet.Load(Game, "menus/options/checkbox");

        // Load controller button sprites (Kenney Input Prompts)
        string[] spriteNames = {
            "xbox_button_color_a", "xbox_button_color_b", "xbox_button_color_x", "xbox_button_color_y",
            "xbox_dpad_up", "xbox_dpad_down", "xbox_dpad_left", "xbox_dpad_right", "xbox_dpad",
            "xbox_lb", "xbox_rb", "xbox_lt", "xbox_rt",
            "xbox_button_start", "xbox_button_back", "xbox_stick_l"
        };
        foreach (var name in spriteNames)
        {
            var tex = Assets.LoadTexture($"game/ui/controller/{name}.png");
            if (tex != null && tex != Assets.Pixel)
                _buttonSprites[name] = tex;
        }

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    public override void Unload()
    {
        _checkboxSheet?.Dispose();
        HighscoreManager.SavePreferences();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _checkerScrollY -= dt * 30f;
        _checkAnimTimer += dt;
        if (_checkAnimTimer >= 1f / 24f) { _checkAnimTimer = 0; _checkAnimFrame++; }
        _selFlash += dt * 6f;
        _alphabetBob += dt;
        _categoryScrollLerp += (_categoryIndex - _categoryScrollLerp) * Math.Min(1f, dt * 14f);

        if (_inControls)
        {
            UpdateControls();
            return;
        }
        if (_inOffset)
        {
            UpdateOffset(dt);
            return;
        }
        if (_inSaveData)
        {
            UpdateSaveData();
            return;
        }
        if (_inSubMenu)
        {
            UpdatePreferences(dt);
            return;
        }

        // Main category navigation
        if (Input.UpPressed)
        {
            _categoryIndex = (_categoryIndex - 1 + _categories.Length) % _categories.Length;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.DownPressed)
        {
            _categoryIndex = (_categoryIndex + 1) % _categories.Length;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.ConfirmPressed)
        {
            Audio.PlaySound("confirmMenu");
            switch (_categoryIndex)
            {
                case 0: _inSubMenu = true; _prefIndex = 0; break;
                case 1: _inControls = true; _controlIndex = 0; break;
                case 2:
                    _inOffset = true;
                    _pendingOffsetsMusic = true;
                    break;
                case 3:
                    _inSaveData = true;
                    break;
                case 4:
                    Game.Scenes.ChangeScene(new MainMenuScene());
                    break;
            }
        }
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new MainMenuScene());
        }
    }

    private void UpdatePreferences(float dt)
    {
        var data = HighscoreManager.Data;
        _prefScrollLerp += (_prefIndex - _prefScrollLerp) * 10f * dt;

        if (Input.UpPressed)
        {
            _prefIndex = (_prefIndex - 1 + _prefItems.Length) % _prefItems.Length;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.DownPressed)
        {
            _prefIndex = (_prefIndex + 1) % _prefItems.Length;
            Audio.PlaySound("scrollMenu");
        }

        float step = 0.05f;
        bool changed = false;

        // Toggle or adjust
        if (Input.ConfirmPressed || Input.LeftPressed || Input.RightPressed)
        {
            switch (_prefIndex)
            {
                case 0: data.Naughtyness = !data.Naughtyness; changed = true; break;
                case 1: data.Downscroll = !data.Downscroll; changed = true; break;
                case 2: data.FlashingLights = !data.FlashingLights; changed = true; break;
                case 3: data.CameraZoom = !data.CameraZoom; changed = true; break;
                case 4: data.AutoPause = !data.AutoPause; changed = true; break;
                case 5: // Music Volume
                    if (Input.LeftPressed) data.MusicVolume = Math.Max(0, data.MusicVolume - step);
                    if (Input.RightPressed) data.MusicVolume = Math.Min(1, data.MusicVolume + step);
                    Audio.MusicVolume = data.MusicVolume;
                    changed = true;
                    break;
                case 6: // SFX Volume
                    if (Input.LeftPressed) data.SfxVolume = Math.Max(0, data.SfxVolume - step);
                    if (Input.RightPressed) data.SfxVolume = Math.Min(1, data.SfxVolume + step);
                    Audio.SfxVolume = data.SfxVolume;
                    if (Input.LeftPressed || Input.RightPressed) Audio.PlaySound("scrollMenu");
                    changed = true;
                    break;
                case 7: // Back
                    if (Input.ConfirmPressed) { _inSubMenu = false; Audio.PlaySound("cancelMenu"); }
                    break;
            }
            if (changed) HighscoreManager.SavePreferences();
        }

        if (Input.BackPressed)
        {
            _inSubMenu = false;
            Audio.PlaySound("cancelMenu");
        }
    }

    private void UpdateControls()
    {
        // Key capture mode: wait for a key press and assign it
        if (_waitingForKey)
        {
            if (Input.BackPressed)
            {
                _waitingForKey = false;
                return;
            }
            var key = Input.GetAnyKeyPressed();
            if (key.HasValue && _rebindTarget >= 0 && _rebindTarget < 4)
            {
                Input.NoteKeysAlt[_rebindTarget] = key.Value;
                Input.SaveBindings();
                Audio.PlaySound("confirmMenu");
                _waitingForKey = false;
            }
            return;
        }

        // Button capture mode: wait for a gamepad button press
        if (_waitingForButton)
        {
            if (Input.BackPressed)
            {
                _waitingForButton = false;
                return;
            }
            var btn = Input.GetAnyButtonPressed();
            if (btn.HasValue)
            {
                if (_gpBindIndex >= 0 && _gpBindIndex < 4)
                {
                    switch (_gpBindCol)
                    {
                        case 0: Input.NoteButtons[_gpBindIndex] = btn.Value; break;
                        case 1: Input.NoteFaceButtons[_gpBindIndex] = btn.Value; break;
                        case 2: Input.NoteTriggerButtons[_gpBindIndex] = btn.Value; break;
                    }
                }
                else if (_gpBindIndex == 4)
                {
                    Input.ConfirmButton = btn.Value;
                }
                else if (_gpBindIndex == 5)
                {
                    Input.CancelButton = btn.Value;
                }
                else if (_gpBindIndex == 6)
                {
                    Input.PauseButton = btn.Value;
                }
                else if (_gpBindIndex == 7)
                {
                    Input.SwitchCharButton = btn.Value;
                }
                Input.SaveBindings();
                Audio.PlaySound("confirmMenu");
                _waitingForButton = false;
            }
            return;
        }

        // Tab switching: Q/E or LB/RB (always available)
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Q) ||
            Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder))
        {
            _controlTab = (_controlTab - 1 + 2) % 2;
            _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
            Audio.PlaySound("scrollMenu");
            return;
        }
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.E) ||
            Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightShoulder))
        {
            _controlTab = (_controlTab + 1) % 2;
            _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
            Audio.PlaySound("scrollMenu");
            return;
        }

        // Tab bar focused: Left/Right switches tabs, Down enters bindings
        if (_controlOnTab)
        {
            if (Input.LeftPressed || Input.RightPressed)
            {
                _controlTab = (_controlTab + 1) % 2;
                _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.DownPressed || Input.ConfirmPressed)
            {
                _controlOnTab = false;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.BackPressed)
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
            return;
        }

        if (_controlTab == 0)
        {
            // Keyboard bindings tab
            if (Input.UpPressed)
            {
                if (_controlIndex == 0)
                {
                    _controlOnTab = true;
                    Audio.PlaySound("scrollMenu");
                    return;
                }
                _controlIndex = (_controlIndex - 1 + _controlNames.Length) % _controlNames.Length;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.DownPressed)
            {
                _controlIndex = (_controlIndex + 1) % _controlNames.Length;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.ConfirmPressed && _controlIndex < 4)
            {
                _waitingForKey = true;
                _rebindTarget = _controlIndex;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.BackPressed || (Input.ConfirmPressed && _controlIndex == _controlNames.Length - 1))
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
        }
        else
        {
            // Controller bindings tab (4 notes + confirm + cancel + pause + switch-char + back = 9 rows)
            int totalRows = 9;
            if (Input.UpPressed)
            {
                if (_gpBindIndex == 0)
                {
                    _controlOnTab = true;
                    Audio.PlaySound("scrollMenu");
                    return;
                }
                _gpBindIndex = (_gpBindIndex - 1 + totalRows) % totalRows;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.DownPressed)
            {
                _gpBindIndex = (_gpBindIndex + 1) % totalRows;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.LeftPressed && _gpBindIndex < 4)
            {
                _gpBindCol = (_gpBindCol - 1 + 3) % 3;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.RightPressed && _gpBindIndex < 4)
            {
                _gpBindCol = (_gpBindCol + 1) % 3;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.ConfirmPressed && _gpBindIndex < 8)
            {
                _waitingForButton = true;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.BackPressed || (Input.ConfirmPressed && _gpBindIndex == 8))
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
        }
    }

    private void UpdateOffset(float dt)
    {
        var data = HighscoreManager.Data;
        // Shift+Left/Right for �5ms jumps, plain Left/Right for �1ms
        bool shiftHeld = Input.IsHeld(Microsoft.Xna.Framework.Input.Keys.LeftShift) ||
                         Input.IsHeld(Microsoft.Xna.Framework.Input.Keys.RightShift);
        if (Input.LeftPressed)
        {
            data.GlobalOffset -= shiftHeld ? 5 : 1;
            HighscoreManager.SavePreferences();
        }
        if (Input.RightPressed)
        {
            data.GlobalOffset += shiftHeld ? 5 : 1;
            HighscoreManager.SavePreferences();
        }
        
        // Visual beat indicator for calibration
        float beatInterval = 60f / _offsetBeatBPM;
        _offsetBeatTimer += dt;
        if (_offsetBeatTimer >= beatInterval)
        {
            _offsetBeatTimer -= beatInterval;
            _offsetBeatVisual = 1f;
            Audio.PlaySound("scrollMenu", 0.5f);
        }
        _offsetBeatVisual = Math.Max(0, _offsetBeatVisual - dt * 4f);
        
        if (Input.BackPressed)
        {
            _inOffset = false;
            _offsetBeatTimer = 0;
            Audio.PlaySound("cancelMenu");
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Background (original: menuBG same as main menu)
        if (_bgTex != null && _bgTex != Assets.Pixel)
        {
            spriteBatch.Draw(_bgTex,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.White);
            // Dark overlay for readability
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.Black * 0.5f);
        }
        else
        {
            DrawCheckerboard(spriteBatch);
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.Black * 0.4f);
        }

        // Title
        var alphabetFont = AlphabetFont.Bold;
        if (alphabetFont != null)
            alphabetFont.DrawStringCentered(spriteBatch, "OPTIONS", 30, Color.White, 0.7f);
        else
        {
            var tf = Assets.GetFont(42);
            if (tf != null)
            {
                var ts = tf.MeasureString("OPTIONS");
                tf.DrawText(spriteBatch, "OPTIONS", new Vector2((FNFGame.SCREEN_WIDTH - ts.X) / 2, 40), Color.White);
            }
        }

        if (_inControls)
            DrawControlsMenu(spriteBatch);
        else if (_inOffset)
            DrawOffsetMenu(spriteBatch);
        else if (_inSaveData)
            DrawSaveDataMenu(spriteBatch);
        else if (_inSubMenu)
            DrawPreferencesMenu(spriteBatch);
        else
            DrawCategoryMenu(spriteBatch);

        // Hint bar
        spriteBatch.Draw(Assets.Pixel,
            new Rectangle(0, FNFGame.SCREEN_HEIGHT - 30, FNFGame.SCREEN_WIDTH, 30),
            new Color(0, 0, 0, 220));
        var hf = Assets.GetFont(14);
        if (hf != null)
        {
            string hint;
            if (_inControls)
            {
                if (_controlOnTab)
                    hint = "LEFT/RIGHT: Switch Tab   DOWN/ENTER: Select Tab   ESC: Back";
                else if (_controlTab == 1)
                    hint = "UP/DOWN: Select   LEFT/RIGHT: Slot   ENTER: Rebind   LB/RB: Tab   ESC: Back";
                else
                    hint = "UP/DOWN: Select   ENTER: Rebind   LB/RB or Q/E: Tab   ESC: Back";
            }
            else if (_inSaveData)
                hint = "UP/DOWN: Navigate   ENTER: Select   ESC: Back";
            else
                hint = "UP/DOWN: Navigate   LEFT/RIGHT: Adjust   ENTER: Select   ESC: Back";
            hf.DrawText(spriteBatch, hint, new Vector2(20, FNFGame.SCREEN_HEIGHT - 22), Color.Gray);
        }

        spriteBatch.End();
    }

    private void DrawCategoryMenu(SpriteBatch sb)
    {
        // Original FNF MenuTypedList: list scrolls so the selected item is fixed near 1/3 height,
        // selected item is full-size bold with a per-character Y-bob, others are smaller and dimmed.
        var alphabetFont = AlphabetFont.Bold;
        const float anchorY = 260f;     // y position the selected item rests at
        const float spacing = 130f;     // distance between consecutive items at full size
        const float selScale = 1.0f;
        const float unselScale = 0.6f;

        for (int i = 0; i < _categories.Length; i++)
        {
            bool sel = i == _categoryIndex;
            float dist = i - _categoryScrollLerp;
            // Smoothly interpolate scale based on distance from the active selection.
            float scale = MathHelper.Lerp(selScale, unselScale, MathHelper.Clamp(MathF.Abs(dist), 0f, 1f));
            float y = anchorY + dist * spacing;
            if (y < -spacing || y > FNFGame.SCREEN_HEIGHT + spacing) continue;

            string text = _categories[i].ToUpper();

            if (alphabetFont != null)
            {
                Color color = sel
                    ? Color.White
                    : Color.White * (0.55f + 0.15f * (1f - MathF.Min(MathF.Abs(dist), 1f)));
                // Slight horizontal nudge for the selected item (matches the original "peek out" feel).
                float xOffset = sel ? 20f : 0f;
                float width = alphabetFont.MeasureWidth(text, scale);
                float x = (FNFGame.SCREEN_WIDTH - width) / 2f + xOffset;
                alphabetFont.DrawString(sb, text, new Vector2(x, y), color, scale, sel, _alphabetBob);
            }
            else
            {
                // Fallback if alphabet atlas failed to load
                var font = Assets.GetFont(sel ? 38 : 24);
                if (font == null) continue;
                var sz = font.MeasureString(text);
                Color color = sel ? Color.White : Color.White * 0.55f;
                font.DrawText(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, y), color);
            }
        }
    }

    private void DrawPreferencesMenu(SpriteBatch sb)
    {
        // Original FNF PreferencesMenu uses AtlasMenuItem (alphabet font) rows that scroll
        // so the selected row is anchored near 1/3 of the screen and others scale down.
        var data = HighscoreManager.Data;
        var alphabetFont = AlphabetFont.Bold;
        const float anchorY = 260f;
        const float spacing = 110f;
        const float selScale = 0.85f;
        const float unselScale = 0.55f;
        int W = FNFGame.SCREEN_WIDTH;

        for (int i = 0; i < _prefItems.Length; i++)
        {
            bool sel = i == _prefIndex;
            float dist = i - _prefScrollLerp;
            float scale = MathHelper.Lerp(selScale, unselScale, MathHelper.Clamp(MathF.Abs(dist), 0f, 1f));
            float y = anchorY + dist * spacing;
            // Hide rows that would collide with the description popup at the bottom of the screen.
            if (y < -spacing || y > FNFGame.SCREEN_HEIGHT - 180) continue;

            string label = _prefItems[i].Name.ToUpper();
            bool isToggle = i <= 4;
            bool isVolume = i == 5 || i == 6;
            bool toggleValue = i switch
            {
                0 => data.Naughtyness,
                1 => data.Downscroll,
                2 => data.FlashingLights,
                3 => data.CameraZoom,
                4 => data.AutoPause,
                _ => false
            };

            float alpha = sel ? 1f : 0.55f + 0.15f * (1f - MathF.Min(MathF.Abs(dist), 1f));
            Color labelCol = Color.White * alpha;

            // Layout: label on the left side of the row, control on the right.
            float labelW = alphabetFont != null ? alphabetFont.MeasureWidth(label, scale) : 0f;
            float labelX = W / 2f - 280f + (sel ? 20f : 0f);
            float labelY = y;
            float controlX = W / 2f + 280f;
            float controlMid = labelY + (alphabetFont != null ? 40f : 24f) * scale * 0.5f;

            if (alphabetFont != null)
            {
                alphabetFont.DrawString(sb, label, new Vector2(labelX, labelY), labelCol, scale, sel, _alphabetBob);
            }
            else
            {
                var font = Assets.GetFont(sel ? 24 : 20);
                font?.DrawText(sb, label, new Vector2(labelX, labelY), labelCol);
            }

            if (isToggle)
            {
                // Checkbox graphic on the right (animated when selected & on).
                if (_checkboxSheet != null)
                {
                    string cbAnim = toggleValue ? "selected" : "unselected";
                    var cbFrames = _checkboxSheet.GetAnimation(cbAnim)
                                ?? _checkboxSheet.GetAnimationFuzzy(cbAnim)
                                ?? _checkboxSheet.Animations.Values.FirstOrDefault();
                    if (cbFrames != null && cbFrames.Count > 0)
                    {
                        var cbf = cbFrames[toggleValue ? Math.Min(_checkAnimFrame % cbFrames.Count, cbFrames.Count - 1) : 0];
                        float cbScale = sel ? 0.7f : 0.5f;
                        int cbW = (int)(cbf.SourceRect.Width * cbScale);
                        int cbH = (int)(cbf.SourceRect.Height * cbScale);
                        sb.Draw(_checkboxSheet.Texture,
                            new Rectangle((int)(controlX - cbW / 2f), (int)(controlMid - cbH / 2f), cbW, cbH),
                            cbf.SourceRect, Color.White * alpha);
                    }
                }
                else
                {
                    string valStr = toggleValue ? "ON" : "OFF";
                    Color valCol = toggleValue ? new Color(120, 255, 160) : new Color(255, 110, 110);
                    if (alphabetFont != null)
                    {
                        float vw = alphabetFont.MeasureWidth(valStr, scale * 0.9f);
                        alphabetFont.DrawString(sb, valStr, new Vector2(controlX - vw / 2f, labelY), valCol * alpha, scale * 0.9f, sel, _alphabetBob);
                    }
                }
            }
            else if (isVolume)
            {
                float vol = i == 5 ? data.MusicVolume : data.SfxVolume;
                int barW = 220;
                int barH = sel ? 18 : 12;
                int barX = (int)(controlX - barW / 2f);
                int barY = (int)(controlMid - barH / 2f);
                int fillW = (int)(barW * vol);

                sb.Draw(Assets.Pixel, new Rectangle(barX - 2, barY - 2, barW + 4, barH + 4), Color.White * (alpha * 0.4f));
                sb.Draw(Assets.Pixel, new Rectangle(barX, barY, barW, barH), new Color(20, 20, 35) * alpha);
                Color barColor = sel ? new Color(49, 176, 209) : new Color(120, 160, 180);
                sb.Draw(Assets.Pixel, new Rectangle(barX, barY, fillW, barH), barColor * alpha);

                if (alphabetFont != null)
                {
                    string pct = $"{(int)(vol * 100)}%";
                    float pw = alphabetFont.MeasureWidth(pct, scale * 0.55f);
                    alphabetFont.DrawString(sb, pct, new Vector2(barX + barW + 14, labelY), Color.White * alpha, scale * 0.55f, false, 0f);
                }
            }
        }

        DrawPreferenceDescription(sb);
    }

    private void DrawPreferenceDescription(SpriteBatch sb)
    {
        string desc = _prefItems[_prefIndex].Description;
        if (string.IsNullOrWhiteSpace(desc))
        {
            return;
        }

        var font = Assets.GetFont(20);
        if (font == null)
        {
            return;
        }

        var size = font.MeasureString(desc);
        int boxW = (int)size.X + 30;
        int boxH = (int)size.Y + 20;
        int x = (FNFGame.SCREEN_WIDTH - boxW) / 2;
        int y = FNFGame.SCREEN_HEIGHT - 150;
        sb.Draw(Assets.Pixel, new Rectangle(x, y, boxW, boxH), new Color(0, 0, 0, 160));
        sb.Draw(Assets.Pixel, new Rectangle(x, y, boxW, 2), new Color(250, 250, 250, 200));
        sb.Draw(Assets.Pixel, new Rectangle(x, y + boxH - 2, boxW, 2), new Color(250, 250, 250, 200));
        font.DrawText(sb, desc, new Vector2(x + 15, y + 10), Color.White);
    }

    private void UpdateSaveData()
    {
        int total = 2;
        if (Input.UpPressed)
        {
            _prefIndex = (_prefIndex - 1 + total) % total;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.DownPressed)
        {
            _prefIndex = (_prefIndex + 1) % total;
            Audio.PlaySound("scrollMenu");
        }

        if (Input.ConfirmPressed)
        {
            if (_prefIndex == 0)
            {
                HighscoreManager.ClearSaveData();
                Audio.PlaySound("confirmMenu");
            }
            else
            {
                _inSaveData = false;
                Audio.PlaySound("cancelMenu");
            }
        }

        if (Input.BackPressed)
        {
            _inSaveData = false;
            Audio.PlaySound("cancelMenu");
        }
    }

    private void DrawSaveDataMenu(SpriteBatch sb)
    {
        string[] items = { "CLEAR SAVE DATA", "BACK" };
        var alphabetFont = AlphabetFont.Bold;
        const float anchorY = 280f;
        const float spacing = 130f;

        for (int i = 0; i < items.Length; i++)
        {
            bool sel = i == _prefIndex;
            float dist = i - _prefIndex; // no smoothing needed for 2-item list
            float scale = sel ? 1.0f : 0.6f;
            float y = anchorY + dist * spacing;

            string text = items[i];
            float alpha = sel ? 1f : 0.55f;
            Color color = (i == 0 && sel) ? new Color(255, 110, 110) : Color.White * alpha;

            if (alphabetFont != null)
            {
                float w = alphabetFont.MeasureWidth(text, scale);
                alphabetFont.DrawString(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - w) / 2f + (sel ? 20f : 0f), y), color, scale, sel, _alphabetBob);
            }
            else
            {
                var font = Assets.GetFont(sel ? 28 : 24);
                if (font == null) continue;
                var sz = font.MeasureString(text);
                font.DrawText(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, y), color);
            }
        }
    }

    private void DrawControlsMenu(SpriteBatch sb)
    {
        var input = FNFGame.Instance.Input;
        int W = FNFGame.SCREEN_WIDTH;

        // ?? Dark panel behind controls area ??
        sb.Draw(Assets.Pixel, new Rectangle(40, 80, W - 80, FNFGame.SCREEN_HEIGHT - 130), new Color(0, 0, 0, 180));
        // Panel border
        sb.Draw(Assets.Pixel, new Rectangle(40, 80, W - 80, 2), new Color(49, 176, 209) * 0.6f);
        sb.Draw(Assets.Pixel, new Rectangle(40, FNFGame.SCREEN_HEIGHT - 50, W - 80, 2), new Color(49, 176, 209) * 0.6f);

        // ?? Tab bar ??
        string[] tabNames = { "KEYBOARD", "CONTROLLER" };
        int tabBarY = 92;
        int tabW = 220;
        int tabH = 40;
        int tabStartX = W / 2 - tabW - 10;
        float tabFlash = _controlOnTab ? (0.6f + 0.4f * MathF.Sin(_selFlash * 2f)) : 0f;

        for (int t = 0; t < 2; t++)
        {
            int tx = tabStartX + t * (tabW + 20);
            bool active = t == _controlTab;
            bool focused = _controlOnTab && active;

            // Tab background
            Color tabBg = active ? new Color(49, 176, 209, 60) : new Color(30, 30, 40, 100);
            if (focused) tabBg = Color.Lerp(tabBg, new Color(49, 176, 209, 120), tabFlash);
            sb.Draw(Assets.Pixel, new Rectangle(tx, tabBarY, tabW, tabH), tabBg);

            // Tab bottom border
            if (active)
                sb.Draw(Assets.Pixel, new Rectangle(tx, tabBarY + tabH - 3, tabW, 3), new Color(49, 176, 209));
            else
                sb.Draw(Assets.Pixel, new Rectangle(tx, tabBarY + tabH - 1, tabW, 1), new Color(80, 80, 100));

            // Tab text
            var tabFont = Assets.GetFont(active ? 20 : 18);
            if (tabFont != null)
            {
                var tsz = tabFont.MeasureString(tabNames[t]);
                Color tc = active ? Color.White : Color.Gray * 0.6f;
                if (focused) tc = Color.Lerp(Color.White, new Color(49, 176, 209), tabFlash * 0.5f);
                tabFont.DrawText(sb, tabNames[t], new Vector2(tx + (tabW - tsz.X) / 2, tabBarY + (tabH - tsz.Y) / 2), tc);
            }

            // Focus arrows on active tab
            if (focused)
            {
                var arrFont = Assets.GetFont(22);
                if (arrFont != null)
                {
                    Color arrCol = new Color(49, 176, 209) * (0.5f + 0.5f * MathF.Sin(_selFlash * 3f));
                    arrFont.DrawText(sb, "<", new Vector2(tx - 22, tabBarY + 8), arrCol);
                    arrFont.DrawText(sb, ">", new Vector2(tx + tabW + 6, tabBarY + 8), arrCol);
                }
            }
        }

        // LB/RB sprites next to tabs
        int sprSize = 28;
        if (_buttonSprites.TryGetValue("xbox_lb", out var lbTex))
            sb.Draw(lbTex, new Rectangle(tabStartX - 38, tabBarY + 6, sprSize, sprSize), Color.White * 0.5f);
        if (_buttonSprites.TryGetValue("xbox_rb", out var rbTex))
            sb.Draw(rbTex, new Rectangle(tabStartX + 2 * (tabW + 20) + 8, tabBarY + 6, sprSize, sprSize), Color.White * 0.5f);

        // ?? Bindings content ??
        if (_controlTab == 0)
            DrawKeyboardBindings(sb, input);
        else
            DrawControllerBindings(sb, input);
    }

    private void DrawKeyboardBindings(SpriteBatch sb, InputManager input)
    {
        int W = FNFGame.SCREEN_WIDTH;
        int startY = 150;
        int lineH = 52;

        Color[] noteColors = {
            new Color(194, 75, 153),  // Left - purple/pink
            new Color(0, 255, 255),   // Down - cyan
            new Color(18, 250, 5),    // Up - green
            new Color(249, 57, 63),   // Right - red
        };

        // Column headers
        int labelX = 120;
        int col1X = W / 2 - 40;
        int col2X = W / 2 + 180;
        var hdrFont = Assets.GetFont(14);
        if (hdrFont != null)
        {
            hdrFont.DrawText(sb, "ACTION", new Vector2(labelX, startY - 24), Color.Gray * 0.7f);
            hdrFont.DrawText(sb, "PRIMARY", new Vector2(col1X, startY - 24), Color.Gray * 0.7f);
            hdrFont.DrawText(sb, "SECONDARY", new Vector2(col2X, startY - 24), Color.Gray * 0.7f);
        }
        // Header divider
        sb.Draw(Assets.Pixel, new Rectangle(80, startY - 4, W - 160, 1), Color.Gray * 0.3f);

        string[][] bindings = {
            new[] { input.NoteKeysAlt[0].ToString(), input.NoteKeysArrow[0].ToString() },
            new[] { input.NoteKeysAlt[1].ToString(), input.NoteKeysArrow[1].ToString() },
            new[] { input.NoteKeysAlt[2].ToString(), input.NoteKeysArrow[2].ToString() },
            new[] { input.NoteKeysAlt[3].ToString(), input.NoteKeysArrow[3].ToString() },
            new[] { "ENTER", "SPACE" },
            new[] { "ESCAPE", "" },
            new[] { "ENTER", "" },
            new[] { "", "" }
        };

        for (int i = 0; i < _controlNames.Length; i++)
        {
            bool sel = i == _controlIndex && !_controlOnTab;
            int y = startY + i * lineH;
            if (y > FNFGame.SCREEN_HEIGHT - 70) continue;

            // Row highlight
            if (sel)
            {
                float pulse = 0.08f + 0.04f * MathF.Sin(_selFlash * 2f);
                sb.Draw(Assets.Pixel, new Rectangle(60, y - 4, W - 120, lineH - 4), new Color(49, 176, 209) * pulse);
            }
            // Row divider
            sb.Draw(Assets.Pixel, new Rectangle(80, y + lineH - 6, W - 160, 1), Color.Gray * 0.12f);

            // Selection arrow
            if (sel)
            {
                var arrFont = Assets.GetFont(20);
                if (arrFont != null)
                {
                    Color arrCol = new Color(49, 176, 209) * (0.6f + 0.4f * MathF.Sin(_selFlash * 3f));
                    arrFont.DrawText(sb, ">", new Vector2(labelX - 24, y + 2), arrCol);
                }
            }

            // Note color indicator
            if (i < 4)
            {
                Color nc = noteColors[i];
                sb.Draw(Assets.Pixel, new Rectangle(labelX - 8, y + 6, 4, 16), nc);
            }

            // Label
            Color labelCol;
            if (sel) labelCol = Color.White;
            else if (i < 4) labelCol = noteColors[i] * 0.8f;
            else labelCol = Color.Gray * 0.7f;

            var labelFont = Assets.GetFont(sel ? 22 : 18);
            if (labelFont != null)
                labelFont.DrawText(sb, _controlNames[i], new Vector2(labelX, y + 2), labelCol);

            // Key boxes
            bool canRebind = i < 4;
            for (int k = 0; k < 2; k++)
            {
                string keyStr = (i < bindings.Length && k < bindings[i].Length) ? bindings[i][k] : "";
                if (string.IsNullOrEmpty(keyStr)) continue;

                int kx = k == 0 ? col1X : col2X;

                if (_waitingForKey && i == _rebindTarget && k == 0)
                {
                    // Flashing "press a key" prompt
                    float f = 0.5f + 0.5f * MathF.Sin(_selFlash * 4f);
                    DrawKeyBox(sb, "...", kx, y, true, Color.Cyan * f, true);
                }
                else
                {
                    Color boxCol = sel ? (canRebind ? new Color(49, 176, 209) : Color.White * 0.8f) : Color.Gray * 0.5f;
                    DrawKeyBox(sb, keyStr, kx, y, sel, boxCol, canRebind && k == 0);
                }
            }
        }
    }

    private void DrawKeyBox(SpriteBatch sb, string text, int x, int y, bool highlighted, Color color, bool editable)
    {
        var font = Assets.GetFont(highlighted ? 18 : 15);
        if (font == null) return;
        var sz = font.MeasureString(text);
        int boxW = Math.Max((int)sz.X + 24, 70);
        int boxH = 30;
        int by = y + 2;

        // Box background
        Color bg = highlighted ? new Color(40, 50, 70, 200) : new Color(25, 25, 35, 150);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, boxH), bg);
        // Box border
        Color border = highlighted ? color : Color.Gray * 0.3f;
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by + boxH - 1, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, 1, boxH), border);
        sb.Draw(Assets.Pixel, new Rectangle(x + boxW - 1, by, 1, boxH), border);

        // Text
        Color textCol = highlighted ? Color.White : Color.Gray * 0.8f;
        font.DrawText(sb, text, new Vector2(x + (boxW - sz.X) / 2, by + (boxH - sz.Y) / 2), textCol);
    }

    private void DrawControllerBindings(SpriteBatch sb, InputManager input)
    {
        int W = FNFGame.SCREEN_WIDTH;
        int startY = 150;
        int lineH = 52;

        Color[] noteColors = {
            new Color(194, 75, 153),
            new Color(0, 255, 255),
            new Color(18, 250, 5),
            new Color(249, 57, 63),
        };

        int labelX = 120;
        int col0X = W / 2 - 80;
        int col1X = col0X + 150;
        int col2X = col1X + 150;

        // Column headers with icons
        var hdrFont = Assets.GetFont(14);
        if (hdrFont != null)
        {
            hdrFont.DrawText(sb, "ACTION", new Vector2(labelX, startY - 24), Color.Gray * 0.7f);
            hdrFont.DrawText(sb, "D-PAD", new Vector2(col0X, startY - 24), Color.Gray * 0.7f);
            hdrFont.DrawText(sb, "FACE", new Vector2(col1X, startY - 24), Color.Gray * 0.7f);
            hdrFont.DrawText(sb, "BUMPER/TRIG", new Vector2(col2X, startY - 24), Color.Gray * 0.7f);
        }
        sb.Draw(Assets.Pixel, new Rectangle(80, startY - 4, W - 160, 1), Color.Gray * 0.3f);

        // ?? Note binding rows (4) ??
        for (int i = 0; i < 4; i++)
        {
            bool rowSel = i == _gpBindIndex && !_controlOnTab;
            int y = startY + i * lineH;

            // Row highlight
            if (rowSel)
            {
                float pulse = 0.08f + 0.04f * MathF.Sin(_selFlash * 2f);
                sb.Draw(Assets.Pixel, new Rectangle(60, y - 4, W - 120, lineH - 4), new Color(49, 176, 209) * pulse);
            }
            sb.Draw(Assets.Pixel, new Rectangle(80, y + lineH - 6, W - 160, 1), Color.Gray * 0.12f);

            // Selection arrow
            if (rowSel)
            {
                var arrFont = Assets.GetFont(20);
                if (arrFont != null)
                {
                    Color arrCol = new Color(49, 176, 209) * (0.6f + 0.4f * MathF.Sin(_selFlash * 3f));
                    arrFont.DrawText(sb, ">", new Vector2(labelX - 24, y + 2), arrCol);
                }
            }

            // Note color bar
            sb.Draw(Assets.Pixel, new Rectangle(labelX - 8, y + 6, 4, 16), noteColors[i]);

            // Label
            Color labelCol = rowSel ? Color.White : noteColors[i] * 0.8f;
            var labelFont = Assets.GetFont(rowSel ? 22 : 18);
            if (labelFont != null)
                labelFont.DrawText(sb, _gpBindNames[i], new Vector2(labelX, y + 2), labelCol);

            // Three binding columns
            Microsoft.Xna.Framework.Input.Buttons[] bindings = {
                input.NoteButtons[i], input.NoteFaceButtons[i], input.NoteTriggerButtons[i]
            };
            int[] colXs = { col0X, col1X, col2X };

            for (int col = 0; col < 3; col++)
            {
                bool cellSel = rowSel && col == _gpBindCol;
                int cx = colXs[col];

                if (_waitingForButton && cellSel)
                {
                    float f = 0.5f + 0.5f * MathF.Sin(_selFlash * 4f);
                    DrawButtonCell(sb, null, "...", cx, y, true, Color.Cyan * f);
                }
                else
                {
                    string spriteName = GetButtonSpriteName(bindings[col]);
                    string fallbackText = FormatButtonName(bindings[col]);
                    Texture2D btnTex = null;
                    if (spriteName != null) _buttonSprites.TryGetValue(spriteName, out btnTex);
                    Color boxCol = cellSel ? new Color(49, 176, 209) : Color.Gray * 0.4f;
                    DrawButtonCell(sb, btnTex, fallbackText, cx, y, cellSel, boxCol);
                }
            }
        }

        // ?? Divider before nav bindings ??
        int divY = startY + 4 * lineH;
        sb.Draw(Assets.Pixel, new Rectangle(80, divY - 4, W - 160, 1), new Color(49, 176, 209) * 0.3f);
        var divFont = Assets.GetFont(12);
        if (divFont != null)
            divFont.DrawText(sb, "NAVIGATION", new Vector2(labelX, divY + 2), Color.Gray * 0.5f);

        // ?? Confirm / Cancel / Pause rows ??
        string[] navLabels = { "Confirm", "Cancel", "Pause", "Switch Char" };
        Microsoft.Xna.Framework.Input.Buttons[] navBindings = { input.ConfirmButton, input.CancelButton, input.PauseButton, input.SwitchCharButton };
        int navStartY = divY + 20;

        for (int n = 0; n < 4; n++)
        {
            int idx = 4 + n;
            bool rowSel = _gpBindIndex == idx && !_controlOnTab;
            int y = navStartY + n * lineH;

            if (rowSel)
            {
                float pulse = 0.08f + 0.04f * MathF.Sin(_selFlash * 2f);
                sb.Draw(Assets.Pixel, new Rectangle(60, y - 4, W - 120, lineH - 4), new Color(49, 176, 209) * pulse);
            }
            sb.Draw(Assets.Pixel, new Rectangle(80, y + lineH - 6, W - 160, 1), Color.Gray * 0.12f);

            if (rowSel)
            {
                var arrFont = Assets.GetFont(20);
                if (arrFont != null)
                {
                    Color arrCol = new Color(49, 176, 209) * (0.6f + 0.4f * MathF.Sin(_selFlash * 3f));
                    arrFont.DrawText(sb, ">", new Vector2(labelX - 24, y + 2), arrCol);
                }
            }

            Color labelCol = rowSel ? Color.White : Color.Gray * 0.7f;
            var labelFont = Assets.GetFont(rowSel ? 22 : 18);
            if (labelFont != null)
                labelFont.DrawText(sb, navLabels[n], new Vector2(labelX, y + 2), labelCol);

            // Single button binding
            if (_waitingForButton && rowSel)
            {
                float f = 0.5f + 0.5f * MathF.Sin(_selFlash * 4f);
                DrawButtonCell(sb, null, "...", col0X, y, true, Color.Cyan * f);
            }
            else
            {
                string spriteName = GetButtonSpriteName(navBindings[n]);
                string fallbackText = FormatButtonName(navBindings[n]);
                Texture2D btnTex = null;
                if (spriteName != null) _buttonSprites.TryGetValue(spriteName, out btnTex);
                Color boxCol = rowSel ? new Color(49, 176, 209) : Color.Gray * 0.4f;
                DrawButtonCell(sb, btnTex, fallbackText, col0X, y, rowSel, boxCol);
            }
        }

        // ?? Back item ??
        {
            bool backSel = _gpBindIndex == 8 && !_controlOnTab;
            int y = navStartY + 4 * lineH + 10;
            if (backSel)
            {
                float pulse = 0.08f + 0.04f * MathF.Sin(_selFlash * 2f);
                sb.Draw(Assets.Pixel, new Rectangle(60, y - 4, W - 120, lineH - 4), new Color(49, 176, 209) * pulse);
            }
            var bFont = Assets.GetFont(backSel ? 24 : 20);
            Color bCol = backSel ? Color.Yellow : Color.Gray;
            if (bFont != null)
            {
                var sz = bFont.MeasureString("BACK");
                bFont.DrawText(sb, "BACK", new Vector2((W - sz.X) / 2, y + 2), bCol);
            }
        }
    }

    private void DrawButtonCell(SpriteBatch sb, Texture2D btnTex, string fallbackText, int x, int y, bool highlighted, Color color)
    {
        int boxW = 80;
        int boxH = 36;
        int by = y;

        // Box background
        Color bg = highlighted ? new Color(40, 50, 70, 200) : new Color(25, 25, 35, 120);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, boxH), bg);
        // Box border
        Color border = highlighted ? color : Color.Gray * 0.2f;
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by + boxH - 1, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, 1, boxH), border);
        sb.Draw(Assets.Pixel, new Rectangle(x + boxW - 1, by, 1, boxH), border);

        if (btnTex != null)
        {
            int sprSize = highlighted ? 32 : 28;
            int sx = x + (boxW - sprSize) / 2;
            int sy = by + (boxH - sprSize) / 2;
            sb.Draw(btnTex, new Rectangle(sx, sy, sprSize, sprSize), Color.White * (highlighted ? 1f : 0.7f));
        }
        else
        {
            var font = Assets.GetFont(highlighted ? 16 : 13);
            if (font != null)
            {
                var sz = font.MeasureString(fallbackText);
                Color tc = highlighted ? Color.White : Color.Gray * 0.8f;
                font.DrawText(sb, fallbackText, new Vector2(x + (boxW - sz.X) / 2, by + (boxH - sz.Y) / 2), tc);
            }
        }
    }

    /// <summary>
    /// Map a Buttons enum value to a Kenney sprite filename (without extension).
    /// </summary>
    internal static string GetButtonSpriteName(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        return btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.A => "xbox_button_color_a",
            Microsoft.Xna.Framework.Input.Buttons.B => "xbox_button_color_b",
            Microsoft.Xna.Framework.Input.Buttons.X => "xbox_button_color_x",
            Microsoft.Xna.Framework.Input.Buttons.Y => "xbox_button_color_y",
            Microsoft.Xna.Framework.Input.Buttons.DPadUp => "xbox_dpad_up",
            Microsoft.Xna.Framework.Input.Buttons.DPadDown => "xbox_dpad_down",
            Microsoft.Xna.Framework.Input.Buttons.DPadLeft => "xbox_dpad_left",
            Microsoft.Xna.Framework.Input.Buttons.DPadRight => "xbox_dpad_right",
            Microsoft.Xna.Framework.Input.Buttons.LeftShoulder => "xbox_lb",
            Microsoft.Xna.Framework.Input.Buttons.RightShoulder => "xbox_rb",
            Microsoft.Xna.Framework.Input.Buttons.LeftTrigger => "xbox_lt",
            Microsoft.Xna.Framework.Input.Buttons.RightTrigger => "xbox_rt",
            Microsoft.Xna.Framework.Input.Buttons.Start => "xbox_button_start",
            Microsoft.Xna.Framework.Input.Buttons.Back => "xbox_button_back",
            Microsoft.Xna.Framework.Input.Buttons.LeftStick => "xbox_stick_l",
            _ => null
        };
    }

    internal static string FormatButtonName(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        return btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.LeftShoulder => "LB",
            Microsoft.Xna.Framework.Input.Buttons.RightShoulder => "RB",
            Microsoft.Xna.Framework.Input.Buttons.LeftTrigger => "LT",
            Microsoft.Xna.Framework.Input.Buttons.RightTrigger => "RT",
            Microsoft.Xna.Framework.Input.Buttons.DPadUp => "D-Up",
            Microsoft.Xna.Framework.Input.Buttons.DPadDown => "D-Down",
            Microsoft.Xna.Framework.Input.Buttons.DPadLeft => "D-Left",
            Microsoft.Xna.Framework.Input.Buttons.DPadRight => "D-Right",
            Microsoft.Xna.Framework.Input.Buttons.LeftStick => "L-Stick",
            Microsoft.Xna.Framework.Input.Buttons.RightStick => "R-Stick",
            _ => btn.ToString()
        };
    }

    private void DrawOffsetMenu(SpriteBatch sb)
    {
        var data = HighscoreManager.Data;
        var font = Assets.GetFont(28);
        if (font != null)
        {
            string text = $"Global Audio Offset: < {data.GlobalOffset} ms >";
            var sz = font.MeasureString(text);
            font.DrawText(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, 220), Color.White);

            // Visual beat indicator for calibration (original: LatencyState-style ticker)
            // Horizontal bar with a moving indicator
            int barX = FNFGame.SCREEN_WIDTH / 2 - 250;
            int barY = 340;
            int barW = 500;
            int barH = 30;
            
            // Bar background
            sb.Draw(Assets.Pixel, new Rectangle(barX - 2, barY - 2, barW + 4, barH + 4), Color.White * 0.2f);
            sb.Draw(Assets.Pixel, new Rectangle(barX, barY, barW, barH), new Color(20, 20, 40));
            
            // Center line (the "target" beat should land here)
            int centerLineX = barX + barW / 2;
            sb.Draw(Assets.Pixel, new Rectangle(centerLineX - 1, barY - 6, 3, barH + 12), Color.White);
            
            // Moving ticker based on beat timer
            float beatInterval = 60f / _offsetBeatBPM;
            float progress = _offsetBeatTimer / beatInterval; // 0?1 within a beat
            int tickerX = barX + (int)(progress * barW);
            Color tickerColor = Color.Lerp(new Color(49, 176, 209), Color.White, _offsetBeatVisual);
            sb.Draw(Assets.Pixel, new Rectangle(tickerX - 4, barY - 4, 9, barH + 8), tickerColor);
            
            // Beat pulse circle
            float pulseSize = 40 + _offsetBeatVisual * 30;
            int cx = FNFGame.SCREEN_WIDTH / 2;
            int cy = 440;
            Color pulseColor = Color.Lerp(new Color(80, 80, 200), Color.White, _offsetBeatVisual);
            sb.Draw(Assets.Pixel,
                new Rectangle((int)(cx - pulseSize / 2), (int)(cy - pulseSize / 2), (int)pulseSize, (int)pulseSize),
                pulseColor * (0.3f + _offsetBeatVisual * 0.7f));
            
            // Label text
            var sf = Assets.GetFont(16);
            if (sf != null)
            {
                string beatText = "Visual Beat Calibration (120 BPM)";
                var bsz = sf.MeasureString(beatText);
                sf.DrawText(sb, beatText, new Vector2((FNFGame.SCREEN_WIDTH - bsz.X) / 2, cy + 50), Color.Gray);
                
                // Offset value description
                string desc = data.GlobalOffset == 0 
                    ? "Offset is neutral" 
                    : data.GlobalOffset > 0 
                        ? $"Audio plays {data.GlobalOffset}ms later" 
                        : $"Audio plays {Math.Abs(data.GlobalOffset)}ms earlier";
                var dsz = sf.MeasureString(desc);
                sf.DrawText(sb, desc, new Vector2((FNFGame.SCREEN_WIDTH - dsz.X) / 2, 270), Color.Gray);
            }

            string hint = "LEFT/RIGHT: �1ms   SHIFT+LEFT/RIGHT: �5ms   ESC: Back";
            var hFont = Assets.GetFont(18);
            if (hFont != null)
            {
                var hsz = hFont.MeasureString(hint);
                hFont.DrawText(sb, hint, new Vector2((FNFGame.SCREEN_WIDTH - hsz.X) / 2, 530), Color.Gray);
            }
        }
    }

    private void DrawMenuItem(SpriteBatch sb, string text, int y, bool selected, Color color)
    {
        var alphabetFont = AlphabetFont.Bold;
        if (alphabetFont != null)
        {
            float scale = selected ? 0.6f : 0.5f;
            alphabetFont.DrawStringCentered(sb, text.ToUpper(), y, color, scale);
        }
        else
        {
            var font = Assets.GetFont(selected ? 32 : 26);
            if (font != null)
            {
                var sz = font.MeasureString(text);
                font.DrawText(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, y), color);
            }
        }
    }

    private void DrawCheckerboard(SpriteBatch sb)
    {
        int tileSize = 80;
        int offY = ((int)_checkerScrollY % (tileSize * 2) + tileSize * 2) % (tileSize * 2);
        for (int y = -tileSize * 2 + offY; y < FNFGame.SCREEN_HEIGHT + tileSize; y += tileSize)
        {
            for (int x = 0; x < FNFGame.SCREEN_WIDTH + tileSize; x += tileSize)
            {
                int row = (y + tileSize * 2) / tileSize;
                int col = x / tileSize;
                Color c = ((row + col) % 2 == 0) ? new Color(50, 25, 70) : new Color(70, 40, 95);
                sb.Draw(Assets.Pixel, new Rectangle(x, y, tileSize, tileSize), c);
            }
        }
    }
}
