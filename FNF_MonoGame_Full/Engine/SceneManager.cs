using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Base class for all game scenes
/// </summary>
public abstract class Scene
{
#if XBOX_UWP
    protected XboxGame Game => XboxGame.Instance;
    protected AssetManager Assets => Game.Assets;
    protected InputManager Input => Game.Input;
    protected XboxAudioManager Audio => Game.Audio;
#else
    protected FNFGame Game => FNFGame.Instance;
    protected AssetManager Assets => Game.Assets;
    protected InputManager Input => Game.Input;
    protected AudioManager Audio => Game.Audio;
#endif
    
    public abstract void Load();
    public abstract void Unload();
    public abstract void Update(GameTime gameTime);
    public abstract void Draw(SpriteBatch spriteBatch);
}

/// <summary>
/// Manages scene transitions and the current active scene.
/// Supports fade-to-black transitions between scenes.
/// </summary>
public class SceneManager
{
    public Scene CurrentScene { get; private set; }
    private Scene _nextScene;
    private bool _transitioning;
    
    // Fade transition
    private float _fadeAlpha;
    private float _fadeDuration = 0.3f;
    private bool _fadingOut;
    private bool _fadingIn;
    
    public void ChangeScene(Scene newScene)
    {
        // If called during Unload (transitioning phase), override the pending scene
        if (_transitioning)
        {
            _nextScene = newScene;
            return;
        }
        
        // Guard: ignore if already fading out to a scene
        if (_fadingOut || _nextScene != null)
            return;
            
        _nextScene = newScene;
        if (CurrentScene != null)
        {
            _fadingOut = true;
            _fadeAlpha = 0f;
        }
        else
        {
            _transitioning = true;
        }
    }
    
    public void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        // Fade out current scene
        if (_fadingOut)
        {
            _fadeAlpha += dt / _fadeDuration;
            if (_fadeAlpha >= 1f)
            {
                _fadeAlpha = 1f;
                _fadingOut = false;
                _transitioning = true;
            }
        }
        
        // Handle scene swap at peak of fade
        if (_transitioning && _nextScene != null)
        {
            CurrentScene?.Unload();
            CurrentScene = _nextScene;
            CurrentScene.Load();
            _nextScene = null;
            _transitioning = false;
            _fadingIn = true;
        }
        
        // Fade in new scene
        if (_fadingIn)
        {
            _fadeAlpha -= dt / _fadeDuration;
            if (_fadeAlpha <= 0f)
            {
                _fadeAlpha = 0f;
                _fadingIn = false;
            }
        }
        
        try
        {
            CurrentScene?.Update(gameTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Scene Update error: {ex.Message}\n{ex.StackTrace}");
            // Fall back to main menu on unhandled scene error
            try
            {
                CurrentScene?.Unload();
                CurrentScene = new FNF_MonoGame.Scenes.MainMenuScene();
                CurrentScene.Load();
                _nextScene = null;
                _fadingOut = false;
                _fadingIn = false;
                _transitioning = false;
                _fadeAlpha = 0;
            }
            catch { } // Last resort: if main menu also fails, let it crash
        }
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        try
        {
            CurrentScene?.Draw(spriteBatch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Scene Draw error: {ex.Message}");
            // If Draw threw mid-SpriteBatch.Begin/End, reset the batch state
            try { spriteBatch.End(); } catch { }
        }
        
        // Draw fade overlay
        if (_fadeAlpha > 0.001f)
        {
            var gd = FNFGame.Instance.GraphicsDevice;
            var pixel = FNFGame.Instance.Assets?.Pixel;
            if (pixel != null)
            {
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
                spriteBatch.Draw(pixel,
                    new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                    Color.Black * _fadeAlpha);
                spriteBatch.End();
            }
        }
    }
}
