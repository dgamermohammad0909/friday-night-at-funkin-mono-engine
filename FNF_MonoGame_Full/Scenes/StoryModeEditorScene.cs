using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// StoryMode Editor - inspect week data, banner layout, track lists,
/// character props, and difficulty settings for story mode.
/// Layout: [Left: Week list] [Center: Banner preview] [Right: Week properties]
/// </summary>
public class StoryModeEditorScene : Scene
{
    const int LW = 220, RW = 260, TH = 34, SH = 22;

    struct WeekInfo
    {
        public string Id, DisplayName;
        public string[] Songs, Props, Difficulties;
        public string BannerAsset;
    }
    private readonly List<WeekInfo> _weeks = new();
    private int _selectedIndex;
    private int _listScroll;
    private int _tab; // 0=Weeks, 1=Layout

    struct LayoutElement { public string Name; public float X, Y; public int W, H; public Color Color; }
    private readonly List<LayoutElement> _layoutElements = new();

    public override void Load()
    {
        // Layout elements from StoryModeScene
        _layoutElements.Add(new LayoutElement { Name = "Banner Area", X = 0, Y = 56, W = 1280, H = 400, Color = new Color(229,192,123) });
        _layoutElements.Add(new LayoutElement { Name = "Week Items", X = 270, Y = 480, W = 740, H = 200, Color = new Color(74,123,204) });
        _layoutElements.Add(new LayoutElement { Name = "Track List", X = 20, Y = 500, W = 240, H = 180, Color = new Color(78,201,176) });
        _layoutElements.Add(new LayoutElement { Name = "Difficulty", X = 800, Y = 480, W = 200, H = 50, Color = new Color(224,108,117) });
        _layoutElements.Add(new LayoutElement { Name = "Left Arrow", X = 780, Y = 480, W = 20, H = 40, Color = new Color(255,180,86) });
        _layoutElements.Add(new LayoutElement { Name = "Right Arrow", X = 1000, Y = 480, W = 20, H = 40, Color = new Color(255,180,86) });
        _layoutElements.Add(new LayoutElement { Name = "Score Display", X = 20, Y = 10, W = 300, H = 40, Color = new Color(140,170,200) });

        // Discover week JSON files
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "levels");
        if (System.IO.Directory.Exists(root))
        {
            foreach (var f in System.IO.Directory.GetFiles(root, "*.json"))
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(f);
                try
                {
                    var data = JObject.Parse(System.IO.File.ReadAllText(f));
                    var songs = (data["songs"] as JArray)?.Select(s => s.ToString()).ToArray() ?? Array.Empty<string>();
                    var props = (data["props"] as JArray)?.Select(p => p["assetPath"]?.ToString() ?? "?").ToArray() ?? Array.Empty<string>();
                    var diffs = (data["difficulties"] as JArray)?.Select(d => d.ToString()).ToArray() ?? new[] { "easy", "normal", "hard" };
                    _weeks.Add(new WeekInfo
                    {
                        Id = id,
                        DisplayName = data["name"]?.ToString() ?? id,
                        Songs = songs, Props = props, Difficulties = diffs,
                        BannerAsset = $"menus/story_menu/weeks/{id}.png"
                    });
                }
                catch { _weeks.Add(new WeekInfo { Id = id, DisplayName = id, Songs = Array.Empty<string>(), Props = Array.Empty<string>(), Difficulties = new[] { "easy", "normal", "hard" } }); }
            }
        }
    }

    public override void Unload() { }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        int maxItems = _tab == 0 ? _weeks.Count : _layoutElements.Count;
        if (Input.UpPressed && maxItems > 0) _selectedIndex = (_selectedIndex - 1 + maxItems) % maxItems;
        if (Input.DownPressed && maxItems > 0) _selectedIndex = (_selectedIndex + 1) % maxItems;

        if (Input.BackPressed) Game.Scenes.ChangeScene(new EditorHubScene());
    }

    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font = Assets.GetFont(13);
        var fontSm = Assets.GetFont(11);
        if (font == null) { sb.Begin(); sb.End(); return; }
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;

        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        EditorUI.FillRect(sb, px, new Rectangle(0, 0, W, H), EditorUI.BgDark);

        // Toolbar
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        font.DrawText(sb, $"StoryMode Editor  |  {_weeks.Count} weeks loaded", new Vector2(LW + 10, 10), EditorUI.TextPrimary);

        // Left panel
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp);
        string[] tabs = { "Weeks", "Layout" };
        int prevTab = _tab;
        _tab = EditorUI.TabBar(sb, px, font, 0, TH, LW, tabs, _tab);
        if (_tab != prevTab) _selectedIndex = 0;

        int listY = TH + 30, listH = H - TH - SH - 30, rowH = 22;

        if (_tab == 0)
        {
            int contentH = _weeks.Count * rowH;
            if (_selectedIndex * rowH < _listScroll) _listScroll = _selectedIndex * rowH;
            if ((_selectedIndex + 1) * rowH > _listScroll + listH) _listScroll = (_selectedIndex + 1) * rowH - listH;
            _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, contentH - listH));
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int i = 0; i < _weeks.Count; i++)
            {
                int ry = listY + i * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _weeks[i].Id,
                    i == _selectedIndex, badge: $"{_weeks[i].Songs.Length} songs", badgeColor: EditorUI.TextDim))
                    _selectedIndex = i;
            }
        }
        else
        {
            for (int i = 0; i < _layoutElements.Count; i++)
            {
                int ry = listY + i * rowH;
                if (ry > listY + listH) break;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 4, rowH), _layoutElements[i].Name,
                    i == _selectedIndex, badge: "UI", badgeColor: _layoutElements[i].Color))
                    _selectedIndex = i;
            }
        }

        // Center: story mode layout preview
        var area = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);
        EditorUI.FillRect(sb, px, area, Color.Black);
        float sc = (float)area.Width / 1280f;

        // Layout preview elements
        for (int i = 0; i < _layoutElements.Count; i++)
        {
            var e = _layoutElements[i];
            int ex = area.X + (int)(e.X * sc);
            int ey = area.Y + (int)(e.Y * sc);
            int ew = (int)(e.W * sc), eh = (int)(e.H * sc);
            bool sel = (i == _selectedIndex && _tab == 1);
            EditorUI.FillRect(sb, px, new Rectangle(ex, ey, ew, eh), (sel ? e.Color : e.Color * 0.3f) * 0.3f);
            EditorUI.DrawBorder(sb, px, new Rectangle(ex, ey, ew, eh), sel ? EditorUI.Gold : e.Color * 0.5f);
            fontSm.DrawText(sb, e.Name, new Vector2(ex + 3, ey + 3), sel ? Color.White : e.Color);
        }

        // Week item rows in center
        if (_tab == 0)
        {
            int itemY = area.Y + (int)(480 * sc);
            for (int i = 0; i < Math.Min(4, _weeks.Count); i++)
            {
                int wi = Math.Clamp(_selectedIndex - 1 + i, 0, _weeks.Count - 1);
                int iy = itemY + i * (int)(50 * sc);
                int iw = (int)(740 * sc), ih = (int)(45 * sc);
                int ix = area.X + (int)(270 * sc);
                bool sel = (wi == _selectedIndex);
                Color bg = sel ? new Color(50, 50, 70) : Color.Black;
                EditorUI.FillRect(sb, px, new Rectangle(ix, iy, iw, ih), bg);
                EditorUI.DrawBorder(sb, px, new Rectangle(ix, iy, iw, ih), sel ? EditorUI.Accent : EditorUI.Border);
                font.DrawText(sb, _weeks[wi].DisplayName, new Vector2(ix + 8, iy + ih / 2 - 7), sel ? Color.White : EditorUI.TextSecondary);
            }
        }

        // Right panel
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Properties", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        if (_tab == 0 && _selectedIndex < _weeks.Count)
        {
            var w = _weeks[_selectedIndex];
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Week ID", w.Id, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", w.DisplayName); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Banner", w.BannerAsset ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Difficulties", string.Join(", ", w.Difficulties)); y += 18;

            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;
            fontSm.DrawText(sb, "Songs:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            foreach (var s in w.Songs)
            {
                if (y > pp.Bottom - 20) break;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, ">", s); y += 16;
            }
            y += 8;

            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;
            fontSm.DrawText(sb, "Props:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            foreach (var p in w.Props)
            {
                if (y > pp.Bottom - 20) break;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, ">", p); y += 16;
            }

            y += 8;
            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;
            fontSm.DrawText(sb, "Scores:", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            foreach (var s in w.Songs)
            {
                if (y > pp.Bottom - 20) break;
                int sc2 = HighscoreManager.GetScore(s, "normal");
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, s, $"{sc2:N0}"); y += 16;
            }
        }
        else if (_tab == 1 && _selectedIndex < _layoutElements.Count)
        {
            var e = _layoutElements[_selectedIndex];
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", e.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{e.X:F0}, {e.Y:F0}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Size", $"{e.W}x{e.H}"); y += 18;
        }

        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            "StoryMode Editor", $"Weeks: {_weeks.Count}", "Up/Down=navigate  Esc=back");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }
}