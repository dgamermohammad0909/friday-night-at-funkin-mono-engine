using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using Newtonsoft.Json;
using FontStashSharp;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Manages loading and caching of game assets (textures, sounds, etc.)
/// </summary>
public class AssetManager : IDisposable
{
    private readonly Game _game;
    private readonly Dictionary<string, Texture2D> _textures = new();
    private readonly Dictionary<string, SoundEffect> _sounds = new();
    private readonly string _contentPath;
    
    /// <summary>
    /// Additional search roots for assets (e.g. funkin.assets directory).
    /// Searched in order after _contentPath.
    /// </summary>
    private readonly List<string> _extraRoots = new();
    
    // Font system
    private FontSystem _fontSystem;
    private Dictionary<int, SpriteFontBase> _fontCache = new();
    
    // Pixel texture for drawing rectangles
    public Texture2D Pixel { get; private set; }

    // Pre-rendered smooth circle texture for button prompts
    private Texture2D _circleTexture;
    
    public AssetManager(Game game)
    {
        _game = game;
#if XBOX_UWP
        // Xbox: Content lives in LocalState folder (uploaded via Dev Portal),
        // NOT in the MSIX package. This keeps the MSIX tiny for fast startup.
        // Upload Content/ folder to: LocalState/Content/ via Xbox Dev Portal File Explorer.
        string localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        string localContent = Path.Combine(localState, "Content");
        if (Directory.Exists(localContent))
        {
            _contentPath = localContent;
        }
        else
        {
            // Fallback: try package install location (for small test builds)
            _contentPath = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content");
        }
#else
        _contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
#endif
        
        // Create pixel texture
        Pixel = new Texture2D(game.GraphicsDevice, 1, 1);
        Pixel.SetData(new[] { Color.White });

        // Create smooth anti-aliased circle texture (128x128)
        _circleTexture = CreateCircleTexture(game.GraphicsDevice, 128);
        
        // Initialize font system
        _fontSystem = new FontSystem();

#if !XBOX_UWP
        // Auto-detect funkin.assets directory (original FNF release assets).
        // Search upward from the executable, then common sibling locations.
        // Skip on Xbox UWP — funkin.assets doesn't exist in the sandboxed package
        // and Directory.Exists() calls are slow on Xbox.
        string funkinAssets = FindFunkinAssets();
        if (funkinAssets != null)
        {
            _extraRoots.Add(funkinAssets);
            Console.WriteLine($"Found funkin.assets: {funkinAssets}");
        }

        // Also support official source checkout layout where assets live under
        // FNF_Official/assets (sibling to this workspace), so week/stage folders
        // like sserafim and weekend1 resolve without manual copying.
        string officialAssets = FindOfficialAssets();
        if (officialAssets != null)
        {
            bool alreadyAdded = false;
            foreach (var root in _extraRoots)
            {
                if (string.Equals(root, officialAssets, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyAdded = true;
                    break;
                }
            }
            if (!alreadyAdded)
            {
                _extraRoots.Add(officialAssets);
                Console.WriteLine($"Found FNF_Official assets: {officialAssets}");
            }
        }
#endif
    }
    
    /// <summary>
    /// Try to locate the funkin.assets directory automatically.
    /// Public so FNFGame can share this without duplicating logic.
    /// </summary>
    public static string FindFunkinAssets()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // Walk up from exe directory looking for funkin.assets
        string dir = baseDir;
        for (int i = 0; i < 6; i++)
        {
            string candidate = Path.Combine(dir, "funkin.assets");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "shared")))
                return candidate;
            string parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        
        return null;
    }

    /// <summary>
    /// Try to locate the official source checkout asset root (FNF_Official/assets).
    /// </summary>
    public static string FindOfficialAssets()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string dir = baseDir;

        for (int i = 0; i < 7; i++)
        {
            string candidate = Path.Combine(dir, "FNF_Official", "assets");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "shared")))
                return candidate;

            string parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        return null;
    }
    
    /// <summary>
    /// Resolve a relative asset path to a full filesystem path.
    /// Tries Content/ first, then maps to funkin.assets structure.
    /// Returns null if not found anywhere.
    /// </summary>
    // Negative cache: paths confirmed not to exist (avoids repeated File.Exists calls)
    private readonly HashSet<string> _missingPaths = new(StringComparer.OrdinalIgnoreCase);
    
    public string ResolvePath(string relativePath)
    {
        // Skip paths already known to be missing
        if (_missingPaths.Contains(relativePath))
            return null;
        
        // 1. Content directory (primary)
        string full = Path.Combine(_contentPath, relativePath);
        if (File.Exists(full)) return full;
        
        // 2. Search extra roots with path mapping
        foreach (var root in _extraRoots)
        {
            // Direct path under root
            string direct = Path.Combine(root, relativePath);
            if (File.Exists(direct)) return direct;
            
            // Map game engine paths ? funkin.assets structure
            string mapped = MapToFunkinAssets(root, relativePath);
            if (mapped != null && File.Exists(mapped)) return mapped;
        }
        
        _missingPaths.Add(relativePath);
        return null;
    }
    
    // Negative cache for directories confirmed not to exist
    private readonly HashSet<string> _missingDirs = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Resolve a relative directory path.
    /// </summary>
    public string ResolveDirectory(string relativePath)
    {
        if (_missingDirs.Contains(relativePath))
            return null;
        
        string full = Path.Combine(_contentPath, relativePath);
        if (Directory.Exists(full)) return full;
        
        foreach (var root in _extraRoots)
        {
            string direct = Path.Combine(root, relativePath);
            if (Directory.Exists(direct)) return direct;
            
            string mapped = MapDirToFunkinAssets(root, relativePath);
            if (mapped != null && Directory.Exists(mapped)) return mapped;
        }
        
        _missingDirs.Add(relativePath);
        return null;
    }
    
    /// <summary>
    /// Map a game-relative path to the funkin.assets directory structure.
    /// </summary>
    private static string MapToFunkinAssets(string root, string path)
    {
        // Normalize separators
        string norm = path.Replace('\\', '/');
        
        // data/characters/{name}.json ? preload/data/characters/{name}.json
        if (norm.StartsWith("data/"))
            return Path.Combine(root, "preload", norm);
        
        // game/characters/{name}/... ? shared/images/characters/{name}/...
        if (norm.StartsWith("game/characters/"))
        {
            string rest = norm["game/characters/".Length..];
            return Path.Combine(root, "shared", "images", "characters", rest);
        }
        
        // game/stages/{folder}/... ? try weekN/images/... and shared/images/...
        if (norm.StartsWith("game/stages/"))
        {
            string rest = norm["game/stages/".Length..];
            // Try shared first
            string shared = Path.Combine(root, "shared", "images", rest);
            if (File.Exists(shared)) return shared;
            // Try each week folder
            for (int w = 1; w <= 7; w++)
            {
                string weekPath = Path.Combine(root, $"week{w}", "images", rest);
                if (File.Exists(weekPath)) return weekPath;
            }
            string we1 = Path.Combine(root, "weekend1", "images", rest);
            if (File.Exists(we1)) return we1;
        }
        
        // game/skins/... ? shared/images/ui/...
        if (norm.StartsWith("game/skins/"))
        {
            string rest = norm["game/skins/".Length..];
            return Path.Combine(root, "shared", "images", "ui", rest);
        }
        
        // images/... ? shared/images/... or preload/images/...
        if (norm.StartsWith("images/"))
        {
            string shared = Path.Combine(root, "shared", norm);
            if (File.Exists(shared)) return shared;
            return Path.Combine(root, "preload", norm);
        }
        
        // songs/... ? songs/... (same path in funkin.assets)
        if (norm.StartsWith("songs/"))
            return Path.Combine(root, norm);
        
        // sounds/... ? shared/sounds/... or preload/sounds/...
        if (norm.StartsWith("sounds/"))
        {
            string shared = Path.Combine(root, "shared", norm);
            if (File.Exists(shared)) return shared;
            return Path.Combine(root, "preload", norm);
        }
        
        return null;
    }
    
    /// <summary>
    /// Map a game-relative directory path to the funkin.assets directory structure.
    /// </summary>
    private static string MapDirToFunkinAssets(string root, string path)
    {
        string norm = path.Replace('\\', '/');
        
        if (norm.StartsWith("data/"))
            return Path.Combine(root, "preload", norm);
        
        if (norm.StartsWith("game/characters/"))
        {
            string rest = norm["game/characters/".Length..];
            return Path.Combine(root, "shared", "images", "characters", rest);
        }
        
        if (norm.StartsWith("game/stages/"))
        {
            string rest = norm["game/stages/".Length..];
            string shared = Path.Combine(root, "shared", "images", rest);
            if (Directory.Exists(shared)) return shared;
            for (int w = 1; w <= 7; w++)
            {
                string weekPath = Path.Combine(root, $"week{w}", "images", rest);
                if (Directory.Exists(weekPath)) return weekPath;
            }
            string we1 = Path.Combine(root, "weekend1", "images", rest);
            if (Directory.Exists(we1)) return we1;
        }
        
        if (norm.StartsWith("images/"))
        {
            string shared = Path.Combine(root, "shared", norm);
            if (Directory.Exists(shared)) return shared;
            return Path.Combine(root, "preload", norm);
        }
        
        if (norm.StartsWith("songs/"))
            return Path.Combine(root, norm);
        
        return null;
    }
    
    public void LoadCommonAssets()
    {
        // Load fonts (required for all text rendering)
        LoadFont("fonts/vcr.ttf");

        // NOTE: Do NOT bulk-load textures from menus/, game/ui/, etc. here.
        // Loading 200+ PNGs synchronously blocks the game loop for several seconds,
        // causing a frozen window on PC and a startup timeout (0x8027025a) on Xbox.
        // All textures are loaded on-demand via LoadTexture() which caches results.
        // Each scene loads its own spritesheets via SpriteSheet.Load() as needed.
    }
    
    public void LoadFont(string path)
    {
        string fullPath = Path.Combine(_contentPath, path);
        if (File.Exists(fullPath))
        {
            byte[] fontData = File.ReadAllBytes(fullPath);
            _fontSystem.AddFont(fontData);
            Console.WriteLine($"Loaded font: {path}");
        }
        else
        {
            Console.WriteLine($"Font not found: {fullPath}");
        }
    }
    
    public SpriteFontBase GetFont(int size)
    {
        if (_fontCache.TryGetValue(size, out var cached))
            return cached;
        
        var font = _fontSystem.GetFont(size);
        _fontCache[size] = font;
        return font;
    }
    
    public Texture2D LoadTexture(string path)
    {
        if (_textures.TryGetValue(path, out var cached))
            return cached;
        
        // Try resolve with and without .png extension
        string fullPath = ResolvePath(path);
        if (fullPath == null && !path.EndsWith(".png"))
            fullPath = ResolvePath(path + ".png");
        
        if (fullPath == null)
        {
            return Pixel; // Return pixel as fallback
        }
        
        using var stream = File.OpenRead(fullPath);
        var texture = Texture2D.FromStream(_game.GraphicsDevice, stream);
        // Skip PremultiplyAlpha — GetData/SetData causes multi-second freezes.
        // Use BlendState.NonPremultiplied when drawing instead.
        _textures[path] = texture;
        return texture;
    }
    
    public void LoadTexturesFromFolder(string folder)
    {
        string fullPath = Path.Combine(_contentPath, folder);
        if (!Directory.Exists(fullPath))
        {
            Console.WriteLine($"Folder not found: {fullPath}");
            return;
        }
        
        foreach (var file in Directory.GetFiles(fullPath, "*.png", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(_contentPath, file);
            LoadTexture(relativePath);
        }
    }
    
    public T LoadJson<T>(string path)
    {
        string fullPath = ResolvePath(path);
        if (fullPath == null && !path.EndsWith(".json"))
            fullPath = ResolvePath(path + ".json");
        
        if (fullPath == null)
        {
            return default;
        }
        
        try
        {
            string json = File.ReadAllText(fullPath);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JSON parse error for '{fullPath}': {ex.Message}");
            return default;
        }
    }
    
    /// <summary>
    /// Get the controller button sprite texture for a given gamepad button.
    /// Sprites are loaded on first call and cached. Returns null if not found.
    /// </summary>
    public Texture2D GetButtonSprite(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        string name = btn switch
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
        if (name == null) return null;
        var tex = LoadTexture($"game/ui/controller/{name}.png");
        return tex != Pixel ? tex : null;
    }

    /// <summary>
    /// Create a smooth anti-aliased filled circle texture.
    /// </summary>
    private static Texture2D CreateCircleTexture(GraphicsDevice gd, int diameter)
    {
        var tex = new Texture2D(gd, diameter, diameter);
        var data = new Color[diameter * diameter];
        float center = (diameter - 1) / 2f;
        float radius = diameter / 2f;

        for (int py = 0; py < diameter; py++)
        {
            for (int px = 0; px < diameter; px++)
            {
                float dx = px - center;
                float dy = py - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                // Smooth AA edge: 1.2px feather
                float aa = Math.Clamp(radius - dist, 0f, 1.2f) / 1.2f;
                if (aa <= 0) { data[py * diameter + px] = Color.Transparent; continue; }
                // Subtle 3D shading: lighter on top, darker on bottom
                float shade = 1.0f - dy / radius * 0.18f;
                // Slight highlight near top-left
                float highlight = Math.Max(0, 1f - (dist / radius)) * 0.08f;
                float normY = (dy + radius) / (radius * 2f);
                highlight *= (1f - normY);
                byte gray = (byte)Math.Clamp((int)(255 * (shade + highlight)), 180, 255);
                data[py * diameter + px] = new Color(gray, gray, gray, (byte)(aa * 255));
            }
        }
        tex.SetData(data);
        return tex;
    }

    /// <summary>
    /// Draw a high-quality controller button prompt (smooth circle + bold letter).
    /// Uses a pre-rendered anti-aliased circle texture for crisp rendering.
    /// </summary>
    public void DrawButtonPrompt(SpriteBatch sb, Microsoft.Xna.Framework.Input.Buttons btn, int x, int y, int size = 28, float alpha = 0.9f)
    {
        Color bgColor;
        string label;
        switch (btn)
        {
            case Microsoft.Xna.Framework.Input.Buttons.A:
                bgColor = new Color(20, 160, 20); label = "A"; break;
            case Microsoft.Xna.Framework.Input.Buttons.B:
                bgColor = new Color(210, 40, 40); label = "B"; break;
            case Microsoft.Xna.Framework.Input.Buttons.X:
                bgColor = new Color(30, 100, 210); label = "X"; break;
            case Microsoft.Xna.Framework.Input.Buttons.Y:
                bgColor = new Color(210, 190, 20); label = "Y"; break;
            case Microsoft.Xna.Framework.Input.Buttons.Start:
                bgColor = new Color(90, 90, 100); label = "\u2261"; break;
            case Microsoft.Xna.Framework.Input.Buttons.Back:
                bgColor = new Color(90, 90, 100); label = "\u25C1"; break;
            case Microsoft.Xna.Framework.Input.Buttons.LeftShoulder:
                bgColor = new Color(70, 70, 80); label = "LB"; break;
            case Microsoft.Xna.Framework.Input.Buttons.RightShoulder:
                bgColor = new Color(70, 70, 80); label = "RB"; break;
            case Microsoft.Xna.Framework.Input.Buttons.LeftTrigger:
                bgColor = new Color(70, 70, 80); label = "LT"; break;
            case Microsoft.Xna.Framework.Input.Buttons.RightTrigger:
                bgColor = new Color(70, 70, 80); label = "RT"; break;
            case Microsoft.Xna.Framework.Input.Buttons.DPadUp:
                bgColor = new Color(70, 70, 80); label = "\u25B2"; break;
            case Microsoft.Xna.Framework.Input.Buttons.DPadDown:
                bgColor = new Color(70, 70, 80); label = "\u25BC"; break;
            case Microsoft.Xna.Framework.Input.Buttons.DPadLeft:
                bgColor = new Color(70, 70, 80); label = "\u25C0"; break;
            case Microsoft.Xna.Framework.Input.Buttons.DPadRight:
                bgColor = new Color(70, 70, 80); label = "\u25B6"; break;
            default:
                bgColor = new Color(70, 70, 80); label = btn.ToString()[..1]; break;
        }

        // Drop shadow (offset circle in dark)
        if (_circleTexture != null)
        {
            sb.Draw(_circleTexture, new Rectangle(x + 2, y + 2, size, size),
                Color.Black * (alpha * 0.35f));
            // Main circle tinted to button color
            sb.Draw(_circleTexture, new Rectangle(x, y, size, size),
                bgColor * alpha);
        }

        // Centered letter
        int cx = x + size / 2;
        int cy = y + size / 2;
        int fontSize = label.Length <= 1 ? (int)(size * 0.52f) : (int)(size * 0.36f);
        var font = GetFont(Math.Max(10, fontSize));
        if (font != null)
        {
            var textSize = font.MeasureString(label);
            font.DrawText(sb, label,
                new Vector2(cx - textSize.X / 2 + 1, cy - textSize.Y / 2 + 1),
                Color.Black * (alpha * 0.6f));
            font.DrawText(sb, label,
                new Vector2(cx - textSize.X / 2, cy - textSize.Y / 2),
                Color.White * alpha);
        }
    }

    /// <summary>
    /// Draw a controller button receptor using the actual Xbox button PNG sprites.
    /// Renders the pre-made button texture with glow effects on press/confirm.
    /// </summary>
    public void DrawButtonReceptor(SpriteBatch sb, Microsoft.Xna.Framework.Input.Buttons btn,
        int x, int y, int size, bool pressed, bool confirm, float alpha = 1f)
    {
        // Load the matching Xbox button texture
        string texPath = btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.X => "game/ui/controller/xbox_button_color_x.png",
            Microsoft.Xna.Framework.Input.Buttons.A => "game/ui/controller/xbox_button_color_a.png",
            Microsoft.Xna.Framework.Input.Buttons.Y => "game/ui/controller/xbox_button_color_y.png",
            Microsoft.Xna.Framework.Input.Buttons.B => "game/ui/controller/xbox_button_color_b.png",
            _ => null
        };

        var btnTex = texPath != null ? LoadTexture(texPath) : null;
        if (btnTex != null && btnTex != Pixel)
        {
            if (pressed || confirm)
            {
                // Glow ring behind button when pressed/confirmed
                var (glowColor, _) = GetButtonInfo(btn);
                int glowSize = size + 12;
                int gx = x + size / 2 - glowSize / 2;
                int gy = y + size / 2 - glowSize / 2;
                if (_circleTexture != null)
                {
                    sb.Draw(_circleTexture, new Rectangle(gx, gy, glowSize, glowSize),
                        glowColor * (alpha * 0.6f));
                }
                // Draw button slightly smaller when pressed (squish effect)
                int shrink = pressed ? 6 : 0;
                sb.Draw(btnTex,
                    new Rectangle(x + shrink / 2, y + shrink / 2, size - shrink, size - shrink),
                    Color.White * alpha);
            }
            else
            {
                // Static: draw button texture at normal size
                sb.Draw(btnTex, new Rectangle(x, y, size, size), Color.White * (alpha * 0.9f));
            }
        }
        else
        {
            // Fallback: draw colored circle with BLACK letter text
            var (bgColor, label) = GetButtonInfo(btn);
            if (_circleTexture != null)
            {
                // Black outline
                sb.Draw(_circleTexture, new Rectangle(x - 2, y - 2, size + 4, size + 4),
                    Color.Black * (alpha * 0.9f));
                Color drawColor = (pressed || confirm)
                    ? new Color(Math.Min(255, bgColor.R + 60), Math.Min(255, bgColor.G + 60), Math.Min(255, bgColor.B + 60))
                    : bgColor;
                int shrink = pressed ? 4 : 0;
                sb.Draw(_circleTexture,
                    new Rectangle(x + shrink / 2, y + shrink / 2, size - shrink, size - shrink),
                    drawColor * alpha);
            }
            // Black letter
            int cx = x + size / 2;
            int cy = y + size / 2;
            int fontSize = Math.Max(12, (int)(size * 0.5f));
            var font = GetFont(fontSize);
            if (font != null)
            {
                var textSize = font.MeasureString(label);
                font.DrawText(sb, label,
                    new Vector2(cx - textSize.X / 2, cy - textSize.Y / 2),
                    Color.Black * alpha);
            }
        }
    }

    /// <summary>
    /// Get the Xbox button color and label for a given button.
    /// Used for controller button receptors and hold note coloring.
    /// </summary>
    public static (Color color, string label) GetButtonInfo(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        return btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.X => (new Color(30, 110, 220), "X"),    // Blue
            Microsoft.Xna.Framework.Input.Buttons.A => (new Color(20, 170, 20), "A"),     // Green
            Microsoft.Xna.Framework.Input.Buttons.Y => (new Color(220, 200, 20), "Y"),    // Yellow
            Microsoft.Xna.Framework.Input.Buttons.B => (new Color(220, 40, 40), "B"),     // Red
            _ => (new Color(90, 90, 100), btn.ToString()[..1])
        };
    }

    public void Dispose()
    {
        Pixel?.Dispose();
        _circleTexture?.Dispose();
        _fontSystem?.Dispose();
        _fontCache.Clear();
        foreach (var tex in _textures.Values)
            tex?.Dispose();
        foreach (var snd in _sounds.Values)
            snd?.Dispose();
        _textures.Clear();
        _sounds.Clear();
    }
}
