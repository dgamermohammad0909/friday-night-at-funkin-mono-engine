using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;
using FontStashSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Chart Editor — real editing tool for note charts.
///
/// Controls:
///   Left-click grid lane = place note at that time position
///   Right-click note     = delete note
///   Click note           = select and inspect
///   Shift+click          = place sustain note (hold for length)
///   Scroll               = navigate through song
///   Ctrl+Scroll          = zoom in/out
///   Ctrl+Left/Right      = change difficulty
///   Ctrl+S               = save chart to disk
///   Ctrl+Z               = undo last action
///   Delete               = delete selected note
/// </summary>
public class ChartEditorScene : Scene
{
    const int LW = 220, RW = 240, TH = 34, SH = 22;
    static readonly Color[] LANE_COLORS = {
        new(198,120,221), new(97,175,239), new(78,201,176), new(224,108,117)
    };

    private readonly List<string> _songNames = new();
    private int _songIndex;
    private Chart _chart;
    private string _chartFilePath = "";
    private readonly string[] _difficulties = { "easy", "normal", "hard" };
    private int _diffIndex = 1;

    private float _scrollY;
    private float _zoom = 1f;
    private int _selectedNote = -1;
    private int _listScroll;
    private bool _dirty;

    // Undo
    private readonly Stack<UndoAction> _undoStack = new();
    record UndoAction(string Type, Note NoteData, int Index);

    public override void Load()
    {
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "songs");
        if (System.IO.Directory.Exists(root))
        {
            foreach (var dir in System.IO.Directory.GetDirectories(root))
                _songNames.Add(System.IO.Path.GetFileName(dir));
            _songNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        if (_songNames.Count > 0) LoadChart(0);
    }

    public override void Unload() { }

    private void LoadChart(int index)
    {
        if (index < 0 || index >= _songNames.Count) return;
        _songIndex = index;
        _chart = Chart.Load(_songNames[index], Assets, _difficulties[_diffIndex]);
        _scrollY = 0; _selectedNote = -1; _dirty = false; _undoStack.Clear();

        // Find chart file path for saving
        string songDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "songs", _songNames[index], "charts");
        _chartFilePath = System.IO.Path.Combine(songDir, "chart.json");
    }

    private void SaveChart()
    {
        if (_chart == null || string.IsNullOrEmpty(_chartFilePath)) return;
        if (!System.IO.File.Exists(_chartFilePath)) { EditorUI.ShowToast("No chart file found!"); return; }

        // Read original JSON, update notes array
        var json = JObject.Parse(System.IO.File.ReadAllText(_chartFilePath));
        string diff = _difficulties[_diffIndex];

        // Build notes array in FNF format
        var scrollSpeed = json["scrollSpeed"] as JObject ?? new JObject();
        var notesObj = json["notes"] as JObject ?? new JObject();

        var noteArr = new JArray();
        foreach (var n in _chart.Notes.OrderBy(n => n.Time))
        {
            var na = new JArray((float)(n.Time * 1000.0), n.Lane + (n.IsPlayerNote ? 0 : 4), (float)(n.SustainLength * 1000.0));
            noteArr.Add(na);
        }
        notesObj[diff] = noteArr;
        json["notes"] = notesObj;

        System.IO.File.WriteAllText(_chartFilePath, json.ToString(Formatting.Indented));
        _dirty = false;
        EditorUI.ShowToast($"Saved {_songNames[_songIndex]} ({diff})");
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        // Ctrl+S = save
        if (Input.IsPressed(Keys.S) && Input.IsHeld(Keys.LeftControl))
        { SaveChart(); return; }

        // Ctrl+Z = undo
        if (Input.IsPressed(Keys.Z) && Input.IsHeld(Keys.LeftControl) && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "add" && u.Index < _chart.Notes.Count) _chart.Notes.RemoveAt(u.Index);
            else if (u.Type == "del") { _chart.Notes.Insert(Math.Min(u.Index, _chart.Notes.Count), u.NoteData); }
            _dirty = true; _selectedNote = -1;
            EditorUI.ShowToast("Undo");
        }

        // Difficulty switch
        if (Input.IsPressed(Keys.Left) && Input.IsHeld(Keys.LeftControl))
        { _diffIndex = (_diffIndex - 1 + _difficulties.Length) % _difficulties.Length; LoadChart(_songIndex); }
        if (Input.IsPressed(Keys.Right) && Input.IsHeld(Keys.LeftControl))
        { _diffIndex = (_diffIndex + 1) % _difficulties.Length; LoadChart(_songIndex); }

        // Delete selected note
        if (Input.IsPressed(Keys.Delete) && _selectedNote >= 0 && _chart != null && _selectedNote < _chart.Notes.Count)
        {
            _undoStack.Push(new UndoAction("del", _chart.Notes[_selectedNote], _selectedNote));
            _chart.Notes.RemoveAt(_selectedNote);
            _selectedNote = -1; _dirty = true;
            EditorUI.ShowToast("Note deleted");
        }

        var gridRect = GridArea();
        if (EditorUI.IsHovered(gridRect))
        {
            int scroll = Input.ScrollDelta;
            if (Input.IsHeld(Keys.LeftControl))
            { if (scroll > 0) _zoom = Math.Min(4f, _zoom * 1.15f); if (scroll < 0) _zoom = Math.Max(0.25f, _zoom / 1.15f); }
            else
            { _scrollY -= scroll * 40; _scrollY = Math.Max(0, _scrollY); }

            // Left click = select or place note
            if (EditorUI.MouseClicked && _chart != null)
            {
                if (!TrySelectNote(gridRect))
                    PlaceNote(gridRect);
            }

            // Right click = delete note under cursor
            if (EditorUI.RightClicked && _chart != null)
                TryDeleteNoteAt(gridRect);
        }

        if (Input.IsPressed(Keys.OemPlus)) _zoom = Math.Min(4f, _zoom * 1.25f);
        if (Input.IsPressed(Keys.OemMinus)) _zoom = Math.Max(0.25f, _zoom / 1.25f);

        if (Input.BackPressed)
        {
            if (_dirty) SaveChart();
            Game.Scenes.ChangeScene(new EditorHubScene());
        }
    }

    private Rectangle GridArea() => new(LW, TH, FNFGame.SCREEN_WIDTH - LW - RW, FNFGame.SCREEN_HEIGHT - TH - SH);

    private bool TrySelectNote(Rectangle gridRect)
    {
        if (_chart == null) return false;
        var mp = EditorUI.MousePos;
        float pps = 200 * _zoom;
        int laneW = gridRect.Width / 8;
        for (int i = 0; i < _chart.Notes.Count; i++)
        {
            var n = _chart.Notes[i];
            float ny = (float)(n.Time * pps) - _scrollY + gridRect.Y + 20;
            int col = (n.IsPlayerNote ? 4 : 0) + n.Lane;
            float nx = gridRect.X + col * laneW + laneW / 2f;
            if (Math.Abs(mp.X - nx) < laneW / 2 && Math.Abs(mp.Y - ny) < 12)
            { _selectedNote = i; return true; }
        }
        return false;
    }

    private void PlaceNote(Rectangle gridRect)
    {
        var mp = EditorUI.MousePos;
        float pps = 200 * _zoom;
        int laneW = gridRect.Width / 8;

        int col = (int)((mp.X - gridRect.X) / laneW);
        if (col < 0 || col >= 8) return;
        float time = (mp.Y - gridRect.Y - 20 + _scrollY) / pps;
        if (time < 0) return;

        // Snap to beat grid
        float secPerBeat = 60f / Math.Max(1, _chart.BPM);
        float snapRes = secPerBeat / 4f; // 1/16th note snap
        time = MathF.Round(time / snapRes) * snapRes;

        bool isPlayer = col >= 4;
        int lane = col % 4;

        var note = new Note { Time = time, Lane = lane, IsPlayerNote = isPlayer, SustainLength = 0 };
        _chart.Notes.Add(note);
        _chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));

        int idx = _chart.Notes.IndexOf(note);
        _undoStack.Push(new UndoAction("add", note, idx));
        _selectedNote = idx;
        _dirty = true;
    }

    private void TryDeleteNoteAt(Rectangle gridRect)
    {
        var mp = EditorUI.MousePos;
        float pps = 200 * _zoom;
        int laneW = gridRect.Width / 8;
        for (int i = 0; i < _chart.Notes.Count; i++)
        {
            var n = _chart.Notes[i];
            float ny = (float)(n.Time * pps) - _scrollY + gridRect.Y + 20;
            int col = (n.IsPlayerNote ? 4 : 0) + n.Lane;
            float nx = gridRect.X + col * laneW + laneW / 2f;
            if (Math.Abs(mp.X - nx) < laneW / 2 && Math.Abs(mp.Y - ny) < 12)
            {
                _undoStack.Push(new UndoAction("del", _chart.Notes[i], i));
                _chart.Notes.RemoveAt(i);
                _selectedNote = -1; _dirty = true;
                EditorUI.ShowToast("Note removed");
                return;
            }
        }
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
        int tx = LW + 8;

        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Save", false)) SaveChart();
        tx += 54;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Undo") && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "add" && u.Index < _chart.Notes.Count) _chart.Notes.RemoveAt(u.Index);
            else if (u.Type == "del") _chart.Notes.Insert(Math.Min(u.Index, _chart.Notes.Count), u.NoteData);
            _dirty = true; _selectedNote = -1;
        }
        tx += 58;
        sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;

        for (int d = 0; d < _difficulties.Length; d++)
        {
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 60, _difficulties[d].ToUpper(), d == _diffIndex))
            { _diffIndex = d; LoadChart(_songIndex); }
            tx += 64;
        }
        tx += 8;
        string nfo = _chart != null ? $"BPM:{_chart.BPM:0} Spd:{_chart.Speed:F1} Notes:{_chart.Notes.Count}" : "No chart";
        font.DrawText(sb, nfo, new Vector2(tx, 10), EditorUI.TextPrimary);
        if (_dirty)
        {
            float nw = font.MeasureString(nfo).X;
            font.DrawText(sb, " [MODIFIED]", new Vector2(tx + nw, 10), EditorUI.Warning);
        }

        // Left panel
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp, $"Songs ({_songNames.Count})", font);
        int listY = lp.Y + 28, listH = lp.Height - 28, rowH = 22;
        int contentH = _songNames.Count * rowH;
        if (_songIndex * rowH < _listScroll) _listScroll = _songIndex * rowH;
        if ((_songIndex + 1) * rowH > _listScroll + listH) _listScroll = (_songIndex + 1) * rowH - listH;
        _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, contentH - listH));
        _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
        for (int i = 0; i < _songNames.Count; i++)
        {
            int ry = listY + i * rowH - _listScroll;
            if (ry + rowH < listY || ry > listY + listH) continue;
            if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _songNames[i], i == _songIndex))
            { if (_dirty) SaveChart(); _songIndex = i; LoadChart(i); }
        }

        // Center chart grid
        DrawChartGrid(sb, px, font, fontSm, W, H);

        // Right panel
        DrawNoteProps(sb, px, font, fontSm, W, H);

        string saveHint = _dirty ? "UNSAVED — Ctrl+S to save" : "Saved";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H, saveHint, $"Zoom:{_zoom:F1}x",
            "LClick=place  RClick=delete  Scroll=nav  Ctrl+Scroll=zoom  Del=remove");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }

    private void DrawChartGrid(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        var area = GridArea();
        EditorUI.FillRect(sb, px, area, new Color(25, 25, 38));

        if (_chart == null) { font.DrawText(sb, "No chart loaded", new Vector2(area.X + 40, area.Y + 40), EditorUI.TextDim); return; }

        float pps = 200 * _zoom;
        int laneW = area.Width / 8;

        // Lane backgrounds
        string[] labels = { "L", "D", "U", "R", "L", "D", "U", "R" };
        for (int i = 0; i < 8; i++)
        {
            int lx = area.X + i * laneW;
            EditorUI.FillRect(sb, px, new Rectangle(lx, area.Y, laneW, area.Height), LANE_COLORS[i % 4] * 0.12f);
            sb.Draw(px, new Rectangle(lx, area.Y, 1, area.Height), EditorUI.Border * 0.4f);
            fontSm.DrawText(sb, labels[i], new Vector2(lx + laneW / 2 - 3, area.Y + 4), LANE_COLORS[i % 4] * 0.6f);
        }
        sb.Draw(px, new Rectangle(area.X + area.Width / 2, area.Y, 2, area.Height), EditorUI.Accent * 0.5f);
        fontSm.DrawText(sb, "OPPONENT", new Vector2(area.X + 8, area.Y + 4), EditorUI.TextDim);
        fontSm.DrawText(sb, "PLAYER", new Vector2(area.X + area.Width / 2 + 8, area.Y + 4), EditorUI.TextDim);

        // Beat lines
        float secPerBeat = 60f / Math.Max(1, _chart.BPM);
        double maxT = _chart.SongLength > 0 ? _chart.SongLength : 180;
        for (float t = 0; t * pps - _scrollY < area.Height && t < maxT; t += secPerBeat / 4f)
        {
            float ly = area.Y + 20 + t * pps - _scrollY;
            if (ly < area.Y || ly > area.Bottom) continue;
            int beat = (int)Math.Round(t / secPerBeat * 4);
            bool isMeasure = beat % 16 == 0;
            bool isBeat = beat % 4 == 0;
            sb.Draw(px, new Rectangle(area.X, (int)ly, area.Width, isMeasure ? 2 : 1),
                isMeasure ? EditorUI.BorderLight * 0.6f : (isBeat ? EditorUI.Border * 0.4f : EditorUI.Border * 0.15f));
            if (isMeasure)
                fontSm.DrawText(sb, $"M{beat / 16 + 1}", new Vector2(area.X + 2, ly - 12), EditorUI.TextDim);
        }

        // Notes
        for (int i = 0; i < _chart.Notes.Count; i++)
        {
            var n = _chart.Notes[i];
            float ny = area.Y + 20 + (float)(n.Time * pps) - _scrollY;
            if (ny < area.Y - 20 || ny > area.Bottom + 20) continue;
            int col = (n.IsPlayerNote ? 4 : 0) + n.Lane;
            int nlx = area.X + col * laneW + 3;
            int nlw = laneW - 6;
            bool sel = (i == _selectedNote);
            Color nc = LANE_COLORS[n.Lane];

            // Sustain
            if (n.SustainLength > 0.01)
            {
                int sh = (int)(n.SustainLength * pps);
                EditorUI.FillRect(sb, px, new Rectangle(nlx + nlw / 2 - 3, (int)ny + 5, 6, sh), nc * 0.5f);
            }

            // Note head
            EditorUI.FillRect(sb, px, new Rectangle(nlx, (int)ny - 6, nlw, 12), sel ? Color.White : nc);
            if (sel) EditorUI.DrawBorder(sb, px, new Rectangle(nlx - 1, (int)ny - 7, nlw + 2, 14), EditorUI.Gold);
        }

        // Mouse hover indicator (snap preview)
        if (EditorUI.IsHovered(area))
        {
            var mp = EditorUI.MousePos;
            int hcol = (int)((mp.X - area.X) / laneW);
            if (hcol >= 0 && hcol < 8)
            {
                float htime = (mp.Y - area.Y - 20 + _scrollY) / pps;
                float snap = secPerBeat / 4f;
                htime = MathF.Round(htime / snap) * snap;
                float hy = area.Y + 20 + htime * pps - _scrollY;
                int hlx = area.X + hcol * laneW + 3;
                EditorUI.DrawBorder(sb, px, new Rectangle(hlx, (int)hy - 6, laneW - 6, 12), LANE_COLORS[hcol % 4] * 0.5f);
            }
        }

        EditorUI.DrawBorder(sb, px, area, EditorUI.Border);
    }

    private void DrawNoteProps(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Chart Info", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        if (_chart != null)
        {
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Song", _chart.SongName ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Artist", _chart.Artist ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "BPM", $"{_chart.BPM:0.#}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Speed", $"{_chart.Speed:F2}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Notes", $"{_chart.Notes.Count}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Player", _chart.PlayerCharacter ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Opponent", _chart.OpponentCharacter ?? "?"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Stage", _chart.Stage ?? "?"); y += 18;

            sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 10;

            if (_selectedNote >= 0 && _selectedNote < _chart.Notes.Count)
            {
                var n = _chart.Notes[_selectedNote];
                fontSm.DrawText(sb, "SELECTED NOTE", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
                string[] dirs = { "Left", "Down", "Up", "Right" };
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Time", $"{n.Time:F3}s"); y += 18;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Lane", $"{n.Lane} ({(n.Lane < 4 ? dirs[n.Lane] : "?")})"); y += 18;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Side", n.IsPlayerNote ? "Player" : "Opponent"); y += 18;
                EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Sustain", n.SustainLength > 0 ? $"{n.SustainLength:F3}s" : "None"); y += 18;
                y += 8;
                fontSm.DrawText(sb, "Del = delete note", new Vector2(rx + 8, y), EditorUI.TextDim);
            }
            else
            {
                fontSm.DrawText(sb, "Click grid to place notes", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
                fontSm.DrawText(sb, "Right-click to delete", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
                fontSm.DrawText(sb, "Click a note to inspect", new Vector2(rx + 8, y), EditorUI.TextDim);
            }
        }
    }
}