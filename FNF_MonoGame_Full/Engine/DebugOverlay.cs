using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FontStashSharp;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Global debug overlay — toggle with F3 in any scene.
/// Shows FPS, mouse position, scene name, and custom debug lines.
/// </summary>
public static class DebugOverlay
{
    public static bool Visible { get; set; }
    private static KeyboardState _prevKb;
    private static readonly List<string> _lines = new();
    private static readonly List<string> _persistentLines = new();

    /// <summary>Add a debug line for the current frame only (cleared each frame).</summary>
    public static void Log(string text) => _lines.Add(text);

    /// <summary>Add a persistent debug line (stays until cleared).</summary>
    public static void Pin(string key, string value)
    {
        for (int i = 0; i < _persistentLines.Count; i++)
        {
            if (_persistentLines[i].StartsWith(key + ":"))
            {
                _persistentLines[i] = $"{key}: {value}";
                return;
            }
        }
        _persistentLines.Add($"{key}: {value}");
    }

    public static void ClearPins() => _persistentLines.Clear();

    public static void Update()
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.F3) && !_prevKb.IsKeyDown(Keys.F3))
            Visible = !Visible;
        _prevKb = kb;
    }

    public static void Draw(SpriteBatch sb, AssetManager assets, string sceneName = null)
    {
        if (!Visible) return;

        var font = assets?.GetFont(16);
        if (font == null) return;

        var mouse = Mouse.GetState();
        float y = 4;
        void DrawLine(string text)
        {
            font.DrawText(sb, text, new Vector2(6, y + 1), Color.Black);
            font.DrawText(sb, text, new Vector2(5, y), Color.Lime);
            y += 18;
        }

        DrawLine($"FPS: {FNFGame.Instance?.CurrentFPS ?? 0}");
        DrawLine($"Mouse: {mouse.X}, {mouse.Y}");
        if (sceneName != null) DrawLine($"Scene: {sceneName}");
        DrawLine($"[F3] Toggle | [F5] Editors");

        foreach (var line in _persistentLines)
            DrawLine(line);
        foreach (var line in _lines)
            DrawLine(line);

        _lines.Clear();
    }
}
