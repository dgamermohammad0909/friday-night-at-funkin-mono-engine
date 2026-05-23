using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FontStashSharp;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Professional dark-theme UI toolkit for all editor scenes.
/// All drawing is done with a single pixel texture + SpriteFontBase.
/// </summary>
public static class EditorUI
{
    // ── Theme Colors ──────────────────────────────────────────────
    public static readonly Color BgDark       = new(30, 30, 46);
    public static readonly Color BgPanel      = new(37, 37, 53);
    public static readonly Color BgToolbar    = new(45, 45, 61);
    public static readonly Color BgInput      = new(25, 25, 38);
    public static readonly Color Border       = new(58, 58, 74);
    public static readonly Color BorderLight  = new(80, 80, 100);
    public static readonly Color TextPrimary  = new(224, 224, 230);
    public static readonly Color TextSecondary= new(140, 140, 160);
    public static readonly Color TextDim      = new(90, 90, 110);
    public static readonly Color Accent       = new(74, 123, 204);
    public static readonly Color AccentHover  = new(94, 143, 224);
    public static readonly Color AccentDim    = new(54, 93, 164);
    public static readonly Color Selected     = new(74, 123, 204, 60);
    public static readonly Color Hover        = new(255, 255, 255, 15);
    public static readonly Color Success      = new(78, 201, 176);
    public static readonly Color Warning      = new(229, 192, 123);
    public static readonly Color Error        = new(224, 108, 117);
    public static readonly Color Gold         = new(255, 215, 0);

    // ── State ─────────────────────────────────────────────────────
    private static MouseState _mouse, _prevMouse;
    private static int _hotItem = -1; // hovered item hash
    private static int _activeItem = -1; // clicked/dragging item hash

    public static MouseState Mouse => _mouse;
    public static bool MouseClicked => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public static bool MouseDown => _mouse.LeftButton == ButtonState.Pressed;
    public static bool MouseReleased => _mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
    public static bool RightClicked => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
    public static Vector2 MousePos => new(_mouse.X, _mouse.Y);
    public static Vector2 MouseDelta => new(_mouse.X - _prevMouse.X, _mouse.Y - _prevMouse.Y);

    public static void UpdateInput()
    {
        _prevMouse = _mouse;
        _mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
    }

    public static bool IsHovered(Rectangle rect) => rect.Contains(_mouse.X, _mouse.Y);

    // ── Basic Drawing ─────────────────────────────────────────────

    public static void FillRect(SpriteBatch sb, Texture2D px, Rectangle r, Color c)
        => sb.Draw(px, r, c);

    public static void DrawBorder(SpriteBatch sb, Texture2D px, Rectangle r, Color c, int t = 1)
    {
        sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, t), c);
        sb.Draw(px, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        sb.Draw(px, new Rectangle(r.X, r.Y, t, r.Height), c);
        sb.Draw(px, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    public static void DrawSeparatorH(SpriteBatch sb, Texture2D px, int x, int y, int w)
        => sb.Draw(px, new Rectangle(x, y, w, 1), Border);

    public static void DrawSeparatorV(SpriteBatch sb, Texture2D px, int x, int y, int h)
        => sb.Draw(px, new Rectangle(x, y, 1, h), Border);

    // ── Panel ─────────────────────────────────────────────────────

    public static void DrawPanel(SpriteBatch sb, Texture2D px, Rectangle r, string title = null, SpriteFontBase font = null)
    {
        FillRect(sb, px, r, BgPanel);
        DrawBorder(sb, px, r, Border);
        if (title != null && font != null)
        {
            FillRect(sb, px, new Rectangle(r.X, r.Y, r.Width, 26), BgToolbar);
            sb.Draw(px, new Rectangle(r.X, r.Y + 25, r.Width, 1), Border);
            font.DrawText(sb, title, new Vector2(r.X + 10, r.Y + 5), TextPrimary);
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────

    public static void DrawToolbar(SpriteBatch sb, Texture2D px, Rectangle r)
    {
        FillRect(sb, px, r, BgToolbar);
        sb.Draw(px, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), Border);
    }

    /// <summary>Draw a toolbar button. Returns true if clicked this frame.</summary>
    public static bool ToolButton(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string label, bool active = false, string tooltip = null)
    {
        int h = 24;
        var r = new Rectangle(x, y, w, h);
        bool hovered = IsHovered(r);
        bool clicked = hovered && MouseClicked;

        Color bg = active ? Accent : (hovered ? new Color(60, 60, 80) : Color.Transparent);
        Color fg = active ? Color.White : (hovered ? TextPrimary : TextSecondary);

        if (bg != Color.Transparent)
            FillRect(sb, px, r, bg);
        if (active)
            DrawBorder(sb, px, r, AccentHover);

        float tw = font.MeasureString(label).X;
        font.DrawText(sb, label, new Vector2(x + (w - tw) / 2, y + 4), fg);

        return clicked;
    }

    // ── Status Bar ────────────────────────────────────────────────

    public static void DrawStatusBar(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int screenW, int screenH, params string[] sections)
    {
        int h = 22;
        var r = new Rectangle(0, screenH - h, screenW, h);
        FillRect(sb, px, r, BgToolbar);
        sb.Draw(px, new Rectangle(0, screenH - h, screenW, 1), Border);

        float x = 8;
        for (int i = 0; i < sections.Length; i++)
        {
            if (i > 0)
            {
                sb.Draw(px, new Rectangle((int)x, screenH - h + 4, 1, h - 8), BorderLight);
                x += 12;
            }
            font.DrawText(sb, sections[i], new Vector2(x, screenH - h + 4), TextSecondary);
            x += font.MeasureString(sections[i]).X + 12;
        }
    }

    // ── List Item ─────────────────────────────────────────────────

    /// <summary>Draw a list row. Returns true if clicked.</summary>
    public static bool ListItem(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        Rectangle r, string text, bool selected, bool enabled = true, string badge = null, Color? badgeColor = null)
    {
        bool hovered = IsHovered(r);
        bool clicked = hovered && MouseClicked && enabled;

        if (selected)
            FillRect(sb, px, r, Selected);
        else if (hovered && enabled)
            FillRect(sb, px, r, Hover);

        if (selected)
            sb.Draw(px, new Rectangle(r.X, r.Y, 3, r.Height), Accent);

        Color fg = selected ? TextPrimary : (enabled ? TextSecondary : TextDim);
        font.DrawText(sb, text, new Vector2(r.X + 10, r.Y + (r.Height - 14) / 2), fg);

        if (badge != null)
        {
            Color bc = badgeColor ?? Accent;
            float bw = font.MeasureString(badge).X + 8;
            var br = new Rectangle(r.Right - (int)bw - 6, r.Y + (r.Height - 16) / 2, (int)bw, 16);
            FillRect(sb, px, br, bc * 0.3f);
            font.DrawText(sb, badge, new Vector2(br.X + 4, br.Y + 1), bc);
        }

        return clicked;
    }

    // ── Property Row ──────────────────────────────────────────────

    public static void PropertyRow(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string label, string value, bool highlight = false)
    {
        if (highlight)
            FillRect(sb, px, new Rectangle(x, y, w, 18), Selected);

        font.DrawText(sb, label, new Vector2(x + 6, y + 2), TextSecondary);
        float valX = x + w * 0.45f;
        font.DrawText(sb, value, new Vector2(valX, y + 2), TextPrimary);
    }

    // ── Toggle / Switch ───────────────────────────────────────────

    public static bool Toggle(SpriteBatch sb, Texture2D px,
        int x, int y, bool value, string label = null, SpriteFontBase font = null)
    {
        int sw = 36, sh = 18;
        var r = new Rectangle(x, y, sw, sh);
        bool clicked = IsHovered(r) && MouseClicked;

        Color track = value ? Accent : new Color(60, 60, 75);
        FillRect(sb, px, r, track);
        DrawBorder(sb, px, r, value ? AccentHover : BorderLight);

        int knobX = value ? r.Right - 16 : r.X + 2;
        FillRect(sb, px, new Rectangle(knobX, y + 2, 14, 14), Color.White);

        if (label != null && font != null)
            font.DrawText(sb, label, new Vector2(x + sw + 8, y + 2), TextPrimary);

        return clicked;
    }

    // ── Slider ────────────────────────────────────────────────────

    public static float Slider(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, float value, string label = null, string format = "F2")
    {
        int h = 16;
        int trackY = y + 4;
        int trackH = 8;

        if (label != null && font != null)
        {
            font.DrawText(sb, label, new Vector2(x, y - 14), TextSecondary);
            font.DrawText(sb, value.ToString(format), new Vector2(x + w - 40, y - 14), TextPrimary);
        }

        // Track
        FillRect(sb, px, new Rectangle(x, trackY, w, trackH), BgInput);
        DrawBorder(sb, px, new Rectangle(x, trackY, w, trackH), Border);

        // Fill
        int fillW = (int)(w * Math.Clamp(value, 0, 1));
        FillRect(sb, px, new Rectangle(x + 1, trackY + 1, fillW - 2, trackH - 2), Accent);

        // Handle
        int handleX = x + fillW - 5;
        FillRect(sb, px, new Rectangle(handleX, y + 1, 10, h), Color.White);

        // Drag interaction
        var trackRect = new Rectangle(x, y, w, h);
        if (IsHovered(trackRect) && MouseDown)
        {
            float newVal = Math.Clamp((_mouse.X - x) / (float)w, 0, 1);
            return newVal;
        }
        return value;
    }

    // ── Tabbar ────────────────────────────────────────────────────

    public static int TabBar(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string[] tabs, int selected)
    {
        int tabW = w / Math.Max(1, tabs.Length);
        int result = selected;

        for (int i = 0; i < tabs.Length; i++)
        {
            var r = new Rectangle(x + i * tabW, y, tabW, 28);
            bool isSel = (i == selected);
            bool hovered = IsHovered(r);

            FillRect(sb, px, r, isSel ? BgPanel : (hovered ? new Color(40, 40, 56) : BgToolbar));

            if (isSel)
                sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), Accent);

            Color fg = isSel ? TextPrimary : TextSecondary;
            float tw = font.MeasureString(tabs[i]).X;
            font.DrawText(sb, tabs[i], new Vector2(r.X + (r.Width - tw) / 2, r.Y + 7), fg);

            if (hovered && MouseClicked) result = i;
        }

        sb.Draw(px, new Rectangle(x, y + 27, w, 1), Border);
        return result;
    }

    // ── Checkerboard (for transparent bg preview) ─────────────────

    public static void DrawCheckerboard(SpriteBatch sb, Texture2D px, Rectangle area, Vector2 offset, float zoom)
    {
        int size = Math.Max(4, (int)(16 * zoom));
        int startX = (int)(offset.X % (size * 2));
        int startY = (int)(offset.Y % (size * 2));

        Color c1 = new(40, 40, 55);
        Color c2 = new(48, 48, 63);

        for (int gx = area.X + startX - size * 2; gx < area.Right; gx += size)
        {
            for (int gy = area.Y + startY - size * 2; gy < area.Bottom; gy += size)
            {
                int cx = (gx - area.X - startX) / size;
                int cy = (gy - area.Y - startY) / size;
                if (gx + size < area.X || gy + size < area.Y) continue;
                Color c = ((cx + cy) % 2 == 0) ? c1 : c2;
                sb.Draw(px, new Rectangle(
                    Math.Max(gx, area.X), Math.Max(gy, area.Y),
                    Math.Min(size, area.Right - Math.Max(gx, area.X)),
                    Math.Min(size, area.Bottom - Math.Max(gy, area.Y))), c);
            }
        }
    }

    // ── Crosshair (origin indicator) ──────────────────────────────

    public static void DrawCrosshair(SpriteBatch sb, Texture2D px, float x, float y, int size, Color c)
    {
        sb.Draw(px, new Rectangle((int)x - size, (int)y, size * 2 + 1, 1), c * 0.6f);
        sb.Draw(px, new Rectangle((int)x, (int)y - size, 1, size * 2 + 1), c * 0.6f);
    }

    // ── Selection Box ─────────────────────────────────────────────

    public static void DrawSelectionBox(SpriteBatch sb, Texture2D px, Rectangle r, Color? color = null)
    {
        var c = color ?? Accent;
        DrawBorder(sb, px, r, c);
        // Corner handles
        int hs = 6;
        FillRect(sb, px, new Rectangle(r.X - hs / 2, r.Y - hs / 2, hs, hs), c);
        FillRect(sb, px, new Rectangle(r.Right - hs / 2, r.Y - hs / 2, hs, hs), c);
        FillRect(sb, px, new Rectangle(r.X - hs / 2, r.Bottom - hs / 2, hs, hs), c);
        FillRect(sb, px, new Rectangle(r.Right - hs / 2, r.Bottom - hs / 2, hs, hs), c);
    }

    // ── Drag Handle ───────────────────────────────────────────────

    /// <summary>Returns new position if dragged, same position otherwise.</summary>
    public static Vector2 DragHandle(SpriteBatch sb, Texture2D px,
        Vector2 worldPos, Vector2 camOffset, float zoom, int id, bool selected)
    {
        float sx = camOffset.X + worldPos.X * zoom;
        float sy = camOffset.Y + worldPos.Y * zoom;
        int size = selected ? 10 : 7;
        var r = new Rectangle((int)sx - size / 2, (int)sy - size / 2, size, size);

        Color c = selected ? Gold : Accent;
        if (IsHovered(r)) c = AccentHover;
        FillRect(sb, px, r, c);
        if (selected) DrawBorder(sb, px, r, Color.White);

        if (selected && MouseDown && IsHovered(new Rectangle(r.X - 20, r.Y - 20, r.Width + 40, r.Height + 40)))
        {
            var delta = MouseDelta;
            worldPos += delta / zoom;
        }

        return worldPos;
    }

    // ── Scrollbar ─────────────────────────────────────────────────

    public static int DrawScrollbar(SpriteBatch sb, Texture2D px,
        int x, int y, int height, int contentHeight, int scroll, int viewHeight)
    {
        if (contentHeight <= viewHeight) return 0;

        int trackW = 8;
        FillRect(sb, px, new Rectangle(x, y, trackW, height), BgInput);

        float ratio = viewHeight / (float)contentHeight;
        int thumbH = Math.Max(20, (int)(height * ratio));
        float scrollRatio = scroll / (float)(contentHeight - viewHeight);
        int thumbY = y + (int)((height - thumbH) * scrollRatio);

        bool hovered = IsHovered(new Rectangle(x, thumbY, trackW, thumbH));
        FillRect(sb, px, new Rectangle(x, thumbY, trackW, thumbH), hovered ? AccentHover : Accent);

        // Scroll wheel on track area
        var trackRect = new Rectangle(x - 20, y, trackW + 40, height);
        if (IsHovered(trackRect))
        {
            int wheelDelta = _prevMouse.ScrollWheelValue - _mouse.ScrollWheelValue;
            if (wheelDelta != 0)
                return Math.Clamp(scroll + wheelDelta / 4, 0, contentHeight - viewHeight);
        }

        return scroll;
    }

    // ── Timeline scrubber ─────────────────────────────────────────

    public static void DrawTimeline(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, int frameCount, int currentFrame, float frameRate)
    {
        int h = 32;
        FillRect(sb, px, new Rectangle(x, y, w, h), BgInput);
        DrawBorder(sb, px, new Rectangle(x, y, w, h), Border);

        if (frameCount <= 0) return;

        // Frame ticks
        float tickSpacing = w / (float)frameCount;
        for (int i = 0; i <= frameCount; i++)
        {
            int tx = x + (int)(i * tickSpacing);
            bool isMajor = (i % 10 == 0);
            int tickH = isMajor ? 12 : 6;
            sb.Draw(px, new Rectangle(tx, y + h - tickH, 1, tickH), isMajor ? BorderLight : Border);
            if (isMajor && font != null)
                font.DrawText(sb, i.ToString(), new Vector2(tx + 2, y + 2), TextDim);
        }

        // Current frame indicator
        int curX = x + (int)(currentFrame * tickSpacing);
        sb.Draw(px, new Rectangle(curX, y, 2, h), Accent);
        FillRect(sb, px, new Rectangle(curX - 4, y, 10, 8), Accent);

        // Time label
        float seconds = currentFrame / Math.Max(1, frameRate);
        string timeStr = $"{currentFrame}f / {seconds:F2}s";
        font?.DrawText(sb, timeStr, new Vector2(x + w - font.MeasureString(timeStr).X - 6, y + h - 16), TextSecondary);
    }

    // ── Grid (for stage/chart editors) ────────────────────────────

    public static void DrawGrid(SpriteBatch sb, Texture2D px,
        Rectangle area, Vector2 camOffset, float zoom, int gridSize = 100)
    {
        int scaledGrid = Math.Max(4, (int)(gridSize * zoom));

        int ox = (int)(camOffset.X) % scaledGrid;
        int oy = (int)(camOffset.Y) % scaledGrid;

        for (int gx = area.X + ox; gx < area.Right; gx += scaledGrid)
            sb.Draw(px, new Rectangle(gx, area.Y, 1, area.Height), new Color(50, 50, 65, 80));
        for (int gy = area.Y + oy; gy < area.Bottom; gy += scaledGrid)
            sb.Draw(px, new Rectangle(area.X, gy, area.Width, 1), new Color(50, 50, 65, 80));
    }

    // ── Notification Toast ────────────────────────────────────────

    private static string _toast;
    private static float _toastTimer;

    public static void ShowToast(string message, float duration = 2f)
    {
        _toast = message;
        _toastTimer = duration;
    }

    public static void UpdateToast(float dt) { if (_toastTimer > 0) _toastTimer -= dt; }

    public static void DrawToast(SpriteBatch sb, Texture2D px, SpriteFontBase font, int screenW, int screenH)
    {
        if (_toastTimer <= 0 || _toast == null || font == null) return;
        float alpha = Math.Min(1f, _toastTimer);
        float tw = font.MeasureString(_toast).X;
        int pw = (int)tw + 24;
        int px2 = (screenW - pw) / 2;
        int py = screenH - 60;
        FillRect(sb, px, new Rectangle(px2, py, pw, 28), BgToolbar * alpha);
        DrawBorder(sb, px, new Rectangle(px2, py, pw, 28), Accent * alpha);
        font.DrawText(sb, _toast, new Vector2(px2 + 12, py + 6), TextPrimary * alpha);
    }
}
