using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LocalAudioServer.Audio;
using LocalAudioServer.Models;

namespace LocalAudioServer.Network;

public sealed class TcpAudioReceiver : IDisposable
{
    public const byte ExpectedProtocolVersion = 0x01;

    private readonly AudioPlaybackEngine _audioEngine;
    private readonly int _port;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public event Action<ServerState, string>? StateChanged;
    public event Action<AudioMetrics>? MetricsUpdated;

    private long _totalBytes;
    private long _totalPackets;
    private long _bytesInCurrentSecond;
    private readonly Stopwatch _bitrateStopwatch = new();
    private double _currentBitrateKbps;

    public ServerState CurrentState { get; private set; } = ServerState.Stopped;

    public TcpAudioReceiver(AudioPlaybackEngine audioEngine, int port = 5000)
    {
        _audioEngine = audioEngine;
        _port = port;
    }

    public void Start()
    {
        if (CurrentState != ServerState.Stopped) return;

        _cts = new CancellationTokenSource();
        _totalBytes = 0;
        _totalPackets = 0;
        _bytesInCurrentSecond = 0;
        _bitrateStopwatch.Restart();

        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        UpdateState(ServerState.Listening, $"À escuta em 127.0.0.1:{_port} (Aguardando Android via ADB reverse)...");
        _listenerTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                client.ReceiveBufferSize = 3840 * 8;

                UpdateState(ServerState.Connected, $"Dispositivo conectado de {client.Client.RemoteEndPoint}. Validando protocolo...");
                await HandleClientAsync(client, ct);

                if (!ct.IsCancellationRequested)
                {
                    UpdateState(ServerState.Listening, $"Cliente desconectou. À escuta em 127.0.0.1:{_port} para nova conexão...");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    UpdateState(ServerState.Listening, $"Aviso: {ex.Message}. Aguardando reconexão...");
                    await Task.Delay(500, ct);
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var networkStream = client.GetStream())
        using (var bufferedStream = new BufferedStream(networkStream, 3840 * 2))
        {
            byte[] versionBuf = new byte[1];
            int vRead = await bufferedStream.ReadAsync(versionBuf, 0, 1, ct);
            if (vRead == 0 || versionBuf[0] != ExpectedProtocolVersion)
            {
                UpdateState(ServerState.Listening, $"Versão de protocolo inválida (recebido: {versionBuf[0]}).");
                return;
            }

            UpdateState(ServerState.Streaming, $"Protocolo v{versionBuf[0]} aceite. Fluxo de áudio ativo: PCM 48kHz / 16-bit / Estéreo");

            byte[] readBuffer = new byte[3840];

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await bufferedStream.ReadExactlyAsync(readBuffer, 0, readBuffer.Length, ct);
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                _audioEngine.AddSamples(readBuffer, 0, readBuffer.Length);

                _totalBytes += readBuffer.Length;
                _totalPackets++;
                _bytesInCurrentSecond += readBuffer.Length;

                if (_bitrateStopwatch.ElapsedMilliseconds >= 250)
                {
                    double elapsedSec = _bitrateStopwatch.Elapsed.TotalSeconds;
                    _currentBitrateKbps = (_bytesInCurrentSecond * 8.0) / (elapsedSec * 1000.0);
                    _bytesInCurrentSecond = 0;
                    _bitrateStopwatch.Restart();

                    MetricsUpdated?.Invoke(new AudioMetrics(
                        _totalBytes,
                        _totalPackets,
                        _currentBitrateKbps,
                        _audioEngine.CurrentBufferLatencyMs,
                        _audioEngine.CurrentPeakLeft,
                        _audioEngine.CurrentPeakRight
                    ));
                }
            }
        }
    }

    public void Stop()
    {
        if (CurrentState == ServerState.Stopped) return;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        UpdateState(ServerState.Stopped, "Servidor parado.");
    }

    private void UpdateState(ServerState state, string message)
    {
        CurrentState = state;
        StateChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        Stop();
    }
}