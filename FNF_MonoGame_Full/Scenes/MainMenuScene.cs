using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Main menu scene � Story Mode, Freeplay, Options, Credits
/// Matches original FNF MainMenuState layout
/// </summary>
public class MainMenuScene : Scene
{
    private readonly string[] _menuItems = { "storymode", "freeplay", "options", "credits" };
    private int _selectedIndex = 0;
    private float _selectTimer;
    private SpriteSheet _buttonsSheet;
    private Texture2D _background;
    private Texture2D _backgroundMagenta;
    private int _animFrame;
    private float _animTimer;
    private float _flashAlpha;
    private bool _confirmed;
    private float _confirmTimer;
    private int _confirmedIndex;

    // GF dancing in background
    private SpriteSheet _gfSheet;
    private int _gfFrame;
    private float _gfAnimTimer;
    private bool _gfDanceLeft;

    // Scrolling checkerboard offset
    private float _checkerScrollY;

    // Smooth scroll for menu buttons (original: camFollow lerp)
    private float _scrollLerp;

    // Version text (original FNF: "v0.5.0" bottom-right, "Friday Night Funkin'" bottom-left)
    private const string VERSION_TEXT = "MonoGame Port Alpha 1 v0.1.0";
    private const string FNF_TEXT = "Friday Night Funkin'";

    public override void Load()
    {
        _buttonsSheet = SpriteSheet.Load(Game, "menus/main_menu/buttons");
        _background = Assets.LoadTexture("menus/main_menu/background.png");
        _backgroundMagenta = Assets.LoadTexture("menus/main_menu/background_pink.png");

        // GF dance � original FNF: gfDance spritesheet in MainMenuState
        _gfSheet = SpriteSheet.Load(Game, "menus/main_menu/gf")
                ?? SpriteSheet.Load(Game, "menus/title/gf");

        if (_buttonsSheet != null)
        {
            Console.WriteLine($"Menu buttons: {_buttonsSheet.Animations.Count} animations");
            foreach (var k in _buttonsSheet.Animations.Keys)
                Console.WriteLine($"  '{k}' ({_buttonsSheet.Animations[k].Count} frames)");
        }

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    public override void Unload()
    {
        _buttonsSheet?.Dispose();
        _gfSheet?.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _selectTimer += delta;
        // Original FNF: camFollow lerp 0.06 per frame at 60fps => ~3.6/s
        _scrollLerp += (_selectedIndex - _scrollLerp) * 6f * delta;

        _animTimer += delta;
        if (_animTimer >= 1f / 24f) { _animTimer = 0; _animFrame++; }

        if (_flashAlpha > 0) _flashAlpha = Math.Max(0, _flashAlpha - delta * 3f);

        // Handle confirmed selection
        if (_confirmed)
        {
            _confirmTimer -= delta;
            if (_confirmTimer <= 0)
            {
                _confirmed = false; // Prevent calling ChangeScene every frame
                switch (_confirmedIndex)
                {
                    case 0: Game.Scenes.ChangeScene(new StoryModeScene()); break;
                    case 1: Game.Scenes.ChangeScene(new FreeplayScene()); break;
                    case 2: Game.Scenes.ChangeScene(new OptionsScene()); break;
                    case 3: Game.Scenes.ChangeScene(new CreditsScene()); break;
                }
            }
            return;
        }

        // Menu navigation
        if (Input.UpPressed)
        {
            _selectedIndex--;
            if (_selectedIndex < 0) _selectedIndex = _menuItems.Length - 1;
            _selectTimer = 0;
            Audio.PlaySound("scrollMenu");
        }

        if (Input.DownPressed)
        {
            _selectedIndex++;
            if (_selectedIndex >= _menuItems.Length) _selectedIndex = 0;
            _selectTimer = 0;
            Audio.PlaySound("scrollMenu");
        }

        // Select menu item
        if (Input.ConfirmPressed)
        {
            Audio.PlaySound("confirmMenu");
            _confirmed = true;
            _confirmedIndex = _selectedIndex;
            _confirmTimer = 1.0f;
            _flashAlpha = 1f;
        }

        _checkerScrollY -= delta * 40f;

        // GF dance � original: dances on beat, switches direction every 2 beats
        _gfAnimTimer += delta;
        if (_gfAnimTimer >= 1f / 24f)
        {
            _gfAnimTimer = 0;
            _gfFrame++;
        }
        
        // Beat detection using integer beat count (avoids floating point drift)
        float beatDuration = 60f / 102f;
        int currentBeat = (int)(Audio.MusicPosition / 1000.0 / beatDuration);
        // Original: GF dances on every beat, swaps direction every 2 beats
        bool shouldDanceLeft = (currentBeat / 2) % 2 == 0;
        if (shouldDanceLeft != _gfDanceLeft)
        {
            _gfDanceLeft = shouldDanceLeft;
            _gfFrame = 0;
        }

        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new TitleScene());
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        DrawBackground(spriteBatch);

        for (int i = 0; i < _menuItems.Length; i++)
        {
            bool isSelected = i == _selectedIndex;

            // In FNF, non-selected items fade out; selected items are brighter
            float alpha = isSelected ? 1.0f : 0.6f;

            // When confirmed, flicker the selected item and hide non-selected
            if (_confirmed)
            {
                if (isSelected)
                    alpha = ((int)(_confirmTimer * 12) % 2 == 0) ? 1f : 0f;
                else
                    alpha = 0f; // Non-selected items disappear on confirm (original behavior)
            }

            DrawMenuButton(spriteBatch, _menuItems[i], i, isSelected, alpha);
        }

        // Controller button prompts beside selected item (shown when gamepad connected)
        if (Input.GamePadConnected && !_confirmed)
        {
            int spacing = 160;
            int baseY = FNFGame.SCREEN_HEIGHT / 2 - 50;
            float selRelPos = _selectedIndex - _scrollLerp;
            int selItemY = baseY + (int)(selRelPos * spacing);
            int centerX = FNFGame.SCREEN_WIDTH / 2;

            // Compute the right edge of the selected menu button sprite
            string selAnim = $"{_menuItems[_selectedIndex]} selected";
            var selFrames = _buttonsSheet?.GetAnimation(selAnim);
            int btnRightEdge = centerX + 200; // fallback
            if (selFrames != null && selFrames.Count > 0)
            {
                int fw = selFrames[0].FrameWidth > 0 ? selFrames[0].FrameWidth : selFrames[0].SourceRect.Width;
                btnRightEdge = centerX + fw / 2;
            }

            int promptSize = 38;
            int promptX = btnRightEdge + 12;
            int promptY = selItemY + 20;

            Assets.DrawButtonPrompt(spriteBatch, Input.ConfirmButton, promptX, promptY, promptSize, 1f);
        }

        // Flash overlay
        if (_flashAlpha > 0)
        {
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.White * _flashAlpha);
        }

        // Version text (original: "Friday Night Funkin'" bottom-left, "v0.5.0" bottom-right)
        var vFont = Assets.GetFont(14);
        if (vFont != null)
        {
            vFont.DrawText(spriteBatch, FNF_TEXT, new Vector2(10, FNFGame.SCREEN_HEIGHT - 22), Color.White);
            var vSize = vFont.MeasureString(VERSION_TEXT);
            vFont.DrawText(spriteBatch, VERSION_TEXT, new Vector2(FNFGame.SCREEN_WIDTH - vSize.X - 10, FNFGame.SCREEN_HEIGHT - 22), Color.White);
        }

        spriteBatch.End();
    }

    private void DrawBackground(SpriteBatch spriteBatch)
    {
        // Original FNF: on confirm, background switches to magenta
        var bg = _confirmed ? _backgroundMagenta : _background;
        if (bg == null || bg == Assets.Pixel) bg = _background;

        if (bg != null && bg != Assets.Pixel)
        {
            // Original: bg.scrollFactor.set(0, Math.max(0, 0.25 - 0.25*(i/(len-1))))
            // With camFollow: background scrolls Y with scrollFactor.y = 0.18
            // camFollow.y moves ~160 per selection, scrollFactor 0.18 => ~29px per selection
            float bgScrollY = _scrollLerp * -29f;
            spriteBatch.Draw(bg,
                new Rectangle(0, (int)bgScrollY, 1280, 720 + 60),
                Color.White);
        }
        else
        {
            DrawCheckerboard(spriteBatch);
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
                Color c = ((row + col) % 2 == 0) ? new Color(60, 35, 85) : new Color(80, 50, 110);
                sb.Draw(Assets.Pixel, new Rectangle(x, y, tileSize, tileSize), c);
            }
        }
    }

    private void DrawGF(SpriteBatch spriteBatch)
    {
        if (_gfSheet?.Texture == null) return;

        string danceAnim = _gfDanceLeft ? "danceLeft" : "danceRight";
        var frames = _gfSheet.GetAnimation(danceAnim);

        if (frames == null || frames.Count == 0)
        {
            var fullAnim = _gfSheet.GetAnimation("gfDance")
                        ?? _gfSheet.Animations.Values.FirstOrDefault();
            if (fullAnim != null && fullAnim.Count >= 2)
            {
                int half = fullAnim.Count / 2;
                frames = _gfDanceLeft ? fullAnim.GetRange(0, half) : fullAnim.GetRange(half, fullAnim.Count - half);
            }
            else
                frames = fullAnim;
        }

        if (frames == null || frames.Count == 0) return;
        var frame = frames[_gfFrame % frames.Count];

        // Original FNF MainMenuState: (FlxG.width * 0.4 + 260, FlxG.height * 0.07), 1x scale
        int x = (int)(1280 * 0.4f) + 260;
        int y = (int)(720 * 0.07f);
        float offX = frame.Offset.X;
        float offY = frame.Offset.Y;
        spriteBatch.Draw(_gfSheet.Texture,
            new Vector2(x + offX, y + offY),
            frame.SourceRect, Color.White);
    }

    private void DrawMenuButton(SpriteBatch spriteBatch, string buttonName, int index, bool isSelected, float alpha)
    {
        // Original FNF: items at y = 60 + i * 160, camera follows selection
        // Camera follow simulated: scrollLerp tracks selection, items offset relative
        int spacing = 160;
        float relPos = index - _scrollLerp;
        int baseY = FNFGame.SCREEN_HEIGHT / 2 - 50;
        int y = baseY + (int)(relPos * spacing);
        int centerX = FNFGame.SCREEN_WIDTH / 2;

        string targetAnim = isSelected ? $"{buttonName} selected" : $"{buttonName} idle";
        List<SpriteFrame> frames = _buttonsSheet?.GetAnimation(targetAnim);

        if (frames != null && frames.Count > 0)
        {
            // Original FNF: idle = frame 0, selected = animates at 24fps
            var frame = isSelected
                ? frames[_animFrame % frames.Count]
                : frames[0];
            // Original: 1x scale, screenCenter(X) centers on frameWidth
            int fw = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
            float offX = frame.Offset.X;
            float offY = frame.Offset.Y;
            int drawX = centerX - fw / 2 + (int)offX;
            int drawY = y + (int)offY;
            spriteBatch.Draw(_buttonsSheet.Texture,
                new Vector2(drawX, drawY),
                frame.SourceRect, Color.White * alpha);
        }
        else
        {
            var color = isSelected ? Color.White : Color.Gray;
            string displayText = buttonName.Replace("_", " ").ToUpper();
            var alphabetFont = AlphabetFont.Bold;
            if (alphabetFont != null)
            {
                float s = isSelected ? 0.7f : 0.55f;
                float w = alphabetFont.MeasureWidth(displayText, s);
                alphabetFont.DrawString(spriteBatch, displayText, new Vector2(centerX - w / 2, y),
                    color * alpha, s);
            }
            else
            {
                var font = Assets.GetFont(32);
                if (font != null)
                {
                    var size = font.MeasureString(displayText);
                    font.DrawText(spriteBatch, displayText,
                        new Vector2(centerX - size.X / 2, y), color * alpha);
                }
            }
        }
    }
}
