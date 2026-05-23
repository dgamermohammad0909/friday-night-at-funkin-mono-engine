using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options Editor - live-tweak game preferences, view current settings,
/// adjust volumes, toggle gameplay options. All changes are persisted.
/// Layout: [Left: Category list] [Center: Options] [Right: Preview/Info]
/// </summary>
public class OptionsEditorScene : Scene
{
    const int LW = 200, RW = 240, TH = 34, SH = 22;

    private readonly string[] _categories = { "Gameplay", "Audio", "Display", "Controls", "Data" };
    private int _catIndex;
    private int _optIndex;
    private int _listScroll;

    // Gameplay option definitions
    struct Option { public string Name, Description, Category; public OptionType Type; }
    enum OptionType { Toggle, Slider, Info }
    private readonly List<Option> _options = new();
    private bool _dirty;

    public override void Load()
    {
        _options.Add(new Option { Name = "Downscroll", Description = "Notes fall down instead of up", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Middlescroll", Description = "Center the note field", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Ghost Tapping", Description = "Allow pressing keys without notes", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Flashing Lights", Description = "Enable screen flash effects", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Camera Zoom", Description = "Camera zooms on beat", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Naughtyness", Description = "Enable mature content", Category = "Gameplay", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Music Volume", Description = "Adjust background music volume", Category = "Audio", Type = OptionType.Slider });
        _options.Add(new Option { Name = "SFX Volume", Description = "Adjust sound effects volume", Category = "Audio", Type = OptionType.Slider });
        _options.Add(new Option { Name = "Global Offset", Description = "Audio offset calibration (ms)", Category = "Audio", Type = OptionType.Info });
        _options.Add(new Option { Name = "FPS Counter", Description = "Show FPS in corner", Category = "Display", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Auto Pause", Description = "Pause when window loses focus", Category = "Display", Type = OptionType.Toggle });
        _options.Add(new Option { Name = "Keyboard", Description = "View keyboard note bindings", Category = "Controls", Type = OptionType.Info });
        _options.Add(new Option { Name = "Controller", Description = "View gamepad note bindings", Category = "Controls", Type = OptionType.Info });
        _options.Add(new Option { Name = "Character", Description = "Selected player character", Category = "Data", Type = OptionType.Info });
        _options.Add(new Option { Name = "Save File", Description = "Save data file location", Category = "Data", Type = OptionType.Info });
    }

    public override void Unload()
    {
        if (_dirty) HighscoreManager.SavePreferences();
    }

    private List<Option> FilteredOptions()
    {
        string cat = _categories[_catIndex];
        return _options.Where(o => o.Category == cat).ToList();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        if (Input.IsPressed(Keys.Left) && !Input.IsHeld(Keys.LeftControl))
        { _catIndex = (_catIndex - 1 + _categories.Length) % _categories.Length; _optIndex = 0; }
        if (Input.IsPressed(Keys.Right) && !Input.IsHeld(Keys.LeftControl))
        { _catIndex = (_catIndex + 1) % _categories.Length; _optIndex = 0; }

        var filtered = FilteredOptions();
        if (Input.UpPressed && filtered.Count > 0) _optIndex = (_optIndex - 1 + filtered.Count) % filtered.Count;
        if (Input.DownPressed && filtered.Count > 0) _optIndex = (_optIndex + 1) % filtered.Count;

        // Toggle / adjust on Enter
        if (Input.ConfirmPressed && _optIndex < filtered.Count)
        {
            var opt = filtered[_optIndex];
            var data = HighscoreManager.Data;
            switch (opt.Name)
            {
                case "Downscroll": data.Downscroll = !data.Downscroll; _dirty = true; break;
                case "Middlescroll": data.Middlescroll = !data.Middlescroll; _dirty = true; break;
                case "Ghost Tapping": data.GhostTapping = !data.GhostTapping; _dirty = true; break;
                case "Flashing Lights": data.FlashingLights = !data.FlashingLights; _dirty = true; break;
                case "Camera Zoom": data.CameraZoom = !data.CameraZoom; _dirty = true; break;
                case "Naughtyness": data.Naughtyness = !data.Naughtyness; _dirty = true; break;
                case "FPS Counter": data.FPSCounter = !data.FPSCounter; _dirty = true; break;
                case "Auto Pause": data.AutoPause = !data.AutoPause; _dirty = true; break;
            }
            if (_dirty) { HighscoreManager.SavePreferences(); EditorUI.ShowToast("Saved!"); }
        }

        // Volume adjust with Left/Right on slider items
        if (_optIndex < filtered.Count && filtered[_optIndex].Type == OptionType.Slider)
        {
            var data = HighscoreManager.Data;
            float delta = 0;
            if (Input.IsPressed(Keys.Left) && Input.IsHeld(Keys.LeftControl)) delta = -0.05f;
            if (Input.IsPressed(Keys.Right) && Input.IsHeld(Keys.LeftControl)) delta = 0.05f;
            if (delta != 0)
            {
                switch (filtered[_optIndex].Name)
                {
                    case "Music Volume": data.MusicVolume = Math.Clamp(data.MusicVolume + delta, 0, 1); Audio.MusicVolume = data.MusicVolume; break;
                    case "SFX Volume": data.SfxVolume = Math.Clamp(data.SfxVolume + delta, 0, 1); Audio.SfxVolume = data.SfxVolume; break;
                }
                _dirty = true; HighscoreManager.SavePreferences();
            }
        }

        if (Input.BackPressed) Game.Scenes.ChangeScene(new EditorHubScene());
    }

    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font = Assets.GetFont(13);
        var fontSm = Assets.GetFont(11);
        var fontLg = Assets.GetFont(18);
        if (font == null) { sb.Begin(); sb.End(); return; }
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;

        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        EditorUI.FillRect(sb, px, new Rectangle(0, 0, W, H), EditorUI.BgDark);

        // Toolbar
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        font.DrawText(sb, "Options Editor  |  Enter=toggle  Ctrl+Left/Right=adjust volume  Left/Right=category",
            new Vector2(10, 10), EditorUI.TextPrimary);

        // Left panel: categories
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp, "Categories", font);
        int rowH = 28;
        for (int i = 0; i < _categories.Length; i++)
        {
            int ry = lp.Y + 30 + i * rowH;
            if (EditorUI.ListItem(sb, px, font, new Rectangle(0, ry, LW - 4, rowH), _categories[i], i == _catIndex))
            { _catIndex = i; _optIndex = 0; }
        }

        // Center: options list
        var center = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);
        EditorUI.FillRect(sb, px, center, EditorUI.BgPanel);
        EditorUI.DrawBorder(sb, px, center, EditorUI.Border);

        string catTitle = _categories[_catIndex];
        fontLg?.DrawText(sb, catTitle, new Vector2(center.X + 16, center.Y + 12), EditorUI.TextPrimary);
        sb.Draw(px, new Rectangle(center.X + 10, center.Y + 36, center.Width - 20, 1), EditorUI.Border);

        var filtered = FilteredOptions();
        var data = HighscoreManager.Data;
        int optY = center.Y + 46;
        int optRowH = 40;

        for (int i = 0; i < filtered.Count; i++)
        {
            var opt = filtered[i];
            int oy = optY + i * optRowH;
            if (oy + optRowH > center.Bottom) break;
            bool sel = (i == _optIndex);
            var optRect = new Rectangle(center.X + 4, oy, center.Width - 8, optRowH - 2);

            if (sel) EditorUI.FillRect(sb, px, optRect, EditorUI.Selected);
            else if (EditorUI.IsHovered(optRect)) EditorUI.FillRect(sb, px, optRect, EditorUI.Hover);
            if (sel) sb.Draw(px, new Rectangle(optRect.X, optRect.Y, 3, optRect.Height), EditorUI.Accent);

            if (EditorUI.IsHovered(optRect) && EditorUI.MouseClicked) _optIndex = i;

            font.DrawText(sb, opt.Name, new Vector2(optRect.X + 12, oy + 4), sel ? EditorUI.TextPrimary : EditorUI.TextSecondary);
            fontSm.DrawText(sb, opt.Description, new Vector2(optRect.X + 12, oy + 22), EditorUI.TextDim);

            // Value display on right side
            string val = GetOptionValue(opt, data);
            Color vc = opt.Type == OptionType.Toggle ? (val == "ON" ? EditorUI.Success : EditorUI.Error) : EditorUI.TextPrimary;
            float vw = font.MeasureString(val).X;
            font.DrawText(sb, val, new Vector2(optRect.Right - vw - 12, oy + 10), vc);

            // Toggle indicator
            if (opt.Type == OptionType.Toggle)
            {
                bool bv = val == "ON";
                int tx = optRect.Right - (int)vw - 52;
                sb.Draw(px, new Rectangle(tx, oy + 12, 30, 14), bv ? EditorUI.Accent : new Color(60, 60, 75));
                sb.Draw(px, new Rectangle(bv ? tx + 17 : tx + 1, oy + 13, 12, 12), Color.White);
            }
        }

        // Right panel: current value detail
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Details", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        if (_optIndex < filtered.Count)
        {
            var opt = filtered[_optIndex];
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Option", opt.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Category", opt.Category); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Type", opt.Type.ToString()); y += 18;
            string val = GetOptionValue(opt, data);
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Value", val); y += 18;

            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 10;
            fontSm.DrawText(sb, opt.Description, new Vector2(rx + 8, y), EditorUI.TextSecondary); y += 20;

            if (opt.Type == OptionType.Slider)
            {
                float sv = opt.Name == "Music Volume" ? data.MusicVolume : data.SfxVolume;
                EditorUI.Slider(sb, px, fontSm, rx + 8, y + 14, pw - 16, sv, opt.Name);
                y += 40;
                fontSm.DrawText(sb, "Ctrl+Left/Right to adjust", new Vector2(rx + 8, y), EditorUI.TextDim);
            }
            else if (opt.Name == "Keyboard")
            {
                y += 4;
                fontSm.DrawText(sb, "Note Bindings (DFJK):", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
                string[] dirs = { "Left", "Down", "Up", "Right" };
                for (int i = 0; i < 4; i++)
                { EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, dirs[i], data.NoteKeysAlt[i]); y += 16; }
                y += 8;
                fontSm.DrawText(sb, "Arrow Keys:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
                for (int i = 0; i < 4; i++)
                { EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, dirs[i], data.NoteKeysArrow[i]); y += 16; }
            }
            else if (opt.Name == "Controller")
            {
                y += 4;
                string[] dirs = { "Left", "Down", "Up", "Right" };
                fontSm.DrawText(sb, "D-Pad:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
                for (int i = 0; i < 4; i++)
                { EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, dirs[i], data.NoteGamepadDPad[i]); y += 16; }
                y += 4;
                fontSm.DrawText(sb, "Face Buttons:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
                for (int i = 0; i < 4; i++)
                { EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, dirs[i], data.NoteGamepadFace[i]); y += 16; }
            }
            else if (opt.Name == "Character")
            {
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Selected", data.SelectedCharacter); y += 18;
            }
            else if (opt.Name == "Save File")
            {
                string sp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save_data.json");
                bool exists = System.IO.File.Exists(sp);
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Path", "save_data.json"); y += 18;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Exists", exists ? "Yes" : "No"); y += 18;
                if (exists)
                {
                    var fi = new System.IO.FileInfo(sp);
                    EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Size", $"{fi.Length:N0} bytes"); y += 18;
                }
            }
            else if (opt.Name == "Global Offset")
            {
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Offset", $"{data.GlobalOffset} ms"); y += 18;
            }
        }

        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            "Options Editor", _dirty ? "UNSAVED CHANGES" : "Saved",
            "Enter=toggle  Left/Right=category  Esc=back");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }

    private string GetOptionValue(Option opt, HighscoreManager.SaveData data)
    {
        return opt.Name switch
        {
            "Downscroll" => data.Downscroll ? "ON" : "OFF",
            "Middlescroll" => data.Middlescroll ? "ON" : "OFF",
            "Ghost Tapping" => data.GhostTapping ? "ON" : "OFF",
            "Flashing Lights" => data.FlashingLights ? "ON" : "OFF",
            "Camera Zoom" => data.CameraZoom ? "ON" : "OFF",
            "Naughtyness" => data.Naughtyness ? "ON" : "OFF",
            "FPS Counter" => data.FPSCounter ? "ON" : "OFF",
            "Auto Pause" => data.AutoPause ? "ON" : "OFF",
            "Music Volume" => $"{data.MusicVolume:P0}",
            "SFX Volume" => $"{data.SfxVolume:P0}",
            "Global Offset" => $"{data.GlobalOffset}ms",
            "Keyboard" => string.Join(" ", data.NoteKeysAlt),
            "Controller" => string.Join(" ", data.NoteGamepadFace),
            "Character" => data.SelectedCharacter,
            "Save File" => "save_data.json",
            _ => "?"
        };
    }
}