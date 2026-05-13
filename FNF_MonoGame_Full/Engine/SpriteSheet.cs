using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Represents a frame in a spritesheet
/// </summary>
public class SpriteFrame
{
    public string Name { get; set; }
    public Rectangle SourceRect { get; set; }
    public Vector2 Offset { get; set; }
    public bool Rotated { get; set; }
    /// <summary>
    /// Original untrimmed frame width (from Sparrow XML frameWidth).
    /// 0 means no trimming info � use SourceRect.Width instead.
    /// </summary>
    public int FrameWidth { get; set; }
    /// <summary>
    /// Original untrimmed frame height (from Sparrow XML frameHeight).
    /// 0 means no trimming info � use SourceRect.Height instead.
    /// </summary>
    public int FrameHeight { get; set; }
}

/// <summary>
/// Loads and manages Sparrow/Starling XML spritesheets used by FNF
/// Also supports Adobe Animate JSON spritemap format
/// </summary>
public class SpriteSheet
{
    public Texture2D Texture { get; private set; }
    public Dictionary<string, SpriteFrame> Frames { get; } = new();
    public Dictionary<string, List<SpriteFrame>> Animations { get; } = new();
    
    /// <summary>
    /// Pre-rendered composite animations for spritemap symbols that are composed
    /// of multiple atlas sprites positioned via transforms.
    /// Key = symbol name, Value = list of (texture, sourceRect, origin) per tick.
    /// Origin is the pixel in the texture corresponding to the animation's (0,0) registration point.
    /// </summary>
    public Dictionary<string, List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>> CompositeAnimations { get; } = new();

    /// <summary>
    /// Raw composite data for runtime compositing (matches original FNF's FlxAnimate approach).
    /// Instead of pre-rendering body parts into atlas textures, stores the raw M3D transform
    /// data per tick so that each body part can be drawn directly at runtime.
    /// Key = animation name (e.g. "Idle", "IdleLeft"), Value = list of ticks, each tick
    /// contains a list of (spriteFrame, affine transform) tuples in draw order.
    /// </summary>
    public Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>> RawCompositeData { get; } = new();

    /// <summary>
    /// Deferred composite data for background-thread loading.
    /// CPU work (CollectPartsWithTransforms) runs on background thread;
    /// GPU work (PreRenderComposite) is deferred to FinalizeComposites on main thread.
    /// </summary>
    public List<(string Name, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> TickParts)> PendingComposites { get; } = new();

    /// <summary>
    /// JIT (Just-In-Time) composite data for on-demand rendering.
    /// Stores tick parts + precomputed origin per animation. GPU rendering
    /// is deferred until the animation is first drawn, then the result is
    /// cached in CompositeAnimations for subsequent frames.
    /// </summary>
    public Dictionary<string, JITCompositeInfo> JITComposites { get; } = new();

    /// <summary>
    /// Stage offset from AN.STI (Stage Instance Transform) in the Animation.json.
    /// Contains the raw (tx, ty) from the STI's M3D matrix � matching FlxAnimate's
    /// applyStageMatrix behavior where offset -= stageMatrix.t.
    /// Applied as: drawPosition = spritePosition + StageOffset.
    /// </summary>
    public Vector2 StageOffset { get; private set; }

    /// <summary>
    /// Custom blend state for rendering non-premultiplied sprites onto a RenderTarget2D.
    /// NonPremultiplied squares alpha (AlphaSourceBlend=SourceAlpha ? outA = srcA*srcA),
    /// causing dark halos when the RT is drawn to screen. This blend state preserves
    /// alpha correctly (AlphaSourceBlend=One ? outA = srcA) while still premultiplying RGB.
    /// The resulting RT has premultiplied RGB and correct alpha � draw with AlphaBlend.
    /// </summary>
    private static readonly BlendState _rtCompositeBlend = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    private SpriteSheet() { }

    /// <summary>
    /// Premultiply alpha on a texture loaded via Texture2D.FromStream
    /// (which loads non-premultiplied). SpriteBatch.AlphaBlend expects premultiplied.
    /// </summary>
    /// <summary>
    /// Premultiply alpha on a texture. Required for textures drawn with custom
    /// MULTIPLY blend state (DestinationColor) � non-premultiplied textures have
    /// non-zero RGB in transparent pixels which causes bright edge halos.
    /// </summary>
    internal static void PremultiplyAlpha(Texture2D texture)
    {
        if (texture == null) return;
        Color[] data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c.A < 255)
                data[i] = Color.FromNonPremultiplied(c.R, c.G, c.B, c.A);
        }
        texture.SetData(data);
    }

    /// <summary>
    /// Load a spritesheet from PNG + XML or JSON files.
    /// When preRenderComposites is true, multi-part spritemap symbols are pre-rendered
    /// into composite textures (used for the DJ character in Freeplay). This is expensive
    /// so it should only be enabled when the composite rendering is actually needed.
    /// </summary>
    public static SpriteSheet Load(Game game, string basePath, bool preRenderComposites = false, string[] preRenderFilter = null, bool deferComposites = false, bool applyStageInstanceTransform = false, bool applyTRP = false)
    {
        var sheet = new SpriteSheet();
        string contentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        
        // Use AssetManager for multi-root path resolution (Content + funkin.assets)
#if XBOX_UWP
        var assets = (game as FNF_MonoGame.XboxGame)?.Assets;
#else
        var assets = (game as FNF_MonoGame.FNFGame)?.Assets;
#endif
        
        // Try loading spritemap folder format first (for new FNF assets)
        string spritemapFolder = assets?.ResolveDirectory(basePath)
                              ?? (Directory.Exists(Path.Combine(contentPath, basePath)) ? Path.Combine(contentPath, basePath) : null);
        if (spritemapFolder != null)
        {
            string spritemapPng = Path.Combine(spritemapFolder, "spritemap1.png");
            string spritemapJson = Path.Combine(spritemapFolder, "spritemap1.json");
            string animationJson = Path.Combine(spritemapFolder, "Animation.json");
            
            if (File.Exists(spritemapPng) && File.Exists(spritemapJson))
            {
                return LoadFromSpritemap(game, spritemapPng, spritemapJson, animationJson, preRenderComposites, preRenderFilter, deferComposites, applyStageInstanceTransform, applyTRP);
            }
        }
        
        // Load texture (PNG + XML or JSON)
        string pngPath = assets?.ResolvePath(basePath + ".png")
                      ?? (File.Exists(Path.Combine(contentPath, basePath + ".png")) ? Path.Combine(contentPath, basePath + ".png") : null);
        if (pngPath == null)
        {
            return null;
        }
        
        using (var stream = File.OpenRead(pngPath))
        {
            sheet.Texture = Texture2D.FromStream(game.GraphicsDevice, stream);
        }
        // Skip PremultiplyAlpha � GetData/SetData causes multi-second freezes on large textures.
        // Use BlendState.NonPremultiplied when drawing instead.

        try
        {
            // Try JSON spritemap first
            string jsonPath = assets?.ResolvePath(basePath + ".json")
                           ?? (File.Exists(Path.Combine(contentPath, basePath + ".json")) ? Path.Combine(contentPath, basePath + ".json") : null);
            if (jsonPath != null)
            {
                LoadFromJson(sheet, jsonPath);
                return sheet;
            }
            
            // Load XML
            string xmlPath = assets?.ResolvePath(basePath + ".xml")
                          ?? (File.Exists(Path.Combine(contentPath, basePath + ".xml")) ? Path.Combine(contentPath, basePath + ".xml") : null);
            if (xmlPath != null)
            {
                // Parse Sparrow XML format
                LoadFromXml(sheet, xmlPath);
                return sheet;
            }
            
            // Try Packer .txt format (used by spirit and some pixel characters)
            string txtPath = assets?.ResolvePath(basePath + ".txt")
                          ?? (File.Exists(Path.Combine(contentPath, basePath + ".txt")) ? Path.Combine(contentPath, basePath + ".txt") : null);
            if (txtPath != null)
            {
                LoadFromPacker(sheet, txtPath);
                return sheet;
            }
            
            // Return sheet with just the full texture as one frame
            sheet.Frames["default"] = new SpriteFrame
            {
                Name = "default",
                SourceRect = new Rectangle(0, 0, sheet.Texture.Width, sheet.Texture.Height),
                Offset = Vector2.Zero
            };
            return sheet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading spritesheet '{basePath}': {ex.Message}");
            sheet.Dispose();
            return null;
        }
    }
    
    
    /// <summary>
    /// Load from Adobe Animate spritemap folder format.
    /// When preRenderComposites is true, composites multi-part symbols into pre-rendered textures.
    /// Otherwise, picks the largest sprite per tick as a simplified fallback.
    /// </summary>
    private static SpriteSheet LoadFromSpritemap(Game game, string pngPath, string spritemapJsonPath, string animationJsonPath, bool preRenderComposites, string[] preRenderFilter = null, bool deferComposites = false, bool applyStageInstanceTransform = false, bool applyTRP = false)
    {
        var sheet = new SpriteSheet();
        
        using (var stream = File.OpenRead(pngPath))
        {
            sheet.Texture = Texture2D.FromStream(game.GraphicsDevice, stream);
        }
        // Skip PremultiplyAlpha for spritemap textures � GetData/SetData on 4096�2048
        // textures transfers ~33MB GPU?CPU each way, causing multi-second freezes.
        // Use BlendState.NonPremultiplied when drawing these textures instead.

        try
        {
            // Parse spritemap JSON (atlas frame definitions)
            string spritemapJson = File.ReadAllText(spritemapJsonPath);
            var spritemapData = JObject.Parse(spritemapJson);
            var sprites = spritemapData["ATLAS"]?["SPRITES"];
            
            var framesByName = new Dictionary<string, SpriteFrame>();
            
            if (sprites != null)
            {
                foreach (var spriteToken in sprites)
                {
                    var sprite = spriteToken["SPRITE"];
                    if (sprite == null) continue;
                    
                    string name = sprite["name"]?.ToString() ?? "";
                    int x = sprite["x"]?.Value<int>() ?? 0;
                    int y = sprite["y"]?.Value<int>() ?? 0;
                    int w = sprite["w"]?.Value<int>() ?? 0;
                    int h = sprite["h"]?.Value<int>() ?? 0;
                    bool rotated = sprite["rotated"]?.Value<bool>() ?? false;
                    
                    var frame = new SpriteFrame
                    {
                        Name = name,
                        SourceRect = new Rectangle(x, y, w, h),
                        Offset = Vector2.Zero,
                        Rotated = rotated
                    };
                    
                    sheet.Frames[name] = frame;
                    framesByName[name] = frame;
                }
            }
            
            // Parse Animation JSON if available
            if (File.Exists(animationJsonPath))
            {
                string animJson = File.ReadAllText(animationJsonPath);
                var animData = JObject.Parse(animJson);
                
                // Read AN.STI (Stage Instance Transform) � the authoring-time placement
                // of the root symbol on the Adobe Animate stage. Original FlxAnimate reads
                // stageMatrix.tx/ty and applies: offset -= stageMatrix.t, so drawPos = pos + (tx,ty).
                var stiSi = animData["AN"]?["STI"]?["SI"];
                if (stiSi != null)
                {
                    var stiM3D = stiSi["M3D"];
                    var stiMX = stiSi["MX"];
                    if (stiM3D != null && stiM3D.Count() >= 14)
                        sheet.StageOffset = new Vector2(stiM3D[12].Value<float>(), stiM3D[13].Value<float>());
                    else if (stiMX != null && stiMX.Count() >= 6)
                        sheet.StageOffset = new Vector2(stiMX[4].Value<float>(), stiMX[5].Value<float>());
                }

                var symbolsByName = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

                var symbolArray = animData["SD"]?["S"]
                               ?? animData["SYMBOL_DICTIONARY"]?["Symbols"];
                
                if (symbolArray != null)
                {
                    foreach (var sym in symbolArray)
                    {
                        string sn = (sym["SN"] ?? sym["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(sn))
                            symbolsByName[sn] = sym;
                    }
                }
                
                // Identify top-level animation symbols from the main timeline (AN)
                var topLevelSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var mainTimeline = animData["AN"]?["TL"]?["L"] ?? animData["ANIMATION"]?["TIMELINE"]?["LAYERS"];
                if (mainTimeline != null)
                {
                    foreach (var layer in mainTimeline)
                    {
                        var layerFrames = layer["FR"] ?? layer["Frames"];
                        if (layerFrames == null) continue;
                        foreach (var frameData in layerFrames)
                        {
                            var elements = frameData["E"] ?? frameData["elements"];
                            if (elements == null) continue;
                            foreach (var element in elements)
                            {
                                var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                                if (si != null)
                                {
                                    string sn = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(sn))
                                        topLevelSymbols.Add(sn);
                                }
                            }
                        }
                    }
                }
                
                // If no main timeline found, treat all symbols as top-level
                if (topLevelSymbols.Count == 0)
                {
                    foreach (var k in symbolsByName.Keys)
                        topLevelSymbols.Add(k);
                }
                
                // Build composite animations only for top-level symbols (performance)
                // When preRenderFilter is set, only pre-render matching symbols
                var symbolFilter = preRenderFilter != null
                    ? new HashSet<string>(preRenderFilter, StringComparer.OrdinalIgnoreCase)
                    : null;

                foreach (var kvp in symbolsByName)
                {
                    if (!topLevelSymbols.Contains(kvp.Key)) continue;

                    // When filter is set, skip non-matching symbols entirely.
                    // This avoids the expensive O(n� � depth) recursive CollectLargestPerTick
                    // for symbols we don't need (body parts, effects, nested components).
                    if (symbolFilter != null && !symbolFilter.Contains(kvp.Key)) continue;

                    int totalDuration = GetSymbolDuration(kvp.Value);
                    if (totalDuration <= 0 || totalDuration > 400) continue;

                    bool shouldComposite = preRenderComposites && (symbolFilter == null || symbolFilter.Contains(kvp.Key));
                    if (shouldComposite)
                    {
                        // Full composite rendering with transforms
                        var tickParts = new List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>();
                        for (int t = 0; t < totalDuration; t++)
                            tickParts.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());

                        CollectPartsWithTransforms(kvp.Value, symbolsByName, framesByName, tickParts, 1, 0, 0, 1, 0, 0,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase), applyTRP: false,
                            symbolCache: new Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>>(StringComparer.OrdinalIgnoreCase));

                        bool hasParts = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 0) { hasParts = true; break; }
                        if (!hasParts) continue;

                        bool multiPart = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 1) { multiPart = true; break; }

                        // Single-part frames may still have non-identity STI/M3D transforms
                        // (e.g. crowd, bar). Detect and route through composite path to preserve them.
                        bool hasTransforms = false;
                        if (!multiPart)
                        {
                            foreach (var tick in tickParts)
                            {
                                if (tick.Count == 1)
                                {
                                    var (_, a, b, c, d, tx, ty) = tick[0];
                                    if (MathF.Abs(a - 1f) > 0.001f || MathF.Abs(d - 1f) > 0.001f ||
                                        MathF.Abs(b) > 0.001f || MathF.Abs(c) > 0.001f ||
                                        MathF.Abs(tx) > 0.001f || MathF.Abs(ty) > 0.001f)
                                    { hasTransforms = true; break; }
                                }
                            }
                        }

                        if (multiPart || hasTransforms)
                        {
                            if (deferComposites)
                            {
                                // Runtime compositing: store raw tick parts so Draw()
                                // renders each body part directly with M3D transforms.
                                // No GPU work needed � safe for background threads.
                                sheet.RawCompositeData[kvp.Key] = tickParts;
                                // Add placeholder frames so PlayAnimation/FindAnim works
                                var placeholders = new List<SpriteFrame>();
                                for (int pi = 0; pi < tickParts.Count; pi++)
                                    placeholders.Add(new SpriteFrame { Name = $"{kvp.Key}_{pi}", SourceRect = Rectangle.Empty });
                                sheet.Animations[kvp.Key] = placeholders;
                            }
                            else
                            {
                                var compositeFrames = PreRenderComposite(game.GraphicsDevice, sheet.Texture, tickParts);
                                if (compositeFrames.Count > 0)
                                {
                                    sheet.CompositeAnimations[kvp.Key] = compositeFrames;
                                    var simpleFrames = new List<SpriteFrame>();
                                    for (int i = 0; i < compositeFrames.Count; i++)
                                    {
                                        simpleFrames.Add(new SpriteFrame
                                        {
                                            Name = $"{kvp.Key}_{i}",
                                            SourceRect = compositeFrames[i].Rect,
                                            Offset = Vector2.Zero,
                                            FrameWidth = compositeFrames[i].Rect.Width,
                                            FrameHeight = compositeFrames[i].Rect.Height
                                        });
                                    }
                                    sheet.Animations[kvp.Key] = simpleFrames;
                                }
                            }
                        }
                        else
                        {
                            var simpleFrames = new List<SpriteFrame>();
                            foreach (var tick in tickParts)
                            {
                                if (tick.Count > 0)
                                    simpleFrames.Add(tick[0].Frame);
                            }
                            if (simpleFrames.Count > 0)
                                sheet.Animations[kvp.Key] = simpleFrames;
                        }
                    }
                    else
                    {
                        // Simple mode: pick the largest atlas sprite per tick (no pre-rendering)
                        var collected = new List<SpriteFrame>();
                        CollectLargestPerTick(kvp.Value, symbolsByName, framesByName, collected,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        if (collected.Count > 0)
                            sheet.Animations[kvp.Key] = collected;
                    }
                }

                // Build main timeline composite and split by frame labels (Enter, Idle, Confirm, etc.)
                // This is needed for animateatlas assets like character select where the main timeline
                // defines sub-animations via frame labels, compositing multiple SD symbols together.
                // Always process when mainTimeline exists � even without preRenderComposites, we need
                // to extract single-part frames (e.g. crowd has 0 symbols, all ASI on main timeline).
                if (mainTimeline != null)
                {
                    var labels = new SortedDictionary<int, string>();
                    int mainDuration = 0;
                    foreach (var layer in mainTimeline)
                    {
                        var layerFrames = layer["FR"] ?? layer["Frames"];
                        if (layerFrames == null) continue;
                        foreach (var fd in layerFrames)
                        {
                            int idx = ReadFrameIndex(fd);
                            int dur = ReadFrameDuration(fd);
                            mainDuration = Math.Max(mainDuration, idx + dur);
                            string label = fd["N"]?.ToString();
                            if (!string.IsNullOrEmpty(label))
                                labels.TryAdd(idx, label);
                        }
                    }

                    // If no labels, treat the whole timeline as one "default" animation
                    if (labels.Count == 0 && mainDuration > 0)
                        labels[0] = "default";

                    if (labels.Count >= 1 && mainDuration > 0 && mainDuration <= 400)
                    {
                        var mainAN = animData["AN"];

                        // Build label ranges and compute the merged tick range we actually need
                        var labelList = labels.ToList();
                        var filterSet = preRenderFilter != null
                            ? new HashSet<string>(preRenderFilter, StringComparer.OrdinalIgnoreCase)
                            : null;

                        // Compute merged tick range from filtered labels to avoid processing
                        // unneeded frame entries (e.g. Cancel's 52 ticks when we only need Idle)
                        int tickMin = 0, tickMax = mainDuration;
                        if (filterSet != null)
                        {
                            tickMin = mainDuration;
                            tickMax = 0;
                            for (int i = 0; i < labelList.Count; i++)
                            {
                                string ln = labelList[i].Value;
                                if (!filterSet.Contains(ln)) continue;
                                int sf = labelList[i].Key;
                                int ef = (i + 1 < labelList.Count) ? labelList[i + 1].Key : mainDuration;
                                tickMin = Math.Min(tickMin, sf);
                                tickMax = Math.Max(tickMax, ef);
                            }
                            if (tickMin >= tickMax) { tickMin = 0; tickMax = mainDuration; }
                        }

                        var tickParts = new List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>();
                        for (int t = 0; t < mainDuration; t++)
                            tickParts.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());

                        // STI (Stage Instance Transform) is the authoring-time placement of the
                        // symbol on the Adobe Animate stage. Original FlxAnimate does NOT apply it
                        // during rendering unless applyStageMatrix() is explicitly called.
                        // Instead of processing mainAN (which bakes in SI M3D stage offsets),
                        // iterate the main timeline manually: resolve each SI to its nested symbol
                        // and collect body parts with identity parent (no stage transform).
                        // ASI elements (direct atlas sprites) keep their local transforms.
                        // Pre-rendered composites auto-compensate via bounding box origin;
                        // per-part rendering (characters) needs the clean local-space data.
                        var stlSymbolCache = new Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>>(StringComparer.OrdinalIgnoreCase);
                        var mainLayers = mainTimeline.ToList();
                        for (int mli = mainLayers.Count - 1; mli >= 0; mli--)
                        {
                            var mlFrames = mainLayers[mli]["FR"] ?? mainLayers[mli]["Frames"];
                            if (mlFrames == null) continue;

                            foreach (var mfd in mlFrames)
                            {
                                int mfIdx = ReadFrameIndex(mfd);
                                int mfDur = ReadFrameDuration(mfd);
                                if (mfIdx + mfDur <= tickMin || mfIdx >= tickMax) continue;

                                var mfElements = mfd["E"] ?? mfd["elements"];
                                if (mfElements == null) continue;

                                int mfStart = Math.Max(mfIdx, tickMin);
                                int mfEnd = Math.Min(mfIdx + mfDur, mainDuration);

                                foreach (var mfEl in mfElements)
                                {
                                    // Direct atlas sprite on main timeline � keep local transform
                                    var mfAsi = mfEl["ASI"] ?? mfEl["ATLAS_SPRITE_instance"];
                                    if (mfAsi != null)
                                    {
                                        string fn = (mfAsi["N"] ?? mfAsi["name"])?.ToString() ?? "";
                                        if (framesByName.TryGetValue(fn, out var fr))
                                        {
                                            var mx = ReadTransform(mfAsi, false);
                                            for (int mt = mfStart; mt < mfEnd; mt++)
                                                tickParts[mt].Add((fr, mx.a, mx.b, mx.c, mx.d, mx.tx, mx.ty));
                                        }
                                    }

                                    // Nested symbol instance � resolve to SD symbol, collect body parts in
                                    // local space, then compose with SI's M3D + TRP for correct positioning.
                                    var mfSi = mfEl["SI"] ?? mfEl["SYMBOL_instance"] ?? mfEl["SYMBOL_Instance"];
                                    if (mfSi != null)
                                    {
                                        string nn = (mfSi["SN"] ?? mfSi["SYMBOL_name"])?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(nn) && symbolsByName.TryGetValue(nn, out var nSym))
                                        {
                                            int nDur = GetSymbolDuration(nSym);
                                            if (nDur <= 0) continue;

                                            if (!stlSymbolCache.TryGetValue(nn, out var localTicks))
                                            {
                                                localTicks = new List<List<(SpriteFrame, float, float, float, float, float, float)>>();
                                                for (int nt = 0; nt < nDur; nt++)
                                                    localTicks.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());

                                                CollectPartsWithTransforms(nSym, symbolsByName, framesByName, localTicks,
                                                    1, 0, 0, 1, 0, 0,
                                                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                                    applyTRP: applyTRP, symbolCache: stlSymbolCache);

                                                stlSymbolCache[nn] = localTicks;
                                            }

                                            int ff = ReadFirstFrame(mfSi);

                                            // Compose SI's M3D + TRP with cached local parts (matches FlxAnimate).
                                            // On the main timeline, each SI's raw (tx,ty) positions the
                                            // sub-symbol's TRP at that coordinate in parent space. The
                                            // AN.STI raw offset then maps parent space ? screen space.
                                            // Applying TRP here would double-offset: the bounding-box
                                            // composite origin can't compensate because the STI offset
                                            // is additive, not TRP-relative. Result: floor half off-screen.
                                            var si = ReadTransform(mfSi, applyTRP);
                                            for (int mt = mfStart; mt < mfEnd; mt++)
                                            {
                                                int nTick = (ff + (mt - mfIdx)) % nDur;
                                                foreach (var (frame, ca, cb, cc, cd, ctx, cty) in localTicks[nTick])
                                                {
                                                    // Compose: world = SI_transform � local_part
                                                    float wa = si.a * ca + si.c * cb;
                                                    float wb = si.b * ca + si.d * cb;
                                                    float wc = si.a * cc + si.c * cd;
                                                    float wd = si.b * cc + si.d * cd;
                                                    float wtx = si.a * ctx + si.c * cty + si.tx;
                                                    float wty = si.b * ctx + si.d * cty + si.ty;
                                                    tickParts[mt].Add((frame, wa, wb, wc, wd, wtx, wty));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        bool hasParts = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 0) { hasParts = true; break; }

                        if (hasParts)
                        {
                            for (int i = 0; i < labelList.Count; i++)
                            {
                                int startFrame = labelList[i].Key;
                                int endFrame = (i + 1 < labelList.Count) ? labelList[i + 1].Key : mainDuration;
                                string animName = labelList[i].Value;
                                int count = endFrame - startFrame;
                                if (count <= 0) continue;

                                // When filter is set, only process matching labels
                                bool shouldProcess = filterSet == null || filterSet.Contains(animName);

                                if (shouldProcess)
                                {
                                    // Pre-render each label's ticks into composite textures.
                                    // Uses the same proven PreRenderComposite path as per-symbol
                                    // composites (DJ, crowd, speakers) � body parts are rendered
                                    // relative to a bounding box with a proper origin, avoiding
                                    // the fragility of absolute M3D screen-position drawing.
                                    var labelTicks = tickParts.GetRange(startFrame, Math.Min(count, tickParts.Count - startFrame));

                                    bool multiPart = false;
                                    foreach (var tick in labelTicks)
                                        if (tick.Count > 1) { multiPart = true; break; }

                                    // Single-part frames may still have non-identity STI/M3D transforms.
                                    bool hasTransforms = false;
                                    if (!multiPart)
                                    {
                                        foreach (var tick in labelTicks)
                                        {
                                            if (tick.Count == 1)
                                            {
                                                var (_, a, b, c, d, tx, ty) = tick[0];
                                                if (MathF.Abs(a - 1f) > 0.001f || MathF.Abs(d - 1f) > 0.001f ||
                                                    MathF.Abs(b) > 0.001f || MathF.Abs(c) > 0.001f ||
                                                    MathF.Abs(tx) > 0.001f || MathF.Abs(ty) > 0.001f)
                                                { hasTransforms = true; break; }
                                            }
                                        }
                                    }

                                    if ((multiPart || hasTransforms) && preRenderComposites)
                                    {
                                        if (deferComposites)
                                        {
                                            // Runtime compositing: store raw tick parts so Draw()
                                            // renders each body part directly with M3D transforms.
                                            // No GPU work needed � safe for background threads.
                                            sheet.RawCompositeData[animName] = labelTicks;
                                            // Add placeholder frames so PlayAnimation/FindAnim works
                                            var ph = new List<SpriteFrame>();
                                            for (int pi = 0; pi < labelTicks.Count; pi++)
                                                ph.Add(new SpriteFrame { Name = $"{animName}_{pi}", SourceRect = Rectangle.Empty });
                                            if (!sheet.Animations.ContainsKey(animName))
                                                sheet.Animations[animName] = ph;
                                        }
                                        else
                                        {
                                            var compositeFrames = PreRenderComposite(game.GraphicsDevice, sheet.Texture, labelTicks);
                                            if (compositeFrames.Count > 0)
                                            {
                                                sheet.CompositeAnimations[animName] = compositeFrames;
                                                var subFrames = new List<SpriteFrame>();
                                                for (int f = 0; f < compositeFrames.Count; f++)
                                                {
                                                    subFrames.Add(new SpriteFrame
                                                    {
                                                        Name = $"{animName}_{f}",
                                                        SourceRect = compositeFrames[f].Rect,
                                                        Offset = Vector2.Zero,
                                                        FrameWidth = compositeFrames[f].Rect.Width,
                                                        FrameHeight = compositeFrames[f].Rect.Height
                                                    });
                                                }
                                                if (!sheet.Animations.ContainsKey(animName))
                                                    sheet.Animations[animName] = subFrames;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Single-part ticks: use direct sprite frames
                                        var subFrames = new List<SpriteFrame>();
                                        foreach (var tick in labelTicks)
                                        {
                                            if (tick.Count > 0)
                                                subFrames.Add(tick[0].Frame);
                                        }
                                        if (subFrames.Count > 0 && !sheet.Animations.ContainsKey(animName))
                                            sheet.Animations[animName] = subFrames;
                                    }
                                }
                                // Skip non-matching labels entirely when filter is set �
                                // no need to build simple frames for unused animations
                                else if (filterSet == null)
                                {
                                    // No filter: register simple frames for all labels
                                    var subTicks = tickParts.GetRange(startFrame, Math.Min(count, tickParts.Count - startFrame));
                                    var simpleFrames = new List<SpriteFrame>();
                                    foreach (var tick in subTicks)
                                    {
                                        if (tick.Count > 0)
                                            simpleFrames.Add(tick[0].Frame);
                                    }
                                    if (simpleFrames.Count > 0 && !sheet.Animations.ContainsKey(animName))
                                        sheet.Animations[animName] = simpleFrames;
                                }
                            }
                            Console.WriteLine($"  Main timeline split into {labelList.Count} label animations: {string.Join(", ", labelList.Select(l => l.Value))}");
                        }
                    }
                }

                // Safety fallback: if Animation.json was parsed but no animations were created
                // (e.g. no symbols AND main timeline didn't produce anything),
                // create a default animation from all loaded frames.
                if (sheet.Animations.Count == 0 && sheet.Frames.Count > 0)
                    sheet.Animations["default"] = sheet.Frames.Values.ToList();
            }
            else
            {
                sheet.Animations["default"] = sheet.Frames.Values.ToList();
            }

            Console.WriteLine($"Loaded spritemap: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations, {sheet.CompositeAnimations.Count} composite");
            foreach (var anim in sheet.Animations.Keys.Take(15))
            {
                Console.WriteLine($"  Animation: '{anim}' ({sheet.Animations[anim].Count} frames){(sheet.CompositeAnimations.ContainsKey(anim) ? " [composite]" : "")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading spritemap: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        return sheet;
    }
    
    /// <summary>
    /// Get the total timeline duration of a symbol.
    /// </summary>
    private static int GetSymbolDuration(JToken symbol)
    {
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) return 0;
        
        int totalDuration = 0;
        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int dur = ReadFrameDuration(frameData);
                totalDuration = Math.Max(totalDuration, index + dur);
            }
        }
        return totalDuration;
    }
    
    /// <summary>
    /// Read a 2D affine transform from an element token.
    /// Supports both MX [a,b,c,d,tx,ty] and M3D [16-element 4x4 matrix] formats.
    /// Applies TRP (Transformation Reference Point) adjustment to the translation,
    /// matching FlxAnimate's parseMatrix behavior where TRP defines the pivot point.
    /// </summary>
    private static (float a, float b, float c, float d, float tx, float ty) ReadTransform(JToken elementToken, bool applyTRP = true)
    {
        float a = 1, b = 0, c = 0, d = 1, tx = 0, ty = 0;

        // Try MX first (2x3 matrix: [a, b, c, d, tx, ty])
        var mx = elementToken?["MX"] as JArray;
        if (mx != null && mx.Count >= 6)
        {
            a = mx[0].Value<float>();
            b = mx[1].Value<float>();
            c = mx[2].Value<float>();
            d = mx[3].Value<float>();
            tx = mx[4].Value<float>();
            ty = mx[5].Value<float>();
        }
        else
        {
            // Try M3D (4x4 matrix stored as 16-element flat array)
            // Layout: [a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, tx, ty, 0, 1]
            var m3d = elementToken?["M3D"] as JArray;
            if (m3d != null && m3d.Count >= 16)
            {
                a = m3d[0].Value<float>();
                b = m3d[1].Value<float>();
                c = m3d[4].Value<float>();
                d = m3d[5].Value<float>();
                tx = m3d[12].Value<float>();
                ty = m3d[13].Value<float>();
            }
            else
            {
                // Try long-form Matrix3D object (row-major 4x4 with m00..m33 keys)
                // Row 0: [m00=a, m01=b, ...], Row 1: [m10=c, m11=d, ...], Row 3: [m30=tx, m31=ty, ...]
                var m3dObj = elementToken?["Matrix3D"];
                if (m3dObj != null && m3dObj.Type == JTokenType.Object)
                {
                    a = m3dObj["m00"]?.Value<float>() ?? 1;
                    b = m3dObj["m01"]?.Value<float>() ?? 0;
                    c = m3dObj["m10"]?.Value<float>() ?? 0;
                    d = m3dObj["m11"]?.Value<float>() ?? 1;
                    tx = m3dObj["m30"]?.Value<float>() ?? 0;
                    ty = m3dObj["m31"]?.Value<float>() ?? 0;
                }
            }
        }

        // Apply TRP (Transformation Reference Point) — matches FlxAnimate exactly.
        // FlxAnimate calls matrix.translate(-trp.x, -trp.y) AFTER setting a,b,c,d,tx,ty.
        // OpenFL's Matrix.translate is a SIMPLE addition (NOT matrix-multiplied):
        //   tx += dx;  ty += dy;
        // So TRP application is just plain subtraction from tx/ty. This gives proper
        // mirror symmetry for flipped instances (a=-1): both sides land symmetrically
        // around the SI's M3D translation, which is the authoring-intended behavior.
        var trp = applyTRP ? (elementToken?["TRP"] ?? elementToken?["transformationPoint"]) : null;
        if (trp != null)
        {
            float trpX = trp["x"]?.Value<float>() ?? 0;
            float trpY = trp["y"]?.Value<float>() ?? 0;
            tx -= trpX;
            ty -= trpY;
        }

        return (a, b, c, d, tx, ty);
    }

    private static int ReadFrameIndex(JToken frameData)
    {
        return frameData?["I"]?.Value<int>()
            ?? frameData?["index"]?.Value<int>()
            ?? 0;
    }

    private static int ReadFrameDuration(JToken frameData)
    {
        return frameData?["DU"]?.Value<int>()
            ?? frameData?["duration"]?.Value<int>()
            ?? 1;
    }

    private static int ReadFirstFrame(JToken symbolInstance)
    {
        return symbolInstance?["FF"]?.Value<int>()
            ?? symbolInstance?["firstFrame"]?.Value<int>()
            ?? 0;
    }
    
    /// <summary>
    /// Recursively collect all atlas sprites visible at each tick of a symbol's timeline,
    /// with their accumulated affine transforms (rotation, scale, translation).
    /// tickMin/tickMax limit processing to frame entries overlapping [tickMin, tickMax),
    /// skipping expensive recursive work for ticks we don't need (e.g. Cancel/Exit labels
    /// when we only need Idle). Only used at the top-level call � nested symbols always
    /// process their full range since FF mapping can reference any internal tick.
    /// symbolCache caches each nested symbol's local-space tick parts so symbols referenced
    /// many times (e.g. NENE CS referenced 57� in pico spectator) are computed only once.
    /// </summary>
    private static void CollectPartsWithTransforms(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts,
        float pa, float pb, float pc, float pd, float ptx, float pty,
        HashSet<string> visited,
        bool applyTRP = true,
        int tickMin = 0, int tickMax = int.MaxValue,
        Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>> symbolCache = null)
    {
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return;

        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) { visited.Remove(symName); return; }

        int totalDuration = tickParts.Count;

        // Process layers in reverse order (last layer = drawn first/behind)
        var layerList = layers.ToList();
        for (int li = layerList.Count - 1; li >= 0; li--)
        {
            var layer = layerList[li];
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;

            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int duration = ReadFrameDuration(frameData);

                // Skip frame entries entirely outside the needed tick range.
                // This avoids expensive recursive symbol traversal for labels we don't need
                // (e.g. skipping Cancel's 52 ticks when we only need Idle + Confirm).
                if (index + duration <= tickMin || index >= tickMax) continue;

                var elements = frameData["E"] ?? frameData["elements"];
                if (elements == null) continue;

                // Clamp the tick range we actually populate
                int tStart = Math.Max(index, tickMin);
                int tEnd = Math.Min(index + duration, Math.Min(totalDuration, tickMax));

                foreach (var element in elements)
                {
                    // Direct atlas sprite
                    var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                    if (asi != null)
                    {
                        string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                        if (framesByName.TryGetValue(frameName, out var frame))
                        {
                            var mx = ReadTransform(asi, applyTRP);
                            // Compose: world = parent � child
                            float wa = pa * mx.a + pc * mx.b;
                            float wb = pb * mx.a + pd * mx.b;
                            float wc = pa * mx.c + pc * mx.d;
                            float wd = pb * mx.c + pd * mx.d;
                            float wtx = pa * mx.tx + pc * mx.ty + ptx;
                            float wty = pb * mx.tx + pd * mx.ty + pty;

                            for (int t = tStart; t < tEnd; t++)
                            {
                                tickParts[t].Add((frame, wa, wb, wc, wd, wtx, wty));
                            }
                        }
                    }

                    // Nested symbol instance
                    var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                    if (si != null)
                    {
                        string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                        {
                            var mx = ReadTransform(si, applyTRP);
                            // Compose parent � child matrix
                            float ca = pa * mx.a + pc * mx.b;
                            float cb = pb * mx.a + pd * mx.b;
                            float cc = pa * mx.c + pc * mx.d;
                            float cd = pb * mx.c + pd * mx.d;
                            float ctx = pa * mx.tx + pc * mx.ty + ptx;
                            float cty = pb * mx.tx + pd * mx.ty + pty;

                            int nestedDuration = GetSymbolDuration(nestedSym);
                            if (nestedDuration > 0)
                            {
                                // Try to use cached local-space tick parts for this symbol.
                                // Symbols like NENE CS are referenced 57� from different keyframes;
                                // computing them once in local space avoids exponential re-traversal.
                                List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> localTicks = null;
                                bool cached = symbolCache != null && symbolCache.TryGetValue(nestedName, out localTicks);

                                if (!cached)
                                {
                                    // Compute nested symbol in local space (identity parent matrix)
                                    localTicks = new List<List<(SpriteFrame, float, float, float, float, float, float)>>();
                                    for (int nt = 0; nt < nestedDuration; nt++)
                                        localTicks.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());

                                    // Use a fresh visited set for cache computation to avoid
                                    // contamination from the current recursion context
                                    var cacheVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    CollectPartsWithTransforms(nestedSym, allSymbols, framesByName, localTicks,
                                        1, 0, 0, 1, 0, 0, cacheVisited, applyTRP, symbolCache: symbolCache);

                                    if (symbolCache != null)
                                        symbolCache[nestedName] = localTicks;
                                }

                                // Map cached local parts onto parent ticks, composing parent transform
                            int firstFrame = ReadFirstFrame(si);
                                for (int t = tStart; t < tEnd; t++)
                                {
                                    int nestedTick = (firstFrame + (t - index)) % nestedDuration;
                                    var parts = localTicks[nestedTick];
                                    foreach (var (frame, la, lb, lc, ld, ltx, lty) in parts)
                                    {
                                        // Compose: world = parent(ca..cty) � local(la..lty)
                                        float wa = ca * la + cc * lb;
                                        float wb = cb * la + cd * lb;
                                        float wc2 = ca * lc + cc * ld;
                                        float wd = cb * lc + cd * ld;
                                        float wtx = ca * ltx + cc * lty + ctx;
                                        float wty = cb * ltx + cd * lty + cty;
                                        tickParts[t].Add((frame, wa, wb, wc2, wd, wtx, wty));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        visited.Remove(symName);
    }
    
    /// <summary>
    /// Pre-render composite frames into a single atlas texture.
    /// All unique frames are tiled into one RenderTarget2D to avoid creating
    /// hundreds of individual RTs (which caused UI freezes during loading).
    /// Applies full affine transforms (rotation, scale, translation) for each body part.
    /// </summary>
    private static List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> PreRenderComposite(
        GraphicsDevice graphicsDevice,
        Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts)
    {
        var result = new List<(Texture2D, Rectangle, Vector2)>();

        // Compute a global bounding box across ALL ticks so every frame is
        // rendered with the same origin. This prevents frame-to-frame anchor
        // shifting that causes wobble in composite animations.
        // Must account for rotation/scale by transforming all 4 corners of each sprite.
        float gMinX = float.MaxValue, gMinY = float.MaxValue;
        float gMaxX = float.MinValue, gMaxY = float.MinValue;
        bool anyParts = false;
        foreach (var parts in tickParts)
        {
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                anyParts = true;
                // For rotated atlas sprites, the stored W/H are swapped.
                // The M3D transform operates on unrotated dimensions.
                float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                // Transform all 4 corners: (0,0), (w,0), (0,h), (w,h)
                float x0 = tx,             y0 = ty;
                float x1 = a * w + tx,     y1 = b * w + ty;
                float x2 = c * h + tx,     y2 = d * h + ty;
                float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
                gMinX = Math.Min(gMinX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
                gMinY = Math.Min(gMinY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
                gMaxX = Math.Max(gMaxX, Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)));
                gMaxY = Math.Max(gMaxY, Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
            }
        }
        if (!anyParts) return result;

        int gWidth = Math.Clamp((int)MathF.Ceiling(gMaxX - gMinX), 1, 4096);
        int gHeight = Math.Clamp((int)MathF.Ceiling(gMaxY - gMinY), 1, 4096);

        // The animation's (0,0) registration point maps to this pixel in the texture.
        var origin = new Vector2(-gMinX, -gMinY);

        // Build dedup cache keys and identify unique frames
        var tickKeys = new string[tickParts.Count];
        var uniqueMap = new Dictionary<string, int>(); // key -> cell index
        var uniqueTickIdx = new List<int>(); // first tick index for each unique frame

        for (int i = 0; i < tickParts.Count; i++)
        {
            if (tickParts[i].Count == 0) continue;
            // Include all 6 transform components (A,B,C,D,TX,TY) at pixel/0.1% precision
            // to avoid merging frames with subtle animation differences (idle sway, bouncing).
            string key = string.Join("|", tickParts[i].Select(p =>
                $"{p.Frame.Name}:{(int)MathF.Round(p.TX)}:{(int)MathF.Round(p.TY)}:{(int)MathF.Round(p.A * 1000)}:{(int)MathF.Round(p.B * 1000)}:{(int)MathF.Round(p.C * 1000)}:{(int)MathF.Round(p.D * 1000)}"));
            tickKeys[i] = key;
            if (!uniqueMap.ContainsKey(key))
            {
                uniqueMap[key] = uniqueMap.Count;
                uniqueTickIdx.Add(i);
            }
        }

        int uniqueCount = uniqueMap.Count;
        if (uniqueCount == 0) return result;

        // Calculate atlas grid layout
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(uniqueCount)));
        int rows = Math.Max(1, (int)Math.Ceiling((double)uniqueCount / cols));
        int atlasW = cols * gWidth;
        int atlasH = rows * gHeight;

        // Constrain to max texture size � maximize cell count within MAX_TEX � MAX_TEX
        const int MAX_TEX = 4096;
        if (atlasW > MAX_TEX || atlasH > MAX_TEX)
        {
            int maxCols = MAX_TEX / Math.Max(1, gWidth);
            int maxRows = MAX_TEX / Math.Max(1, gHeight);
            // Try fitting: constrain cols first, compute rows, then check height
            cols = Math.Min(cols, maxCols);
            rows = (int)Math.Ceiling((double)uniqueCount / Math.Max(1, cols));
            if (rows > maxRows)
            {
                // Need more cols to reduce rows � use as many cols as possible
                rows = maxRows;
                cols = (int)Math.Ceiling((double)uniqueCount / Math.Max(1, maxRows));
                cols = Math.Min(cols, maxCols);
            }
            atlasW = cols * gWidth;
            atlasH = rows * gHeight;
        }
        atlasW = Math.Min(atlasW, MAX_TEX);
        atlasH = Math.Min(atlasH, MAX_TEX);
        int actualCols = Math.Max(1, atlasW / Math.Max(1, gWidth));
        int maxCells = actualCols * (atlasH / Math.Max(1, gHeight));

        // Create ONE atlas RenderTarget2D for all unique frames
        var rt = new RenderTarget2D(graphicsDevice, atlasW, atlasH);
        graphicsDevice.SetRenderTarget(rt);
        graphicsDevice.Clear(Color.Transparent);

        // Use per-body-part Matrix transforms for full affine fidelity (including skew/shear).
        // SpriteBatch.Draw's rotation+scale decomposition loses skew, causing body parts to
        // separate by ~5-15px. Instead, we pass the full 2D affine matrix [a,b,c,d,tx,ty] as
        // SpriteBatch's transformMatrix, which correctly transforms all 4 sprite corners.
        // One Begin/End per body part is fine since PreRenderComposite runs once during loading.
        using var sb = new SpriteBatch(graphicsDevice);

        foreach (var ti in uniqueTickIdx)
        {
            int cellIdx = uniqueMap[tickKeys[ti]];
            if (cellIdx >= maxCells) continue;

            int col = cellIdx % actualCols;
            int row = cellIdx / actualCols;
            float cellX = col * gWidth;
            float cellY = row * gHeight;

            foreach (var (frame, a, b, c, d, tx, ty) in tickParts[ti])
            {
                float adjustedTx = tx - gMinX + cellX;
                float adjustedTy = ty - gMinY + cellY;

                // Build the full 2D affine transform as a Matrix.
                // For non-rotated sprites: pixel (px,py) ? (a*px + c*py + tx, b*px + d*py + ty)
                // For rotated atlas sprites (stored 90� CW): atlas pixel (ax,ay) maps to
                // original pixel (ay, sourceWidth-ax), so the matrix columns swap/negate.
                Matrix transformMatrix;
                if (frame.Rotated)
                {
                    float sw = frame.SourceRect.Width;
                    transformMatrix = new Matrix(
                        -c, -d, 0, 0,
                         a,  b, 0, 0,
                         0,  0, 1, 0,
                        c * sw + adjustedTx, d * sw + adjustedTy, 0, 1
                    );
                }
                else
                {
                    transformMatrix = new Matrix(
                        a, b, 0, 0,
                        c, d, 0, 0,
                        0, 0, 1, 0,
                        adjustedTx, adjustedTy, 0, 1
                    );
                }

                // CullNone is critical: mirrored parts (a<0, e.g. right-side speakers)
                // produce matrices with negative determinant, reversing triangle winding.
                sb.Begin(SpriteSortMode.Deferred, _rtCompositeBlend,
                    rasterizerState: RasterizerState.CullNone,
                    transformMatrix: transformMatrix);
                sb.Draw(atlas, Vector2.Zero, frame.SourceRect, Color.White);
                sb.End();
            }
        }
        graphicsDevice.SetRenderTarget(null);

        // Build result list � map each tick to its atlas cell
        for (int i = 0; i < tickParts.Count; i++)
        {
            if (tickParts[i].Count == 0)
            {
                if (result.Count > 0) result.Add(result[^1]);
                continue;
            }

            int cellIdx = uniqueMap[tickKeys[i]];
            if (cellIdx >= maxCells)
            {
                if (result.Count > 0) result.Add(result[^1]);
                continue;
            }

            int col = cellIdx % actualCols;
            int row = cellIdx / actualCols;
            result.Add((rt, new Rectangle(col * gWidth, row * gHeight, gWidth, gHeight), origin));
        }

        return result;
    }

    /// <summary>
    /// Finalize deferred composite rendering on the main thread.
    /// Called after a background-thread Load(deferComposites: true) completes.
    /// Performs GPU work (RenderTarget2D, SpriteBatch) that requires the main thread.
    /// </summary>
    public void FinalizeComposites(GraphicsDevice graphicsDevice)
    {
        if (PendingComposites.Count == 0) return;

        int count = PendingComposites.Count;
        foreach (var (name, tickParts) in PendingComposites)
        {
            var compositeFrames = PreRenderComposite(graphicsDevice, Texture, tickParts);
            if (compositeFrames.Count > 0)
            {
                CompositeAnimations[name] = compositeFrames;
                var simpleFrames = new List<SpriteFrame>();
                for (int i = 0; i < compositeFrames.Count; i++)
                {
                    simpleFrames.Add(new SpriteFrame
                    {
                        Name = $"{name}_{i}",
                        SourceRect = compositeFrames[i].Rect,
                        Offset = Vector2.Zero,
                        FrameWidth = compositeFrames[i].Rect.Width,
                        FrameHeight = compositeFrames[i].Rect.Height
                    });
                }
                Animations[name] = simpleFrames;
            }
        }
        PendingComposites.Clear();
        Console.WriteLine($"FinalizeComposites: rendered {count} deferred composites");
    }

    /// <summary>
    /// Convert RawCompositeData to pre-rendered CompositeAnimations on the main thread.
    /// Called after background-thread loading completes (deferComposites: true).
    /// PreRenderComposite uses full affine Matrix transforms for pixel-perfect body part
    /// assembly � no skew/shear loss from rotation+scale decomposition.
    /// </summary>
    public void RenderRawComposites(GraphicsDevice graphicsDevice)
    {
        if (RawCompositeData.Count == 0 || Texture == null) return;

        // Save/restore render target (PreRenderComposite creates its own RT)
        var prevTargets = graphicsDevice.GetRenderTargets();
        var prevRT = prevTargets.Length > 0 ? prevTargets[0].RenderTarget as RenderTarget2D : null;

        int count = 0;
        foreach (var kvp in RawCompositeData)
        {
            string animName = kvp.Key;
            var tickParts = kvp.Value;

            var compositeFrames = PreRenderComposite(graphicsDevice, Texture, tickParts);

            if (compositeFrames.Count > 0)
            {
                CompositeAnimations[animName] = compositeFrames;
                var simpleFrames = new List<SpriteFrame>();
                for (int i = 0; i < compositeFrames.Count; i++)
                {
                    simpleFrames.Add(new SpriteFrame
                    {
                        Name = $"{animName}_{i}",
                        SourceRect = compositeFrames[i].Rect,
                        Offset = Vector2.Zero,
                        FrameWidth = compositeFrames[i].Rect.Width,
                        FrameHeight = compositeFrames[i].Rect.Height
                    });
                }
                Animations[animName] = simpleFrames;
                count++;
            }
        }
        RawCompositeData.Clear();

        graphicsDevice.SetRenderTarget(prevRT);
    }

    /// <summary>
    /// Render a JIT (Just-In-Time) composite animation on demand.
    /// Called on the main thread before drawing when the animation hasn't been rendered yet.
    /// Calls PreRenderComposite for this single animation and caches the result.
    /// Saves and restores the current render target since this may be called
    /// while FNFGame.Draw has the scene render target active.
    /// </summary>
    public void RenderJITAnimation(GraphicsDevice graphicsDevice, string animName)
    {
        if (!JITComposites.TryGetValue(animName, out var jit) || jit.Rendered) return;

        // Save current render target � PreRenderComposite will switch to its own RT
        // and then set to null. We need to restore the caller's RT (e.g. FNFGame._renderTarget).
        var prevTargets = graphicsDevice.GetRenderTargets();
        var prevRT = prevTargets.Length > 0 ? prevTargets[0].RenderTarget as RenderTarget2D : null;

        var compositeFrames = PreRenderComposite(graphicsDevice, Texture, jit.TickParts);

        // Restore previous render target
        graphicsDevice.SetRenderTarget(prevRT);

        if (compositeFrames.Count > 0)
        {
            CompositeAnimations[animName] = compositeFrames;
            var simpleFrames = new List<SpriteFrame>();
            for (int i = 0; i < compositeFrames.Count; i++)
            {
                simpleFrames.Add(new SpriteFrame
                {
                    Name = $"{animName}_{i}",
                    SourceRect = compositeFrames[i].Rect,
                    Offset = Vector2.Zero,
                    FrameWidth = compositeFrames[i].Rect.Width,
                    FrameHeight = compositeFrames[i].Rect.Height
                });
            }
            Animations[animName] = simpleFrames;
        }
        jit.Rendered = true;
        Console.WriteLine($"JIT rendered composite: {animName} ({compositeFrames.Count} frames)");
    }

    /// <summary>
    /// Simple fallback: recursively collect all unique atlas sprites from a symbol.
    /// </summary>
    private static HashSet<SpriteFrame> CollectAllSprites(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        HashSet<string> visited)
    {
        var result = new HashSet<SpriteFrame>();
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return result;
        
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers != null)
        {
            foreach (var layer in layers)
            {
                var layerFrames = layer["FR"] ?? layer["Frames"];
                if (layerFrames == null) continue;
                foreach (var frameData in layerFrames)
                {
                    var elements = frameData["E"] ?? frameData["elements"];
                    if (elements == null) continue;
                    foreach (var element in elements)
                    {
                        var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                        if (asi != null)
                        {
                            string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                            if (framesByName.TryGetValue(frameName, out var frame))
                                result.Add(frame);
                        }
                        var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                        if (si != null)
                        {
                            string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                                foreach (var ns in CollectAllSprites(nestedSym, allSymbols, framesByName, visited))
                                    result.Add(ns);
                        }
                    }
                }
            }
        }
        
        visited.Remove(symName);
        return result;
    }
    
    /// <summary>
    /// Simple fallback: for each tick, pick the largest atlas sprite across all layers.
    /// Used when pre-render compositing is disabled.
    /// </summary>
    private static void CollectLargestPerTick(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        List<SpriteFrame> result,
        HashSet<string> visited)
    {
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return;

        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) { visited.Remove(symName); return; }

        int totalDuration = 0;
        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int dur = ReadFrameDuration(frameData);
                totalDuration = Math.Max(totalDuration, index + dur);
            }
        }

        if (totalDuration == 0) { visited.Remove(symName); return; }

        var tickFrames = new SpriteFrame[totalDuration];
        var tickAreas = new int[totalDuration];

        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;

            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int duration = ReadFrameDuration(frameData);
                var elements = frameData["E"] ?? frameData["elements"];
                if (elements == null) continue;

                SpriteFrame bestSprite = null;
                int bestArea = 0;

                foreach (var element in elements)
                {
                    var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                    if (asi != null)
                    {
                        string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                        if (framesByName.TryGetValue(frameName, out var frame))
                        {
                            int area = frame.SourceRect.Width * frame.SourceRect.Height;
                            if (area > bestArea) { bestArea = area; bestSprite = frame; }
                        }
                    }
                    var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                    if (si != null)
                    {
                        string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                        {
                            foreach (var ns in CollectAllSprites(nestedSym, allSymbols, framesByName, visited))
                            {
                                int area = ns.SourceRect.Width * ns.SourceRect.Height;
                                if (area > bestArea) { bestArea = area; bestSprite = ns; }
                            }
                        }
                    }
                }

                if (bestSprite != null)
                {
                    for (int t = index; t < index + duration && t < totalDuration; t++)
                    {
                        if (bestArea > tickAreas[t])
                        {
                            tickFrames[t] = bestSprite;
                            tickAreas[t] = bestArea;
                        }
                    }
                }
            }
        }

        for (int t = 0; t < totalDuration; t++)
        {
            if (tickFrames[t] != null)
                result.Add(tickFrames[t]);
        }

        visited.Remove(symName);
    }
    
    private static void LoadFromJson(SpriteSheet sheet, string jsonPath)
    {
        try
        {
            string json = File.ReadAllText(jsonPath);
            var data = JObject.Parse(json);
            var sprites = data["ATLAS"]?["SPRITES"];
            
            if (sprites != null)
            {
                foreach (var spriteToken in sprites)
                {
                    var sprite = spriteToken["SPRITE"];
                    if (sprite == null) continue;
                    
                    string name = sprite["name"]?.ToString() ?? "";
                    int x = sprite["x"]?.Value<int>() ?? 0;
                    int y = sprite["y"]?.Value<int>() ?? 0;
                    int w = sprite["w"]?.Value<int>() ?? 0;
                    int h = sprite["h"]?.Value<int>() ?? 0;
                    
                    var frame = new SpriteFrame
                    {
                        Name = name,
                        SourceRect = new Rectangle(x, y, w, h),
                        Offset = Vector2.Zero
                    };
                    
                    sheet.Frames[name] = frame;
                    
                    string animName = GetAnimationName(name);
                    if (!sheet.Animations.ContainsKey(animName))
                    {
                        sheet.Animations[animName] = new List<SpriteFrame>();
                    }
                    sheet.Animations[animName].Add(frame);
                }
            }

            // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
            SortAnimationFrames(sheet.Animations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading JSON spritesheet: {ex.Message}");
        }
    }
    
    private static void LoadFromXml(SpriteSheet sheet, string xmlPath)
    {
        var xml = XDocument.Load(xmlPath);
        var root = xml.Root;
        
        foreach (var subTexture in root.Elements("SubTexture"))
        {
            string name = subTexture.Attribute("name")?.Value ?? "";
            int x = int.Parse(subTexture.Attribute("x")?.Value ?? "0");
            int y = int.Parse(subTexture.Attribute("y")?.Value ?? "0");
            int width = int.Parse(subTexture.Attribute("width")?.Value ?? "0");
            int height = int.Parse(subTexture.Attribute("height")?.Value ?? "0");
            
            // Frame offset and original size (for trimmed sprites)
            int frameX = int.Parse(subTexture.Attribute("frameX")?.Value ?? "0");
            int frameY = int.Parse(subTexture.Attribute("frameY")?.Value ?? "0");
            int frameWidth = int.Parse(subTexture.Attribute("frameWidth")?.Value ?? "0");
            int frameHeight = int.Parse(subTexture.Attribute("frameHeight")?.Value ?? "0");
            
            var frame = new SpriteFrame
            {
                Name = name,
                SourceRect = new Rectangle(x, y, width, height),
                Offset = new Vector2(-frameX, -frameY),
                Rotated = subTexture.Attribute("rotated")?.Value == "true",
                FrameWidth = frameWidth,
                FrameHeight = frameHeight
            };
            
            sheet.Frames[name] = frame;
            
            // Group frames by animation name (everything before the frame number)
            string animName = GetAnimationName(name);
            if (!sheet.Animations.ContainsKey(animName))
            {
                sheet.Animations[animName] = new List<SpriteFrame>();
            }
            sheet.Animations[animName].Add(frame);
        }

        // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
        SortAnimationFrames(sheet.Animations);

        Console.WriteLine($"Loaded spritesheet from XML: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations");
        if (sheet.Animations.Count <= 10)
        {
            foreach (var anim in sheet.Animations.Keys)
                Console.WriteLine($"  Animation: '{anim}' ({sheet.Animations[anim].Count} frames)");
        }
    }
    
    /// <summary>
    /// Load from Packer .txt format: "name = x y w h" per line.
    /// Used by some pixel/retro characters (e.g., spirit).
    /// </summary>
    private static void LoadFromPacker(SpriteSheet sheet, string txtPath)
    {
        var lines = File.ReadAllLines(txtPath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Format: "name = x y w h" 
            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;
            
            string name = line[..eqIdx].Trim();
            string[] parts = line[(eqIdx + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            
            if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
                !int.TryParse(parts[2], out int w) || !int.TryParse(parts[3], out int h))
                continue;
            
            var frame = new SpriteFrame
            {
                Name = name,
                SourceRect = new Rectangle(x, y, w, h),
                Offset = Vector2.Zero
            };
            
            sheet.Frames[name] = frame;
            
            // Packer names use underscore separator: "idle spirit_0" -> "idle spirit"
            string animName = name;
            int lastUnderscore = animName.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < animName.Length - 1)
            {
                string suffix = animName[(lastUnderscore + 1)..];
                if (int.TryParse(suffix, out _))
                    animName = animName[..lastUnderscore];
            }
            if (!sheet.Animations.ContainsKey(animName))
                sheet.Animations[animName] = new List<SpriteFrame>();
            sheet.Animations[animName].Add(frame);
        }

        // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
        SortAnimationFrames(sheet.Animations);

        Console.WriteLine($"Loaded spritesheet from Packer: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations");
    }
    
    /// <summary>
    /// Extract animation name from frame name (e.g., "idle0001" -> "idle")
    /// </summary>
    private static string GetAnimationName(string frameName)
    {
        // Remove trailing digits
        int i = frameName.Length - 1;
        while (i >= 0 && char.IsDigit(frameName[i]))
        {
            i--;
        }
        return i >= 0 ? frameName.Substring(0, i + 1) : frameName;
    }

    /// <summary>
    /// Extract the trailing frame number from a frame name (e.g., "idle0003" ? 3).
    /// Used to sort frames within an animation to match HaxeFlixel's addByPrefix behavior.
    /// </summary>
    private static int ExtractFrameNumber(string frameName)
    {
        int i = frameName.Length - 1;
        while (i >= 0 && char.IsDigit(frameName[i])) i--;
        if (i < frameName.Length - 1 && int.TryParse(frameName[(i + 1)..], out int num))
            return num;
        return 0;
    }

    /// <summary>
    /// Sort frames within each animation by their trailing frame number.
    /// Matches HaxeFlixel's addByPrefix behavior which sorts frames by name.
    /// </summary>
    private static void SortAnimationFrames(Dictionary<string, List<SpriteFrame>> animations)
    {
        foreach (var anim in animations.Values)
        {
            anim.Sort((a, b) => ExtractFrameNumber(a.Name).CompareTo(ExtractFrameNumber(b.Name)));
        }
    }
    
    // Cache for fuzzy animation lookups to avoid repeated linear scans
    private readonly Dictionary<string, List<SpriteFrame>> _fuzzyCache = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Get a specific animation by name (exact, case-insensitive, or partial match)
    /// Returns null if no match found
    /// </summary>
    public List<SpriteFrame> GetAnimation(string name)
    {
        // Try exact match first
        if (Animations.TryGetValue(name, out var frames))
            return frames;
        
        // Check fuzzy cache (covers previously resolved case-insensitive lookups)
        if (_fuzzyCache.TryGetValue(name, out var cached))
            return cached;
        
        // Try case-insensitive match
        foreach (var kvp in Animations)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _fuzzyCache[name] = kvp.Value; // Cache to avoid future linear scans
                return kvp.Value;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Get animation with fuzzy fallback � always returns something if any animation exists
    /// Used by AnimatedSprite when a guaranteed result is desired
    /// </summary>
    public List<SpriteFrame> GetAnimationFuzzy(string name)
    {
        // Check fuzzy cache first (avoids repeated linear scans)
        if (_fuzzyCache.TryGetValue(name, out var cached))
            return cached;
        
        var result = GetAnimation(name);
        if (result != null) { _fuzzyCache[name] = result; return result; }
        
        // Try partial match (animation name contains the search term)
        foreach (var kvp in Animations)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                _fuzzyCache[name] = kvp.Value;
                return kvp.Value;
            }
        }
        
        // Fallback to "default" animation if exists
        if (name != "default" && Animations.TryGetValue("default", out var defaultFrames))
        {
            _fuzzyCache[name] = defaultFrames;
            return defaultFrames;
        }
        
        // Fallback to first available animation
        if (Animations.Count > 0)
        {
            var first = Animations.Values.First();
            _fuzzyCache[name] = first;
            return first;
        }
        
        return null;
    }
    
    /// <summary>
    /// Get a frame by exact name
    /// </summary>
    public SpriteFrame GetFrame(string name)
    {
        return Frames.TryGetValue(name, out var frame) ? frame : null;
    }
    
    /// <summary>
    /// Dispose all GPU resources (atlas texture + composite textures).
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        _fuzzyCache.Clear();
        
        if (Texture != null && !Texture.IsDisposed)
            Texture.Dispose();
        Texture = null;
        
        var disposed = new HashSet<Texture2D>();
        foreach (var compList in CompositeAnimations.Values)
        {
            foreach (var (tex, _, _) in compList)
            {
                if (tex != null && !tex.IsDisposed && disposed.Add(tex))
                    tex.Dispose();
            }
        }
        CompositeAnimations.Clear();
        RawCompositeData.Clear();
        JITComposites.Clear();
        Animations.Clear();
        Frames.Clear();
    }
}

/// <summary>
/// Stores JIT composite data for an animation: tick parts and precomputed origin.
/// GPU rendering is deferred until the animation is first drawn.
/// </summary>
public class JITCompositeInfo
{
    public List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> TickParts;
    public Vector2 Origin;
    public bool Rendered;

    public JITCompositeInfo(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts)
    {
        TickParts = tickParts;
        // Precompute global bounding box origin (same logic as PreRenderComposite)
        // so GetCompositeOrigin can return correct values before GPU rendering.
        float gMinX = float.MaxValue, gMinY = float.MaxValue;
        foreach (var parts in tickParts)
        {
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                float x0 = tx, y0 = ty;
                float x1 = a * w + tx, y1 = b * w + ty;
                float x2 = c * h + tx, y2 = d * h + ty;
                float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
                gMinX = Math.Min(gMinX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
                gMinY = Math.Min(gMinY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
            }
        }
        Origin = (gMinX < float.MaxValue) ? new Vector2(-gMinX, -gMinY) : Vector2.Zero;
    }
}

/// <summary>
/// Animated sprite that uses a SpriteSheet
/// </summary>
public class AnimatedSprite
{
    public SpriteSheet Sheet { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public float Rotation { get; set; }
    public Color Tint { get; set; } = Color.White;
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;
    
    private string _currentAnim = "";
    private string _resolvedAnimName = ""; // Actual key in Animations/CompositeAnimations
    private List<SpriteFrame> _currentFrames;
    private int _currentFrameIndex;
    private float _frameTimer;
    private float _frameRate = 24f; // FPS
    private bool _looping = true;
    private bool _finished;
    private int _loopFrame = 0; // Frame to loop back to when looping

    public string CurrentAnimation => _currentAnim;
    public bool Finished => _finished;
    public int FrameIndex => _currentFrameIndex;
    public Action OnFinish { get; set; }
    
    /// <summary>
    /// Animation frame rate in FPS. Original FNF default is 24.
    /// Can be overridden per-animation from character JSON data.
    /// </summary>
    public float FrameRate
    {
        get => _frameRate;
        set => _frameRate = Math.Max(1f, value);
    }
    
    public void PlayAnimation(string name, bool force = false, bool loop = true)
    {
        if (_currentAnim == name && !force) return;

        // Try exact match first (fast path when name is already resolved)
        List<SpriteFrame> frames = null;
        if (Sheet != null && Sheet.Animations.TryGetValue(name, out frames) && frames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            _currentFrames = frames;
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }

        // Check pre-rendered CompositeAnimations (already GPU-rendered)
        if (Sheet != null && Sheet.CompositeAnimations.TryGetValue(name, out var compFrames) && compFrames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            // Create placeholder frames matching composite frame count for Update() advancement
            _currentFrames = CreatePlaceholderFrames(name, compFrames.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }

        // Check RawCompositeData (runtime compositing � parts drawn individually in Draw)
        if (Sheet != null && Sheet.RawCompositeData.TryGetValue(name, out var rawFrames) && rawFrames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            _currentFrames = CreatePlaceholderFrames(name, rawFrames.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }

        // Check JIT composites (deferred GPU rendering � sets _resolvedAnimName so
        // EnsureCurrentFrameRendered can trigger RenderJITAnimation on the main thread)
        if (Sheet != null && Sheet.JITComposites.TryGetValue(name, out var jit))
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            // Create placeholder frames matching tick count for Update() advancement
            _currentFrames = CreatePlaceholderFrames(name, jit.TickParts.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }

        // Fallback to fuzzy search in Animations
        frames = Sheet?.GetAnimationFuzzy(name);
        if (frames != null && frames.Count > 0)
        {
            _currentAnim = name;
            // Find the resolved key name
            _resolvedAnimName = name;
            foreach (var kvp in Sheet.Animations)
            {
                if (kvp.Value == frames) { _resolvedAnimName = kvp.Key; break; }
            }
            _currentFrames = frames;
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
        }
    }

    private static List<SpriteFrame> CreatePlaceholderFrames(string name, int count)
    {
        var frames = new List<SpriteFrame>(count);
        for (int i = 0; i < count; i++)
            frames.Add(new SpriteFrame { Name = $"{name}_{i}" });
        return frames;
    }
    
    public void PlayAnimationFromFrame(string name, int startFrame, bool loop = true, int loopFrame = 0)
    {
        PlayAnimation(name, true, loop);
        if (_currentFrames != null && startFrame < _currentFrames.Count)
            _currentFrameIndex = startFrame;
        _loopFrame = loopFrame;
    }
    
    public void Update(float deltaTime)
    {
        if (_currentFrames == null || _currentFrames.Count == 0 || _finished)
            return;
        
        _frameTimer += deltaTime;
        float frameDuration = 1f / _frameRate;
        
        while (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrameIndex++;
            
            if (_currentFrameIndex >= _currentFrames.Count)
            {
                if (_looping)
                {
                    _currentFrameIndex = Math.Min(_loopFrame, _currentFrames.Count - 1);
                }
                else
                {
                    _currentFrameIndex = _currentFrames.Count - 1;
                    _finished = true;
                    _frameTimer = 0; // Clear accumulated time before callback
                    OnFinish?.Invoke();
                    break; // Exit loop � callback may have reset animation state
                }
            }
        }
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        if (Sheet == null || _currentFrames == null || _currentFrames.Count == 0)
            return;

        // Runtime compositing: draw each body part directly using M3D transforms.
        // Matches original FNF's FlxAnimate � no pre-rendering, no GPU atlas waste,
        // and full affine transform fidelity (including shear).
        // Scale is applied as an outer transform: M_final = Scale * M3D + Position
        if (!string.IsNullOrEmpty(_resolvedAnimName) &&
            Sheet.RawCompositeData.TryGetValue(_resolvedAnimName, out var rawFrames) &&
            rawFrames.Count > 0)
        {
            if (Sheet.Texture == null) return;
            int idx = _currentFrameIndex % rawFrames.Count;
            var parts = rawFrames[idx];
            float sx = Scale.X, sy = Scale.Y;
            foreach (var (partFrame, a, b, c, d, tx, ty) in parts)
            {
                DrawTransformedPart(spriteBatch, Sheet.Texture, partFrame,
                    a * sx, b * sy, c * sx, d * sy,
                    tx * sx + Position.X, ty * sy + Position.Y);
            }
            return;
        }

        // Try pre-rendered composite animation (per-symbol composites for DJ, crowd, etc.)
        if (!string.IsNullOrEmpty(_resolvedAnimName) &&
            Sheet.CompositeAnimations.TryGetValue(_resolvedAnimName, out var compFrames) &&
            compFrames.Count > 0)
        {
            int idx = _currentFrameIndex % compFrames.Count;
            var (tex, rect, compOrigin) = compFrames[idx];
            // compOrigin is a world-space offset (from PreRenderComposite bounding box).
            // Apply it as a flat translation so Scale doesn't multiply the offset
            // (MonoGame's origin param does drawPos = Position - origin * Scale, which
            // pushes sprites off-screen when Scale != 1, e.g. bar at scale 2.5x).
            Vector2 drawPos = Position - compOrigin;
            // HaxeFlixel scales from sprite center (origin = frameW/2, frameH/2).
            // Compensate for MonoGame's top-left scaling when Scale != (1,1).
            // At Scale (1,1) the adjustment is zero � no effect on normal sprites.
            drawPos.X -= rect.Width * 0.5f * (Scale.X - 1f);
            drawPos.Y -= rect.Height * 0.5f * (Scale.Y - 1f);
SpriteEffects compEffects = Effects;
            spriteBatch.Draw(
                tex,
                drawPos,
                rect,
                Tint,
                Rotation,
                Vector2.Zero,
                Scale,
                compEffects,
                0f
            );
            return;
        }


        // Standard single-texture drawing
        if (Sheet.Texture == null) return;

        var frame = _currentFrames[_currentFrameIndex];
        Vector2 offset = frame.Offset;
        
        if (frame.Rotated)
        {
            // Sparrow atlas rotated="true": sprite is stored 90� clockwise in the atlas.
            // To draw it upright, rotate -90� with origin at (sourceWidth, 0).
            // After -90� rotation, the drawn size is (sourceHeight � sourceWidth).
            //
            // Flip handling: MonoGame applies SpriteEffects to texture UVs independently
            // of the geometric rotation. For the -90� unrotation:
            //   - FlipV on the source ? FlipH of the final (unrotated) result
            //   - FlipH on the source ? FlipV of the final (unrotated) result
            
            bool wantFlipH = (Effects & SpriteEffects.FlipHorizontally) != 0;
            
            // Mirror the trimmed-sprite X offset within the logical frame.
            // Unrotated content width = SourceRect.Height (atlas W/H are swapped).
            if (wantFlipH && frame.FrameWidth > 0)
            {
                offset = new Vector2(frame.FrameWidth - frame.SourceRect.Height - offset.X, offset.Y);
            }
            
            var destPos = Position + offset * Scale;
            var rotOrigin = new Vector2(frame.SourceRect.Width, 0);
            float rot = Rotation - MathF.PI / 2f;
            
            // Swap flip axes for the rotated draw
            var rotEffects = SpriteEffects.None;
            if (wantFlipH)
                rotEffects = SpriteEffects.FlipVertically;
            
            spriteBatch.Draw(
                Sheet.Texture,
                destPos,
                frame.SourceRect,
                Tint,
                rot,
                rotOrigin,
                Scale,
                rotEffects,
                0f
            );
        }
        else
        {
            // When flipped horizontally, mirror the X offset so trimmed frames stay stable.
            // Matches original FNF/Flixel: flipped offset.x = frameWidth - width - offset.x
            if ((Effects & SpriteEffects.FlipHorizontally) != 0 && frame.FrameWidth > 0)
            {
                offset = new Vector2(frame.FrameWidth - frame.SourceRect.Width - offset.X, offset.Y);
            }
            
            var destPos = Position + offset * Scale;
            
            spriteBatch.Draw(
                Sheet.Texture,
                destPos,
                frame.SourceRect,
                Tint,
                Rotation,
                Vector2.Zero,
                Scale,
                Effects,
                0f
            );
        }
    }
    
    public SpriteFrame GetCurrentFrame()
    {
        if (_currentFrames == null || _currentFrames.Count == 0)
            return null;
        return _currentFrames[_currentFrameIndex];
    }
    
    /// <summary>
    /// Get the composite registration point (origin) for the current animation frame.
    /// Returns null if the current animation is not a composite (animateatlas) animation.
    /// The origin represents the animation's (0,0) registration point within the composite texture,
    /// which for FNF characters is typically at the character's feet/anchor position.
    /// </summary>
    public Vector2? GetCompositeOrigin()
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return null;
        if (Sheet.CompositeAnimations.TryGetValue(_resolvedAnimName, out var compFrames) && compFrames.Count > 0)
        {
            int idx = _currentFrameIndex % compFrames.Count;
            return compFrames[idx].Origin;
        }
        // JIT fallback: use precomputed origin before GPU rendering has occurred
        if (Sheet.JITComposites.TryGetValue(_resolvedAnimName, out var jit))
            return jit.Origin;
        return null;
    }

    /// <summary>
    /// Ensure the current animation's composite frame is GPU-rendered and cached.
    /// Must be called on the main thread BEFORE SpriteBatch.Begin/Draw.
    /// For JIT composites, this triggers PreRenderComposite on first access.
    /// </summary>
    public void EnsureCurrentFrameRendered(GraphicsDevice gd)
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return;
        if (!Sheet.JITComposites.TryGetValue(_resolvedAnimName, out var jit) || jit.Rendered) return;
        Sheet.RenderJITAnimation(gd, _resolvedAnimName);
        // Refresh _currentFrames since RenderJITAnimation updates Animations
        if (Sheet.Animations.TryGetValue(_resolvedAnimName, out var newFrames))
            _currentFrames = newFrames;
    }

    /// <summary>
    /// Check if the current animation uses runtime compositing (RawCompositeData).
    /// When true, Position acts as an offset to the absolute M3D screen positions.
    /// </summary>
    public bool IsRuntimeComposite()
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return false;
        return Sheet.RawCompositeData.ContainsKey(_resolvedAnimName);
    }

    /// <summary>
    /// Draw a single body part with full affine transform decomposition.
    /// Handles atlas-rotated sprites, flips, rotation, and scale.
    /// </summary>
    private void DrawTransformedPart(SpriteBatch spriteBatch, Texture2D atlas, SpriteFrame frame,
        float a, float b, float c, float d, float tx, float ty)
    {
        float det = a * d - b * c;
        float rotation;
        float scaleX, scaleY;
        var effects = SpriteEffects.None;
        float drawX = tx;
        float drawY = ty;
        Vector2 drawOrigin = Vector2.Zero;

        // Rotated atlas sprites are stored 90� CW. Need -90� correction.
        float rotOffset = frame.Rotated ? -MathF.PI / 2f : 0f;
        if (frame.Rotated)
            drawOrigin = new Vector2(frame.SourceRect.Width, 0);

        float unrotW = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
        float unrotH = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;

        // Fast path: no M3D rotation/skew (just scale + optional flip)
        if (MathF.Abs(b) < 0.001f && MathF.Abs(c) < 0.001f)
        {
            rotation = rotOffset;
            scaleX = MathF.Abs(a);
            scaleY = MathF.Abs(d);
            if (a < 0)
            {
                if (frame.Rotated)
                    effects |= SpriteEffects.FlipVertically;
                else
                    effects |= SpriteEffects.FlipHorizontally;
                drawX = a * unrotW + tx;
            }
            if (d < 0)
            {
                if (frame.Rotated)
                    effects |= SpriteEffects.FlipHorizontally;
                else
                    effects |= SpriteEffects.FlipVertically;
                drawY = d * unrotH + ty;
            }
        }
        else
        {
            // General rotation + scale decomposition
            rotation = MathF.Atan2(b, a) + rotOffset;
            scaleX = MathF.Sqrt(a * a + b * b);
            scaleY = MathF.Sqrt(c * c + d * d);
            if (det < 0) scaleY = -scaleY;
        }

        spriteBatch.Draw(atlas,
            new Vector2(drawX, drawY),
            frame.SourceRect,
            Tint,
            rotation,
            drawOrigin,
            new Vector2(scaleX, scaleY),
            effects,
            0f);
    }
}
