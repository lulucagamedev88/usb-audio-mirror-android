using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalAudioServer.Audio;

public sealed class AudioPlaybackEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int BitsPerSample = 16;
    public const int Channels = 2;
    public const int BytesPerSample = BitsPerSample / 8; // 2 bytes
    public const int BytesPerFrame = Channels * BytesPerSample; // 4 bytes
    public const int BytesPerMillisecond = (SampleRate * BytesPerFrame) / 1000; // 192 bytes/ms

    private const int TargetMaxBufferBytes = BytesPerMillisecond * 150; // 28.800 bytes

    private readonly WaveFormat _waveFormat;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private WasapiOut? _wasapiOut;
    private readonly object _lock = new();

    public float CurrentPeakLeft { get; private set; }
    public float CurrentPeakRight { get; private set; }
    public int CurrentBufferLatencyMs => _bufferedWaveProvider != null 
        ? _bufferedWaveProvider.BufferedBytes / BytesPerMillisecond 
        : 0;

    public AudioPlaybackEngine()
    {
        _waveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_wasapiOut != null) return;

            _bufferedWaveProvider = new BufferedWaveProvider(_waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };

            _wasapiOut = new WasapiOut(AudioClientShareMode.Shared, 20);
            _wasapiOut.Init(_bufferedWaveProvider);
            _wasapiOut.Play();
        }
    }

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_bufferedWaveProvider == null) return;

            // Descarte parcial cirúrgico do excedente acumulado (Correção B)
            int excess = _bufferedWaveProvider.BufferedBytes - TargetMaxBufferBytes;
            if (excess > 0)
            {
                byte[] tmp = new byte[excess];
                _bufferedWaveProvider.Read(tmp, 0, excess);
            }

            _bufferedWaveProvider.AddSamples(buffer, offset, count);
            CalculatePeaks(buffer, offset, count);
        }
    }

    private void CalculatePeaks(byte[] buffer, int offset, int count)
    {
        int maxL = 0;
        int maxR = 0;

        for (int i = offset; i < offset + count - 3; i += 4)
        {
            short left = (short)(buffer[i] | (buffer[i + 1] << 8));
            short right = (short)(buffer[i + 2] | (buffer[i + 3] << 8));

            int absL = Math.Abs((int)left);
            int absR = Math.Abs((int)right);

            if (absL > maxL) maxL = absL;
            if (absR > maxR) maxR = absR;
        }

        float newPeakL = maxL / 32768f;
        float newPeakR = maxR / 32768f;

        CurrentPeakLeft = Math.Max(newPeakL, CurrentPeakLeft * 0.75f);
        CurrentPeakRight = Math.Max(newPeakR, CurrentPeakRight * 0.75f);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_wasapiOut != null)
            {
                try { _wasapiOut.Stop(); } catch { }
                try { _wasapiOut.Dispose(); } catch { }
                _wasapiOut = null;
            }

            _bufferedWaveProvider?.ClearBuffer();
            _bufferedWaveProvider = null;
            CurrentPeakLeft = 0f;
            CurrentPeakRight = 0f;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}