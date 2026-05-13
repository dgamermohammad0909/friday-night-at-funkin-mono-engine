using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Audio;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

/// <summary>
/// Friday Night Funkin' - MonoGame Port
/// Main game class that handles game loop and scene management
/// </summary>
public class FNFGame : Game
{
    public static FNFGame Instance { get; private set; }
    
    public GraphicsDeviceManager Graphics { get; private set; }
    public SpriteBatch SpriteBatch { get; private set; }
    public AssetManager Assets { get; private set; }
    public InputManager Input { get; private set; }
    public AudioManager Audio { get; private set; }
    public SceneManager Scenes { get; private set; }
    
    // Screen settings
    public const int SCREEN_WIDTH = 1280;
    public const int SCREEN_HEIGHT = 720;
    
    // FPS counter
    private int _frameCount;
    private float _fpsTimer;
    public int CurrentFPS { get; private set; } = 60;
    private string _fpsText = "60 FPS";
    
    // Fullscreen toggle state
    private KeyboardState _prevKbState;
    private bool _isFullscreen;
    private bool _isBorderless;
    
    // Render target for resolution-independent rendering
    // All scenes draw at SCREEN_WIDTH x SCREEN_HEIGHT, then this is scaled to the back buffer
    private RenderTarget2D _renderTarget;

    // True only when the window auto-paused audio because it lost focus. Prevents Activated
    // from resuming music that was paused by the user via the in-game pause menu.
    private bool _focusAutoPaused;


    /// <summary>
    /// Get the destination rectangle for drawing the render target onto the back buffer,
    /// maintaining aspect ratio with letterboxing/pillarboxing.
    /// </summary>
    public Rectangle GetScaledDestination()
    {
        int bufW = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int bufH = GraphicsDevice.PresentationParameters.BackBufferHeight;
        float scaleX = (float)bufW / SCREEN_WIDTH;
        float scaleY = (float)bufH / SCREEN_HEIGHT;
        float scale = Math.Min(scaleX, scaleY);
        int w = (int)(SCREEN_WIDTH * scale);
        int h = (int)(SCREEN_HEIGHT * scale);
        int x = (bufW - w) / 2;
        int y = (bufH - h) / 2;
        return new Rectangle(x, y, w, h);
    }
    
    public FNFGame()
    {
        Instance = this;
        Graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        
        Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
        Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
        Graphics.SynchronizeWithVerticalRetrace = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0); // 60 FPS
    }

    protected override void Initialize()
    {
        Window.Title = "Friday Night Funkin' - MonoGame Edition";
        
        // Initialize managers
        Input = new InputManager();
        Audio = new AudioManager();
        Scenes = new SceneManager();
        
        // Auto-pause on focus loss. Only resume on Activated if WE were the ones who auto-paused
        // due to focus loss � otherwise clicking back on the window un-pauses music that the user
        // deliberately paused via the in-game pause menu.
        Activated += (s, e) =>
        {
            if (_focusAutoPaused && Audio != null)
            {
                _focusAutoPaused = false;
                Audio.ResumeMusic();
            }
        };
        Deactivated += (s, e) =>
        {
            if (Engine.HighscoreManager.Data.AutoPause && Audio != null && Audio.MusicPlaying)
            {
                _focusAutoPaused = true;
                Audio.PauseMusic();
            }
        };
        
        base.Initialize();
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(GraphicsDevice, SCREEN_WIDTH, SCREEN_HEIGHT);
        Assets = new AssetManager(this);

        // Note: This project copies raw files from `Content/` and does not use an MGCB pipeline.
        // MonoGame effects require precompiled `.mgfxo` built via the content pipeline.
        // If/when MGCB is introduced, we can load and apply the Freeplay BlueFade effect here.
        
        // Share funkin.assets search roots with AudioManager
        // AssetManager auto-detects the funkin.assets directory on construction
        var funkinPath = AssetManager.FindFunkinAssets();
        if (funkinPath != null)
            Audio.ExtraContentRoots.Add(funkinPath);
        
        // Load common assets
        Assets.LoadCommonAssets();
        
        // Load saved preferences (volume, character selection, etc.)
        var saveData = Engine.HighscoreManager.Data;
        Audio.MusicVolume = saveData.MusicVolume;
        Audio.SfxVolume = saveData.SfxVolume;
        
        // Load custom key bindings
        Input.LoadBindings();

        Scenes.ChangeScene(new Scenes.TitleScene());
    }
    
    protected override void Update(GameTime gameTime)
    {
        // Update input
        Input.Update();
        
        // Process deferred audio callbacks (intro?loop chaining, fade-outs) on main thread
        Audio.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        
        // Fullscreen toggle: F11 = exclusive fullscreen, Alt+Enter = borderless windowed fullscreen
        var kbState = Keyboard.GetState();
        bool f11Pressed = kbState.IsKeyDown(Keys.F11) && !_prevKbState.IsKeyDown(Keys.F11);
        bool altEnterPressed = (kbState.IsKeyDown(Keys.LeftAlt) || kbState.IsKeyDown(Keys.RightAlt))
            && kbState.IsKeyDown(Keys.Enter) && !_prevKbState.IsKeyDown(Keys.Enter);
        
        if (f11Pressed)
            ToggleFullscreen(false); // Exclusive fullscreen
        else if (altEnterPressed)
            ToggleFullscreen(true); // Borderless windowed fullscreen

        // F5 = open Editor Hub from any scene (check BEFORE storing prevKbState)
        if (kbState.IsKeyDown(Keys.F5) && !_prevKbState.IsKeyDown(Keys.F5))
        {
            if (Scenes.CurrentScene is not EditorHubScene)
                Scenes.ChangeScene(new EditorHubScene(Scenes.CurrentScene));
        }

        _prevKbState = kbState;

        // FPS counter
        _frameCount++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 1.0f)
        {
            CurrentFPS = _frameCount;
            _fpsText = $"{CurrentFPS} FPS";
            _frameCount = 0;
            _fpsTimer -= 1.0f;
        }

        // Debug overlay (F3 toggle)
        Engine.DebugOverlay.Update();

        // Update current scene (each scene handles its own input)
        Scenes.Update(gameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Render all scene content to the fixed-resolution render target
        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(Color.Black);
        Scenes.Draw(SpriteBatch);

        // Switch back to the real back buffer and draw the render target scaled
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        var dest = GetScaledDestination();

        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        SpriteBatch.Draw(_renderTarget, dest, Color.White);
        SpriteBatch.End();
        
        // FPS counter overlay (drawn directly on back buffer so it's always crisp)
        if (Engine.HighscoreManager.Data.FPSCounter && Assets != null)
        {
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
            var fpsFont = Assets.GetFont(16);
            if (fpsFont != null)
                fpsFont.DrawText(SpriteBatch, _fpsText,
                    new Vector2(dest.Right - 90, dest.Top + 4), Color.White);
            SpriteBatch.End();
        }

        // Debug overlay (F3) — drawn on top of everything
        if (Engine.DebugOverlay.Visible && Assets != null)
        {
            SpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            string sceneName = Scenes.CurrentScene?.GetType().Name ?? "(none)";
            Engine.DebugOverlay.Draw(SpriteBatch, Assets, sceneName);
            SpriteBatch.End();
        }
        
        base.Draw(gameTime);
    }

    /// <summary>
    /// Toggle between windowed and fullscreen modes.
    /// F11 = exclusive fullscreen (changes resolution to native).
    /// Alt+Enter = borderless windowed fullscreen (keeps desktop resolution).
    /// </summary>
    public void ToggleFullscreen(bool borderless)
    {
        if (borderless)
        {
            // Borderless windowed fullscreen
            if (_isBorderless)
            {
                // Return to windowed
                _isBorderless = false;
                _isFullscreen = false;
                Graphics.IsFullScreen = false;
                Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
                Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
                Window.IsBorderless = false;
                Graphics.ApplyChanges();
            }
            else
            {
                // Go borderless fullscreen at desktop resolution
                _isBorderless = true;
                _isFullscreen = false;
                Graphics.IsFullScreen = false;
                Graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                Graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
                Window.IsBorderless = true;
                Graphics.ApplyChanges();
            }
        }
        else
        {
            // Exclusive fullscreen
            if (_isFullscreen)
            {
                // Return to windowed
                _isFullscreen = false;
                _isBorderless = false;
                Graphics.IsFullScreen = false;
                Graphics.PreferredBackBufferWidth = SCREEN_WIDTH;
                Graphics.PreferredBackBufferHeight = SCREEN_HEIGHT;
                Window.IsBorderless = false;
                Graphics.ApplyChanges();
            }
            else
            {
                _isFullscreen = true;
                _isBorderless = false;
                Window.IsBorderless = false;
                Graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                Graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
                Graphics.IsFullScreen = true;
                Graphics.ApplyChanges();
            }
        }
        
        Console.WriteLine($"Display mode: {((_isFullscreen) ? "Fullscreen" : (_isBorderless) ? "Borderless Fullscreen" : "Windowed")} " +
            $"({Graphics.PreferredBackBufferWidth}x{Graphics.PreferredBackBufferHeight})");
    }

    protected override void UnloadContent()
    {
        Scenes.CurrentScene?.Unload();
        _renderTarget?.Dispose();
        _renderTarget = null;
        AlphabetFont.DisposeAll();
        Assets?.Dispose();
        Audio?.Dispose();
        base.UnloadContent();
    }

}
