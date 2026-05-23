using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FontStashSharp;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame;

public class LoadingScene : Scene
{
    private int _frameCount;
    private string _error;
    private Texture2D _pixel;
    private FontSystem _fontSystem;
    private readonly Dictionary<int, SpriteFontBase> _fontCache = new();
    private string _fontPath;
    private bool _fontLoaded;

    // Content download state
    private ContentDownloader _downloader;
    private bool _downloadPhase;
    private bool _downloadStarted;

    public override void Load()
    {
        _frameCount = 0;
        _pixel = new Texture2D(XboxGame.Instance.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _fontSystem = new FontSystem();
        _fontPath = ResolveDownloadFontPath();
        if (!string.IsNullOrWhiteSpace(_fontPath) && File.Exists(_fontPath))
        {
            _fontSystem.AddFont(File.ReadAllBytes(_fontPath));
            _fontLoaded = true;
        }

        // Check if content needs downloading from GitHub
        _downloader = new ContentDownloader();
        _downloadPhase = _downloader.NeedsDownload();
    }

    public override void Unload()
    {
        _pixel?.Dispose();
        _pixel = null;
        _fontCache.Clear();
        _fontSystem = null;
        _fontLoaded = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (_error != null) return;

        // Phase 1: Download content from GitHub if needed
        if (_downloadPhase)
        {
            if (!_downloadStarted)
            {
                _downloadStarted = true;
                Task.Run(() => _downloader.DownloadContentAsync());
            }

            if (_downloader.HasError)
            {
                _error = _downloader.ErrorMessage;
                return;
            }

            if (_downloader.IsComplete)
            {
                _downloadPhase = false;
                _frameCount = 0; // Reset for init phase
            }
            return;
        }

        // Phase 2: Initialize game managers (spread across frames to avoid timeout)
        _frameCount++;
        if (_frameCount <= 2) return;

        try
        {
            switch (_frameCount)
            {
                case 3:
                    var err = XboxGame.Instance.InitializeManagers();
                    if (err != null) { _error = err; return; }
                    break;

                case 4:
                    Assets.LoadFont("fonts/vcr.ttf");
                    break;

                case 5:
                    try
                    {
                        var saveData = HighscoreManager.Data;
                        XboxGame.Instance.Audio.MusicVolume = saveData.MusicVolume;
                        XboxGame.Instance.Audio.SfxVolume = saveData.SfxVolume;
                        XboxGame.Instance.Input.LoadBindings();
                    }
                    catch { }
                    break;

                default:
                    Game.Scenes.ChangeScene(new TitleScene());
                    break;
            }
        }
        catch (Exception ex)
        {
            _error = $"Frame {_frameCount}: {ex.GetType().Name}\n{ex.Message}\n{ex.StackTrace}";
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var px = Assets?.Pixel ?? _pixel;
        if (px == null) return;

        spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

        var viewport = XboxGame.Instance.GraphicsDevice.Viewport;
        int screenW = viewport.Width;
        int screenH = viewport.Height;

        // Dark background (FNF-style)
        spriteBatch.Draw(px,
            new Rectangle(0, 0, screenW, screenH),
            Color.Black);

        // Download progress UI
        if (_downloadPhase && _downloader != null && _error == null)
        {
            DrawDownloadProgress(spriteBatch, px, screenW, screenH);
        }
        else if (_error != null)
        {
            DrawError(spriteBatch);
        }

        spriteBatch.End();
    }

    private void DrawDownloadProgress(SpriteBatch spriteBatch, Texture2D px, int screenW, int screenH)
    {
        int barPadding = 20;
        int barHeight = 12;
        int pieceCount = 16;
        int pieceGap = 8;
        int lineHeight = 30;

        float progress = _downloader.Progress;
        Color green = new Color(120, 200, 40);
        Color greenDim = new Color(60, 100, 20);
        Color greenDark = new Color(30, 60, 12);

        int lineY = (int)(screenH * 0.67f);
        int barW = screenW - barPadding * 2;
        int barY = lineY + 8;

        // Progress line box (outline)
        spriteBatch.Draw(px, new Rectangle(-2, lineY, screenW + 4, 2), green);
        spriteBatch.Draw(px, new Rectangle(-2, lineY + lineHeight, screenW + 4, 2), green);
        spriteBatch.Draw(px, new Rectangle(-2, lineY, 2, lineHeight + 2), green);
        spriteBatch.Draw(px, new Rectangle(screenW, lineY, 2, lineHeight + 2), green);

        // Segmented bar
        float pieceWidth = (barW / (float)pieceCount) - pieceGap;
        int maxFill = (int)(barW * progress);
        for (int i = 0; i < pieceCount; i++)
        {
            int pieceX = (int)(i * (pieceWidth + pieceGap));
            if (pieceX + pieceWidth > maxFill) break;
            spriteBatch.Draw(px, new Rectangle(pieceX, barY, (int)pieceWidth, barHeight), green);
        }

        // Left status label
        int leftSize = 32;
        string leftText = "DOWNLOADING ASSETS...";
        Vector2 leftMeasure = MeasureDownloadText(leftText, leftSize);
        int leftY = (int)(lineY - leftMeasure.Y * 3.0f);
        DrawDownloadText(spriteBatch, leftText, barPadding, leftY, green, leftSize);

        // Percent bottom-right
        string pctText = $"{(int)(progress * 100)}%";
        int pctSize = 16;
        Vector2 pctMeasure = MeasureDownloadText(pctText, pctSize);
        int pctX = screenW - barPadding - (int)pctMeasure.X;
        int pctY = screenH - barPadding - barHeight - pctSize - 4;
        DrawDownloadText(spriteBatch, pctText, pctX, pctY, green, pctSize);

        // Right-side labels and boxes
        int rightX = (int)(screenW * 0.64f);
        int groupY = leftY;
        DrawDownloadText(spriteBatch, "NATURAL  STEREO", rightX + 40, groupY, green, 16);
        DrawDownloadText(spriteBatch, "ENHANCED", rightX - 40, groupY + 40, greenDim, 16);

        spriteBatch.Draw(px, new Rectangle(rightX, groupY + 40, 128, 20), greenDark);
        spriteBatch.Draw(px, new Rectangle(rightX, groupY + 40, 64, 20), green);
        spriteBatch.Draw(px, new Rectangle(rightX + 70, groupY + 40, 58, 20), green);
        DrawDownloadText(spriteBatch, "DSP", rightX + 10, groupY + 42, Color.Black, 16);
        DrawDownloadText(spriteBatch, "FNF", rightX + 78, groupY + 42, Color.Black, 16);

        // Status line
        string status = _downloader.Status ?? "";
        DrawDownloadText(spriteBatch, status, barPadding, lineY + 26, greenDim, 14);
    }

    private void DrawDownloadText(SpriteBatch spriteBatch, string text, int x, int y, Color color, int size)
    {
        var font = GetDownloadFont(size);
        if (font != null)
        {
            font.DrawText(spriteBatch, text, new Vector2(x, y), color);
        }
        else
        {
            DrawBlockText(spriteBatch, _pixel, text.ToUpperInvariant(), x, y, color, Math.Max(2, size / 8));
        }
    }

    private SpriteFontBase GetDownloadFont(int size)
    {
        if (_fontSystem == null || !_fontLoaded) return null;
        if (_fontCache.TryGetValue(size, out var cached)) return cached;
        var font = _fontSystem.GetFont(size);
        _fontCache[size] = font;
        return font;
    }

    private Vector2 MeasureDownloadText(string text, int size)
    {
        var font = GetDownloadFont(size);
        if (font != null)
        {
            return font.MeasureString(text);
        }

        int scale = Math.Max(2, size / 8);
        int charSpacing = scale * 4;
        int spaceWidth = scale * 3;
        int width = 0;
        int height = scale * 5;

        foreach (char c in text)
        {
            width += (c == ' ') ? spaceWidth : charSpacing;
        }

        return new Vector2(width, height);
    }

    private static string ResolveDownloadFontPath()
    {
        string[] candidates = { "DS-DIGIT.TTF", "DS-DIGI.TTF", "DS-DIGIB.TTF", "DS-DIGII.TTF" };

#if XBOX_UWP
        string localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        string localContent = Path.Combine(localState, "Content", "fonts");
        foreach (var name in candidates)
        {
            string localFont = Path.Combine(localContent, name);
            if (File.Exists(localFont)) return localFont;
        }

        string installedContent = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content", "fonts");
        foreach (var name in candidates)
        {
            string installedFont = Path.Combine(installedContent, name);
            if (File.Exists(installedFont)) return installedFont;
        }
#else
        string contentRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "fonts");
        foreach (var name in candidates)
        {
            string font = Path.Combine(contentRoot, name);
            if (File.Exists(font)) return font;
        }
#endif

        return null;
    }

    /// <summary>
    /// Draw text using pixel block characters at a given scale.
    /// Scale 3 = easily readable on TV, Scale 5 = large title text.
    /// </summary>
    private static void DrawBlockText(SpriteBatch spriteBatch, Texture2D px, string text, int x, int y, Color color, int scale = 4)
    {
        int charSpacing = scale * 4; // space between characters
        int spaceWidth = scale * 3;  // width of a space character
        int cx = x;
        foreach (char c in text)
        {
            if (c == ' ') { cx += spaceWidth; continue; }
            var pattern = GetCharPattern(c);
            if (pattern != null)
            {
                for (int row = 0; row < pattern.Length; row++)
                {
                    for (int col = 0; col < pattern[row].Length; col++)
                    {
                        if (pattern[row][col] == '#')
                            spriteBatch.Draw(px, new Rectangle(cx + col * scale, y + row * scale, scale, scale), color);
                    }
                }
            }
            cx += charSpacing;
        }
    }

    /// <summary>
    /// Tiny 5x7 pixel font patterns for basic characters.
    /// Only covers what we need for download status text.
    /// </summary>
    private static string[] GetCharPattern(char c)
    {
        return char.ToUpper(c) switch
        {
            '0' => new[] { "###", "#.#", "#.#", "#.#", "###" },
            '1' => new[] { ".#.", "##.", ".#.", ".#.", "###" },
            '2' => new[] { "###", "..#", "###", "#..", "###" },
            '3' => new[] { "###", "..#", "###", "..#", "###" },
            '4' => new[] { "#.#", "#.#", "###", "..#", "..#" },
            '5' => new[] { "###", "#..", "###", "..#", "###" },
            '6' => new[] { "###", "#..", "###", "#.#", "###" },
            '7' => new[] { "###", "..#", "..#", "..#", "..#" },
            '8' => new[] { "###", "#.#", "###", "#.#", "###" },
            '9' => new[] { "###", "#.#", "###", "..#", "###" },
            '%' => new[] { "#.#", "..#", ".#.", "#..", "#.#" },
            '/' => new[] { "..#", "..#", ".#.", "#..", "#.." },
            '.' => new[] { "...", "...", "...", "...", ".#." },
            'A' => new[] { ".#.", "#.#", "###", "#.#", "#.#" },
            'B' => new[] { "##.", "#.#", "##.", "#.#", "##." },
            'C' => new[] { ".##", "#..", "#..", "#..", ".##" },
            'D' => new[] { "##.", "#.#", "#.#", "#.#", "##." },
            'E' => new[] { "###", "#..", "##.", "#..", "###" },
            'F' => new[] { "###", "#..", "##.", "#..", "#.." },
            'G' => new[] { ".##", "#..", "#.#", "#.#", ".##" },
            'H' => new[] { "#.#", "#.#", "###", "#.#", "#.#" },
            'I' => new[] { "###", ".#.", ".#.", ".#.", "###" },
            'K' => new[] { "#.#", "#.#", "##.", "#.#", "#.#" },
            'L' => new[] { "#..", "#..", "#..", "#..", "###" },
            'M' => new[] { "#.#", "###", "#.#", "#.#", "#.#" },
            'N' => new[] { "#.#", "##.", "###", "#.#", "#.#" },
            'O' => new[] { ".#.", "#.#", "#.#", "#.#", ".#." },
            'P' => new[] { "##.", "#.#", "##.", "#..", "#.." },
            'R' => new[] { "##.", "#.#", "##.", "#.#", "#.#" },
            'S' => new[] { ".##", "#..", ".#.", "..#", "##." },
            'T' => new[] { "###", ".#.", ".#.", ".#.", ".#." },
            'U' => new[] { "#.#", "#.#", "#.#", "#.#", "###" },
            'V' => new[] { "#.#", "#.#", "#.#", "#.#", ".#." },
            'W' => new[] { "#.#", "#.#", "#.#", "###", "#.#" },
            'X' => new[] { "#.#", "#.#", ".#.", "#.#", "#.#" },
            'Y' => new[] { "#.#", "#.#", ".#.", ".#.", ".#." },
            ':' => new[] { "...", ".#.", "...", ".#.", "..." },
            '-' => new[] { "...", "...", "###", "...", "..." },
            _ => null,
        };
    }

    private void DrawError(SpriteBatch spriteBatch)
    {
        var font = Assets?.GetFont(14);
        if (font != null)
        {
            font.DrawText(spriteBatch, "ERROR:", new Vector2(30, 30), Color.Red);
            int y = 55;
            foreach (var line in _error.Split('\n'))
            {
                font.DrawText(spriteBatch, line, new Vector2(30, y), Color.White);
                y += 18;
            }
        }
        else
        {
            // Fonts not loaded yet — use block text for error display
            var px = _pixel;
            if (px == null) return;
            DrawBlockText(spriteBatch, px, "ERROR", 30, 30, Color.Red, 5);
            int ey = 70;
            foreach (var line in _error.Split('\n'))
            {
                            DrawBlockText(spriteBatch, px, line, 30, ey, Color.White, 3);
                                ey += 25;
                            }
                        }
                    }
                }
