using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace LocalAudioServer.Bluetooth;

public enum BluetoothConnectionState { Disconnected, Connecting, Connected, Failed }
public enum CallStatus { Idle, Incoming, Active, Ended }

public sealed class BluetoothCallManager : IDisposable
{
    private PhoneLineTransportDevice? _transportDevice;
    private CallControl? _callControl;
    private ulong _currentCallToken;

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
            InitializeCallControl();

            string selector = PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransportType.Bluetooth);
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
            if (devices.Count == 0)
            {
                string fallbackSelector = PhoneLineTransportDevice.GetDeviceSelector();
                devices = await DeviceInformation.FindAllAsync(fallbackSelector);
            }

            if (devices.Count == 0)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, "Nenhum telemóvel Bluetooth emparelhado com suporte a chamadas HFP foi encontrado.");
                return false;
            }

            DeviceInformation targetDevice = devices[0];
            ConnectedDeviceName = targetDevice.Name;
            UpdateConnectionState(BluetoothConnectionState.Connecting, $"A associar a {ConnectedDeviceName}...");

            _transportDevice = PhoneLineTransportDevice.FromId(targetDevice.Id);
            if (_transportDevice == null)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, $"Não foi possível instanciar PhoneLineTransportDevice.");
                return false;
            }

            DeviceAccessStatus accessStatus = await _transportDevice.RequestAccessAsync();
            if (accessStatus != DeviceAccessStatus.Allowed)
            {
                UpdateConnectionState(BluetoothConnectionState.Failed, $"Acesso recusado pelo Windows ({accessStatus}).");
                return false;
            }

            bool connectSuccess = await _transportDevice.ConnectAsync();
            if (!connectSuccess && _transportDevice.IsConnected()) connectSuccess = true;

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

    private void InitializeCallControl()
    {
        try
        {
            if (_callControl != null) return;
            _callControl = CallControl.GetDefault();
            if (_callControl != null)
            {
                _callControl.AnswerRequested += CallControl_AnswerRequested;
                _callControl.HangUpRequested += CallControl_HangUpRequested;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Erro CallControl: {ex.Message}"); }
    }

    private void CallControl_AnswerRequested(CallControl sender) => AnswerCall();
    private void CallControl_HangUpRequested(CallControl sender) => HangUpCall();

    public void NotifyIncomingCall(string callerId)
    {
        CallerInfo = callerId;
        CurrentCallStatus = CallStatus.Incoming;
        try { if (_callControl != null) _currentCallToken = _callControl.IndicateNewIncomingCall(true, callerId); } catch { }
        CallStatusChanged?.Invoke(CallStatus.Incoming, $"Chamada recebida de: {callerId}");
    }

    public void AnswerCall()
    {
        if (CurrentCallStatus != CallStatus.Incoming) return;
        CurrentCallStatus = CallStatus.Active;
        try { if (_callControl != null && _currentCallToken != 0) _callControl.IndicateActiveCall(_currentCallToken); } catch { }
        CallStatusChanged?.Invoke(CallStatus.Active, $"Chamada em curso com {CallerInfo}");
    }

    public void HangUpCall()
    {
        if (CurrentCallStatus == CallStatus.Idle) return;
        CurrentCallStatus = CallStatus.Ended;
        try { if (_callControl != null && _currentCallToken != 0) { _callControl.IndicateHangUp(_currentCallToken); _currentCallToken = 0; } } catch { }
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
        if (_callControl != null)
        {
            _callControl.AnswerRequested -= CallControl_AnswerRequested;
            _callControl.HangUpRequested -= CallControl_HangUpRequested;
            _callControl = null;
        }
    }
}
