using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Handles keyboard and gamepad input with press detection.
/// Full Xbox controller support:
///   Notes: DPad or X(Left)/A(Down)/Y(Up)/B(Right) or LT/LB/RB/RT
///   Confirm: A / Start
///   Back: B / Back
///   Navigate: DPad / Left Stick
///   Pause: Start
/// </summary>
public class InputManager
{
    private KeyboardState _currentKeyboard;
    private KeyboardState _previousKeyboard;
    private GamePadState _currentGamePad;
    private GamePadState _previousGamePad;
    private int _previousScrollValue;
    private int _currentScrollValue;
    
    // Stick deadzone threshold
    private const float STICK_DEADZONE = 0.5f;
    private bool _prevStickUp, _prevStickDown, _prevStickLeft, _prevStickRight;
    
    // Whether a gamepad is connected (used for UI hints)
    public bool GamePadConnected => _currentGamePad.IsConnected;

    // Auto-detect last active input device (switches display between arrows and controller buttons)
    public enum InputDevice { Keyboard, Controller }
    public InputDevice LastDevice { get; private set; } = InputDevice.Keyboard;

    // FNF note inputs (Left, Down, Up, Right)
    public bool[] NotePressed { get; } = new bool[4];
    public bool[] NoteHeld { get; } = new bool[4];
    public bool[] NoteReleased { get; } = new bool[4];
    
    // Key bindings for notes (customizable)
    public Keys[] NoteKeysAlt { get; set; } = { Keys.D, Keys.F, Keys.J, Keys.K };
    public Keys[] NoteKeysArrow { get; set; } = { Keys.Left, Keys.Down, Keys.Up, Keys.Right };
    public Buttons[] NoteButtons { get; set; } = { Buttons.DPadLeft, Buttons.DPadDown, Buttons.DPadUp, Buttons.DPadRight };
    // Xbox face button note layout: X=Left, A=Down, Y=Up, B=Right (matches arrow colors)
    public Buttons[] NoteFaceButtons { get; set; } = { Buttons.X, Buttons.A, Buttons.Y, Buttons.B };
    // Xbox trigger/bumper note layout: LT=Left, LB=Down, RB=Up, RT=Right
    public Buttons[] NoteTriggerButtons { get; set; } = { Buttons.LeftTrigger, Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.RightTrigger };

    // Configurable menu navigation buttons (controller)
    public Buttons ConfirmButton { get; set; } = Buttons.A;
    public Buttons CancelButton { get; set; } = Buttons.B;
    public Buttons PauseButton { get; set; } = Buttons.Start;
    public Buttons SwitchCharButton { get; set; } = Buttons.Y;

    // When true, face buttons (A/B/X/Y) are used for notes instead of menu navigation
    // This is set to true during gameplay and false in menus
    public bool GameplayMode { get; set; }
    
    public void Update()
    {
        _previousKeyboard = _currentKeyboard;
        _previousGamePad = _currentGamePad;
        _currentKeyboard = Keyboard.GetState();
        _currentGamePad = GamePad.GetState(PlayerIndex.One);
        _previousScrollValue = _currentScrollValue;
        _currentScrollValue = Mouse.GetState().ScrollWheelValue;
        
        // Track left stick for press detection (digital from analog)
        bool stickUp = _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE;
        bool stickDown = _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE;
        bool stickLeft = _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE;
        bool stickRight = _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE;
        
        // Update note inputs
        for (int i = 0; i < 4; i++)
        {
            bool keyHeld = _currentKeyboard.IsKeyDown(NoteKeysAlt[i]) || 
                          _currentKeyboard.IsKeyDown(NoteKeysArrow[i]) ||
                          _currentGamePad.IsButtonDown(NoteButtons[i]) ||
                          _currentGamePad.IsButtonDown(NoteTriggerButtons[i]);
            
            // Face buttons only count as note inputs during gameplay
            if (GameplayMode)
                keyHeld = keyHeld || _currentGamePad.IsButtonDown(NoteFaceButtons[i]);
            
            bool prevHeld = _previousKeyboard.IsKeyDown(NoteKeysAlt[i]) || 
                           _previousKeyboard.IsKeyDown(NoteKeysArrow[i]) ||
                           _previousGamePad.IsButtonDown(NoteButtons[i]) ||
                           _previousGamePad.IsButtonDown(NoteTriggerButtons[i]);
            
            if (GameplayMode)
                prevHeld = prevHeld || _previousGamePad.IsButtonDown(NoteFaceButtons[i]);
            
            NoteHeld[i] = keyHeld;
            NotePressed[i] = keyHeld && !prevHeld;
            NoteReleased[i] = !keyHeld && prevHeld;
        }
        
        // Auto-detect which input device was used last
        // Check if any keyboard key was pressed this frame
        if (_currentKeyboard.GetPressedKeyCount() > 0 && _previousKeyboard.GetPressedKeyCount() == 0)
            LastDevice = InputDevice.Keyboard;
        else if (_currentKeyboard.GetPressedKeyCount() > _previousKeyboard.GetPressedKeyCount())
            LastDevice = InputDevice.Keyboard;
        // Check if any gamepad button was pressed this frame
        if (_currentGamePad.IsConnected)
        {
            bool anyGpNow = _currentGamePad.IsButtonDown(Buttons.A) || _currentGamePad.IsButtonDown(Buttons.B) ||
                _currentGamePad.IsButtonDown(Buttons.X) || _currentGamePad.IsButtonDown(Buttons.Y) ||
                _currentGamePad.IsButtonDown(Buttons.DPadUp) || _currentGamePad.IsButtonDown(Buttons.DPadDown) ||
                _currentGamePad.IsButtonDown(Buttons.DPadLeft) || _currentGamePad.IsButtonDown(Buttons.DPadRight) ||
                _currentGamePad.IsButtonDown(Buttons.LeftShoulder) || _currentGamePad.IsButtonDown(Buttons.RightShoulder) ||
                _currentGamePad.IsButtonDown(Buttons.LeftTrigger) || _currentGamePad.IsButtonDown(Buttons.RightTrigger) ||
                _currentGamePad.IsButtonDown(Buttons.Start) || _currentGamePad.IsButtonDown(Buttons.Back) ||
                stickUp || stickDown || stickLeft || stickRight;
            bool anyGpPrev = _previousGamePad.IsButtonDown(Buttons.A) || _previousGamePad.IsButtonDown(Buttons.B) ||
                _previousGamePad.IsButtonDown(Buttons.X) || _previousGamePad.IsButtonDown(Buttons.Y) ||
                _previousGamePad.IsButtonDown(Buttons.DPadUp) || _previousGamePad.IsButtonDown(Buttons.DPadDown) ||
                _previousGamePad.IsButtonDown(Buttons.DPadLeft) || _previousGamePad.IsButtonDown(Buttons.DPadRight) ||
                _previousGamePad.IsButtonDown(Buttons.LeftShoulder) || _previousGamePad.IsButtonDown(Buttons.RightShoulder) ||
                _previousGamePad.IsButtonDown(Buttons.LeftTrigger) || _previousGamePad.IsButtonDown(Buttons.RightTrigger) ||
                _previousGamePad.IsButtonDown(Buttons.Start) || _previousGamePad.IsButtonDown(Buttons.Back) ||
                _prevStickUp || _prevStickDown || _prevStickLeft || _prevStickRight;
            if (anyGpNow && !anyGpPrev)
                LastDevice = InputDevice.Controller;
        }

        // Save stick state for next frame
        _prevStickUp = stickUp;
        _prevStickDown = stickDown;
        _prevStickLeft = stickLeft;
        _prevStickRight = stickRight;
    }
    
    public bool IsPressed(Keys key)
    {
        return _currentKeyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    }
    
    public bool IsHeld(Keys key)
    {
        return _currentKeyboard.IsKeyDown(key);
    }
    
    public bool IsReleased(Keys key)
    {
        return !_currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyDown(key);
    }
    
    public bool IsGamePadPressed(Buttons button)
    {
        return _currentGamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button);
    }
    
    public bool IsGamePadHeld(Buttons button)
    {
        return _currentGamePad.IsButtonDown(button);
    }
    
    // Left stick press detection (digital from analog with deadzone)
    private bool StickUpPressed => _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE && !_prevStickUp;
    private bool StickDownPressed => _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE && !_prevStickDown;
    private bool StickLeftPressed => _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE && !_prevStickLeft;
    private bool StickRightPressed => _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE && !_prevStickRight;
    
    /// <summary>
    /// Mouse scroll wheel delta this frame (positive = scroll up, negative = scroll down).
    /// </summary>
    public int ScrollDelta => _currentScrollValue - _previousScrollValue;
    
    // Menu navigation: Keyboard + DPad + Left Stick
    public bool ConfirmPressed => IsPressed(Keys.Enter) || IsPressed(Keys.Space) 
        || (!GameplayMode && IsGamePadPressed(ConfirmButton)) || IsGamePadPressed(Buttons.Start);
    public bool BackPressed => IsPressed(Keys.Escape) || IsPressed(Keys.Back) 
        || (!GameplayMode && IsGamePadPressed(CancelButton)) || IsGamePadPressed(Buttons.Back);
    /// <summary>
    /// Confirm button held (keyboard or controller).
    /// </summary>
    public bool ConfirmHeld => IsHeld(Keys.Enter) || IsHeld(Keys.Space)
        || (!GameplayMode && IsGamePadHeld(ConfirmButton)) || IsGamePadHeld(Buttons.Start);
    /// <summary>
    /// Pause button held (keyboard or controller).
    /// </summary>
    public bool PauseHeld => IsHeld(Keys.LeftShift) || IsHeld(Keys.RightShift)
        || IsGamePadHeld(PauseButton);
    public bool UpPressed => IsPressed(Keys.Up) || IsPressed(Keys.W) 
        || IsGamePadPressed(Buttons.DPadUp) || StickUpPressed;
    public bool DownPressed => IsPressed(Keys.Down) || IsPressed(Keys.S) 
        || IsGamePadPressed(Buttons.DPadDown) || StickDownPressed;
    public bool LeftPressed => IsPressed(Keys.Left) || IsPressed(Keys.A) 
        || IsGamePadPressed(Buttons.DPadLeft) || StickLeftPressed;
    public bool RightPressed => IsPressed(Keys.Right) || IsPressed(Keys.D) 
        || IsGamePadPressed(Buttons.DPadRight) || StickRightPressed;

    // Menu navigation held state (for hold-repeat in menus)
    public bool UpHeld => IsHeld(Keys.Up) || IsHeld(Keys.W)
        || IsGamePadHeld(Buttons.DPadUp) || _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE;
    public bool DownHeld => IsHeld(Keys.Down) || IsHeld(Keys.S)
        || IsGamePadHeld(Buttons.DPadDown) || _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE;
    public bool LeftHeld => IsHeld(Keys.Left) || IsHeld(Keys.A)
        || IsGamePadHeld(Buttons.DPadLeft) || _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE;
    public bool RightHeld => IsHeld(Keys.Right) || IsHeld(Keys.D)
        || IsGamePadHeld(Buttons.DPadRight) || _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE;

    // Switch character (Freeplay -> Character Select). Tab on keyboard, configurable button on controller.
    public bool SwitchCharPressed => IsPressed(Keys.Tab)
        || (!GameplayMode && IsGamePadPressed(SwitchCharButton));

    /// <summary>
    /// Returns the first key that was just pressed this frame, or null if none.
    /// Used for key rebinding capture.
    /// </summary>
    public Keys? GetAnyKeyPressed()
    {
        var pressed = _currentKeyboard.GetPressedKeys();
        foreach (var key in pressed)
        {
            if (!_previousKeyboard.IsKeyDown(key)
                && key != Keys.None && key != Keys.Escape)
                return key;
        }
        return null;
    }

    /// <summary>
    /// Returns the first gamepad button that was just pressed this frame, or null if none.
    /// Used for controller rebinding capture. Excludes Start/Back (reserved for navigation).
    /// </summary>
    public Buttons? GetAnyButtonPressed()
    {
        Buttons[] candidates = {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
            Buttons.LeftShoulder, Buttons.RightShoulder,
            Buttons.LeftTrigger, Buttons.RightTrigger,
            Buttons.LeftStick, Buttons.RightStick
        };
        foreach (var btn in candidates)
        {
            if (_currentGamePad.IsButtonDown(btn) && !_previousGamePad.IsButtonDown(btn))
                return btn;
        }
        return null;
    }
    
    /// <summary>
    /// Load key bindings from save data.
    /// </summary>
    public void LoadBindings()
    {
        var data = HighscoreManager.Data;
        if (data.NoteKeysAlt != null && data.NoteKeysAlt.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Keys>(data.NoteKeysAlt[i], out var k))
                    NoteKeysAlt[i] = k;
            }
        }
        if (data.NoteKeysArrow != null && data.NoteKeysArrow.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Keys>(data.NoteKeysArrow[i], out var k))
                    NoteKeysArrow[i] = k;
            }
        }
        if (data.NoteGamepadDPad != null && data.NoteGamepadDPad.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadDPad[i], out var b))
                    NoteButtons[i] = b;
            }
        }
        if (data.NoteGamepadFace != null && data.NoteGamepadFace.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadFace[i], out var b))
                    NoteFaceButtons[i] = b;
            }
        }
        if (data.NoteGamepadTrigger != null && data.NoteGamepadTrigger.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadTrigger[i], out var b))
                    NoteTriggerButtons[i] = b;
            }
        }
        if (!string.IsNullOrEmpty(data.ConfirmGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.ConfirmGamepadButton, out var cb))
            ConfirmButton = cb;
        if (!string.IsNullOrEmpty(data.CancelGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.CancelGamepadButton, out var xb))
            CancelButton = xb;
        if (!string.IsNullOrEmpty(data.PauseGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.PauseGamepadButton, out var pb))
            PauseButton = pb;
        if (!string.IsNullOrEmpty(data.SwitchCharGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.SwitchCharGamepadButton, out var sb))
            SwitchCharButton = sb;
    }
    
    /// <summary>
    /// Save current key bindings to save data.
    /// </summary>
    public void SaveBindings()
    {
        var data = HighscoreManager.Data;
        data.NoteKeysAlt = new string[4];
        data.NoteKeysArrow = new string[4];
        data.NoteGamepadDPad = new string[4];
        data.NoteGamepadFace = new string[4];
        data.NoteGamepadTrigger = new string[4];
        for (int i = 0; i < 4; i++)
        {
            data.NoteKeysAlt[i] = NoteKeysAlt[i].ToString();
            data.NoteKeysArrow[i] = NoteKeysArrow[i].ToString();
            data.NoteGamepadDPad[i] = NoteButtons[i].ToString();
            data.NoteGamepadFace[i] = NoteFaceButtons[i].ToString();
            data.NoteGamepadTrigger[i] = NoteTriggerButtons[i].ToString();
        }
        data.ConfirmGamepadButton = ConfirmButton.ToString();
        data.CancelGamepadButton = CancelButton.ToString();
        data.PauseGamepadButton = PauseButton.ToString();
        data.SwitchCharGamepadButton = SwitchCharButton.ToString();
        HighscoreManager.SavePreferences();
    }
}
