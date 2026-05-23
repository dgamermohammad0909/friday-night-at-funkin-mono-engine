using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// A single character glyph from the FNF alphabet spritesheet.
/// Stores the source rect, offset, and frame dimensions.
/// </summary>
public class AlphabetGlyph
{
    public Rectangle SourceRect { get; set; }
    public Vector2 Offset { get; set; }
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
}

/// <summary>
/// FNF bitmap alphabet font system — loads alphabet_bold.png + XML (Sparrow format).
/// Matches the original AtlasText.hx / Alphabet.hx rendering.
/// 
/// The atlas contains frames named like "A bold0000", "A bold0001", etc.
/// Each character has multiple animation frames; we use the first frame (0000) for static text.
/// Special characters map to descriptive names (e.g. '!' ? "exclamation_mark bold").
/// </summary>
public class AlphabetFont
{
    public Texture2D Texture { get; private set; }
    public float MaxHeight { get; private set; }

    // char ? first frame glyph
    private readonly Dictionary<char, AlphabetGlyph> _glyphs = new();
    // char ? all animation frames
    private readonly Dictionary<char, List<AlphabetGlyph>> _animFrames = new();

    private static AlphabetFont _boldInstance;
    private static AlphabetFont _defaultInstance;

    /// <summary>
    /// Dispose static singleton font instances. Called at game shutdown.
    /// </summary>
    public static void DisposeAll()
    {
        // Default may be the same instance as Bold (fallback), so track to avoid double-dispose
        var disposedBold = _boldInstance;
        if (_boldInstance?.Texture != null && !_boldInstance.Texture.IsDisposed)
            _boldInstance.Texture.Dispose();
        _boldInstance = null;
        if (_defaultInstance != null && _defaultInstance != disposedBold
            && _defaultInstance.Texture != null && !_defaultInstance.Texture.IsDisposed)
            _defaultInstance.Texture.Dispose();
        _defaultInstance = null;
    }

    /// <summary>
    /// Get the bold alphabet font (used by most menus).
    /// </summary>
    public static AlphabetFont Bold
    {
        get
        {
            if (_boldInstance == null)
                _boldInstance = Load("fonts/alphabet/alphabet_bold");
            return _boldInstance;
        }
    }

    /// <summary>
    /// Get the default/regular alphabet font.
    /// </summary>
    public static AlphabetFont Default
    {
        get
        {
            if (_defaultInstance == null)
            {
                _defaultInstance = Load("fonts/alphabet/alphabet_regular");
                _defaultInstance ??= Bold; // fallback
            }
            return _defaultInstance;
        }
    }

    /// <summary>
    /// Load an alphabet font from a Sparrow atlas (PNG + XML).
    /// </summary>
    public static AlphabetFont Load(string basePath)
    {
        var font = new AlphabetFont();
        var assets = FNFGame.Instance?.Assets;
        
        string pngPath = assets?.ResolvePath(basePath + ".png");
        string xmlPath = assets?.ResolvePath(basePath + ".xml");
        
        // Fallback to Content directory
        if (pngPath == null || xmlPath == null)
        {
            string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
            pngPath ??= Path.Combine(contentPath, basePath + ".png");
            xmlPath ??= Path.Combine(contentPath, basePath + ".xml");
        }

        if (!File.Exists(pngPath) || !File.Exists(xmlPath))
        {
            return null;
        }

        using (var stream = File.OpenRead(pngPath))
        {
            font.Texture = Texture2D.FromStream(FNFGame.Instance.GraphicsDevice, stream);
        }

        var doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root == null) return null;

        // Parse all SubTexture elements
        foreach (var sub in root.Elements("SubTexture"))
        {
            string name = sub.Attribute("name")?.Value ?? "";
            int x = int.Parse(sub.Attribute("x")?.Value ?? "0");
            int y = int.Parse(sub.Attribute("y")?.Value ?? "0");
            int w = int.Parse(sub.Attribute("width")?.Value ?? "0");
            int h = int.Parse(sub.Attribute("height")?.Value ?? "0");
            int fx = int.Parse(sub.Attribute("frameX")?.Value ?? "0");
            int fy = int.Parse(sub.Attribute("frameY")?.Value ?? "0");
            int fw = int.Parse(sub.Attribute("frameWidth")?.Value ?? w.ToString());
            int fh = int.Parse(sub.Attribute("frameHeight")?.Value ?? h.ToString());

            var glyph = new AlphabetGlyph
            {
                SourceRect = new Rectangle(x, y, w, h),
                Offset = new Vector2(-fx, -fy),
                FrameWidth = fw > 0 ? fw : w,
                FrameHeight = fh > 0 ? fh : h
            };

            if (fh > font.MaxHeight) font.MaxHeight = fh;
            if (h > font.MaxHeight) font.MaxHeight = h;

            // Parse the character from the frame name
            // Format: "A bold0000", "exclamation_mark bold0000", etc.
            char? ch = ParseCharFromFrameName(name);
            if (ch == null) continue;

            char c = ch.Value;
            if (!font._animFrames.ContainsKey(c))
                font._animFrames[c] = new List<AlphabetGlyph>();
            font._animFrames[c].Add(glyph);

            // Use the first frame as the static glyph
            if (!font._glyphs.ContainsKey(c))
                font._glyphs[c] = glyph;
        }

        Console.WriteLine($"AlphabetFont loaded: {basePath} ({font._glyphs.Count} chars, maxH={font.MaxHeight})");
        return font;
    }

    /// <summary>
    /// Parse a character from a Sparrow frame name.
    /// Examples: "A bold0000" ? 'A', "exclamation_mark bold0000" ? '!'
    /// </summary>
    private static char? ParseCharFromFrameName(string name)
    {
        // Strip the frame number suffix (last 4 digits)
        // Format: "{prefix} bold0000" or "{prefix} regular0000"
        string prefix;
        int boldIdx = name.IndexOf(" bold", StringComparison.OrdinalIgnoreCase);
        int regIdx = name.IndexOf(" regular", StringComparison.OrdinalIgnoreCase);
        if (boldIdx >= 0)
            prefix = name[..boldIdx];
        else if (regIdx >= 0)
            prefix = name[..regIdx];
        else
            return null;

        // Single letter or digit
        if (prefix.Length == 1) return prefix[0];

        // Special character names (matching AtlasChar.getAnimPrefix in original)
        return prefix.ToLowerInvariant() switch
        {
            "ampersand" => '&',
            "apostrophe" or "apostraphie" => '\'',
            "asterisk" or "multiply x" => '*',
            "at_sign" or "at sign" => '@',
            "backslash" or "back slash" => '\\',
            "caret" => '^',
            "colon" => ':',
            "comma" => ',',
            "dash" => '-',
            "dollar_sign" or "dollar sign" => '$',
            "end_double_quote" or "end quote" => '\u201D',
            "equals" => '=',
            "exclamation_mark" or "exclamation point" => '!',
            "greater_than" or "greater than" => '>',
            "hashtag" => '#',
            "heart" => '\u2665',
            "left_bracket" => '[',
            "left_parenthesis" => '(',
            "less_than" or "less than" => '<',
            "multiplication_sign" or "multiply_x" => '*',
            "percent_sign" => '%',
            "period" => '.',
            "plus_sign" => '+',
            "question_mark" or "question mark" => '?',
            "right_bracket" => ']',
            "right_parenthesis" => ')',
            "semicolon" => ';',
            "slash" or "forward slash" => '/',
            "start_double_quote" or "start quote" => '\u201C',
            "tilde" => '~',
            "underscore" => '_',
            "vertical_bar" => '|',
            _ => null
        };
    }

    /// <summary>
    /// Measure the width of a string rendered with this font.
    /// </summary>
    public float MeasureWidth(string text, float scale = 1f)
    {
        float x = 0;
        foreach (char c in text)
        {
            if (c == ' ')
            {
                x += 40 * scale;
                continue;
            }
            char upper = char.ToUpper(c);
            if (_glyphs.TryGetValue(upper, out var g))
                x += g.FrameWidth * scale;
            else if (_glyphs.TryGetValue(c, out var g2))
                x += g2.FrameWidth * scale;
            else
                x += 40 * scale; // unknown char fallback
        }
        return x;
    }

    /// <summary>
    /// Measure the full size of a string (supports newlines).
    /// </summary>
    public Vector2 MeasureString(string text, float scale = 1f)
    {
        float maxW = 0;
        float curW = 0;
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                if (curW > maxW) maxW = curW;
                curW = 0;
                lines++;
                continue;
            }
            if (c == ' ')
            {
                curW += 40 * scale;
                continue;
            }
            char upper = char.ToUpper(c);
            if (_glyphs.TryGetValue(upper, out var g))
                curW += g.FrameWidth * scale;
            else if (_glyphs.TryGetValue(c, out var g2))
                curW += g2.FrameWidth * scale;
            else
                curW += 40 * scale;
        }
        if (curW > maxW) maxW = curW;
        return new Vector2(maxW, lines * MaxHeight * scale);
    }

    /// <summary>
    /// Draw a string at the given position. Each character is drawn from the atlas.
    /// Supports per-character Y bobbing when yBob is true (like original menu items).
    /// </summary>
    public void DrawString(SpriteBatch spriteBatch, string text, Vector2 position,
        Color color, float scale = 1f, bool yBob = false, float bobTimer = 0f)
    {
        if (Texture == null || string.IsNullOrEmpty(text)) return;

        float xCursor = 0;
        float yCursor = 0;
        int charIndex = 0;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                xCursor = 0;
                yCursor += MaxHeight * scale;
                charIndex = 0;
                continue;
            }
            if (c == ' ')
            {
                xCursor += 40 * scale;
                charIndex++;
                continue;
            }

            // Try uppercase first (bold atlas is uppercase only)
            char lookup = char.ToUpper(c);
            if (!_glyphs.TryGetValue(lookup, out var glyph))
            {
                if (!_glyphs.TryGetValue(c, out glyph))
                {
                    xCursor += 40 * scale;
                    charIndex++;
                    continue;
                }
            }

            float yOff = 0;
            if (yBob)
            {
                // Original: each character bobs with sin wave offset by index
                yOff = MathF.Sin((bobTimer * 3f) + charIndex * 0.5f) * 4f * scale;
            }

            float drawX = position.X + xCursor + glyph.Offset.X * scale;
            float drawY = position.Y + yCursor + yOff + glyph.Offset.Y * scale;
            // Align to bottom of line (like original: y + maxHeight - charHeight)
            drawY += (MaxHeight - glyph.FrameHeight) * scale;

            spriteBatch.Draw(Texture,
                new Vector2(drawX, drawY),
                glyph.SourceRect,
                color,
                0f,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                0f);

            xCursor += glyph.FrameWidth * scale;
            charIndex++;
        }
    }

    /// <summary>
    /// Draw a string centered horizontally at the given Y position.
    /// </summary>
    public void DrawStringCentered(SpriteBatch spriteBatch, string text, float y,
        Color color, float scale = 1f, bool yBob = false, float bobTimer = 0f)
    {
        float width = MeasureWidth(text, scale);
        float x = (FNFGame.SCREEN_WIDTH - width) / 2f;
        DrawString(spriteBatch, text, new Vector2(x, y), color, scale, yBob, bobTimer);
    }
}
