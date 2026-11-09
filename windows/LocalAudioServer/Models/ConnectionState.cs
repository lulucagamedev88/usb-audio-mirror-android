namespace LocalAudioServer.Models;

public enum ServerState
{
    Stopped,
    Listening,
    Connected,
    Streaming
}

public record AudioMetrics(
    long TotalBytesReceived,
    long TotalPacketsReceived,
    double CurrentBitrateKbps,
    int BufferLatencyMs,
    float PeakVolumeLeft,
    float PeakVolumeRight
);