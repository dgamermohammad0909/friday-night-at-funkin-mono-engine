using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using Newtonsoft.Json;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Character JSON data model � matches data/characters/*.json format.
/// </summary>
public class CharacterJsonData
{
    [JsonProperty("version")] public string Version { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("renderType")] public string RenderType { get; set; }
    [JsonProperty("assetPath")] public string AssetPath { get; set; }
    [JsonProperty("flipX")] public bool FlipX { get; set; }
    [JsonProperty("isPixel")] public bool IsPixel { get; set; }
    [JsonProperty("offsets")] public float[] Offsets { get; set; }
    [JsonProperty("cameraOffsets")] public float[] CameraOffsets { get; set; }
    [JsonProperty("singTime")] public float SingTime { get; set; } = 4f;
    [JsonProperty("scale")] public float Scale { get; set; } = 1f;
    [JsonProperty("startingAnimation")] public string StartingAnimation { get; set; }
    [JsonProperty("animations")] public List<CharacterAnimJsonData> Animations { get; set; }
    [JsonProperty("death")] public CharacterDeathData Death { get; set; }
    [JsonProperty("healthBarColor")] public string HealthBarColor { get; set; }
}

public class CharacterAnimJsonData
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("prefix")] public string Prefix { get; set; }
    [JsonProperty("offsets")] public float[] Offsets { get; set; }
    [JsonProperty("looped")] public bool Looped { get; set; }
    [JsonProperty("frameRate")] public int FrameRate { get; set; } = 24;
    [JsonProperty("frameIndices")] public int[] FrameIndices { get; set; }
    [JsonProperty("asset")] public string Asset { get; set; }
    [JsonProperty("assetPath")] public string AssetPath { get; set; }
}

public class CharacterDeathData
{
    [JsonProperty("cameraOffsets")] public float[] CameraOffsets { get; set; }
}

/// <summary>
/// Represents an animated character (BF, GF, Dad, etc.)
/// Uses Sparrow XML spritesheets for animation.
/// Loads animation mappings from data/characters/*.json when available.
/// </summary>
public class Character
{
    public string Name { get; }
    /// <summary>Original character name before sprite resolution (e.g., "gf-car" before mapping to "gf").</summary>
    public string OriginalName { get; private set; }
    public Vector2 Position { get; set; }
    public float Scale { get; set; } = 0.7f;
    public bool FlipX { get; set; } = false;
    
    // JSON-loaded character data (null if no JSON found)
    public CharacterJsonData JsonData { get; private set; }
    // Camera offsets from character JSON (can be overridden by stage JSON)
    public float[] CameraOffsets { get; set; }
    // Global offsets from character JSON
    public float[] CharOffsets { get; private set; }
    
    /// <summary>
    /// Approximate midpoint of the character sprite (center of current frame).
    /// Matches HaxeFlixel's getMidpoint(): position + (frameWidth*scale/2, frameHeight*scale/2).
    /// Used for camera follow calculations.
    /// </summary>
    public Vector2 GetMidpoint()
    {
        // Default frame size: 300px for normal characters, 30px for pixel (before 6x scale)
        float defaultSize = (JsonData?.IsPixel == true) ? 30f : 300f;
        float halfW = defaultSize * Scale / 2f;
        float halfH = defaultSize * Scale / 2f;
        if (_sprite?.GetCurrentFrame() is SpriteFrame frame)
        {
            int fw = frame.FrameWidth > 0 ? frame.FrameWidth : (frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width);
            int fh = frame.FrameHeight > 0 ? frame.FrameHeight : (frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height);
            halfW = fw * Scale / 2f;
            halfH = fh * Scale / 2f;
        }
        // Include globalOffsets (CharOffsets) so camera follow matches original FNF
        float offX = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[0] : 0f;
        float offY = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[1] : 0f;
        return new Vector2(Position.X + halfW + offX, Position.Y + halfH + offY);
    }
    
    /// <summary>
    /// Character origin point (horizontal center, vertical bottom) matching original FNF.
    /// Original: characterOrigin = (width/2, height) in BaseCharacter.hx.
    /// Stage JSON positions represent the character's feet position (bottom-center).
    /// The sprite is drawn offset by the origin so the feet align with the stage position.
    /// </summary>
    public Vector2 GetCharacterOrigin()
    {
        float defaultSize = (JsonData?.IsPixel == true) ? 30f : 300f;
        float w = defaultSize * Scale;
        float h = defaultSize * Scale;
        if (_sprite?.GetCurrentFrame() is SpriteFrame frame)
        {
            int fw = frame.FrameWidth > 0 ? frame.FrameWidth : (frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width);
            int fh = frame.FrameHeight > 0 ? frame.FrameHeight : (frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height);
            w = fw * Scale;
            h = fh * Scale;
        }
        return new Vector2(w / 2f, h);
    }
    
    /// <summary>
    /// Health bar color for this character (matches original FNF HealthIcon.hx colors).
    /// </summary>
    public Color HealthBarColor { get; private set; } = Color.Gray;
    
    private AnimatedSprite _sprite;
    private string _currentAnimation = "idle";
    private float _animHoldTime = 0;
    private int _danceInterval = 2; // beats
    private int _lastDanceBeat = -1;
    private bool _danceLeft = true; // For GF-type characters that alternate danceLeft/danceRight
    private bool _hasDualDance; // True if character has separate danceLeft/danceRight animations
    
    // Animation name mappings from JSON (name -> prefix)
    private Dictionary<string, string> _jsonAnimMappings;
    // Animation-specific offsets from JSON
    private Dictionary<string, Vector2> _jsonAnimOffsets;
    // Per-animation frame rates from JSON (name -> fps)
    private Dictionary<string, int> _jsonAnimFrameRates;
    // Per-animation loop flags from JSON (name -> looped)
    private Dictionary<string, bool> _jsonAnimLooped;
    // Per-animation frame indices from JSON (name -> indices into parent animation)
    private Dictionary<string, int[]> _jsonAnimFrameIndices;
    
    // Animation name cache: maps logical name -> resolved spritesheet animation name
    private readonly Dictionary<string, string> _animCache = new();
    
    // Track if current animation is sing/miss to avoid StartsWith checks every frame
    private bool _isSingOrMiss;
    
    /// <summary>
    /// Step crochet in seconds (set by PlayScene from Conductor).
    /// Used to compute accurate sing hold time matching original FNF.
    /// </summary>
    public float StepCrochet { get; set; } = 0.15f;
    
    
    private static readonly Dictionary<string, string[]> AnimationMappings = new()
    {
        { "idle", new[] { "idle", "Idle", "default", "BF idle dance", "Dad idle dance", "GF Dancing Beat", "speakers", "Pico Idle Dance", "BF idle shaking", "Mom Idle", "spooky dance", "monster idle" } },
        { "danceLeft", new[] { "danceLeft", "GF Dancing Beat0", "spooky dance0" } },
        { "danceRight", new[] { "danceRight", "GF Dancing Beat1", "spooky dance1" } },
        { "singLEFT", new[] { "singLEFT", "SingLEFT", "Left", "left", "BF NOTE LEFT", "Dad Sing Note LEFT", "Pico NOTE LEFT", "GF left note", "Mom Left Pose", "Mom Pose Left", "Monster left note" } },
        { "singDOWN", new[] { "singDOWN", "SingDOWN", "Down", "down", "BF NOTE DOWN", "Dad Sing Note DOWN", "Pico Down Note", "GF Down Note", "MOM DOWN POSE", "monster down" } },
        { "singUP", new[] { "singUP", "SingUP", "Up", "up", "BF NOTE UP", "Dad Sing Note UP", "pico Up note", "GF Up Note", "Mom Up Pose", "monster up note" } },
        { "singRIGHT", new[] { "singRIGHT", "SingRIGHT", "Right", "right", "BF NOTE RIGHT", "Dad Sing Note RIGHT", "Pico Note Right", "GF Right Note", "Monster Right note" } },
        { "hey", new[] { "BF HEY!!", "Hey", "Cheer", "GF Cheer" } },
        { "scared", new[] { "scared", "GF FEAR", "gf scared" } },
        { "missLEFT", new[] { "singLEFTmiss", "BF NOTE LEFT MISS", "Pico Left Note MISS" } },
        { "missDOWN", new[] { "singDOWNmiss", "BF NOTE DOWN MISS", "Pico Down Note MISS" } },
        { "missUP", new[] { "singUPmiss", "BF NOTE UP MISS", "Pico Up Note MISS" } },
        { "missRIGHT", new[] { "singRIGHTmiss", "BF NOTE RIGHT MISS", "Pico Right Note MISS" } },
        { "firstDeath", new[] { "BF dies", "firstDeath", "Pico Death Intro" } },
        { "deathLoop", new[] { "BF Dead Loop", "deathLoop", "Pico Death Loop" } },
        { "deathConfirm", new[] { "BF Dead confirm", "deathConfirm", "Pico Death Confirm" } },
    };
    
    private static readonly Dictionary<string, bool> CharacterFlipData = new()
    {
        { "bf", true },
        { "bf_pixel", true },
        { "pico", true },
        { "dad", false },
        { "mom", false },
        { "gf", false },
        { "spooky", false },
        { "monster", false },
        { "tankman", false },
        { "darnell", false },
        { "nene", false },
        { "parents_christmas", false },
    };
    
    /// <summary>
    /// Health bar colors per character (matches original FNF HealthIcon.hx / Constants.hx).
    /// RGB values from getOffsetsFor() in the Haxe source.
    /// </summary>
    private static readonly Dictionary<string, Color> HealthBarColors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bf",                  new Color(0x31, 0xB0, 0xD1) },
        { "bf-car",              new Color(0x31, 0xB0, 0xD1) },
        { "bf-christmas",        new Color(0x31, 0xB0, 0xD1) },
        { "bf-dark",             new Color(0x31, 0xB0, 0xD1) },
        { "bf-pixel",            new Color(0x7B, 0xD6, 0xF6) },
        { "bf-holding-gf",       new Color(0x31, 0xB0, 0xD1) },
        { "pico",                new Color(0xB7, 0xD8, 0x55) },
        { "pico-blazin",         new Color(0xB7, 0xD8, 0x55) },
        { "pico-player",         new Color(0xB7, 0xD8, 0x55) },
        { "pico-playable",       new Color(0xB7, 0xD8, 0x55) },
        { "pico-christmas",      new Color(0xB7, 0xD8, 0x55) },
        { "dad",                 new Color(0xAF, 0x66, 0xCE) },
        { "mom",                 new Color(0xD8, 0x55, 0x8E) },
        { "mom-car",             new Color(0xD8, 0x55, 0x8E) },
        { "parents-christmas",   new Color(0xAF, 0x66, 0xCE) },
        { "gf",                  new Color(0xA5, 0x00, 0x4D) },
        { "gf-car",              new Color(0xA5, 0x00, 0x4D) },
        { "gf-christmas",        new Color(0xA5, 0x00, 0x4D) },
        { "gf-pixel",            new Color(0xA5, 0x00, 0x4D) },
        { "gf-tankmen",          new Color(0xA5, 0x00, 0x4D) },
        { "spooky",              new Color(0xD5, 0x78, 0x22) },
        { "monster",             new Color(0xF3, 0xFF, 0x6E) },
        { "monster-christmas",   new Color(0xF3, 0xFF, 0x6E) },
        { "senpai",              new Color(0xFF, 0xAA, 0x6F) },
        { "senpai-angry",        new Color(0xFF, 0xAA, 0x6F) },
        { "spirit",              new Color(0xFF, 0x38, 0x07) },
        { "tankman",             new Color(0x53, 0x56, 0x54) },
        { "tankman-atlas",       new Color(0x53, 0x56, 0x54) },
        { "darnell",             new Color(0xFF, 0x4D, 0x00) },
        { "darnell-blazin",      new Color(0xFF, 0x4D, 0x00) },
        { "nene",                new Color(0xFF, 0x78, 0xBF) },
    };
    
    public Character(string name, float x, float y)
    {
        Name = name;
        Position = new Vector2(x, y);
        
        // Set health bar color from lookup (try full name, then base name)
        if (HealthBarColors.TryGetValue(name, out var color))
            HealthBarColor = color;
        else
        {
            string baseName = name.Split('-')[0].Split('_')[0];
            if (HealthBarColors.TryGetValue(baseName, out var baseColor))
                HealthBarColor = baseColor;
        }
    }
    
    
    
    
    /// <summary>
    /// Load character data from JSON (data/characters/{name}.json).
    /// Must be called before LoadSprites for JSON animation mappings to work.
    /// </summary>
    public void LoadJsonData(AssetManager assets, string originalName = null)
    {
        OriginalName = originalName ?? Name;
        string charName = OriginalName;
        JsonData = assets.LoadJson<CharacterJsonData>($"data/characters/{charName}");
        if (JsonData == null && charName != Name)
            JsonData = assets.LoadJson<CharacterJsonData>($"data/characters/{Name}");
        
        if (JsonData != null)
        {
            CameraOffsets = JsonData.CameraOffsets;
            CharOffsets = JsonData.Offsets;
            
            // Parse healthBarColor from JSON (hex string like "#31B0D1")
            if (!string.IsNullOrEmpty(JsonData.HealthBarColor))
            {
                string hex = JsonData.HealthBarColor.TrimStart('#');
                if (hex.Length >= 6)
                {
                    try
                    {
                        int r = Convert.ToInt32(hex[..2], 16);
                        int g = Convert.ToInt32(hex[2..4], 16);
                        int b = Convert.ToInt32(hex[4..6], 16);
                        HealthBarColor = new Color(r, g, b);
                    }
                    catch { }
                }
            }
            
            // Build animation mappings from JSON
            if (JsonData.Animations != null && JsonData.Animations.Count > 0)
            {
                _jsonAnimMappings = new Dictionary<string, string>();
                _jsonAnimOffsets = new Dictionary<string, Vector2>();
                _jsonAnimFrameRates = new Dictionary<string, int>();
                _jsonAnimLooped = new Dictionary<string, bool>();
                _jsonAnimFrameIndices = new Dictionary<string, int[]>();
                foreach (var anim in JsonData.Animations)
                {
                    if (!string.IsNullOrEmpty(anim.Name) && !string.IsNullOrEmpty(anim.Prefix))
                    {
                        _jsonAnimMappings[anim.Name] = anim.Prefix;
                        if (anim.Offsets != null && anim.Offsets.Length >= 2)
                            _jsonAnimOffsets[anim.Name] = new Vector2(anim.Offsets[0], anim.Offsets[1]);
                        if (anim.FrameRate > 0)
                            _jsonAnimFrameRates[anim.Name] = anim.FrameRate;
                        _jsonAnimLooped[anim.Name] = anim.Looped;
                        if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                            _jsonAnimFrameIndices[anim.Name] = anim.FrameIndices;
                    }
                }
                Console.WriteLine($"Loaded character JSON for '{charName}': {_jsonAnimMappings.Count} animations");
            }
        }
    }
    
    /// <summary>
    /// Set the character's flip based on their role (player or opponent).
    /// Original FNF logic: opponent uses flipX as-is from character data,
    /// player inverts flipX.
    /// </summary>
    public void SetFlipForRole(bool isPlayer)
    {
        // If JSON data loaded, use flipX from there
        bool baseFlip = false;
        if (JsonData != null)
        {
            baseFlip = JsonData.FlipX;
        }
        else
        {
            string baseName = Name.Split('-')[0].Split('_')[0];
            if (CharacterFlipData.TryGetValue(baseName, out bool flip))
                baseFlip = flip;
            else if (Name.StartsWith("bf"))
                baseFlip = true;
        }
        
        FlipX = isPlayer ? !baseFlip : baseFlip;
    }
    
    public void LoadSprites(Game game)
    {
        _animCache.Clear(); // Clear cache for fresh sprite loading
        
        // Build search paths for character spritesheets.
        // Priority: Sparrow XML (full pre-rendered frames) > spritemap (body parts needing compositing).
        // Sparrow XML files are reliable single-texture sprites; spritemap folders contain
        // individual body parts that must be composited which is more fragile.
        var sparrowPaths = new List<string>();
        var spritemapPaths = new List<string>();
        
        // If JSON data has an assetPath, try that FIRST (variant-specific sprites like bf-car)
        if (JsonData?.AssetPath != null)
        {
            string ap = JsonData.AssetPath;
            if (ap.StartsWith("shared:"))
                ap = ap["shared:".Length..];
            
            // Try as Sparrow XML, then as spritemap
            sparrowPaths.Add($"images/{ap}");
            sparrowPaths.Add($"game/{ap}");
            
            // For variant characters (e.g., bf-car with assetPath "characters/bf-car"),
            // also check for variant sprites in the base character folder
            // e.g., game/characters/bf/car_sprites (bf -> car_sprites from "bf-car")
            string assetName = Path.GetFileName(ap); // "bf-car" or "momCar"
            if (assetName.Contains('-'))
            {
                int dashIdx = assetName.IndexOf('-');
                string variantSuffix = assetName[(dashIdx + 1)..]; // "car"
                sparrowPaths.Add($"game/characters/{Name}/{variantSuffix}_sprites");
            }
            
            // Also try variant from original character name (e.g., "gf-car" -> "car_sprites")
            string origName = OriginalName ?? Name;
            if (origName.Contains('-') && origName != assetName)
            {
                int dashIdx = origName.IndexOf('-');
                string variantSuffix = origName[(dashIdx + 1)..];
                sparrowPaths.Add($"game/characters/{Name}/{variantSuffix}_sprites");
            }
            
            spritemapPaths.Add($"images/{ap}");
            spritemapPaths.Add($"game/{ap}");
        }
        
        // Standard Sparrow XML path (game/characters/{name}/sprites.png+xml)
        sparrowPaths.Add($"game/characters/{Name}/sprites");
        
        // More Sparrow fallback paths
        sparrowPaths.Add($"game/characters/{Name}");
        sparrowPaths.Add($"images/characters/{Name}");
        
        // Spritemap folder paths
        spritemapPaths.Add($"game/characters/{Name}/default_spritemap");
        spritemapPaths.Add($"game/characters/{Name}");
        spritemapPaths.Add($"images/characters/{Name}");
        
        _sprite = new AnimatedSprite();

        // Determine loading order based on character JSON renderType.
        // Characters with "animateatlas" or "multianimateatlas" renderType should
        // use composite (spritemap) rendering first � this matches the original FNF
        // frame sizes. Sparrow pre-rendered sprites may be at a different resolution.
        bool preferComposite = JsonData?.RenderType is "animateatlas" or "multianimateatlas";

        if (preferComposite)
        {
            // Phase 1: Try spritemap with composite rendering (matches original frame sizes)
            foreach (var path in spritemapPaths)
            {
                _sprite.Sheet = SpriteSheet.Load(game, path, preRenderComposites: true);
                if (_sprite.Sheet != null)
                {
                    Console.WriteLine($"Loaded character sprites (composite): {Name} from {path} ({_sprite.Sheet.Animations.Count} animations, {_sprite.Sheet.CompositeAnimations.Count} composite)");
                    break;
                }
            }

            // Phase 2: Fall back to Sparrow XML if composite not available
            if (_sprite.Sheet == null)
            {
                foreach (var path in sparrowPaths)
                {
                    _sprite.Sheet = TryLoadSparrow(game, path);
                    if (_sprite.Sheet != null)
                    {
                        Console.WriteLine($"Loaded character sprites (Sparrow fallback): {Name} from {path} ({_sprite.Sheet.Animations.Count} animations)");
                        break;
                    }
                }
            }
        }
        else
        {
            // Phase 1: Try Sparrow XML paths first (standard characters like Pico)
            foreach (var path in sparrowPaths)
            {
                _sprite.Sheet = TryLoadSparrow(game, path);
                if (_sprite.Sheet != null)
                {
                    Console.WriteLine($"Loaded character sprites (Sparrow): {Name} from {path} ({_sprite.Sheet.Animations.Count} animations)");
                    break;
                }
            }

            // Phase 2: Fall back to spritemap with composite rendering
            if (_sprite.Sheet == null)
            {
                foreach (var path in spritemapPaths)
                {
                    _sprite.Sheet = SpriteSheet.Load(game, path, preRenderComposites: true);
                    if (_sprite.Sheet != null)
                    {
                        Console.WriteLine($"Loaded character sprites (spritemap): {Name} from {path} ({_sprite.Sheet.Animations.Count} animations, {_sprite.Sheet.CompositeAnimations.Count} composite)");
                        break;
                    }
                }
            }
        }
        
        if (_sprite.Sheet == null)
        {
            Console.WriteLine($"Failed to load sprites for: {Name}");
        }
        
        // Apply scale from character JSON (default 1.0 for normal characters)
        // Original FNF: character scale comes from character JSON, NOT hardcoded.
        // Pixel characters have scale=6 in their JSON, but also set isPixel=true.
        if (JsonData != null)
        {
            Scale = JsonData.Scale;
        }
        
        _sprite.Scale = new Vector2(Scale, Scale);
        _sprite.Effects = FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        
        // Detect dual dance (GF-type characters with danceLeft/danceRight)
        _hasDualDance = ResolveAnimName("danceLeft") != null && ResolveAnimName("danceRight") != null;
        if (_hasDualDance)
        {
            _danceInterval = 1; // GF dances every beat
            Console.WriteLine($"Character '{Name}' has dual dance (danceLeft/danceRight)");
        }
        
        // Play starting animation (original FNF: dance(true) then updateHitbox)
        // For dual-dance characters "idle" doesn't exist � use danceRight or startingAnimation.
        // This ensures a valid frame is loaded before GetCharacterOrigin() is called.
        string startAnim = JsonData?.StartingAnimation;
        if (_hasDualDance)
        {
            PlayAnimation(startAnim ?? "danceRight", true);
        }
        else if (startAnim != null && ResolveAnimName(startAnim) != null)
        {
            PlayAnimation(startAnim, true);
        }
        else
        {
            PlayAnimation("idle");
        }
    }
    
    public void PlayAnimation(string anim, bool force = false)
    {
        if (_sprite?.Sheet == null) return;
        
        _currentAnimation = anim;
        _isSingOrMiss = anim.StartsWith("sing") || anim.StartsWith("miss")
                     || anim == "hey" || anim == "scared" || anim == "cheer"
                     || anim == "firstDeath" || anim == "deathLoop" || anim == "deathConfirm";
        // Original FNF: holdTimer >= Conductor.stepCrochet * singDuration * 0.0011
        // Use actual StepCrochet for BPM-accurate hold times
        float singTime = JsonData?.SingTime ?? 4f;
        float holdTime = StepCrochet * singTime * 1.1f; // 0.0011 factor from original (ms*0.0011 = s*1.1)
        if (holdTime < 0.3f) holdTime = 0.3f;
        
        // Use JSON looped flag when available, otherwise default logic
        bool shouldLoop;
        if (_jsonAnimLooped != null && _jsonAnimLooped.TryGetValue(anim, out bool looped))
            shouldLoop = looped;
        else
            shouldLoop = !_isSingOrMiss;
        
        // Check cache first (O(1) after first resolution)
        if (_animCache.TryGetValue(anim, out var cachedName))
        {
            _sprite.PlayAnimation(cachedName, force, shouldLoop);
            ApplyAnimFrameRate(anim);
            if (_isSingOrMiss)
                _animHoldTime = holdTime;
            return;
        }
        
        // Try JSON animation prefix mapping first (highest priority)
        if (_jsonAnimMappings != null && _jsonAnimMappings.TryGetValue(anim, out var prefix))
        {
            // Check if this animation has specific frame indices (e.g., GF danceLeft/danceRight)
            int[] frameIndices = null;
            _jsonAnimFrameIndices?.TryGetValue(anim, out frameIndices);
            
            // Try to match the JSON prefix against actual spritesheet animation names.
            // Use strict matching (exact / case-insensitive / partial substring) � do NOT
            // fall back to the first animation, because that prevents the hardcoded
            // AnimationMappings from being tried when JSON prefixes don't match.
            var prefixFrames = TryMatchSheetAnimation(prefix, out string resolvedKey);
            if (prefixFrames != null && prefixFrames.Count > 0)
            {
                
                // If frame indices are specified, create a sub-animation with only those frames
                if (frameIndices != null && frameIndices.Length > 0)
                {
                    string subKey = $"{resolvedKey}__{anim}";
                    if (!_sprite.Sheet.Animations.ContainsKey(subKey))
                    {
                        var parentFrames = _sprite.Sheet.Animations.TryGetValue(resolvedKey, out var pf) ? pf : prefixFrames;
                        var subFrames = new List<SpriteFrame>();
                        foreach (int idx in frameIndices)
                        {
                            if (idx >= 0 && idx < parentFrames.Count)
                                subFrames.Add(parentFrames[idx]);
                        }
                        if (subFrames.Count > 0)
                            _sprite.Sheet.Animations[subKey] = subFrames;
                        
                        // Also create composite sub-animation if parent has one,
                        // so AnimatedSprite.Draw finds the pre-rendered frames for
                        // the sub-key (e.g., "GF Dancing Beat__danceLeft").
                        if (_sprite.Sheet.CompositeAnimations.TryGetValue(resolvedKey, out var parentComp))
                        {
                            var subComp = new List<(Texture2D, Rectangle, Vector2)>();
                            foreach (int idx in frameIndices)
                            {
                                if (idx >= 0 && idx < parentComp.Count)
                                    subComp.Add(parentComp[idx]);
                            }
                            if (subComp.Count > 0)
                                _sprite.Sheet.CompositeAnimations[subKey] = subComp;
                        }
                    }
                    if (_sprite.Sheet.Animations.ContainsKey(subKey))
                        resolvedKey = subKey;
                }
                
                _animCache[anim] = resolvedKey;
                _sprite.PlayAnimation(resolvedKey, force, shouldLoop);
                ApplyAnimFrameRate(anim);
                if (_isSingOrMiss)
                    _animHoldTime = holdTime;
                return;
            }
        }
        
        // Resolve: try hardcoded mapped names
        if (AnimationMappings.TryGetValue(anim, out var possibleNames))
        {
            foreach (var name in possibleNames)
            {
                if (_sprite.Sheet.Animations.ContainsKey(name))
                {
                    _animCache[anim] = name;
                    _sprite.PlayAnimation(name, force, shouldLoop);
                    ApplyAnimFrameRate(anim);
                    if (_isSingOrMiss)
                        _animHoldTime = holdTime;
                    return;
                }
                foreach (var kvp in _sprite.Sheet.Animations)
                {
                    if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        _animCache[anim] = kvp.Key;
                        _sprite.PlayAnimation(kvp.Key, force, shouldLoop);
                        ApplyAnimFrameRate(anim);
                        if (_isSingOrMiss)
                            _animHoldTime = holdTime;
                        return;
                    }
                }
            }
        }
        
        // Try direct name
        var directFrames = _sprite.Sheet.GetAnimation(anim);
        if (directFrames != null && directFrames.Count > 0)
        {
            _animCache[anim] = anim;
            _sprite.PlayAnimation(anim, force, shouldLoop);
            ApplyAnimFrameRate(anim);
        }
    }
    
    /// <summary>
    /// Apply per-animation frame rate from character JSON data.
    /// Falls back to 24fps (original FNF default).
    /// </summary>
    private void ApplyAnimFrameRate(string animName)
    {
        if (_sprite == null) return;
        if (_jsonAnimFrameRates != null && _jsonAnimFrameRates.TryGetValue(animName, out int fps) && fps > 0)
            _sprite.FrameRate = fps;
        else
            _sprite.FrameRate = 24f;
    }
    
    /// <summary>
    /// Resolve an animation name to the actual spritesheet key without playing it.
    /// Returns null if the animation can't be found.
    /// </summary>
    private string ResolveAnimName(string anim)
    {
        if (_sprite?.Sheet == null) return null;
        
        // Check cache
        if (_animCache.TryGetValue(anim, out var cached)) return cached;
        
        // JSON mapping (strict � don't fall back to first animation)
        if (_jsonAnimMappings != null && _jsonAnimMappings.TryGetValue(anim, out var prefix))
        {
            var frames = TryMatchSheetAnimation(prefix, out string resolvedKey);
            if (frames != null && frames.Count > 0)
                return resolvedKey;
        }
        
        // Hardcoded mappings (exact then case-insensitive, matching PlayAnimation)
        if (AnimationMappings.TryGetValue(anim, out var names))
        {
            foreach (var name in names)
            {
                if (_sprite.Sheet.GetAnimation(name) != null)
                    return name;
            }
            // Case-insensitive fallback
            foreach (var name in names)
            {
                foreach (var kvp in _sprite.Sheet.Animations)
                {
                    if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return kvp.Key;
                }
            }
        }
        
        // Direct name
        if (_sprite.Sheet.GetAnimation(anim) != null)
            return anim;
        
        return null;
    }
    
    /// <summary>
    /// Try to match a JSON animation prefix against actual spritesheet animation names.
    /// Uses strict matching: exact ? case-insensitive ? key.StartsWith(prefix) ?
    /// key.Contains(prefix) ? prefix.Contains(key). Does NOT fall back to the first
    /// animation, so callers can try hardcoded AnimationMappings when the JSON prefix
    /// doesn't genuinely match any spritesheet animation.
    /// </summary>
    private List<SpriteFrame> TryMatchSheetAnimation(string prefix, out string resolvedKey)
    {
        resolvedKey = prefix;
        if (_sprite?.Sheet == null) return null;
        
        // 1. Exact match
        if (_sprite.Sheet.Animations.TryGetValue(prefix, out var exact))
        {
            resolvedKey = prefix;
            return exact;
        }
        
        // 2. Case-insensitive exact match
        foreach (var kvp in _sprite.Sheet.Animations)
        {
            if (kvp.Key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedKey = kvp.Key;
                return kvp.Value;
            }
        }
        
        // 3. Key starts with prefix (e.g., prefix "GF Dancing Beat" matches "GF Dancing Beat Hair blowing CAR")
        foreach (var kvp in _sprite.Sheet.Animations)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedKey = kvp.Key;
                return kvp.Value;
            }
        }
        
        // 4. Key contains prefix (e.g., prefix "Idle" matches "BF idle dance")
        foreach (var kvp in _sprite.Sheet.Animations)
        {
            if (kvp.Key.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedKey = kvp.Key;
                return kvp.Value;
            }
        }
        
        // 5. Prefix contains key (e.g., prefix "Dad idle dance" matches key "idle")
        foreach (var kvp in _sprite.Sheet.Animations)
        {
            if (prefix.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                resolvedKey = kvp.Key;
                return kvp.Value;
            }
        }
        
        // No genuine match found � return null so caller can try hardcoded mappings
        return null;
    }
    
    /// <summary>
    /// Whether the character is in a death state (prevents idle/dance overrides).
    /// </summary>
    public bool IsDead { get; set; } = false;
    
    /// <summary>
    /// Load the death spritesheet (separate from main sprites).
    /// Original FNF loads bf/dead_sprites for game over.
    /// </summary>
    public void LoadDeathSprites(Game game)
    {
        _animCache.Clear(); // Clear cache since we're loading a new spritesheet
        string[] tryPaths = new[]
        {
            $"game/characters/{Name}/dead_sprites",
            $"game/characters/{Name}/gameover",
            $"game/characters/{Name}/death",
        };
        
        foreach (var path in tryPaths)
        {
            var sheet = SpriteSheet.Load(game, path);
            if (sheet != null)
            {
                // Dispose old composite sprites before replacing
                _sprite?.Sheet?.Dispose();
                _sprite = new AnimatedSprite();
                _sprite.Sheet = sheet;
                _sprite.Scale = new Vector2(Scale, Scale);
                _sprite.Effects = FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                Console.WriteLine($"Loaded death sprites for {Name}: {path}");
                return;
            }
        }
        Console.WriteLine($"No death sprites found for {Name}, using current sprites");
    }
    
    /// <summary>
    /// Try to load a Sparrow XML spritesheet (PNG + XML) at the given path.
    /// Returns null if no XML exists (avoids loading spritemap folders which contain
    /// individual body parts instead of full character frames).
    /// </summary>
    private static SpriteSheet TryLoadSparrow(Game game, string basePath)
    {
        string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        #if XBOX_UWP
                var assets = (game as FNF_MonoGame.XboxGame)?.Assets;
        #else
                var assets = (game as FNF_MonoGame.FNFGame)?.Assets;
        #endif
        
        // Check that an XML file exists � this distinguishes Sparrow from spritemap
        string xmlPath = assets?.ResolvePath(basePath + ".xml")
                      ?? (File.Exists(Path.Combine(contentPath, basePath + ".xml")) ? Path.Combine(contentPath, basePath + ".xml") : null);
        if (xmlPath == null) return null;
        
        // XML exists � load via standard SpriteSheet.Load (it will find the PNG + XML)
        return SpriteSheet.Load(game, basePath, preRenderComposites: false);
    }
    
    public void Update(float deltaTime)
    {
        _sprite?.Update(deltaTime);
        
        // Don't auto-return to idle during death animations
        if (IsDead) return;
        
        // Return to idle after sing/miss animation hold time
        // Original FNF: holdTimer counts down, returns to idle when holdTimer <= 0 AND anim finished
        // With loop=false, _sprite.Finished becomes true when anim plays through once
        if (_isSingOrMiss)
        {
            _animHoldTime -= deltaTime;
            if (_animHoldTime <= 0)
            {
                // Wait for animation to finish playing, or force after generous timeout
                if (_sprite != null && (_sprite.Finished || _animHoldTime < -0.5f))
                {
                    PlayAnimation("idle");
                }
            }
        }
    }
    
    public void Dance(int beat)
    {
        // Don't dance during death
        if (IsDead) return;
        
        // Characters dance on even beats (or based on their dance interval)
        if (beat % _danceInterval == 0 && beat != _lastDanceBeat)
        {
            _lastDanceBeat = beat;
            if (!_isSingOrMiss || _animHoldTime <= 0)
            {
                if (_hasDualDance)
                {
                    // GF-type: alternate between danceLeft and danceRight each beat
                    _danceLeft = !_danceLeft;
                    PlayAnimation(_danceLeft ? "danceLeft" : "danceRight", true);
                }
                else
                {
                    PlayAnimation("idle", true);
                }
            }
        }
    }
    
    public void Sing(int direction)
    {
        string anim = direction switch
        {
            0 => "singLEFT",
            1 => "singDOWN",
            2 => "singUP",
            3 => "singRIGHT",
            _ => "idle"
        };
        PlayAnimation(anim);
    }
    
    /// <summary>
    /// Play a miss animation for the given lane direction.
    /// Convenience method so callers don't need to build the animation name.
    /// </summary>
    public void Miss(int direction)
    {
        string anim = direction switch
        {
            0 => "missLEFT",
            1 => "missDOWN",
            2 => "missUP",
            3 => "missRIGHT",
            _ => "idle"
        };
        PlayAnimation(anim);
    }
    
    public void Draw(SpriteBatch spriteBatch, AssetManager assets, float cameraX = 0f, float cameraY = 0f, float zoom = 1f)
    {
        if (_sprite?.Sheet?.Texture != null)
        {
            // Apply per-animation offsets from character JSON
            float animOffX = 0, animOffY = 0;
            if (_jsonAnimOffsets != null && _currentAnimation != null 
                && _jsonAnimOffsets.TryGetValue(_currentAnimation, out var animOff))
            {
                animOffX = animOff.X;
                animOffY = animOff.Y;
            }
            // Apply global character offsets from JSON
            float charOffX = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[0] : 0f;
            float charOffY = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[1] : 0f;

            Vector2 drawPos = new Vector2(
                cameraX - animOffX + charOffX, 
                cameraY - animOffY + charOffY);

            // Character.Position is top-left world position across gameplay systems.
            // Composite draw path subtracts compOrigin internally, so offset by that
            // origin here to keep composite characters aligned with normal sprites.
            if (!_sprite.IsRuntimeComposite())
            {
                var compOrigin = _sprite.GetCompositeOrigin();
                if (compOrigin.HasValue)
                    drawPos += compOrigin.Value;
            }

            _sprite.Position = drawPos;
            _sprite.Scale = new Vector2(Scale, Scale);
            _sprite.Effects = FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            _sprite.Draw(spriteBatch);
        }
        else
        {
            // Fallback: Draw placeholder if sprites not loaded
            DrawPlaceholder(spriteBatch, assets, cameraX, cameraY);
        }
    }
    
    private void DrawPlaceholder(SpriteBatch spriteBatch, AssetManager assets, float cameraX = 0f, float cameraY = 0f)
    {
        Color bodyColor = Name switch
        {
            "bf" or "bf_pixel" => Color.Cyan,
            "gf" => Color.HotPink,
            "dad" => Color.Purple,
            "mom" => Color.Magenta,
            "pico" => Color.LimeGreen,
            _ => Color.Gray
        };
        
        int x = (int)(Position.X + cameraX);
        int y = (int)(Position.Y + cameraY);
        
        // Simple placeholder shape
        spriteBatch.Draw(assets.Pixel, new Rectangle(x, y, 80, 120), bodyColor);
        spriteBatch.Draw(assets.Pixel, new Rectangle(x + 10, y - 60, 60, 60), bodyColor);
    }
    
    /// <summary>
    /// Dispose GPU resources (composite textures from spritesheet).
    /// </summary>
    public void Dispose()
    {
        _sprite?.Sheet?.Dispose();
        _sprite = null;
    }
}
