using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Windows.Graphics.Display;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

/// <summary>
/// Xbox/UWP version of FNFGame.
/// Uses SwapChainPanel for rendering and XboxAudioManager for audio.
/// Initialized via XamlGame&lt;XboxGame&gt;.Create() in GamePage.
/// </summary>
public class XboxGame : Game
{
    public static XboxGame Instance { get; private set; }

    public GraphicsDeviceManager Graphics { get; private set; }
    public SpriteBatch SpriteBatch { get; private set; }
    public AssetManager Assets { get; private set; }
    public InputManager Input { get; private set; }
    public XboxAudioManager Audio { get; private set; }
    public SceneManager Scenes { get; private set; }

    public const int SCREEN_WIDTH = 1280;
    public const int SCREEN_HEIGHT = 720;

    private int _frameCount;
    private float _fpsTimer;
    public int FPS { get; private set; }
    public int CurrentFPS => FPS;

    public XboxGame()
    {
        Instance = this;
        Graphics = new GraphicsDeviceManager(this);
        Graphics.IsFullScreen = true;
        Graphics.PreferredBackBufferWidth = 1920;
        Graphics.PreferredBackBufferHeight = 1080;
        Graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
    }

    // Render target for resolution-independent rendering
    // All scenes draw at SCREEN_WIDTH x SCREEN_HEIGHT, then scaled to back buffer
    private RenderTarget2D _renderTarget;

    protected override void Initialize()
    {
        // Ensure the mouse cursor stays hidden (belt and suspenders with GamePage)
        try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = null; }
        catch { }

        // Auto-adjust back buffer to the current display resolution.
        try
        {
            var display = DisplayInformation.GetForCurrentView();
            int width = Convert.ToInt32(display.ScreenWidthInRawPixels);
            int height = Convert.ToInt32(display.ScreenHeightInRawPixels);
            if (width > 0 && height > 0)
            {
                Graphics.PreferredBackBufferWidth = width;
                Graphics.PreferredBackBufferHeight = height;
            }
        }
        catch { }

        // Apply back buffer settings once after the SwapChainPanel is ready.
        try { Graphics.ApplyChanges(); } catch { }
        base.Initialize();
    }

    /// <summary>
    /// Get the destination rectangle for scaling the render target to the back buffer
    /// with correct aspect ratio (letterbox/pillarbox).
    /// </summary>
    public Rectangle GetScaledDestination()
    {
        var viewport = GraphicsDevice.Viewport;
        var safeArea = viewport.TitleSafeArea;

        int viewW = Math.Max(1, viewport.Width);
        int viewH = Math.Max(1, viewport.Height);

        float scale = Math.Min(viewW / (float)SCREEN_WIDTH, viewH / (float)SCREEN_HEIGHT);
        int destW = (int)(SCREEN_WIDTH * scale);
        int destH = (int)(SCREEN_HEIGHT * scale);
        int destX = (viewW - destW) / 2;
        int destY = (viewH - destH) / 2;
        var dest = new Rectangle(destX, destY, destW, destH);

        if (!safeArea.Contains(dest))
        {
            int safeW = Math.Max(1, safeArea.Width);
            int safeH = Math.Max(1, safeArea.Height);
            float safeScale = Math.Min(safeW / (float)SCREEN_WIDTH, safeH / (float)SCREEN_HEIGHT);
            int safeDestW = (int)(SCREEN_WIDTH * safeScale);
            int safeDestH = (int)(SCREEN_HEIGHT * safeScale);
            int safeDestX = safeArea.X + (safeW - safeDestW) / 2;
            int safeDestY = safeArea.Y + (safeH - safeDestH) / 2;
            return new Rectangle(safeDestX, safeDestY, safeDestW, safeDestH);
        }

        return dest;
    }

    /// <summary>
    /// Error message to display if something goes wrong during startup.
    /// LoadingScene reads this to show errors on screen.
    /// </summary>
    public string StartupError { get; set; }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(GraphicsDevice, SCREEN_WIDTH, SCREEN_HEIGHT);
        Scenes = new SceneManager();
        Scenes.ChangeScene(new LoadingScene());
    }

    public string InitializeManagers()
    {
        try
        {
            if (Assets == null) Assets = new AssetManager(this);
            if (Input == null) Input = new InputManager();
            if (Audio == null) Audio = new XboxAudioManager(Assets);
            return null;
        }
        catch (Exception ex)
        {
            return $"InitManagers: {ex.GetType().Name}\n{ex.Message}\n{ex.StackTrace}";
        }
    }

    protected override void Update(GameTime gameTime)
    {
        Input?.Update();

        _frameCount++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 1f)
        {
            FPS = _frameCount;
            _frameCount = 0;
            _fpsTimer -= 1f;
        }

        Audio?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        Scenes?.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderTarget == null || SpriteBatch == null)
        {
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
            return;
        }

        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(Color.Black);
        Scenes?.Draw(SpriteBatch);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        var dest = GetScaledDestination();
        SpriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        SpriteBatch.Draw(_renderTarget, dest, Color.White);
        SpriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Scenes?.CurrentScene?.Unload();
        AlphabetFont.DisposeAll();
        Assets?.Dispose();
        Audio?.Dispose();
        base.UnloadContent();
    }
}
