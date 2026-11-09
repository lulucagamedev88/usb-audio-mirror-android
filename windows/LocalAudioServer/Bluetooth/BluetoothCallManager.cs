using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Calls; // <-- A LINHA QUE FALTAVA AQUI
using Windows.Devices.Enumeration;

namespace LocalAudioServer.Bluetooth;

public enum BluetoothConnectionState { Disconnected, Connecting, Connected, Failed }
public enum CallStatus { Idle, Incoming, Active, Ended }

public sealed class BluetoothCallManager : IDisposable
{
    private PhoneLineTransportDevice? _transportDevice;

    public BluetoothConnectionState ConnectionState { get; private set; } = BluetoothConnectionState.Disconnected;
    public CallStatus CurrentCallStatus { get; private set; } = CallStatus.Idle;
    public string ConnectedDeviceName { get; private set; } = string.Empty;
    public string CallerInfo { get; private set; } = string.Empty;

    public event Action<BluetoothConnectionState, string>? ConnectionStatusChanged;
    public event Action<CallStatus, string>? CallStatusChanged;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            UpdateConnectionState(BluetoothConnectionState.Connecting, "A procurar telemóvel Bluetooth emparelhado...");
            
            string selector = PhoneLineTransportDevice.GetDeviceSelector();
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);

            if (devices.Count == 0)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, "Nenhum telemóvel Bluetooth emparelhado encontrado.");
                return false;
            }

            DeviceInformation targetDevice = devices[0];
            ConnectedDeviceName = targetDevice.Name;
            UpdateConnectionState(BluetoothConnectionState.Connecting, $"A associar a {ConnectedDeviceName}...");

            _transportDevice = PhoneLineTransportDevice.FromId(targetDevice.Id);
            if (_transportDevice == null)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, $"Não foi possível instanciar o dispositivo Bluetooth.");
                return false;
            }

            DeviceAccessStatus accessStatus = await _transportDevice.RequestAccessAsync();
            if (accessStatus != DeviceAccessStatus.Allowed)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, $"Acesso recusado pelo Windows ({accessStatus}).");
                return false;
            }

            bool connectSuccess = await _transportDevice.ConnectAsync();

            if (connectSuccess)
            {
                UpdateConnectionState(BluetoothConnectionState.Connected, $"Conectado via Bluetooth HFP a {ConnectedDeviceName}");
                return true;
            }
            UpdateConnectionState(BluetoothConnectionState.Failed, $"Falha ao estabelecer perfil HFP com {ConnectedDeviceName}.");
            return false;
        }
        catch (Exception ex)
        {
            UpdateConnectionState(BluetoothConnectionState.Failed, $"Erro de conexão Bluetooth: {ex.Message}");
            return false;
        }
    }

    public void NotifyIncomingCall(string callerId)
    {
        CallerInfo = callerId;
        CurrentCallStatus = CallStatus.Incoming;
        CallStatusChanged?.Invoke(CallStatus.Incoming, $"Chamada recebida de: {callerId}");
    }

    public void AnswerCall()
    {
        if (CurrentCallStatus != CallStatus.Incoming) return;
        CurrentCallStatus = CallStatus.Active;
        CallStatusChanged?.Invoke(CallStatus.Active, $"Chamada em curso com {CallerInfo}");
    }

    public void HangUpCall()
    {
        if (CurrentCallStatus == CallStatus.Idle) return;
        CurrentCallStatus = CallStatus.Ended;
        
        CallStatusChanged?.Invoke(CallStatus.Ended, "Chamada terminada.");
        Task.Delay(1000).ContinueWith(_ => {
            CurrentCallStatus = CallStatus.Idle;
            CallerInfo = string.Empty;
            CallStatusChanged?.Invoke(CallStatus.Idle, "Nenhuma chamada em curso.");
        });
    }

    public void Disconnect()
    {
        if (CurrentCallStatus != CallStatus.Idle) HangUpCall();
        _transportDevice = null;
        UpdateConnectionState(BluetoothConnectionState.Disconnected, "Bluetooth desconectado.");
    }

    private void UpdateConnectionState(BluetoothConnectionState state, string message)
    {
        ConnectionState = state;
        ConnectionStatusChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        Disconnect();
    }
}
