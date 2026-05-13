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
/// Stage Editor — real editing tool. Drag props/characters to reposition,
/// adjust scale/zIndex with keyboard shortcuts, Ctrl+S to save JSON.
///
/// Controls:
///   Left-click viewport  = select nearest prop/character
///   Left-drag selected   = move prop/character position
///   Shift+drag selected  = move camera offset (characters only)
///   +/- (with selection) = adjust scale +-0.05
///   PgUp/PgDn            = adjust zIndex +-10
///   Delete               = remove selected prop
///   Ctrl+Z               = undo last move
///   Ctrl+S               = save stage JSON to disk
///   Right-drag / WASD    = pan viewport
///   Scroll               = zoom
///   V                    = toggle wireframe / textured view
///   R                    = reset view
/// </summary>
public class StageEditorScene : Scene
{
    const int LW = 240, RW = 250, TH = 34, SH = 22;

    private readonly List<string> _stageNames = new();
    private int _stageIndex;
    private JObject _stageData;
    private string _currentStage = "";
    private string _stageFilePath = "";
    private string _stageDirectory = ""; // directory from stage JSON (e.g., "week1")

    // Editable data (mutable classes, not structs)
    private readonly List<PropInfo> _props = new();
    private readonly List<CharInfo> _chars = new();
    private int _selectedProp = -1;
    private int _selectedChar = -1;
    private bool _dirty;

    private Vector2 _camOffset;
    private float _zoom = 0.3f;
    private int _listScroll;
    private int _tab; // 0=Stages, 1=Props, 2=Chars

    // View mode: wireframe vs textured (V to toggle)
    private bool _texturedMode = true;

    // Loaded textures for props (keyed by prop index)
    private readonly Dictionary<int, Texture2D> _propTextures = new();
    private readonly Dictionary<int, SpriteSheet> _propSheets = new();
    private readonly Dictionary<int, AnimatedSprite> _propSprites = new();
    // Loaded character sprites (keyed by char index)
    private readonly Dictionary<int, AnimatedSprite> _charSprites = new();

    // Dragging
    private bool _dragging;
    private bool _dragCamOffset; // shift+drag = camera offset
    private Vector2 _dragStart;

    // Undo stack
    private readonly Stack<UndoAction> _undoStack = new();

    class PropInfo { public string Name, AssetPath; public float X, Y, ScaleX, ScaleY; public int ZIndex; public float ScrollX, ScrollY; public bool HasAnims; }
    class CharInfo { public string Name; public float X, Y, CamX, CamY; public int ZIndex; }
    record UndoAction(string Type, int Index, float OldX, float OldY, float OldCamX = 0, float OldCamY = 0);

    public override void Load()
    {
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages");
        if (System.IO.Directory.Exists(root))
        {
            foreach (var f in System.IO.Directory.GetFiles(root, "*.json"))
                _stageNames.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            _stageNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        if (_stageNames.Count > 0) LoadStage(0);
    }

    public override void Unload() { }

    private void LoadStage(int index)
    {
        if (index < 0 || index >= _stageNames.Count) return;
        _stageIndex = index;
        _currentStage = _stageNames[index];
        _props.Clear(); _chars.Clear();
        _propTextures.Clear(); _propSheets.Clear(); _propSprites.Clear(); _charSprites.Clear();
        _selectedProp = -1; _selectedChar = -1;
        _dirty = false; _undoStack.Clear();

        _stageFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages", _currentStage + ".json");
        if (!System.IO.File.Exists(_stageFilePath)) return;
        _stageData = JObject.Parse(System.IO.File.ReadAllText(_stageFilePath));
        _stageDirectory = _stageData["directory"]?.ToString() ?? "";

        var propsArr = _stageData["props"] as JArray;
        if (propsArr != null)
        {
            int propIdx = 0;
            foreach (var p in propsArr)
            {
                var pos = p["position"] as JArray;
                var sc = p["scale"] as JArray;
                var scroll = p["scroll"] as JArray;
                var anims = p["animations"] as JArray;
                bool hasAnims = anims != null && anims.Count > 0;
                _props.Add(new PropInfo
                {
                    Name = p["name"]?.ToString() ?? "?",
                    AssetPath = p["assetPath"]?.ToString() ?? "",
                    X = pos?.Count > 0 ? (float)pos[0] : 0,
                    Y = pos?.Count > 1 ? (float)pos[1] : 0,
                    ScaleX = sc?.Count > 0 ? (float)sc[0] : 1,
                    ScaleY = sc?.Count > 1 ? (float)sc[1] : 1,
                    ZIndex = (int)(p["zIndex"] ?? 0),
                    ScrollX = scroll?.Count > 0 ? (float)scroll[0] : 1,
                    ScrollY = scroll?.Count > 1 ? (float)scroll[1] : 1,
                    HasAnims = hasAnims,
                });
                propIdx++;
            }
        }

        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            foreach (var kvp in charsObj)
            {
                var cv = kvp.Value;
                var pos = cv["position"] as JArray;
                var cam = cv["cameraOffsets"] as JArray;
                _chars.Add(new CharInfo
                {
                    Name = kvp.Key,
                    X = pos?.Count > 0 ? (float)pos[0] : 0,
                    Y = pos?.Count > 1 ? (float)pos[1] : 0,
                    CamX = cam?.Count > 0 ? (float)cam[0] : 0,
                    CamY = cam?.Count > 1 ? (float)cam[1] : 0,
                    ZIndex = (int)(cv["zIndex"] ?? 0)
                });
            }
        }

        // Load real textures for props and characters
        LoadStageTextures();
        LoadCharacterSprites();
    }

    /// <summary>
    /// Resolve stage folder name to actual Content directory name (mirrors PlayScene logic).
    /// </summary>
    private string ResolveStageFolder(string stageName)
    {
        string resolved = stageName switch
        {
            "mainStage" => "stage", "stage" => "stage",
            "mainStageErect" => "stage_erect", "stageErect" => "stage_erect", "stage_erect" => "stage_erect",
            "spookyMansion" => "spooky", "spookyMansionErect" => "spooky", "spooky" => "spooky",
            "phillyTrain" => "philly_train", "phillyTrainErect" => "philly_train_erect",
            "philly" => "philly_train", "philly-train" => "philly_train", "philly_train_erect" => "philly_train_erect",
            "limoRide" => "limo", "limoRideErect" => "limo_erect", "limo" => "limo", "limo_erect" => "limo_erect",
            "phillyStreets" => "philly_streets", "phillyStreetsErect" => "philly_streets", "philly_streets" => "philly_streets",
            "mallXmas" => "christmas", "mallXmasErect" => "christmas", "mallEvil" => "christmas",
            "school" => "school", "schoolEvil" => "school", "schoolErect" => "school", "schoolEvilErect" => "school",
            "tankmanBattlefield" => "tankman", "tankmanBattlefieldErect" => "tankman",
            "phillyBlazin" => "philly_blazin",
            "weeb" => "school",
            _ => stageName
        };
        if (Assets.ResolveDirectory($"game/stages/{resolved}") != null) return resolved;
        if (Assets.ResolveDirectory($"game/stages/{stageName}") != null) return stageName;
        return "stage";
    }

    /// <summary>
    /// Load a static prop texture using the same multi-path resolution as PlayScene.
    /// </summary>
    private Texture2D LoadPropTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        if (assetPath.StartsWith("#") && (assetPath.Length == 7 || assetPath.Length == 9))
        {
            try
            {
                string hex = assetPath.TrimStart('#');
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex[2..4], 16);
                int b = Convert.ToInt32(hex[4..6], 16);
                var colorTex = new Texture2D(Game.GraphicsDevice, 1, 1);
                colorTex.SetData(new[] { new Color(r, g, b) });
                return colorTex;
            }
            catch { return null; }
        }

        string stageFolder = ResolveStageFolder(_currentStage);
        string resolvedAsset = assetPath;
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string prefix = ResolveStageFolder(parts[0]);
            if (prefix == stageFolder) resolvedAsset = string.Join('/', parts[1..]);
            else if (parts[0] == "erect") resolvedAsset = string.Join('/', parts[1..]);
        }

        var tex = Assets.LoadTexture($"game/stages/{stageFolder}/{resolvedAsset}.png");
        if (tex != null && tex != Assets.Pixel) return tex;

        if (!string.IsNullOrEmpty(_stageDirectory))
        {
            tex = Assets.LoadTexture($"{_stageDirectory}/images/{assetPath}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }

        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string subFolder = ResolveStageFolder(parts[0]);
            string fileName = parts[^1];
            tex = Assets.LoadTexture($"game/stages/{subFolder}/{fileName}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
            tex = Assets.LoadTexture($"game/stages/{subFolder}/erect/{fileName}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }

        tex = Assets.LoadTexture($"game/stages/{stageFolder}/erect/{assetPath}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        tex = Assets.LoadTexture($"images/{assetPath}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        return null;
    }

    /// <summary>
    /// Load a spritesheet for an animated prop using the same path resolution as PlayScene.
    /// </summary>
    private SpriteSheet LoadPropSheet(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("#")) return null;
        string stageFolder = ResolveStageFolder(_currentStage);
        string propPath = assetPath;
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string prefix = ResolveStageFolder(parts[0]);
            if (prefix == stageFolder) propPath = string.Join('/', parts[1..]);
            else if (parts[0] == "erect") propPath = string.Join('/', parts[1..]);
        }

        var sheet = SpriteSheet.Load(Game, $"game/stages/{stageFolder}/{propPath}");
        if (sheet == null && !string.IsNullOrEmpty(_stageDirectory))
            sheet = SpriteSheet.Load(Game, $"{_stageDirectory}/images/{assetPath}");
        if (sheet == null && assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string subFolder = ResolveStageFolder(parts[0]);
            string fileName = parts[^1];
            sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/{fileName}");
            if (sheet == null) sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/erect/{fileName}");
        }
        if (sheet == null) sheet = SpriteSheet.Load(Game, $"game/stages/{stageFolder}/erect/{assetPath}");
        if (sheet == null) sheet = SpriteSheet.Load(Game, $"images/{assetPath}");
        return sheet;
    }

    /// <summary>Load textures/sheets for all stage props.</summary>
    private void LoadStageTextures()
    {
        var propsArr = _stageData?["props"] as JArray;
        if (propsArr == null) return;

        for (int i = 0; i < _props.Count && i < propsArr.Count; i++)
        {
            var prop = _props[i];
            if (string.IsNullOrEmpty(prop.AssetPath)) continue;

            if (prop.HasAnims)
            {
                // Animated prop — load spritesheet
                var sheet = LoadPropSheet(prop.AssetPath);
                if (sheet != null)
                {
                    _propSheets[i] = sheet;

                    // Register animations from JSON
                    var animsArr = propsArr[i]["animations"] as JArray;
                    string startAnim = propsArr[i]["startingAnimation"]?.ToString();
                    if (animsArr != null)
                    {
                        foreach (var a in animsArr)
                        {
                            string aPrefix = a["prefix"]?.ToString() ?? a["name"]?.ToString() ?? "";
                            string aName = a["name"]?.ToString() ?? aPrefix;
                            if (string.IsNullOrEmpty(aPrefix)) continue;
                            var frames = sheet.GetAnimationFuzzy(aPrefix);
                            if (frames == null || frames.Count == 0) continue;
                            var idxArr = a["frameIndices"] as JArray;
                            if (idxArr != null && idxArr.Count > 0)
                            {
                                var subFrames = new List<SpriteFrame>();
                                foreach (int idx in idxArr.Select(j => (int)j))
                                    if (idx >= 0 && idx < frames.Count) subFrames.Add(frames[idx]);
                                if (subFrames.Count > 0) sheet.Animations[aName] = subFrames;
                            }
                            else if (aName != aPrefix && !sheet.Animations.ContainsKey(aName))
                                sheet.Animations[aName] = frames;
                            if (string.IsNullOrEmpty(startAnim)) startAnim = aName;
                        }
                    }

                    var sprite = new AnimatedSprite { Sheet = sheet };
                    sprite.Position = new Vector2(prop.X, prop.Y);
                    sprite.Scale = new Vector2(prop.ScaleX, prop.ScaleY);
                    if (!string.IsNullOrEmpty(startAnim))
                    {
                        var f = sheet.GetAnimationFuzzy(startAnim);
                        if (f != null && f.Count > 0)
                        {
                            string key = sheet.Animations.FirstOrDefault(k => k.Value == f).Key ?? startAnim;
                            sprite.PlayAnimation(key, loop: true);
                        }
                    }
                    _propSprites[i] = sprite;
                }
            }
            else
            {
                // Static prop — load texture
                var tex = LoadPropTexture(prop.AssetPath);
                if (tex != null) _propTextures[i] = tex;
            }
        }
    }

    /// <summary>Load character sprites for the stage characters (bf, dad, gf).</summary>
    private void LoadCharacterSprites()
    {
        // Map stage character slots to actual character names from a simple lookup
        // The stage JSON has character slots (bf, dad, gf) but not the actual character names.
        // We need the level JSONs or a default mapping. Use common defaults per stage.
        string[] defaultChars = { "bf", "dad", "gf" };

        for (int i = 0; i < _chars.Count; i++)
        {
            string charSlot = _chars[i].Name; // "bf", "dad", or "gf"
            string charName = charSlot switch
            {
                "bf" => "bf",
                "dad" => "dad",
                "gf" => "gf",
                _ => charSlot
            };

            // Try to load character JSON and create a sprite
            var charJson = Assets.LoadJson<CharacterJsonData>($"data/characters/{charName}");
            if (charJson == null) continue;

            string assetPath = charJson.AssetPath ?? charName;
            // Try loading spritesheet from game/characters/{name}/
            SpriteSheet sheet = null;
            string[] tryPaths = {
                $"game/characters/{charName}/sprites",
                $"game/characters/{charName}",
                $"game/characters/{charName}/default_spritemap"
            };
            foreach (var p in tryPaths)
            {
                sheet = SpriteSheet.Load(Game, p, preRenderComposites: true);
                if (sheet != null) break;
            }
            if (sheet == null) continue;

            // Register animations from character JSON
            if (charJson.Animations != null)
            {
                foreach (var anim in charJson.Animations)
                {
                    string prefix = anim.Prefix ?? anim.Name ?? "";
                    string name = anim.Name ?? prefix;
                    if (string.IsNullOrEmpty(prefix)) continue;
                    var frames = sheet.GetAnimationFuzzy(prefix);
                    if (frames != null && frames.Count > 0)
                    {
                        if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                        {
                            var sub = new List<SpriteFrame>();
                            foreach (int idx in anim.FrameIndices)
                                if (idx >= 0 && idx < frames.Count) sub.Add(frames[idx]);
                            if (sub.Count > 0) sheet.Animations[name] = sub;
                        }
                        else if (!sheet.Animations.ContainsKey(name))
                            sheet.Animations[name] = frames;
                    }
                }
            }

            var sprite = new AnimatedSprite { Sheet = sheet };
            float scale = charJson.Scale > 0 ? charJson.Scale : 1f;
            sprite.Scale = new Vector2(scale, scale);
            sprite.Effects = charJson.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            // Play idle animation
            string startAnim = charJson.StartingAnimation ?? "idle";
            var idleFrames = sheet.GetAnimationFuzzy(startAnim);
            if (idleFrames == null || idleFrames.Count == 0)
                idleFrames = sheet.GetAnimationFuzzy("idle");
            if (idleFrames != null && idleFrames.Count > 0)
            {
                string key = sheet.Animations.FirstOrDefault(k => k.Value == idleFrames).Key ?? startAnim;
                sprite.PlayAnimation(key, loop: true);
            }

            _charSprites[i] = sprite;
        }
    }

    private void SaveStage()
    {
        if (_stageData == null || string.IsNullOrEmpty(_stageFilePath)) return;

        // Write prop changes back to JObject
        var propsArr = _stageData["props"] as JArray;
        if (propsArr != null)
        {
            for (int i = 0; i < Math.Min(_props.Count, propsArr.Count); i++)
            {
                var p = _props[i];
                propsArr[i]["position"] = new JArray(Math.Round(p.X, 1), Math.Round(p.Y, 1));
                propsArr[i]["scale"] = new JArray(Math.Round(p.ScaleX, 2), Math.Round(p.ScaleY, 2));
                propsArr[i]["zIndex"] = p.ZIndex;
            }
        }

        // Write character changes
        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            foreach (var ch in _chars)
            {
                if (charsObj[ch.Name] is JObject co)
                {
                    co["position"] = new JArray(Math.Round(ch.X, 1), Math.Round(ch.Y, 1));
                    co["cameraOffsets"] = new JArray(Math.Round(ch.CamX, 1), Math.Round(ch.CamY, 1));
                    co["zIndex"] = ch.ZIndex;
                }
            }
        }

        System.IO.File.WriteAllText(_stageFilePath, _stageData.ToString(Formatting.Indented));
        _dirty = false;
        EditorUI.ShowToast($"Saved {_currentStage}.json");
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        // V = toggle wireframe/textured view
        if (Input.IsPressed(Keys.V) && !Input.IsHeld(Keys.LeftControl))
        {
            _texturedMode = !_texturedMode;
            EditorUI.ShowToast(_texturedMode ? "Textured View" : "Wireframe View");
        }

        // Update animated sprites
        if (_texturedMode)
        {
            foreach (var kvp in _propSprites)
                kvp.Value.Update(dt);
            foreach (var kvp in _charSprites)
                kvp.Value.Update(dt);
        }

        // Ctrl+S = save
        if (Input.IsPressed(Keys.S) && Input.IsHeld(Keys.LeftControl))
        { SaveStage(); return; }

        // Ctrl+Z = undo
        if (Input.IsPressed(Keys.Z) && Input.IsHeld(Keys.LeftControl) && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "prop" && u.Index < _props.Count)
            { _props[u.Index].X = u.OldX; _props[u.Index].Y = u.OldY; }
            else if (u.Type == "char" && u.Index < _chars.Count)
            { _chars[u.Index].X = u.OldX; _chars[u.Index].Y = u.OldY; _chars[u.Index].CamX = u.OldCamX; _chars[u.Index].CamY = u.OldCamY; }
            _dirty = true;
            EditorUI.ShowToast("Undo");
        }

        // Stage switching (from list only, not arrow keys while editing)
        var vpRect = new Rectangle(LW, TH, FNFGame.SCREEN_WIDTH - LW - RW, FNFGame.SCREEN_HEIGHT - TH - SH);

        // Viewport interactions
        if (EditorUI.IsHovered(vpRect))
        {
            int scroll = Input.ScrollDelta;
            if (scroll > 0) _zoom = Math.Min(3f, _zoom * 1.1f);
            if (scroll < 0) _zoom = Math.Max(0.03f, _zoom / 1.1f);

            // Right-drag = pan
            if (EditorUI.Mouse.RightButton == ButtonState.Pressed)
                _camOffset += EditorUI.MouseDelta;

            float ox = vpRect.X + vpRect.Width / 2f + _camOffset.X;
            float oy = vpRect.Y + vpRect.Height / 2f + _camOffset.Y;

            // Left click = select + start drag
            if (EditorUI.MouseClicked)
            {
                _dragCamOffset = Input.IsHeld(Keys.LeftShift);
                TrySelect(ox, oy);
                if (_selectedProp >= 0 || _selectedChar >= 0)
                {
                    _dragging = true;
                    _dragStart = EditorUI.MousePos;
                    // Push undo
                    if (_selectedProp >= 0)
                        _undoStack.Push(new UndoAction("prop", _selectedProp, _props[_selectedProp].X, _props[_selectedProp].Y));
                    else if (_selectedChar >= 0)
                    {
                        var ch = _chars[_selectedChar];
                        _undoStack.Push(new UndoAction("char", _selectedChar, ch.X, ch.Y, ch.CamX, ch.CamY));
                    }
                }
            }

            // Dragging
            if (_dragging && EditorUI.MouseDown)
            {
                var delta = EditorUI.MouseDelta / _zoom;
                if (_selectedProp >= 0)
                { _props[_selectedProp].X += delta.X; _props[_selectedProp].Y += delta.Y; _dirty = true; }
                else if (_selectedChar >= 0)
                {
                    if (_dragCamOffset)
                    { _chars[_selectedChar].CamX += delta.X; _chars[_selectedChar].CamY += delta.Y; }
                    else
                    { _chars[_selectedChar].X += delta.X; _chars[_selectedChar].Y += delta.Y; }
                    _dirty = true;
                }
            }

            if (EditorUI.MouseReleased) _dragging = false;
        }
        else
        {
            _dragging = false;
        }

        // Scale adjust (+/- on selected prop)
        if (_selectedProp >= 0)
        {
            float sd = 0;
            if (Input.IsPressed(Keys.OemPlus) || Input.IsPressed(Keys.Add)) sd = 0.05f;
            if (Input.IsPressed(Keys.OemMinus) || Input.IsPressed(Keys.Subtract)) sd = -0.05f;
            if (sd != 0)
            {
                _props[_selectedProp].ScaleX = Math.Max(0.05f, _props[_selectedProp].ScaleX + sd);
                _props[_selectedProp].ScaleY = Math.Max(0.05f, _props[_selectedProp].ScaleY + sd);
                _dirty = true;
            }
        }

        // ZIndex adjust (PgUp/PgDn)
        if (Input.IsPressed(Keys.PageUp))
        {
            if (_selectedProp >= 0) { _props[_selectedProp].ZIndex += 10; _dirty = true; }
            if (_selectedChar >= 0) { _chars[_selectedChar].ZIndex += 10; _dirty = true; }
        }
        if (Input.IsPressed(Keys.PageDown))
        {
            if (_selectedProp >= 0) { _props[_selectedProp].ZIndex -= 10; _dirty = true; }
            if (_selectedChar >= 0) { _chars[_selectedChar].ZIndex -= 10; _dirty = true; }
        }

        // Delete prop
        if (Input.IsPressed(Keys.Delete) && _selectedProp >= 0)
        {
            _props.RemoveAt(_selectedProp);
            _selectedProp = -1; _dirty = true;
            EditorUI.ShowToast("Prop deleted");
        }

        // WASD pan
        float panSpeed = 400f / _zoom * dt;
        if (Input.IsHeld(Keys.A) && !Input.IsHeld(Keys.LeftControl)) _camOffset.X += panSpeed;
        if (Input.IsHeld(Keys.D) && !Input.IsHeld(Keys.LeftControl)) _camOffset.X -= panSpeed;
        if (Input.IsHeld(Keys.W)) _camOffset.Y += panSpeed;
        if (Input.IsHeld(Keys.S) && !Input.IsHeld(Keys.LeftControl)) _camOffset.Y -= panSpeed;
        if (Input.IsPressed(Keys.R) && !Input.IsHeld(Keys.LeftControl)) { _camOffset = Vector2.Zero; _zoom = 0.3f; }

        if (Input.BackPressed)
        {
            if (_dirty) SaveStage(); // auto-save on exit
            Game.Scenes.ChangeScene(new EditorHubScene());
        }
    }

    private void TrySelect(float ox, float oy)
    {
        var mp = EditorUI.MousePos;
        _selectedProp = -1; _selectedChar = -1;

        // Check props (reverse order = top-most first)
        for (int i = _props.Count - 1; i >= 0; i--)
        {
            var p = _props[i];
            float sx = ox + p.X * _zoom;
            float sy = oy + p.Y * _zoom;
            int pw, ph;
            if (_texturedMode && _propTextures.TryGetValue(i, out var tex))
            { pw = Math.Max(20, (int)(tex.Width * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(tex.Height * p.ScaleY * _zoom)); }
            else if (_texturedMode && _propSprites.TryGetValue(i, out var spr))
            {
                var f = spr.GetCurrentFrame();
                int fw = f != null ? (f.Rotated ? f.SourceRect.Height : f.SourceRect.Width) : 200;
                int fh = f != null ? (f.Rotated ? f.SourceRect.Width : f.SourceRect.Height) : 150;
                pw = Math.Max(20, (int)(fw * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(fh * p.ScaleY * _zoom));
            }
            else
            { pw = Math.Max(20, (int)(200 * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(150 * p.ScaleY * _zoom)); }
            var r = new Rectangle((int)sx, (int)sy, pw, ph);
            if (r.Contains((int)mp.X, (int)mp.Y))
            { _selectedProp = i; _selectedChar = -1; _tab = 1; return; }
        }

        // Check characters
        for (int i = _chars.Count - 1; i >= 0; i--)
        {
            var ch = _chars[i];
            float sx = ox + ch.X * _zoom;
            float sy = oy + ch.Y * _zoom;
            int cw, chh;
            if (_texturedMode && _charSprites.TryGetValue(i, out var cs))
            {
                var f = cs.GetCurrentFrame();
                float scale = cs.Scale.X;
                int fw = f != null ? (f.Rotated ? f.SourceRect.Height : f.SourceRect.Width) : 80;
                int fh = f != null ? (f.Rotated ? f.SourceRect.Width : f.SourceRect.Height) : 120;
                cw = Math.Max(20, (int)(fw * scale * _zoom)); chh = Math.Max(20, (int)(fh * scale * _zoom));
            }
            else
            { cw = Math.Max(20, (int)(80 * _zoom)); chh = Math.Max(20, (int)(120 * _zoom)); }
            var r = new Rectangle((int)sx - cw / 2, (int)sy - chh, cw, chh);
            if (r.Contains((int)mp.X, (int)mp.Y))
            { _selectedChar = i; _selectedProp = -1; _tab = 2; return; }
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
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Save", false, "Ctrl+S"))
        { SaveStage(); }
        tx += 54;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Undo") && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "prop" && u.Index < _props.Count) { _props[u.Index].X = u.OldX; _props[u.Index].Y = u.OldY; }
            else if (u.Type == "char" && u.Index < _chars.Count) { _chars[u.Index].X = u.OldX; _chars[u.Index].Y = u.OldY; _chars[u.Index].CamX = u.OldCamX; _chars[u.Index].CamY = u.OldCamY; }
            _dirty = true;
        }
        tx += 58;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 70, _texturedMode ? "Textured" : "Wireframe", _texturedMode, "V"))
        {
            _texturedMode = !_texturedMode;
            EditorUI.ShowToast(_texturedMode ? "Textured View" : "Wireframe View");
        }
        tx += 74;
        sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;
        string info = $"Stage: {_currentStage}  Props: {_props.Count}  Chars: {_chars.Count}";
        font.DrawText(sb, info, new Vector2(tx, 10), EditorUI.TextPrimary);

        if (_dirty)
        {
            float dw = font.MeasureString(info).X;
            font.DrawText(sb, " [MODIFIED]", new Vector2(tx + dw, 10), EditorUI.Warning);
        }

        // Left panel
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp, "Browser", font);
        string[] tabs = { "Stages", "Props", "Chars" };
        _tab = EditorUI.TabBar(sb, px, font, 0, TH + 26, LW, tabs, _tab);
        int listY = TH + 56, listH = H - TH - SH - 56, rowH = 22;

        if (_tab == 0)
        {
            int contentH = _stageNames.Count * rowH;
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int i = 0; i < _stageNames.Count; i++)
            {
                int ry = listY + i * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _stageNames[i], i == _stageIndex))
                { if (_dirty) SaveStage(); LoadStage(i); }
            }
        }
        else if (_tab == 1)
        {
            for (int i = 0; i < _props.Count; i++)
            {
                int ry = listY + i * rowH;
                if (ry > listY + listH) break;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _props[i].Name, i == _selectedProp,
                    badge: $"z{_props[i].ZIndex}", badgeColor: EditorUI.TextDim))
                { _selectedProp = i; _selectedChar = -1; }
            }
        }
        else
        {
            for (int i = 0; i < _chars.Count; i++)
            {
                int ry = listY + i * rowH;
                if (ry > listY + listH) break;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _chars[i].Name, i == _selectedChar,
                    badge: $"z{_chars[i].ZIndex}", badgeColor: EditorUI.TextDim))
                { _selectedChar = i; _selectedProp = -1; }
            }
        }

        // Center viewport
        DrawViewport(sb, px, font, fontSm, W, H);

        // Right panel
        DrawProperties(sb, px, font, fontSm, W, H);

        // Status bar
        string saveHint = _dirty ? "UNSAVED — Ctrl+S to save" : "Saved";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            saveHint, $"Zoom: {_zoom:P0}",
            "LeftDrag=move  Shift+Drag=camOffset  +/-=scale  PgUp/Dn=zIndex  Del=remove  V=view  Ctrl+Z=undo");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }

    private void DrawViewport(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        var area = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);
        EditorUI.FillRect(sb, px, area, new Color(22, 22, 35));

        if (!_texturedMode)
            EditorUI.DrawGrid(sb, px, area, _camOffset, _zoom, 100);

        float ox = area.X + area.Width / 2f + _camOffset.X;
        float oy = area.Y + area.Height / 2f + _camOffset.Y;

        // Build combined draw list: all props + chars sorted by zIndex for proper interleaving
        var drawItems = new List<(int ZIndex, string Kind, int Index)>();
        for (int i = 0; i < _props.Count; i++)
            drawItems.Add((_props[i].ZIndex, "prop", i));
        for (int i = 0; i < _chars.Count; i++)
            drawItems.Add((_chars[i].ZIndex, "char", i));
        drawItems.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));

        Color[] charColors = { new(78,201,176), new(224,108,117), new(229,192,123) };

        foreach (var (zIdx, kind, idx) in drawItems)
        {
            if (kind == "prop")
                DrawProp(sb, px, fontSm, ox, oy, idx);
            else
                DrawChar(sb, px, fontSm, ox, oy, idx, charColors);
        }

        EditorUI.DrawCrosshair(sb, px, ox, oy, 30, EditorUI.BorderLight);

        // View mode label
        string modeLabel = _texturedMode ? "TEXTURED (V)" : "WIREFRAME (V)";
        fontSm.DrawText(sb, modeLabel, new Vector2(area.X + 6, area.Y + 4), EditorUI.TextDim);
    }

    private void DrawProp(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float ox, float oy, int i)
    {
        var p = _props[i];
        float sx = ox + p.X * _zoom;
        float sy = oy + p.Y * _zoom;
        bool sel = (i == _selectedProp);

        if (_texturedMode)
        {
            bool drewTexture = false;

            // Try animated sprite first
            if (_propSprites.TryGetValue(i, out var animSprite))
            {
                var frame = animSprite.GetCurrentFrame();
                if (frame != null)
                {
                    int fw = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                    int fh = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                    int drawW = Math.Max(1, (int)(fw * p.ScaleX * _zoom));
                    int drawH = Math.Max(1, (int)(fh * p.ScaleY * _zoom));
                    var destRect = new Rectangle((int)sx, (int)sy, drawW, drawH);
                    sb.Draw(animSprite.Sheet.Texture, destRect, frame.SourceRect, Color.White);
                    drewTexture = true;
                    if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
                }
            }
            // Try static texture
            else if (_propTextures.TryGetValue(i, out var tex))
            {
                int drawW = Math.Max(1, (int)(tex.Width * p.ScaleX * _zoom));
                int drawH = Math.Max(1, (int)(tex.Height * p.ScaleY * _zoom));
                var destRect = new Rectangle((int)sx, (int)sy, drawW, drawH);
                sb.Draw(tex, destRect, Color.White);
                drewTexture = true;
                if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
            }

            // Fallback to wireframe box if no texture loaded
            if (!drewTexture)
                DrawPropWireframe(sb, px, fontSm, sx, sy, p, sel);
        }
        else
        {
            DrawPropWireframe(sb, px, fontSm, sx, sy, p, sel);
        }
    }

    private void DrawPropWireframe(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float sx, float sy, PropInfo p, bool sel)
    {
        int pw = Math.Max(4, (int)(200 * p.ScaleX * _zoom));
        int ph = Math.Max(4, (int)(150 * p.ScaleY * _zoom));
        Color c = sel ? EditorUI.Accent : new Color(60, 60, 80, 120);
        EditorUI.FillRect(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), c);
        if (sel) EditorUI.DrawSelectionBox(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), EditorUI.Gold);
        else EditorUI.DrawBorder(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), EditorUI.Border);
        if (_zoom > 0.12f)
            fontSm.DrawText(sb, p.Name, new Vector2(sx + 3, sy + 3), EditorUI.TextPrimary);
    }

    private void DrawChar(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float ox, float oy, int i, Color[] charColors)
    {
        var ch = _chars[i];
        float sx = ox + ch.X * _zoom;
        float sy = oy + ch.Y * _zoom;
        bool sel = (i == _selectedChar);
        Color col = charColors[i % charColors.Length];

        if (_texturedMode && _charSprites.TryGetValue(i, out var charSprite))
        {
            var frame = charSprite.GetCurrentFrame();
            if (frame != null)
            {
                float scale = charSprite.Scale.X;
                int fw = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                int fh = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                int drawW = Math.Max(1, (int)(fw * scale * _zoom));
                int drawH = Math.Max(1, (int)(fh * scale * _zoom));
                // Character position is at feet; draw sprite above that point
                var destRect = new Rectangle((int)sx - drawW / 2, (int)sy - drawH, drawW, drawH);
                sb.Draw(charSprite.Sheet.Texture, destRect, frame.SourceRect, Color.White,
                    0f, Vector2.Zero, charSprite.Effects, 0);
                if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
                // Feet marker
                sb.Draw(px, new Rectangle((int)sx - 3, (int)sy - 3, 6, 6), col);
            }
            else
            {
                DrawCharWireframe(sb, px, fontSm, sx, sy, ch, sel, col);
            }
        }
        else
        {
            DrawCharWireframe(sb, px, fontSm, sx, sy, ch, sel, col);
        }

        // Camera offset line + marker (always visible)
        float camSX = sx + ch.CamX * _zoom;
        float camSY = sy + ch.CamY * _zoom;
        sb.Draw(px, new Rectangle((int)Math.Min(sx, camSX), (int)sy, Math.Max(2, (int)Math.Abs(camSX - sx)), 1), col * 0.5f);
        sb.Draw(px, new Rectangle((int)camSX, (int)Math.Min(sy, camSY), 1, Math.Max(2, (int)Math.Abs(camSY - sy))), col * 0.5f);
        var camC = sel ? Color.White : col * 0.7f;
        sb.Draw(px, new Rectangle((int)camSX - 5, (int)camSY - 5, 10, 10), camC);
        sb.Draw(px, new Rectangle((int)camSX - 3, (int)camSY - 3, 6, 6), new Color(22, 22, 35));
        sb.Draw(px, new Rectangle((int)camSX - 1, (int)camSY - 1, 2, 2), camC);
    }

    private void DrawCharWireframe(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float sx, float sy, CharInfo ch, bool sel, Color col)
    {
        int cw = Math.Max(8, (int)(80 * _zoom));
        int chh = Math.Max(8, (int)(120 * _zoom));
        var charRect = new Rectangle((int)sx - cw / 2, (int)sy - chh, cw, chh);
        EditorUI.FillRect(sb, px, charRect, (sel ? col : col * 0.4f) * 0.6f);
        if (sel) EditorUI.DrawSelectionBox(sb, px, charRect, EditorUI.Gold);
        else EditorUI.DrawBorder(sb, px, charRect, col);
        sb.Draw(px, new Rectangle((int)sx - 4, (int)sy - 4, 8, 8), col);
        if (_zoom > 0.1f)
            fontSm.DrawText(sb, ch.Name.ToUpper(), new Vector2(charRect.X + 3, charRect.Y + 3), Color.White);
    }

    private void DrawProperties(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Properties", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Stage", _currentStage); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", _stageData?["name"]?.ToString() ?? "?"); y += 18;
        float cz = _stageData != null ? (float)(_stageData["cameraZoom"] ?? 1.0) : 1.0f;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "CamZoom", $"{cz:F2}"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Dir", _stageData?["directory"]?.ToString() ?? "?"); y += 18;
        sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;

        if (_selectedProp >= 0 && _selectedProp < _props.Count)
        {
            var p = _props[_selectedProp];
            fontSm.DrawText(sb, "SELECTED PROP", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", p.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Asset", p.AssetPath); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{p.X:F1}, {p.Y:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scale", $"{p.ScaleX:F2} x {p.ScaleY:F2}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Z-Index", $"{p.ZIndex}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scroll", $"{p.ScrollX:F1}, {p.ScrollY:F1}"); y += 18;
            y += 8;
            fontSm.DrawText(sb, "Drag to move | +/- scale", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "PgUp/Dn zIndex | Del remove", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
        else if (_selectedChar >= 0 && _selectedChar < _chars.Count)
        {
            var ch = _chars[_selectedChar];
            fontSm.DrawText(sb, $"CHARACTER: {ch.Name.ToUpper()}", new Vector2(rx + 8, y), EditorUI.Accent); y += 16;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{ch.X:F1}, {ch.Y:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "CamOffset", $"{ch.CamX:F1}, {ch.CamY:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Z-Index", $"{ch.ZIndex}"); y += 18;
            y += 8;
            fontSm.DrawText(sb, "Drag to move position", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "Shift+Drag = camera offset", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "PgUp/Dn = adjust zIndex", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
        else
        {
            fontSm.DrawText(sb, "Click a prop or character", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "in the viewport to select", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "and edit it.", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
    }
}