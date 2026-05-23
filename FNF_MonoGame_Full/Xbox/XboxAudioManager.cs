using System;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Audio;
using NVorbis;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame;

/// <summary>
/// Xbox/UWP-compatible audio manager using NVorbis (pure managed OGG decoder)
/// instead of NAudio (which requires Windows desktop APIs).
/// Provides the same public API as AudioManager so game code works unchanged.
/// </summary>
public class XboxAudioManager : IDisposable
{
    private readonly AssetManager _assets;
    
    // Music streaming
    private DynamicSoundEffectInstance _musicTrack;
    private VorbisReader _musicReader;
    private Thread _musicThread;
    private volatile bool _isMusicStreaming;
    private bool _musicLoop;
    private readonly object _musicLock = new();
    
    // Voice tracks (player + opponent)
    private DynamicSoundEffectInstance _playerVoice;
    private VorbisReader _playerVoiceReader;
    private Thread _playerVoiceThread;
    private volatile bool _isPlayerVoiceStreaming;
    
    private DynamicSoundEffectInstance _opponentVoice;
    private VorbisReader _opponentVoiceReader;
    private Thread _opponentVoiceThread;
    private volatile bool _isOpponentVoiceStreaming;
    
    // Sound effects cache
    private readonly Dictionary<string, SoundEffect> _soundCache = new();
    
    // Volume
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;

    // Extra content roots (for funkin.assets)
    public List<string> ExtraContentRoots { get; } = new();
    
    // Playback timer
    private System.Diagnostics.Stopwatch _playbackTimer = new();
    private double _playbackOffset;
    private bool _musicPaused;

    // Intro→loop chaining
    private string _pendingLoopPath;
    private Action _onMusicComplete;
    private volatile Action _pendingCallback;

    // Music fade-out state
    private float _fadeOutTimer;
    private float _fadeOutDuration;
    private float _fadeOutStartVolume;
    private bool _isFadingOut;
    private Action _fadeOutCallback;

    public XboxAudioManager(AssetManager assets)
    {
        _assets = assets;
    }
    
    // === Properties matching AudioManager API ===
    
    public double MusicPosition => _musicPaused 
        ? _playbackOffset * 1000.0 
        : (_playbackOffset + _playbackTimer.Elapsed.TotalSeconds) * 1000.0;
    
    public double MusicLength
    {
        get
        {
            lock (_musicLock)
            {
                return _musicReader?.TotalTime.TotalSeconds ?? 0;
            }
        }
    }
    
    public bool MusicPlaying => _musicTrack != null && _musicTrack.State == SoundState.Playing;
    public bool HasPlayerVoice => _playerVoice != null && _isPlayerVoiceStreaming;
    public bool HasOpponentVoice => _opponentVoice != null && _isOpponentVoiceStreaming;

    public float MusicVisualizerLevel { get; private set; }
    
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            if (_musicTrack != null)
                _musicTrack.Volume = _musicVolume;
        }
    }

    public float SfxVolume
    {
        get => _sfxVolume;
        set => _sfxVolume = Math.Clamp(value, 0f, 1f);
    }

    public double VoicePosition => MusicPosition; // approximate
    
    public void Update(float deltaTime = 1f / 60f)
    {
        // Process deferred callbacks (intro→loop chaining) on main thread
        var cb = _pendingCallback;
        if (cb != null)
        {
            _pendingCallback = null;
            cb.Invoke();
        }

        // Tick music fade-out (fades voices alongside music — matches original FNF)
        if (_isFadingOut && _musicTrack != null)
        {
            _fadeOutTimer += deltaTime;
            float t = Math.Clamp(_fadeOutTimer / _fadeOutDuration, 0f, 1f);
            float fadeVolume = _fadeOutStartVolume * (1f - t);
            _musicTrack.Volume = fadeVolume;
            if (_playerVoice != null)
                try { _playerVoice.Volume = fadeVolume; } catch { }
            if (_opponentVoice != null)
                try { _opponentVoice.Volume = fadeVolume; } catch { }
            if (t >= 1f)
            {
                _isFadingOut = false;
                var fadeCb = _fadeOutCallback;
                _fadeOutCallback = null;
                if (fadeCb != null)
                    fadeCb.Invoke();
                else
                    StopMusic();
            }
        }
    }
    
    // === Music playback ===
    
    public void PlayMusic(string path, bool loop = true, int startTimeMs = 0)
    {
        StopMusic();
        
        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) return;
        
        try
        {
            _musicReader = new VorbisReader(fullPath);
            if (startTimeMs > 0)
            {
                long startSample = (long)Math.Round((_musicReader.SampleRate * (double)startTimeMs) / 1000.0);
                if (startSample > 0 && startSample < _musicReader.TotalSamples)
                    _musicReader.SeekTo(startSample);
            }
            int sampleRate = _musicReader.SampleRate;
            int channels = _musicReader.Channels;
            
            _musicTrack = new DynamicSoundEffectInstance(sampleRate, 
                channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            _musicTrack.Volume = _musicVolume;
            _musicLoop = loop;
            
            _playbackOffset = Math.Max(0, startTimeMs) / 1000.0;
            _playbackTimer.Restart();
            _musicPaused = false;
            
            _isMusicStreaming = true;
            _musicThread = new Thread(() => StreamOgg(_musicReader, _musicTrack, ref _isMusicStreaming, _musicLoop, true));
            _musicThread.IsBackground = true;
            _musicThread.Start();
            
            _musicTrack.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play music '{path}': {ex.Message}");
        }
    }
    
    public void StopMusic()
    {
        _isMusicStreaming = false;
        _onMusicComplete = null;
        _pendingCallback = null;
        _pendingLoopPath = null;
        _isFadingOut = false;
        _fadeOutCallback = null;
        _playbackTimer.Stop();
        _musicPaused = false;

        try
        {
            _musicTrack?.Stop();
            _musicTrack?.Dispose();
        }
        catch { }
        _musicTrack = null;

        try
        {
            _musicReader?.Dispose();
        }
        catch { }
        _musicReader = null;

        _musicThread = null;

        StopVoices();
    }
    
    public void PauseMusic()
    {
        if (_musicTrack != null && _musicTrack.State == SoundState.Playing)
        {
            _musicPaused = true;
            _playbackOffset += _playbackTimer.Elapsed.TotalSeconds;
            _playbackTimer.Stop();
            _musicTrack.Pause();
            try { _playerVoice?.Pause(); } catch { }
            try { _opponentVoice?.Pause(); } catch { }
        }
    }

    public void ResumeMusic()
    {
        if (_musicTrack != null && _musicPaused)
        {
            _musicPaused = false;
            _playbackTimer.Restart();
            _musicTrack.Resume();
            try { _playerVoice?.Resume(); } catch { }
            try { _opponentVoice?.Resume(); } catch { }
        }
    }
    
    // === Voice tracks ===
    
    public void PlayVoices(string path)
    {
        StopPlayerVoice();
        
        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) return;
        
        try
        {
            _playerVoiceReader = new VorbisReader(fullPath);
            _playerVoice = new DynamicSoundEffectInstance(
                _playerVoiceReader.SampleRate,
                _playerVoiceReader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            
            _isPlayerVoiceStreaming = true;
            _playerVoiceThread = new Thread(() => StreamOgg(_playerVoiceReader, _playerVoice, ref _isPlayerVoiceStreaming, false));
            _playerVoiceThread.IsBackground = true;
            _playerVoiceThread.Start();
            
            _playerVoice.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play voice '{path}': {ex.Message}");
        }
    }
    
    public void PlayOpponentVoices(string path)
    {
        StopOpponentVoice();
        
        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) return;
        
        try
        {
            _opponentVoiceReader = new VorbisReader(fullPath);
            _opponentVoice = new DynamicSoundEffectInstance(
                _opponentVoiceReader.SampleRate,
                _opponentVoiceReader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            
            _isOpponentVoiceStreaming = true;
            _opponentVoiceThread = new Thread(() => StreamOgg(_opponentVoiceReader, _opponentVoice, ref _isOpponentVoiceStreaming, false));
            _opponentVoiceThread.IsBackground = true;
            _opponentVoiceThread.Start();
            
            _opponentVoice.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play opponent voice '{path}': {ex.Message}");
        }
    }
    
    public void StopVoices()
    {
        StopPlayerVoice();
        StopOpponentVoice();
    }
    
    private void StopPlayerVoice()
    {
        _isPlayerVoiceStreaming = false;
        try { _playerVoice?.Stop(); _playerVoice?.Dispose(); } catch { }
        try { _playerVoiceReader?.Dispose(); } catch { }
        _playerVoice = null;
        _playerVoiceReader = null;
    }
    
    private void StopOpponentVoice()
    {
        _isOpponentVoiceStreaming = false;
        try { _opponentVoice?.Stop(); _opponentVoice?.Dispose(); } catch { }
        try { _opponentVoiceReader?.Dispose(); } catch { }
        _opponentVoice = null;
        _opponentVoiceReader = null;
    }
    
    public void SetPlayerVoiceVolume(float volume)
    {
        if (_playerVoice != null)
            _playerVoice.Volume = Math.Clamp(volume, 0f, 1f);
    }
    
    public void ResyncVoices()
    {
        // Not strictly needed with DynamicSoundEffectInstance streaming
    }
    
    // === Sound effects ===
    
    public void PlaySound(string name, float volume = 1f)
    {
        try
        {
            if (!_soundCache.TryGetValue(name, out var sfx))
            {
                string fullPath = ResolveAudioPath(name);
                if (fullPath == null || !File.Exists(fullPath)) return;
                
                // Decode entire OGG to PCM for SoundEffect
                using var reader = new VorbisReader(fullPath);
                int totalSamples = (int)(reader.TotalSamples * reader.Channels);
                float[] floatBuf = new float[totalSamples];
                int read = reader.ReadSamples(floatBuf, 0, totalSamples);
                
                byte[] pcm = new byte[read * 2];
                for (int i = 0; i < read; i++)
                {
                    short s = (short)(Math.Clamp(floatBuf[i], -1f, 1f) * 32767);
                    pcm[i * 2] = (byte)(s & 0xFF);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }
                
                sfx = new SoundEffect(pcm, reader.SampleRate,
                    reader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                _soundCache[name] = sfx;
            }
            
            sfx?.Play(_sfxVolume * Math.Clamp(volume, 0f, 1f), 0f, 0f);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play sound '{name}': {ex.Message}");
        }
    }
    
    public void PreloadSound(string name)
    {
        // Trigger cache load
        try
        {
            if (_soundCache.ContainsKey(name)) return;
            string fullPath = ResolveAudioPath(name);
            if (fullPath == null || !File.Exists(fullPath)) return;
            
            using var reader = new VorbisReader(fullPath);
            int totalSamples = (int)(reader.TotalSamples * reader.Channels);
            float[] floatBuf = new float[totalSamples];
            int read = reader.ReadSamples(floatBuf, 0, totalSamples);
            
            byte[] pcm = new byte[read * 2];
            for (int i = 0; i < read; i++)
            {
                short s = (short)(Math.Clamp(floatBuf[i], -1f, 1f) * 32767);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            
            _soundCache[name] = new SoundEffect(pcm, reader.SampleRate,
                reader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
        }
        catch { }
    }
    
    public bool HasCachedSound(string name) => _soundCache.ContainsKey(name);

    public void LoadSound(string name, string path) => PreloadSound(path);

    public void PlayMusicWithIntro(string introPath, string loopPath)
    {
        string introFull = ResolveAudioPath(introPath);
        if (introFull != null && File.Exists(introFull))
        {
            PlayMusic(introPath, false);
            _pendingLoopPath = loopPath;
            _onMusicComplete = () =>
            {
                string pending = _pendingLoopPath;
                _pendingLoopPath = null;
                if (pending != null)
                    PlayMusic(pending, true);
            };
        }
        else
        {
            PlayMusic(loopPath, true);
        }
    }

    public void FadeOutMusic(float duration = 0.5f, Action onComplete = null)
    {
        if (_musicTrack == null || !MusicPlaying)
        {
            onComplete?.Invoke();
            return;
        }
        _fadeOutTimer = 0;
        _fadeOutDuration = Math.Max(0.01f, duration);
        _fadeOutStartVolume = _musicTrack.Volume;
        _fadeOutCallback = onComplete;
        _isFadingOut = true;
    }

    public void StopOpponentVoices() => StopOpponentVoice();

    public void SetOpponentVoiceVolume(float volume)
    {
        if (_opponentVoice != null)
            _opponentVoice.Volume = Math.Clamp(volume, 0f, 1f);
    }
    
    // === OGG streaming thread ===
    
    private void StreamOgg(VorbisReader reader, DynamicSoundEffectInstance track, ref bool streaming, bool loop, bool isMusic = false)
    {
        const int BUFFER_SIZE = 4096;
        float[] floatBuf = new float[BUFFER_SIZE];
        byte[] pcmBuf = new byte[BUFFER_SIZE * 2];

        try
        {
            while (streaming && track.State != SoundState.Stopped)
            {
                while (track.PendingBufferCount < 3 && streaming)
                {
                    int samplesRead = reader.ReadSamples(floatBuf, 0, BUFFER_SIZE);
                    if (samplesRead == 0)
                    {
                        if (loop)
                        {
                            reader.SeekTo(0);
                            continue;
                        }
                        streaming = false;
                        // For music: fire completion callback (intro→loop chaining)
                        if (isMusic)
                        {
                            var onComplete = _onMusicComplete;
                            _onMusicComplete = null;
                            if (onComplete != null)
                                _pendingCallback = onComplete;
                        }
                        break;
                    }

                    for (int i = 0; i < samplesRead; i++)
                    {
                        short s = (short)(Math.Clamp(floatBuf[i], -1f, 1f) * 32767);
                        pcmBuf[i * 2] = (byte)(s & 0xFF);
                        pcmBuf[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                    }

                    track.SubmitBuffer(pcmBuf, 0, samplesRead * 2);
                }

                Thread.Sleep(5);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Streaming error: {ex.Message}");
        }
    }
    
    // === Path resolution ===
    
    private string ResolveAudioPath(string path)
    {
        // Try direct path with .ogg extension, then without
        string result = _assets.ResolvePath(path + ".ogg")
            ?? _assets.ResolvePath(path);
        if (result != null) return result;

        // Try tracks/ subfolder (e.g. songs/tutorial/tracks/Inst.ogg)
        string dirPart = Path.GetDirectoryName(path);
        string filePart = Path.GetFileName(path);
        if (dirPart != null)
        {
            string tracksPath = Path.Combine(dirPart, "tracks", filePart);
            result = _assets.ResolvePath(tracksPath + ".ogg")
                ?? _assets.ResolvePath(tracksPath);
        }

        return result;
    }
    
    public void Dispose()
    {
        StopMusic();
        StopVoices();
        foreach (var sfx in _soundCache.Values)
            sfx?.Dispose();
        _soundCache.Clear();
    }
}
