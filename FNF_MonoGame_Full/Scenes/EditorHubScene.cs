using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

public class EditorHubScene : Scene
{
    private struct EditorCard { public string Title, Desc, Icon; public Color IconColor; }
    private readonly EditorCard[] _cards = {
        new() { Title = "Animation Editor",  Desc = "Browse spritesheets, preview animations, inspect frames", Icon = ">", IconColor = new Color(78,201,176) },
        new() { Title = "Chart Editor",      Desc = "View note charts, beat grid, song properties",           Icon = "#", IconColor = new Color(224,108,117) },
        new() { Title = "Stage Editor",      Desc = "Position stage props, characters, camera offsets",       Icon = "=", IconColor = new Color(229,192,123) },
        new() { Title = "CharSelect Editor", Desc = "Tweak character select scene layout and positions",      Icon = "@", IconColor = new Color(74,123,204) },
        new() { Title = "Freeplay Editor",   Desc = "Adjust freeplay capsule layout, DJ, backgrounds",       Icon = "~", IconColor = new Color(198,120,221) },
        new() { Title = "StoryMode Editor",  Desc = "Edit week banner positions, prop layout, track list",    Icon = "S", IconColor = new Color(255,180,86) },
        new() { Title = "Options Editor",    Desc = "Live-tweak game preferences, volumes, key bindings",     Icon = "*", IconColor = new Color(140,170,200) },
        new() { Title = "Composite Debug",   Desc = "Debug AnimateAtlas body parts, transforms, outlines",    Icon = "!", IconColor = new Color(255,100,100) },
    };
    private int _sel;
    private Scene _returnScene;
    public EditorHubScene(Scene returnScene = null) { _returnScene = returnScene; }
    public override void Load() { }
    public override void Unload() { }
    public override void Update(GameTime gameTime)
    {
        EditorUI.UpdateInput();
        if (Input.UpPressed) _sel = (_sel - 1 + _cards.Length + 1) % (_cards.Length + 1);
        if (Input.DownPressed) _sel = (_sel + 1) % (_cards.Length + 1);
        for (int i = 0; i <= _cards.Length; i++)
        {
            var r = CardRect(i);
            if (EditorUI.IsHovered(r)) { _sel = i; if (EditorUI.MouseClicked) { Go(); return; } }
        }
        if (Input.ConfirmPressed) Go();
        if (Input.BackPressed) Game.Scenes.ChangeScene(_returnScene ?? new MainMenuScene());
    }
    private Rectangle CardRect(int i) => new((FNFGame.SCREEN_WIDTH - 540) / 2, 110 + i * 58, 540, 52);
    private void Go()
    {
        Scene t = _sel switch {
            0 => new AnimationEditorScene(), 1 => new ChartEditorScene(), 2 => new StageEditorScene(),
            3 => new CharSelectEditorScene(), 4 => new FreeplayEditorScene(),
            5 => new StoryModeEditorScene(), 6 => new OptionsEditorScene(),
            7 => new CompositeDebugScene(),
            _ => _returnScene ?? new MainMenuScene()
        };
        Game.Scenes.ChangeScene(t);
    }
    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        EditorUI.FillRect(sb, px, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), EditorUI.BgDark);
        var tf = Assets.GetFont(28); var f = Assets.GetFont(16); var fs = Assets.GetFont(12);
        if (f == null) { sb.End(); return; }
        string title = "EDITOR TOOLS";
        float tw = tf?.MeasureString(title).X ?? 200;
        tf?.DrawText(sb, title, new Vector2((FNFGame.SCREEN_WIDTH - tw) / 2, 40), EditorUI.TextPrimary);
        string sub = "F5 from any scene  |  Arrow keys or mouse to select";
        float sw = fs?.MeasureString(sub).X ?? 200;
        fs?.DrawText(sb, sub, new Vector2((FNFGame.SCREEN_WIDTH - sw) / 2, 74), EditorUI.TextDim);
        for (int i = 0; i < _cards.Length; i++)
        {
            var r = CardRect(i); bool sel = (i == _sel);
            EditorUI.FillRect(sb, px, r, sel ? new Color(45,55,75) : EditorUI.BgPanel);
            EditorUI.DrawBorder(sb, px, r, sel ? EditorUI.Accent : EditorUI.Border);
            if (sel) sb.Draw(px, new Rectangle(r.X, r.Y, 4, r.Height), _cards[i].IconColor);
            var ic = Assets.GetFont(22);
            ic?.DrawText(sb, _cards[i].Icon, new Vector2(r.X + 16, r.Y + 13), _cards[i].IconColor);
            f.DrawText(sb, _cards[i].Title, new Vector2(r.X + 50, r.Y + 8), sel ? Color.White : EditorUI.TextPrimary);
            fs?.DrawText(sb, _cards[i].Desc, new Vector2(r.X + 50, r.Y + 30), EditorUI.TextSecondary);
        }
        var br = CardRect(_cards.Length); bool bs = (_sel == _cards.Length);
        EditorUI.FillRect(sb, px, br, bs ? new Color(60,40,40) : EditorUI.BgPanel);
        EditorUI.DrawBorder(sb, px, br, bs ? EditorUI.Error : EditorUI.Border);
        string bt = "<- Back to Game"; float bw = f.MeasureString(bt).X;
        f.DrawText(sb, bt, new Vector2(br.X + (br.Width - bw) / 2, br.Y + 16), bs ? EditorUI.Error : EditorUI.TextSecondary);
        EditorUI.DrawStatusBar(sb, px, fs, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "F5 = toggle editors", "Esc = back", $"FPS: {Game.CurrentFPS}");
        sb.End();
    }
}
