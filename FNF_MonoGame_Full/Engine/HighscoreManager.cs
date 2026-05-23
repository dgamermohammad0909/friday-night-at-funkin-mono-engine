using Newtonsoft.Json;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Persists highscores and song completion data to disk.
/// Matches the original FNF Highscore.hx save structure.
/// </summary>
public static class HighscoreManager
{
    private static readonly string SavePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "save_data.json");

    private static SaveData _data;

    /// <summary>
    /// Score entry for a song+difficulty combination.
    /// </summary>
    public class ScoreEntry
    {
        public int Score { get; set; }
        public int MaxCombo { get; set; }
        public float ClearPercent { get; set; }
        public string Rank { get; set; }
    }

    public class SaveData
    {
        public Dictionary<string, ScoreEntry> SongScores { get; set; } = new();
        public Dictionary<string, ScoreEntry> WeekScores { get; set; } = new();
        public float MusicVolume { get; set; } = 1f;
        public float SfxVolume { get; set; } = 1f;
        public bool Downscroll { get; set; }
        public bool GhostTapping { get; set; } = true;
        public bool FlashingLights { get; set; } = true;
        public bool CameraZoom { get; set; } = true;
        public bool Naughtyness { get; set; } = true;
        public bool Middlescroll { get; set; }
        public bool FPSCounter { get; set; }
        public bool AutoPause { get; set; } = true;
        public int GlobalOffset { get; set; }
        public string SelectedCharacter { get; set; } = "bf";
        
        // Key bindings (stored as key name strings for serialization)
        public string[] NoteKeysAlt { get; set; } = { "D", "F", "J", "K" };
        public string[] NoteKeysArrow { get; set; } = { "Left", "Down", "Up", "Right" };

        // Controller bindings (stored as Buttons enum name strings)
        public string[] NoteGamepadDPad { get; set; } = { "DPadLeft", "DPadDown", "DPadUp", "DPadRight" };
        public string[] NoteGamepadFace { get; set; } = { "X", "A", "Y", "B" };
        public string[] NoteGamepadTrigger { get; set; } = { "LeftTrigger", "LeftShoulder", "RightShoulder", "RightTrigger" };

        // Controller menu navigation buttons
        public string ConfirmGamepadButton { get; set; } = "A";
        public string CancelGamepadButton { get; set; } = "B";
        public string PauseGamepadButton { get; set; } = "Start";
        public string SwitchCharGamepadButton { get; set; } = "Y";

        // Freeplay favorites
        public HashSet<string> FavoriteSongs { get; set; } = new();
    }

    public static SaveData Data
    {
        get
        {
            _data ??= LoadFromDisk();
            return _data;
        }
    }

    private static string MakeKey(string songName, string difficulty)
        => $"{songName.ToLowerInvariant()}_{difficulty.ToLowerInvariant()}";

    public static int GetScore(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.Score : 0;
    }

    public static float GetClearPercent(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.ClearPercent : 0f;
    }

    public static string GetRank(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.Rank : null;
    }

    /// <summary>
    /// Save a score. Returns true if it's a new highscore.
    /// </summary>
    public static bool SaveScore(string songName, string difficulty, int score,
        int maxCombo, float clearPercent, string rank)
    {
        string key = MakeKey(songName, difficulty);
        bool isNew = true;

        if (Data.SongScores.TryGetValue(key, out var existing))
        {
            if (existing.Score >= score)
                isNew = false;
        }

        if (isNew || !Data.SongScores.ContainsKey(key))
        {
            Data.SongScores[key] = new ScoreEntry
            {
                Score = score,
                MaxCombo = maxCombo,
                ClearPercent = clearPercent,
                Rank = rank
            };
            SaveToDisk();
        }

        return isNew;
    }

    public static int GetWeekScore(string weekId, string difficulty)
    {
        string key = MakeKey(weekId, difficulty);
        return Data.WeekScores.TryGetValue(key, out var entry) ? entry.Score : 0;
    }

    public static bool SaveWeekScore(string weekId, string difficulty, int score)
    {
        string key = MakeKey(weekId, difficulty);
        bool isNew = !Data.WeekScores.TryGetValue(key, out var existing) || score > existing.Score;
        if (isNew)
        {
            Data.WeekScores[key] = new ScoreEntry { Score = score };
            SaveToDisk();
        }
        return isNew;
    }

    public static void SavePreferences()
    {
        SaveToDisk();
    }

    public static void ClearSaveData()
    {
        lock (_writeLock)
        {
            try
            {
                _data = new SaveData();
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing save data: {ex.Message}");
            }
        }
    }

    private static SaveData LoadFromDisk()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                var data = JsonConvert.DeserializeObject<SaveData>(json);
                if (data != null)
                {
                    Console.WriteLine($"Loaded save data: {data.SongScores.Count} song scores");
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading save data: {ex.Message}");
        }
        return new SaveData();
    }

    // Lock to prevent concurrent async writes from racing on the same file
    private static readonly object _writeLock = new();
    
    private static void SaveToDisk()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            // Write to disk on a background thread to avoid frame hitches
            string path = SavePath;
            Task.Run(() =>
            {
                lock (_writeLock)
                {
                    try { File.WriteAllText(path, json); }
                    catch (Exception ex) { Console.WriteLine($"Error saving data: {ex.Message}"); }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error serializing save data: {ex.Message}");
        }
    }
}
