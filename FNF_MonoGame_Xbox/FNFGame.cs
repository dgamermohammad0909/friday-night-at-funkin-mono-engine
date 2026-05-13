using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame_Xbox;

/// <summary>
/// Friday Night Funkin' Xbox Test - MonoGame Version
/// This is a simple test to verify Xbox deployment works via NativeAOT
/// </summary>
public class FNFGame : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private SpriteFont _font;
    private Texture2D _pixel;
    
    // Simple game state
    private int _score = 0;
    private float _noteTimer = 0;
    private float _noteSpeed = 2f;
    private List<Note> _notes = new();
    private Random _random = new();
    
    // Arrow positions (like FNF)
    private readonly int[] _arrowX = { 200, 300, 400, 500 };
    private readonly Color[] _arrowColors = { Color.Purple, Color.Cyan, Color.Green, Color.Red };
    private readonly Keys[] _arrowKeys = { Keys.Left, Keys.Down, Keys.Up, Keys.Right };
    
    // Controller support
    private GamePadState _previousGamePadState;
    private KeyboardState _previousKeyboardState;

    public FNFGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 720;
    }

    protected override void Initialize()
    {
        Window.Title = "FNF Xbox Test - MonoGame";
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        
        // Create a simple pixel texture for drawing
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        
        // Note: In a real game, you'd load a SpriteFont here
        // _font = Content.Load<SpriteFont>("GameFont");
    }

    protected override void Update(GameTime gameTime)
    {
        var gamePadState = GamePad.GetState(PlayerIndex.One);
        var keyboardState = Keyboard.GetState();
        
        // Exit on Back button or Escape
        if (gamePadState.Buttons.Back == ButtonState.Pressed || keyboardState.IsKeyDown(Keys.Escape))
            Exit();

        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        // Spawn notes periodically
        _noteTimer += deltaTime;
        if (_noteTimer >= 0.8f)
        {
            _noteTimer = 0;
            int lane = _random.Next(4);
            _notes.Add(new Note { Lane = lane, Y = -50 });
        }
        
        // Update notes
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            _notes[i].Y += _noteSpeed * deltaTime * 300;
            
            // Remove notes that go off screen
            if (_notes[i].Y > 800)
            {
                _notes.RemoveAt(i);
            }
        }
        
        // Check for input (keyboard)
        for (int i = 0; i < 4; i++)
        {
            if (IsKeyPressed(keyboardState, _arrowKeys[i]))
            {
                CheckNoteHit(i);
            }
        }
        
        // Check for input (gamepad D-pad)
        if (IsButtonPressed(gamePadState, Buttons.DPadLeft)) CheckNoteHit(0);
        if (IsButtonPressed(gamePadState, Buttons.DPadDown)) CheckNoteHit(1);
        if (IsButtonPressed(gamePadState, Buttons.DPadUp)) CheckNoteHit(2);
        if (IsButtonPressed(gamePadState, Buttons.DPadRight)) CheckNoteHit(3);
        
        _previousGamePadState = gamePadState;
        _previousKeyboardState = keyboardState;

        base.Update(gameTime);
    }

    private bool IsKeyPressed(KeyboardState current, Keys key)
    {
        return current.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
    }
    
    private bool IsButtonPressed(GamePadState current, Buttons button)
    {
        return current.IsButtonDown(button) && !_previousGamePadState.IsButtonDown(button);
    }
    
    private void CheckNoteHit(int lane)
    {
        // Find closest note in this lane near the hit zone (Y = 600)
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            if (_notes[i].Lane == lane && Math.Abs(_notes[i].Y - 600) < 50)
            {
                _score += 100;
                _notes.RemoveAt(i);
                return;
            }
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin();
        
        // Draw hit zone line
        DrawRect(0, 590, 1280, 4, Color.Gray);
        
        // Draw arrow receptors
        for (int i = 0; i < 4; i++)
        {
            DrawRect(_arrowX[i] - 25, 575, 50, 50, _arrowColors[i] * 0.3f);
        }
        
        // Draw notes
        foreach (var note in _notes)
        {
            DrawRect(_arrowX[note.Lane] - 25, (int)note.Y, 50, 30, _arrowColors[note.Lane]);
        }
        
        // Draw score (using simple rectangles since we don't have a font)
        // In a real game, you'd draw: _spriteBatch.DrawString(_font, $"Score: {_score}", ...);
        DrawRect(10, 10, 20 + _score / 10, 30, Color.Yellow); // Score bar
        
        // Draw instructions
        DrawRect(550, 680, 180, 30, Color.DarkGray); // Background for "XBOX TEST"
        
        _spriteBatch.End();

        base.Draw(gameTime);
    }
    
    private void DrawRect(int x, int y, int width, int height, Color color)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, width, height), color);
    }

    protected override void UnloadContent()
    {
        _pixel?.Dispose();
        base.UnloadContent();
    }
}

public class Note
{
    public int Lane { get; set; }
    public float Y { get; set; }
}
