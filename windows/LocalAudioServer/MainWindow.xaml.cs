using System;
using System.Windows;
using System.Windows.Media;
using LocalAudioServer.Audio;
using LocalAudioServer.Models;
using LocalAudioServer.Network;

namespace LocalAudioServer;

public partial class MainWindow : Window
{
    private readonly AudioPlaybackEngine _audioEngine;
    private readonly TcpAudioReceiver _receiver;
    private bool _isServerRunning;

    public MainWindow()
    {
        InitializeComponent();

        _audioEngine = new AudioPlaybackEngine();
        _receiver = new TcpAudioReceiver(_audioEngine, port: 5000);

        _receiver.StateChanged += Receiver_StateChanged;
        _receiver.MetricsUpdated += Receiver_MetricsUpdated;

        Closing += MainWindow_Closing;
    }

    private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
    {
        if (_isServerRunning) StopServer();
        else StartServer();
    }

    private void StartServer()
    {
        try
        {
            _audioEngine.Start();
            _receiver.Start();
            _isServerRunning = true;

            BtnToggleServer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            ((System.Windows.Controls.TextBlock)BtnToggleServer.Content).Text = "Parar Servidor";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao iniciar servidor ou áudio WASAPI: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServer()
    {
        _receiver.Stop();
        _audioEngine.Stop();
        _isServerRunning = false;

        BtnToggleServer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5"));
        ((System.Windows.Controls.TextBlock)BtnToggleServer.Content).Text = "Iniciar Servidor";

        VuMeterLeft.Value = 0;
        VuMeterRight.Value = 0;
        TxtBitrate.Text = "0 kbps";
        TxtLatency.Text = "0 ms";
    }

    private void Receiver_StateChanged(ServerState state, string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtStatus.Text = state switch
            {
                ServerState.Stopped => "Servidor parado",
                ServerState.Listening => "À escuta em 127.0.0.1:5000",
                ServerState.Connected => "Telemóvel conectado",
                ServerState.Streaming => "Áudio em reprodução ativa",
                _ => "Desconhecido"
            };

            TxtSubStatus.Text = message;

            switch (state)
            {
                case ServerState.Stopped:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    TxtBadge.Text = "INATIVO";
                    TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    break;
                case ServerState.Listening:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
                    TxtBadge.Text = "AGUARDANDO";
                    TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
                    break;
                case ServerState.Connected:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E"));
                    TxtBadge.Text = "CONECTADO";
                    TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15"));
                    break;
                case ServerState.Streaming:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    TxtBadge.Text = "TRANSMITINDO";
                    TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    break;
            }
        });
    }

    private void Receiver_MetricsUpdated(AudioMetrics metrics)
    {
        Dispatcher.InvokeAsync(() =>
        {
            VuMeterLeft.Value = metrics.PeakVolumeLeft;
            VuMeterRight.Value = metrics.PeakVolumeRight;

            TxtBitrate.Text = $"{metrics.CurrentBitrateKbps:F0} kbps";
            TxtLatency.Text = $"{metrics.BufferLatencyMs} ms";

            double megabytes = metrics.TotalBytesReceived / (1024.0 * 1024.0);
            TxtTotalReceived.Text = $"{megabytes:F2} MB";
            TxtTotalPackets.Text = $"{metrics.TotalPacketsReceived:N0} pacotes";
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _receiver.Stop();
        _receiver.Dispose();
        _audioEngine.Dispose();
    }
}