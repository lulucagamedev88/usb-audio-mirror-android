using System;
using System.Windows;
using System.Windows.Media;
using LocalAudioServer.Audio;
using LocalAudioServer.Bluetooth;
using LocalAudioServer.Models;
using LocalAudioServer.Network;

namespace LocalAudioServer;

public partial class MainWindow : Window
{
    private readonly AudioPlaybackEngine _audioEngine;
    private readonly TcpAudioReceiver _receiver;
    private readonly BluetoothCallManager _callManager;
    private bool _isServerRunning;
    private bool _isBluetoothConnecting;

    public MainWindow()
    {
        InitializeComponent();

        _audioEngine = new AudioPlaybackEngine();
        _receiver = new TcpAudioReceiver(_audioEngine, port: 5000);
        _receiver.StateChanged += Receiver_StateChanged;
        _receiver.MetricsUpdated += Receiver_MetricsUpdated;

        _callManager = new BluetoothCallManager();
        _callManager.ConnectionStatusChanged += CallManager_ConnectionStatusChanged;
        _callManager.CallStatusChanged += CallManager_CallStatusChanged;

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
            BtnToggleServer.Content = "Parar Servidor USB";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao iniciar servidor: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServer()
    {
        _receiver.Stop();
        _audioEngine.Stop();
        _isServerRunning = false;
        BtnToggleServer.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5"));
        BtnToggleServer.Content = "Iniciar Servidor USB";
        VuMeterLeft.Value = 0; VuMeterRight.Value = 0;
        TxtBitrate.Text = "0 kbps"; TxtLatency.Text = "0 ms";
        TxtTotalReceived.Text = "0 MB"; TxtTotalPackets.Text = "0 pacotes";
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
                    TxtBadge.Text = "INATIVO"; TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")); break;
                case ServerState.Listening:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
                    TxtBadge.Text = "AGUARDANDO"; TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA")); break;
                case ServerState.Connected:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E"));
                    TxtBadge.Text = "CONECTADO"; TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15")); break;
                case ServerState.Streaming:
                    BadgeState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    TxtBadge.Text = "TRANSMITINDO"; TxtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")); break;
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

    private async void BtnToggleBluetooth_Click(object sender, RoutedEventArgs e)
    {
        if (_callManager.ConnectionState == BluetoothConnectionState.Connected)
        {
            _callManager.Disconnect();
        }
        else
        {
            if (_isBluetoothConnecting) return;
            _isBluetoothConnecting = true;
            BtnToggleBluetooth.IsEnabled = false;

            bool success = await _callManager.ConnectAsync();

            _isBluetoothConnecting = false;
            BtnToggleBluetooth.IsEnabled = true;

            if (!success)
            {
                MessageBox.Show("Não foi possível conectar ao Bluetooth.\nCertifique-se de que o telemóvel está emparelhado e o Bluetooth ligado.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void BtnAnswerCall_Click(object sender, RoutedEventArgs e) => _callManager.AnswerCall();
    private void BtnHangUpCall_Click(object sender, RoutedEventArgs e) => _callManager.HangUpCall();

    private void BtnSimulateCall_Click(object sender, RoutedEventArgs e)
    {
        if (_callManager.CurrentCallStatus == CallStatus.Idle)
            _callManager.NotifyIncomingCall("+351 912 345 678");
        else
            _callManager.HangUpCall();
    }

    private void CallManager_ConnectionStatusChanged(BluetoothConnectionState state, string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtBtStatus.Text = state switch
            {
                BluetoothConnectionState.Disconnected => "Bluetooth desligado",
                BluetoothConnectionState.Connecting => "A ligar Bluetooth...",
                BluetoothConnectionState.Connected => $"Conectado: {_callManager.ConnectedDeviceName}",
                BluetoothConnectionState.Failed => "Falha na conexão Bluetooth",
                _ => "Desconhecido"
            };
            TxtBtSubStatus.Text = message;

            switch (state)
            {
                case BluetoothConnectionState.Disconnected:
                    BadgeBtState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    TxtBtBadge.Text = "DESCONECTADO"; TxtBtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    BtnToggleBluetooth.Content = "Ligar Bluetooth HFP"; BtnToggleBluetooth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7")); break;
                case BluetoothConnectionState.Connecting:
                    BadgeBtState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E"));
                    TxtBtBadge.Text = "A LIGAR"; TxtBtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15")); break;
                case BluetoothConnectionState.Connected:
                    BadgeBtState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    TxtBtBadge.Text = "CONECTADO HFP"; TxtBtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    BtnToggleBluetooth.Content = "Desligar Bluetooth"; BtnToggleBluetooth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")); break;
                case BluetoothConnectionState.Failed:
                    BadgeBtState.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));
                    TxtBtBadge.Text = "FALHA"; TxtBtBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                    BtnToggleBluetooth.Content = "Tentar Novamente"; BtnToggleBluetooth.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7")); break;
            }
        });
    }

    private void CallManager_CallStatusChanged(CallStatus status, string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            switch (status)
            {
                case CallStatus.Idle:
                    TxtCallState.Text = "Nenhuma chamada em curso"; TxtCallerId.Text = "Linha livre";
                    BadgeCallStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                    TxtCallBadge.Text = "INATIVO"; TxtCallBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                    BtnAnswerCall.IsEnabled = false; BtnHangUpCall.IsEnabled = false; break;
                case CallStatus.Incoming:
                    TxtCallState.Text = "Chamada a receber!"; TxtCallerId.Text = message;
                    BadgeCallStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#854D0E"));
                    TxtCallBadge.Text = "A TOCAR..."; TxtCallBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15"));
                    BtnAnswerCall.IsEnabled = true; BtnHangUpCall.IsEnabled = true; break;
                case CallStatus.Active:
                    TxtCallState.Text = "Chamada em curso (HFP)"; TxtCallerId.Text = message;
                    BadgeCallStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    TxtCallBadge.Text = "EM CHAMADA"; TxtCallBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    BtnAnswerCall.IsEnabled = false; BtnHangUpCall.IsEnabled = true; break;
                case CallStatus.Ended:
                    TxtCallState.Text = "Chamada terminada"; TxtCallerId.Text = message;
                    BadgeCallStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    TxtCallBadge.Text = "TERMINADA"; TxtCallBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    BtnAnswerCall.IsEnabled = false; BtnHangUpCall.IsEnabled = false; break;
            }
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _receiver.Stop();
        _receiver.Dispose();
        _audioEngine.Dispose();
        _callManager.Dispose();
    }
}
