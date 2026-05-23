using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Freeplay Editor - inspect the freeplay scene layout, song capsule positions,
/// DJ position, difficulty selectors, album art and background elements.
/// Layout: [Left: Song/Element list] [Center: Preview] [Right: Properties]
/// </summary>
public class FreeplayEditorScene : Scene
{
    const int LW = 240, RW = 250, TH = 34, SH = 22;

    struct FreeplaySong { public string Name, Album; public float BPM; public string[] Difficulties; }
    private readonly List<FreeplaySong> _songs = new();

    struct LayoutElement { public string Name, Category; public float X, Y, Scale; public Color Color; }
    private readonly List<LayoutElement> _elements = new();
    private int _selectedIndex;
    private int _tab; // 0=Elements, 1=Songs
    private int _listScroll;

    public override void Load()
    {
        // Layout elements from FreeplayScene
        _elements.Add(new LayoutElement { Name = "bgBlue", Category = "Background", X = 0, Y = 0, Scale = 1, Color = new Color(74,123,204) });
        _elements.Add(new LayoutElement { Name = "pinkBack", Category = "Background", X = 0, Y = 0, Scale = 1, Color = new Color(198,120,221) });
        _elements.Add(new LayoutElement { Name = "djBF", Category = "Character", X = -10, Y = 290, Scale = 0.67f, Color = new Color(78,201,176) });
        _elements.Add(new LayoutElement { Name = "highscoreBox", Category = "UI", X = 830, Y = 65, Scale = 1, Color = new Color(229,192,123) });
        _elements.Add(new LayoutElement { Name = "capsuleOrigin", Category = "UI", X = 270, Y = 150, Scale = 1, Color = new Color(224,108,117) });
        _elements.Add(new LayoutElement { Name = "capsuleSpacing", Category = "UI", X = 0, Y = 80, Scale = 1, Color = new Color(140,170,200) });
        _elements.Add(new LayoutElement { Name = "diffArrowLeft", Category = "UI", X = 200, Y = 50, Scale = 1, Color = new Color(255,180,86) });
        _elements.Add(new LayoutElement { Name = "diffArrowRight", Category = "UI", X = 480, Y = 50, Scale = 1, Color = new Color(255,180,86) });
        _elements.Add(new LayoutElement { Name = "diffLabel", Category = "UI", X = 300, Y = 50, Scale = 1, Color = EditorUI.TextPrimary });
        _elements.Add(new LayoutElement { Name = "albumArt", Category = "UI", X = 950, Y = 400, Scale = 1, Color = new Color(198,120,221) });
        _elements.Add(new LayoutElement { Name = "scrollText1", Category = "Text", X = 0, Y = 220, Scale = 1, Color = EditorUI.TextDim });
        _elements.Add(new LayoutElement { Name = "scrollText2", Category = "Text", X = 0, Y = 334, Scale = 1, Color = EditorUI.TextDim });
        _elements.Add(new LayoutElement { Name = "scrollText3", Category = "Text", X = 0, Y = 448, Scale = 1, Color = EditorUI.TextDim });

        // Discover songs
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "songs");
        if (System.IO.Directory.Exists(root))
        {
            foreach (var dir in System.IO.Directory.GetDirectories(root))
            {
                string name = System.IO.Path.GetFileName(dir);
                string metaPath = System.IO.Path.Combine(dir, "charts", "meta.json");
                string album = "vol1"; float bpm = 120; string[] diffs = { "easy", "normal", "hard" };
                if (System.IO.File.Exists(metaPath))
                {
                    try
                    {
                        var meta = JObject.Parse(System.IO.File.ReadAllText(metaPath));
                        album = meta["album"]?.ToString() ?? album;
                        var tc = meta["timeChanges"] as JArray;
                        if (tc != null && tc.Count > 0) bpm = (float)(tc[0]["bpm"] ?? 120);
                        var pc = meta["playData"]?["difficulties"] as JArray;
                        if (pc != null) diffs = pc.Select(d => d.ToString()).ToArray();
                    }
                    catch { }
                }
                _songs.Add(new FreeplaySong { Name = name, Album = album, BPM = bpm, Difficulties = diffs });
            }
        }
    }

    public override void Unload() { }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        int maxItems = _tab == 0 ? _elements.Count : _songs.Count;
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
        font.DrawText(sb, $"Freeplay Editor  |  {_songs.Count} songs  |  {_elements.Count} layout elements",
            new Vector2(LW + 10, 10), EditorUI.TextPrimary);

        // Left panel
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp);
        string[] tabs = { "Elements", "Songs" };
        int prevTab = _tab;
        _tab = EditorUI.TabBar(sb, px, font, 0, TH, LW, tabs, _tab);
        if (_tab != prevTab) _selectedIndex = 0;

        int listY = TH + 30, listH = H - TH - SH - 30, rowH = 22;

        if (_tab == 0)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                int ry = listY + i * rowH;
                if (ry > listY + listH) break;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 4, rowH), _elements[i].Name,
                    i == _selectedIndex, badge: _elements[i].Category, badgeColor: _elements[i].Color))
                    _selectedIndex = i;
            }
        }
        else
        {
            int contentH = _songs.Count * rowH;
            if (_selectedIndex * rowH < _listScroll) _listScroll = _selectedIndex * rowH;
            if ((_selectedIndex + 1) * rowH > _listScroll + listH) _listScroll = (_selectedIndex + 1) * rowH - listH;
            _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, contentH - listH));
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int i = 0; i < _songs.Count; i++)
            {
                int ry = listY + i * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _songs[i].Name,
                    i == _selectedIndex, badge: $"{_songs[i].BPM:0}bpm", badgeColor: EditorUI.TextDim))
                    _selectedIndex = i;
            }
        }

        // Center: freeplay layout preview
        var area = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);
        EditorUI.FillRect(sb, px, area, new Color(22, 22, 35));

        // Draw layout at ~50% scale preview
        float sc = (float)area.Height / 720f;
        for (int i = 0; i < _elements.Count; i++)
        {
            var e = _elements[i];
            float ex = area.X + e.X * sc;
            float ey = area.Y + e.Y * sc;
            int ew, eh;
            if (e.Category == "Background") { ew = (int)(area.Width); eh = (int)(area.Height); ex = area.X; ey = area.Y; }
            else if (e.Category == "Character") { ew = (int)(120 * sc); eh = (int)(200 * sc); }
            else { ew = (int)(100 * sc); eh = (int)(30 * sc); }
            bool sel = (i == _selectedIndex && _tab == 0);
            EditorUI.FillRect(sb, px, new Rectangle((int)ex, (int)ey, ew, eh), (sel ? e.Color : e.Color * 0.3f) * 0.4f);
            EditorUI.DrawBorder(sb, px, new Rectangle((int)ex, (int)ey, ew, eh), sel ? EditorUI.Gold : e.Color * 0.6f);
            fontSm.DrawText(sb, e.Name, new Vector2(ex + 2, ey + 2), sel ? Color.White : e.Color);
        }

        // Draw song capsule mockup
        if (_tab == 1 && _songs.Count > 0)
        {
            float capX = area.X + 270 * sc;
            int visCount = Math.Min(6, _songs.Count);
            for (int i = 0; i < visCount; i++)
            {
                int si = Math.Clamp(_selectedIndex - 2 + i, 0, _songs.Count - 1);
                float capY = area.Y + (150 + i * 80) * sc;
                int cw = (int)(400 * sc), ch = (int)(60 * sc);
                bool sel = (si == _selectedIndex);
                Color bg = sel ? new Color(60, 30, 60) : new Color(35, 35, 50);
                EditorUI.FillRect(sb, px, new Rectangle((int)capX, (int)capY, cw, ch), bg);
                EditorUI.DrawBorder(sb, px, new Rectangle((int)capX, (int)capY, cw, ch), sel ? EditorUI.Accent : EditorUI.Border);
                font.DrawText(sb, _songs[si].Name, new Vector2(capX + 10, capY + ch / 2 - 7), sel ? Color.White : EditorUI.TextSecondary);
            }
        }

        // Right panel
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Properties", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        if (_tab == 0 && _selectedIndex < _elements.Count)
        {
            var e = _elements[_selectedIndex];
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", e.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Category", e.Category); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{e.X:F0}, {e.Y:F0}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scale", $"{e.Scale:F2}"); y += 18;
        }
        else if (_tab == 1 && _selectedIndex < _songs.Count)
        {
            var s = _songs[_selectedIndex];
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Song", s.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Album", s.Album ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "BPM", $"{s.BPM:0.#}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Difficulties", string.Join(", ", s.Difficulties ?? Array.Empty<string>())); y += 18;

            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;
            int score = HighscoreManager.GetScore(s.Name, "normal");
            float pct = HighscoreManager.GetClearPercent(s.Name, "normal");
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Hi-Score", $"{score:N0}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Clear %", $"{pct:P1}"); y += 18;
            bool fav = HighscoreManager.Data.FavoriteSongs.Contains(s.Name);
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Favorite", fav ? "Yes" : "No"); y += 18;
        }

        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            "Freeplay Editor", $"Songs: {_songs.Count}", "Up/Down=navigate  Esc=back");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }
}