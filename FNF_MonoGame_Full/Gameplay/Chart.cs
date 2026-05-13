using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Represents a song chart with all note data
/// Supports both legacy FNF format and new Funkin' format
/// </summary>
public class Chart
{
    public string SongName { get; set; }
    public float BPM { get; set; } = 120;
    public float Speed { get; set; } = 1.0f;
    public double SongLength { get; set; } = 180; // seconds
    public string PlayerCharacter { get; set; } = "bf";
    public string OpponentCharacter { get; set; } = "dad";
    public string GirlfriendCharacter { get; set; } = "gf";
    public string Stage { get; set; } = "stage";
    public string Difficulty { get; set; } = "normal";
    public string Artist { get; set; } = "Unknown";
    public string Charter { get; set; } = "Unknown";
    public string NoteStyle { get; set; } = "funkin";
    
    public List<Note> Notes { get; set; } = new();
    public List<Section> Sections { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();
    
    /// <summary>
    /// BPM changes parsed from timeChanges. Each entry is (timeSeconds, newBPM).
    /// Sorted by time. The first entry is (0, initialBPM).
    /// </summary>
    public List<(double Time, float Bpm)> BPMChanges { get; set; } = new();
    
    public static Chart Load(string songName, AssetManager assets, string difficulty = "normal")
    {
        // Try multiple name variants (dad-battle, dad_battle, dadbattle)
        var candidates = new List<string> { songName };
        if (songName.Contains('-'))
        {
            candidates.Add(songName.Replace('-', '_'));
            candidates.Add(songName.Replace("-", ""));
        }
        if (songName.Contains('_'))
        {
            candidates.Add(songName.Replace('_', '-'));
            candidates.Add(songName.Replace("_", ""));
        }
        
        foreach (var name in candidates.Distinct())
        {
            // Try new format first (charts/chart.json)
            string newChartPath = $"songs/{name}/charts/chart.json";
            string metaPath = $"songs/{name}/charts/meta.json";
            
            var newChart = assets.LoadJson<NewChartFile>(newChartPath);
            if (newChart?.Notes != null)
            {
                var meta = assets.LoadJson<ChartMeta>(metaPath);
                return ParseNewFNFChart(songName, newChart, meta, difficulty);
            }
            
            // Try legacy format
            string legacyPath = $"songs/{name}/chart";
            var legacyChart = assets.LoadJson<ChartFile>(legacyPath);
            if (legacyChart?.Song != null)
            {
                return ParseLegacyFNFChart(legacyChart, difficulty);
            }
        }
        
        // Return demo chart if all fails
        return CreateDemoChart(songName);
    }
    
    private static Chart ParseNewFNFChart(string songName, NewChartFile chartData, ChartMeta meta, string difficulty = "normal")
    {
        // Pick scroll speed for the chosen difficulty
        float scrollSpeed = chartData.ScrollSpeed?.GetSpeed(difficulty) ?? 1.5f;
        
        var chart = new Chart
        {
            SongName = songName,
            BPM = meta?.TimeChanges?.FirstOrDefault()?.Bpm ?? 100,
            Speed = scrollSpeed,
            PlayerCharacter = meta?.PlayData?.Characters?.Player ?? "bf",
            OpponentCharacter = meta?.PlayData?.Characters?.Opponent ?? "dad",
            GirlfriendCharacter = meta?.PlayData?.Characters?.Girlfriend ?? "gf",
            Stage = meta?.PlayData?.Stage ?? "stage",
            Difficulty = difficulty,
            Artist = meta?.Artist ?? "Unknown",
            Charter = meta?.Charter ?? "Unknown",
            NoteStyle = meta?.PlayData?.NoteStyle ?? "funkin"
        };
        
        // Get notes from the specified difficulty, fall back to others
        List<NewNote> noteList = chartData.Notes.GetByName(difficulty);
        
        // Difficulty-aware fallback chain
        if (noteList == null || noteList.Count == 0)
        {
            noteList = difficulty.ToLower() switch
            {
                "easy" => chartData.Notes.Normal ?? chartData.Notes.Hard,
                "hard" => chartData.Notes.Normal ?? chartData.Notes.Easy,
                "erect" => chartData.Notes.Hard ?? chartData.Notes.Normal,
                "nightmare" => chartData.Notes.Erect ?? chartData.Notes.Hard,
                _ => chartData.Notes.Normal ?? chartData.Notes.Easy ?? chartData.Notes.Hard
            };
        }
        
        // Final fallback: try any non-null list with notes
        if (noteList == null || noteList.Count == 0)
        {
            noteList = chartData.Notes.GetAny();
        }
        
        if (noteList != null)
        {
            foreach (var note in noteList)
            {
                // Original FNF: getStrumlineIndex = floor(data / 4)
                // Strumline 0 = PLAYER (BF), Strumline 1 = OPPONENT (Dad)
                // d=0-3: player notes (strumline 0), d=4-7: opponent notes (strumline 1)
                bool isPlayerNote = note.D < 4;
                int lane = note.D % 4;
                
                chart.Notes.Add(new Note
                {
                    Time = note.T / 1000.0, // Convert ms to seconds
                    Lane = lane,
                    SustainLength = (note.L ?? 0) / 1000.0,
                    IsPlayerNote = isPlayerNote
                });
            }
        }
        
        // Sort notes by time
        chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        
        // Parse events
        if (chartData.Events != null)
        {
            foreach (var ev in chartData.Events)
            {
                chart.Events.Add(new GameEvent
                {
                    Time = ev.T / 1000.0,
                    Name = ev.E ?? "",
                    Value = ev.V
                });
            }
            chart.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        
        // Parse BPM changes from timeChanges
        chart.BPMChanges.Add((0.0, chart.BPM));
        if (meta?.TimeChanges != null && meta.TimeChanges.Count > 1)
        {
            for (int i = 1; i < meta.TimeChanges.Count; i++)
            {
                var tc = meta.TimeChanges[i];
                chart.BPMChanges.Add((tc.T / 1000.0, tc.Bpm));
            }
            chart.BPMChanges.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        
        // Calculate song length from last note + buffer
        if (chart.Notes.Count > 0)
        {
            chart.SongLength = chart.Notes.Last().Time + 5;
        }
        
        return chart;
    }
    
    private static Chart ParseLegacyFNFChart(ChartFile chartFile, string difficulty = "normal")
    {
        var chart = new Chart
        {
            SongName = chartFile.Song.Song,
            BPM = chartFile.Song.Bpm,
            Speed = chartFile.Song.Speed,
            PlayerCharacter = chartFile.Song.Player1 ?? "bf",
            OpponentCharacter = chartFile.Song.Player2 ?? "dad",
            GirlfriendCharacter = chartFile.Song.GfVersion ?? "gf",
            Stage = chartFile.Song.Stage ?? "stage",
            Difficulty = difficulty
        };
        
        // Parse sections and notes
        double currentTime = 0;
        double stepCrochet = 15000.0 / chart.BPM; // milliseconds per step
        bool? lastMustHit = null;
        float currentBpm = chart.BPM;
        
        // Initialize BPM changes with starting BPM
        chart.BPMChanges.Add((0.0, chart.BPM));
        
        if (chartFile.Song.Notes != null)
        {
            foreach (var section in chartFile.Song.Notes)
            {
                int stepsInSection = section.LengthInSteps ?? 16;
                bool mustHitSection = section.MustHitSection ?? true;
                
                // Handle BPM changes in legacy charts
                if (section.ChangeBPM == true && section.Bpm.HasValue && section.Bpm.Value > 0
                    && Math.Abs(section.Bpm.Value - currentBpm) > 0.001f)
                {
                    currentBpm = section.Bpm.Value;
                    stepCrochet = 15000.0 / currentBpm;
                    chart.BPMChanges.Add((currentTime, currentBpm));
                }
                
                // Generate FocusCamera events from mustHitSection changes
                // (legacy charts don't have explicit FocusCamera events)
                if (lastMustHit != mustHitSection)
                {
                    lastMustHit = mustHitSection;
                    chart.Events.Add(new GameEvent
                    {
                        Time = currentTime,
                        Name = "FocusCamera",
                        // Match official FNF V2 FocusCameraSongEvent convention:
                        // 0 = Boyfriend (player), 1 = Dad (opponent), 2 = Girlfriend.
                        Value = mustHitSection ? 0 : 1
                    });
                }
                
                if (section.SectionNotes != null)
                {
                    foreach (var noteData in section.SectionNotes)
                    {
                        if (noteData.Count >= 2)
                        {
                            double noteTime = noteData[0] / 1000.0; // Convert to seconds
                            int noteDirection = (int)noteData[1];
                            double sustainLength = noteData.Count > 2 ? noteData[2] / 1000.0 : 0;
                            
                            // Determine if this is a player note
                            bool isPlayerNote = (noteDirection < 4) == mustHitSection;
                            
                            chart.Notes.Add(new Note
                            {
                                Time = noteTime,
                                Lane = noteDirection % 4,
                                SustainLength = sustainLength,
                                IsPlayerNote = isPlayerNote
                            });
                        }
                    }
                }
                
                currentTime += stepsInSection * stepCrochet / 1000.0;
            }
        }
        
        chart.SongLength = currentTime;
        
        // Sort notes by time
        chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        
        // Sort events by time
        chart.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        
        return chart;
    }
    
    private static Chart CreateDemoChart(string songName)
    {
        var chart = new Chart
        {
            SongName = songName,
            BPM = 150,
            Speed = 1.5f,
            SongLength = 60
        };
        
        // Create a simple demo pattern
        var random = new Random(songName.GetHashCode());
        double time = 2.0; // Start 2 seconds in
        
        while (time < chart.SongLength - 5)
        {
            chart.Notes.Add(new Note
            {
                Time = time,
                Lane = random.Next(4),
                IsPlayerNote = true
            });
            
            time += 60.0 / chart.BPM * (random.Next(2) + 1) * 0.5; // Random spacing
        }
        
        return chart;
    }
}

/// <summary>
/// Represents a single note in the chart
/// </summary>
public class Note
{
    public double Time { get; set; }      // Time in seconds
    public int Lane { get; set; }          // 0=Left, 1=Down, 2=Up, 3=Right
    public double SustainLength { get; set; } // Hold note duration
    public bool IsPlayerNote { get; set; } = true;
    public bool IsHit { get; set; } = false;
    public bool HoldComplete { get; set; } = false; // True when hold note fully consumed
}

public class Section
{
    public bool MustHitSection { get; set; }
    public int LengthInSteps { get; set; } = 16;
    public List<Note> Notes { get; set; } = new();
}

// JSON deserialization classes for FNF chart format
public class ChartFile
{
    [JsonProperty("song")]
    public ChartSong Song { get; set; }
}

public class ChartSong
{
    [JsonProperty("song")]
    public string Song { get; set; }
    
    [JsonProperty("bpm")]
    public float Bpm { get; set; }
    
    [JsonProperty("speed")]
    public float Speed { get; set; }
    
    [JsonProperty("player1")]
    public string Player1 { get; set; }
    
    [JsonProperty("player2")]
    public string Player2 { get; set; }
    
    [JsonProperty("gfVersion")]
    public string GfVersion { get; set; }
    
    [JsonProperty("stage")]
    public string Stage { get; set; }
    
    [JsonProperty("notes")]
    public List<ChartSection> Notes { get; set; }
}

public class ChartSection
{
    [JsonProperty("mustHitSection")]
    public bool? MustHitSection { get; set; }
    
    [JsonProperty("lengthInSteps")]
    public int? LengthInSteps { get; set; }
    
    [JsonProperty("sectionNotes")]
    public List<List<double>> SectionNotes { get; set; }
    
    [JsonProperty("bpm")]
    public float? Bpm { get; set; }
    
    [JsonProperty("changeBPM")]
    public bool? ChangeBPM { get; set; }
}

// ============================================
// NEW FNF FORMAT (Funkin' / Psych Engine style)
// ============================================

public class NewChartFile
{
    [JsonProperty("scrollSpeed")]
    public ScrollSpeed ScrollSpeed { get; set; }
    
    [JsonProperty("notes")]
    public NotesByDifficulty Notes { get; set; }
    
    [JsonProperty("events")]
    public List<ChartEvent> Events { get; set; }
}

public class ScrollSpeed
{
    [JsonProperty("easy")]
    public float? Easy { get; set; }
    
    [JsonProperty("normal")]
    public float? Normal { get; set; }
    
    [JsonProperty("hard")]
    public float? Hard { get; set; }
    
    [JsonProperty("erect")]
    public float? Erect { get; set; }
    
    [JsonProperty("nightmare")]
    public float? Nightmare { get; set; }
    
    [JsonProperty("default")]
    public float? Default { get; set; }
    
    [JsonExtensionData]
    public Dictionary<string, JToken> ExtraSpeeds { get; set; }
    
    public float GetSpeed(string difficulty)
    {
        float? result = difficulty?.ToLower() switch
        {
            "easy" => Easy,
            "normal" => Normal,
            "hard" => Hard,
            "erect" => Erect,
            "nightmare" => Nightmare,
            _ => null
        };
        if (result.HasValue) return result.Value;
        
        if (ExtraSpeeds != null && difficulty != null 
            && ExtraSpeeds.TryGetValue(difficulty, out var token))
        {
            try { return token.ToObject<float>(); }
            catch { }
        }
        return Default ?? 1.5f;
    }
}

public class NotesByDifficulty
{
    [JsonProperty("easy")]
    public List<NewNote> Easy { get; set; }
    
    [JsonProperty("normal")]
    public List<NewNote> Normal { get; set; }
    
    [JsonProperty("hard")]
    public List<NewNote> Hard { get; set; }
    
    [JsonProperty("erect")]
    public List<NewNote> Erect { get; set; }
    
    [JsonProperty("nightmare")]
    public List<NewNote> Nightmare { get; set; }
    
    /// <summary>
    /// Captures any difficulty names not covered by the fixed properties above
    /// (e.g. custom mod difficulties like "funkin", "pico", etc.)
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JToken> ExtraDifficulties { get; set; }
    
    /// <summary>
    /// Get notes for a difficulty by name, checking both fixed properties and extras.
    /// </summary>
    public List<NewNote> GetByName(string difficulty)
    {
        var result = difficulty?.ToLower() switch
        {
            "easy" => Easy,
            "normal" => Normal,
            "hard" => Hard,
            "erect" => Erect,
            "nightmare" => Nightmare,
            _ => null
        };
        if (result != null) return result;
        
        // Check extension data for custom difficulties
        if (ExtraDifficulties != null && difficulty != null 
            && ExtraDifficulties.TryGetValue(difficulty, out var token))
        {
            try { return token.ToObject<List<NewNote>>(); }
            catch { }
        }
        return null;
    }
    
    /// <summary>
    /// Get the first non-null, non-empty note list from any difficulty.
    /// </summary>
    public List<NewNote> GetAny()
    {
        return Erect ?? Nightmare ?? Normal ?? Easy ?? Hard ?? GetFirstExtra();
    }
    
    private List<NewNote> GetFirstExtra()
    {
        if (ExtraDifficulties == null) return null;
        foreach (var kvp in ExtraDifficulties)
        {
            try
            {
                var list = kvp.Value.ToObject<List<NewNote>>();
                if (list != null && list.Count > 0) return list;
            }
            catch { }
        }
        return null;
    }
}

public class NewNote
{
    [JsonProperty("d")]
    public int D { get; set; } // Direction: 0-3 = opponent (strumline 0), 4-7 = player (strumline 1)
    
    [JsonProperty("t")]
    public double T { get; set; } // Time in milliseconds
    
    [JsonProperty("l")]
    public double? L { get; set; } // Length (for sustain notes)
}

public class ChartEvent
{
    [JsonProperty("e")]
    public string E { get; set; } // Event name
    
    [JsonProperty("t")]
    public double T { get; set; } // Time
    
    [JsonProperty("v")]
    public object V { get; set; } // Value
}

public class ChartMeta
{
    [JsonProperty("songName")]
    public string SongName { get; set; }
    
    [JsonProperty("artist")]
    public string Artist { get; set; }
    
    [JsonProperty("charter")]
    public string Charter { get; set; }
    
    [JsonProperty("playData")]
    public PlayData PlayData { get; set; }
    
    [JsonProperty("timeChanges")]
    public List<TimeChange> TimeChanges { get; set; }
}

public class PlayData
{
    [JsonProperty("stage")]
    public string Stage { get; set; }
    
    [JsonProperty("noteStyle")]
    public string NoteStyle { get; set; }
    
    [JsonProperty("characters")]
    public Characters Characters { get; set; }
}

public class Characters
{
    [JsonProperty("player")]
    public string Player { get; set; }
    
    [JsonProperty("girlfriend")]
    public string Girlfriend { get; set; }
    
    [JsonProperty("opponent")]
    public string Opponent { get; set; }
}

public class TimeChange
{
    [JsonProperty("t")]
    public double T { get; set; }
    
    [JsonProperty("bpm")]
    public float Bpm { get; set; }
}

/// <summary>
/// Parsed game event (from chart events array).
/// Common events: "FocusCamera", "ZoomCamera", "SetCameraBop", "PlayAnimation"
/// </summary>
public class GameEvent
{
    public double Time { get; set; }
    public string Name { get; set; }
    public object Value { get; set; }
    public bool Fired { get; set; }
}
