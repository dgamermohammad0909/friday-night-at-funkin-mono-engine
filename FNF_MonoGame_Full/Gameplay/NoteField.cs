namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Manages the note field and note hit detection
/// </summary>
public class NoteField
{
    private readonly List<Note> _activeNotes = new(128);
    private readonly Chart _chart;
    private readonly Conductor _conductor;
    
    // Timing windows in seconds (matches FNF PBOT1 scoring system)
    public const float KILLER_WINDOW = 0.0125f;  // 12.5ms
    public const float SICK_WINDOW = 0.045f;     // 45ms
    public const float GOOD_WINDOW = 0.090f;     // 90ms
    public const float BAD_WINDOW = 0.135f;      // 135ms
    public const float SHIT_WINDOW = 0.160f;     // 160ms — same as miss threshold
    public const float MISS_WINDOW = 0.160f;     // 160ms — PBOT1_MISS_THRESHOLD
    
    // How far ahead to load notes (seconds)
    private const float SPAWN_DISTANCE = 2.0f;
    
    private int _nextNoteIndex = 0;
    
    // Note object pool to avoid GC allocations
    private readonly Queue<Note> _notePool = new(128);
    
    public NoteField(Chart chart, Conductor conductor)
    {
        _chart = chart;
        _conductor = conductor;
        // Pre-allocate pool
        for (int i = 0; i < 64; i++)
            _notePool.Enqueue(new Note());
    }
    
    private Note RentNote()
    {
        if (_notePool.Count > 0)
        {
            var note = _notePool.Dequeue();
            note.IsHit = false;
            note.HoldComplete = false;
            return note;
        }
        return new Note();
    }
    
    private void ReturnNote(Note note)
    {
        _notePool.Enqueue(note);
    }
    
    // O(1) swap-remove: swap element at index with last element, then remove last
    private void SwapRemoveAt(int index)
    {
        int last = _activeNotes.Count - 1;
        if (index < last)
            _activeNotes[index] = _activeNotes[last];
        _activeNotes.RemoveAt(last);
    }
    
    // Reusable lists to avoid per-frame allocations (reduces GC pressure)
    private readonly List<Note> _missedNotesBuffer = new();
    private readonly List<Note> _visibleNotesBuffer = new(64);
    private readonly List<Note> _opponentNotesBuffer = new();
    
    /// <summary>
    /// Update the note field, returns any notes that were missed.
    /// Caller must call ReturnMissedNotes() after processing the returned list.
    /// </summary>
    public List<Note> Update(float deltaTime)
    {
        _missedNotesBuffer.Clear();
        double currentTime = _conductor.SongPosition;
        
        // Spawn upcoming notes
        SpawnUpcomingNotes(currentTime);
        
        // Check for missed/expired notes — iterate backwards for safe swap-removal
        for (int i = _activeNotes.Count - 1; i >= 0; i--)
        {
            var note = _activeNotes[i];
            
            if (note.IsPlayerNote && !note.IsHit)
            {
                double timeDiff = currentTime - note.Time;
                
                if (timeDiff > MISS_WINDOW)
                {
                    _missedNotesBuffer.Add(note);
                    SwapRemoveAt(i);
                    // Don't return to pool yet — caller still needs the note data
                }
            }
            else if (!note.IsPlayerNote)
            {
                // Remove old opponent notes after their sustain has ended
                double noteEndTime = note.Time + note.SustainLength;
                if (currentTime - noteEndTime > 0.5)
                {
                    SwapRemoveAt(i);
                    ReturnNote(note);
                }
            }
        }
        
        return _missedNotesBuffer;
    }
    
    /// <summary>
    /// Return missed notes to the pool after the caller has finished processing them.
    /// </summary>
    public void ReturnMissedNotes()
    {
        for (int i = 0; i < _missedNotesBuffer.Count; i++)
            ReturnNote(_missedNotesBuffer[i]);
        _missedNotesBuffer.Clear();
    }
    
    private void SpawnUpcomingNotes(double currentTime)
    {
        // Add notes that are coming up
        while (_nextNoteIndex < _chart.Notes.Count)
        {
            var chartNote = _chart.Notes[_nextNoteIndex];
            
            if (chartNote.Time <= currentTime + SPAWN_DISTANCE)
            {
                var note = RentNote();
                note.Time = chartNote.Time;
                note.Lane = chartNote.Lane;
                note.SustainLength = chartNote.SustainLength;
                note.IsPlayerNote = chartNote.IsPlayerNote;
                _activeNotes.Add(note);
                _nextNoteIndex++;
            }
            else
            {
                break;
            }
        }
    }
    
    /// <summary>
    /// Get a hittable note in the given lane, if any.
    /// Returns the closest note to currentTime within the hit window.
    /// </summary>
    public Note GetHittableNote(int lane, double currentTime)
    {
        int count = _activeNotes.Count;
        Note best = null;
        double bestDiff = double.MaxValue;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (note.Lane == lane && note.IsPlayerNote && !note.IsHit)
            {
                double timeDiff = Math.Abs(currentTime - note.Time);
                
                if (timeDiff <= SHIT_WINDOW && timeDiff < bestDiff)
                {
                    best = note;
                    bestDiff = timeDiff;
                }
            }
        }
        
        return best;
    }
    
    /// <summary>
    /// Get the earliest unhit player note in a lane that is at or past the given time.
    /// Used by botplay to auto-hit notes without the timing window constraint.
    /// </summary>
    public Note GetBotplayNote(int lane, double currentTime)
    {
        int count = _activeNotes.Count;
        Note best = null;
        double bestTime = double.MaxValue;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (note.Lane == lane && note.IsPlayerNote && !note.IsHit && currentTime >= note.Time)
            {
                if (note.Time < bestTime)
                {
                    best = note;
                    bestTime = note.Time;
                }
            }
        }
        
        return best;
    }
    
    /// <summary>
    /// Mark a note as hit. For hold notes, keep in active list so sustain renders.
    /// For non-hold notes, remove from list.
    /// </summary>
    public void RemoveNote(Note note)
    {
        note.IsHit = true;
        if (note.SustainLength <= 0)
        {
            int idx = _activeNotes.IndexOf(note);
            if (idx >= 0)
            {
                SwapRemoveAt(idx);
                ReturnNote(note);
            }
        }
        // Hold notes stay in _activeNotes so the sustain tail keeps rendering
    }
    
    /// <summary>
    /// Fully remove a hold note when the hold is complete or released.
    /// </summary>
    public void RemoveHoldNote(Note note)
    {
        note.IsHit = true;
        note.HoldComplete = true;
        int idx = _activeNotes.IndexOf(note);
        if (idx >= 0)
        {
            SwapRemoveAt(idx);
            ReturnNote(note);
        }
    }
    
    /// <summary>
    /// Get all visible notes for rendering
    /// </summary>
    public List<Note> GetVisibleNotes(double currentTime)
    {
        _visibleNotesBuffer.Clear();
        int count = _activeNotes.Count;
        double visibleStart = currentTime - 0.1;
        double visibleEnd = currentTime + 2.0;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            
            // Skip completed hold notes
            if (note.HoldComplete) continue;
            
            // Skip hit non-hold notes (hold notes stay visible for sustain rendering)
            if (note.IsHit && note.SustainLength <= 0) continue;
            
            // Only show notes approaching or at the strumline (not past it)
            // For hold notes that are hit, show them as long as the sustain is active
            if (note.IsHit && note.SustainLength > 0)
            {
                // Show hold note if sustain end is still in the future
                double sustainEnd = note.Time + note.SustainLength;
                if (sustainEnd >= visibleStart)
                {
                    _visibleNotesBuffer.Add(note);
                }
            }
            else if (note.Time >= visibleStart && note.Time <= visibleEnd)
            {
                _visibleNotesBuffer.Add(note);
            }
        }
        return _visibleNotesBuffer;
    }
    
    /// <summary>
    /// Get opponent notes that should trigger animations
    /// </summary>
    public List<Note> GetOpponentNotes(double currentTime)
    {
        _opponentNotesBuffer.Clear();
        int count = _activeNotes.Count;
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (!note.IsPlayerNote && !note.IsHit && currentTime >= note.Time)
            {
                _opponentNotesBuffer.Add(note);
            }
        }
        return _opponentNotesBuffer;
    }
    
    /// <summary>
    /// Get an active opponent sustain note in the given lane if the sustain is still playing.
    /// Used to keep opponent singing during hold notes (original FNF behavior).
    /// </summary>
    public Note GetActiveOpponentSustain(int lane, double currentTime)
    {
        int count = _activeNotes.Count;
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (!note.IsPlayerNote && note.IsHit && !note.HoldComplete
                && note.Lane == lane && note.SustainLength > 0
                && currentTime <= note.Time + note.SustainLength)
            {
                return note;
            }
        }
        return null;
    }
    
    public void Reset()
    {
        _activeNotes.Clear();
        _nextNoteIndex = 0;
    }
}
