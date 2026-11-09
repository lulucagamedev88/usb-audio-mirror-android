package com.localcast.audio

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import android.os.Binder
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.Process
import android.util.Log
import androidx.core.app.NotificationCompat
import java.util.concurrent.atomic.AtomicBoolean

class AudioCaptureService : Service(), TcpAudioClient.StatusListener {
    private val tag = "AudioCaptureService"

    companion object {
        const val ACTION_START_FOREGROUND = "com.localcast.audio.ACTION_START_FOREGROUND"
        const val ACTION_STOP = "com.localcast.audio.ACTION_STOP"
        const val CHANNEL_ID = "usb_audio_mirror_channel"
        const val NOTIFICATION_ID = 1001

        var serviceStateListener: ServiceStateListener? = null

        interface ServiceStateListener {
            fun onStateChanged(isRunning: Boolean, status: String)
            fun onPacketProgress(totalPackets: Long, totalBytes: Long)
        }
    }

    inner class LocalBinder : Binder() {
        fun getService(): AudioCaptureService = this@AudioCaptureService
    }

    private val binder = LocalBinder()
    private val isRecording = AtomicBoolean(false)
    private val mainHandler = Handler(Looper.getMainLooper())

    private var mediaProjection: MediaProjection? = null
    private var audioRecord: AudioRecord? = null
    private var tcpClient: TcpAudioClient? = null
    private var captureThread: Thread? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        startForegroundWithNotification("A aguardar consentimento de MediaProjection...")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopCapture()
            return START_NOT_STICKY
        }
        startForegroundWithNotification("Serviço ativo em segundo plano...")
        return START_NOT_STICKY
    }

    override fun onBind(intent: Intent?): IBinder = binder

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                getString(R.string.channel_name),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = getString(R.string.channel_description)
                setSound(null, null)
                enableVibration(false)
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager?.createNotificationChannel(channel)
        }
    }

    private fun startForegroundWithNotification(statusText: String) {
        val notification = buildNotification(statusText)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun buildNotification(statusText: String): Notification {
        val stopIntent = Intent(this, AudioCaptureService::class.java).apply { action = ACTION_STOP }
        val stopPendingIntent = PendingIntent.getService(
            this, 0, stopIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_IMMUTABLE else 0)
        )
        val activityIntent = Intent(this, MainActivity::class.java)
        val activityPendingIntent = PendingIntent.getActivity(
            this, 0, activityIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_IMMUTABLE else 0)
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.service_running_title))
            .setContentText(statusText)
            .setSmallIcon(android.R.drawable.ic_lock_silent_mode_off)
            .setContentIntent(activityPendingIntent)
            .addAction(android.R.drawable.ic_media_pause, getString(R.string.stop_capture), stopPendingIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    fun startCaptureWithProjection(projection: MediaProjection) {
        if (isRecording.get()) return
        this.mediaProjection = projection

        projection.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                stopCapture()
            }
        }, Handler(Looper.getMainLooper()))

        val captureConfig = AudioPlaybackCaptureConfiguration.Builder(projection)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
            .build()

        val audioFormat = AudioFormat.Builder()
            .setEncoding(AudioConfig.ENCODING)
            .setSampleRate(AudioConfig.SAMPLE_RATE)
            .setChannelMask(AudioConfig.CHANNEL_MASK)
            .build()

        val minBuf = AudioRecord.getMinBufferSize(AudioConfig.SAMPLE_RATE, AudioConfig.CHANNEL_MASK, AudioConfig.ENCODING)
        val baseBuf = minBuf.coerceAtLeast(7680)
        val remainder = baseBuf % AudioConfig.CHUNK_SIZE_BYTES
        val bufferSizeInBytes = if (remainder == 0) baseBuf else baseBuf + (AudioConfig.CHUNK_SIZE_BYTES - remainder)

        try {
            audioRecord = AudioRecord.Builder()
                .setAudioFormat(audioFormat)
                .setAudioPlaybackCaptureConfig(captureConfig)
                .setBufferSizeInBytes(bufferSizeInBytes)
                .build()

            if (audioRecord?.state != AudioRecord.STATE_INITIALIZED) {
                stopCapture()
                return
            }

            tcpClient = TcpAudioClient(listener = this)
            tcpClient?.start()

            isRecording.set(true)
            audioRecord?.startRecording()

            captureThread = Thread({
                Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_AUDIO)
                val buffer = ByteArray(AudioConfig.CHUNK_SIZE_BYTES)

                while (isRecording.get()) {
                    var bytesReadTotal = 0
                    while (bytesReadTotal < AudioConfig.CHUNK_SIZE_BYTES && isRecording.get()) {
                        val record = audioRecord ?: break
                        val read = record.read(buffer, bytesReadTotal, AudioConfig.CHUNK_SIZE_BYTES - bytesReadTotal, AudioRecord.READ_BLOCKING)
                        if (read > 0) bytesReadTotal += read
                        else if (read < 0) break
                    }

                    if (bytesReadTotal == AudioConfig.CHUNK_SIZE_BYTES) {
                        tcpClient?.enqueueChunk(buffer, bytesReadTotal)
                    }
                }
            }, "AudioRecordCaptureThread")
            captureThread?.start()
            notifyState(true, getString(R.string.status_streaming))
        } catch (e: Exception) {
            stopCapture()
        }
    }

    fun stopCapture() {
        isRecording.set(false)
        try { captureThread?.interrupt(); captureThread = null } catch (_: Exception) {}
        try { audioRecord?.stop(); audioRecord?.release(); audioRecord = null } catch (_: Exception) {}
        try { tcpClient?.stop(); tcpClient = null } catch (_: Exception) {}
        try { mediaProjection?.stop(); mediaProjection = null } catch (_: Exception) {}

        notifyState(false, getString(R.string.status_idle))
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun notifyState(isRunning: Boolean, status: String) {
        mainHandler.post { serviceStateListener?.onStateChanged(isRunning, status) }
    }

    override fun onStatusChanged(status: TcpAudioClient.ConnectionStatus, message: String) {
        mainHandler.post {
            val notification = buildNotification(message)
            val manager = getSystemService(NotificationManager::class.java)
            manager?.notify(NOTIFICATION_ID, notification)
            serviceStateListener?.onStateChanged(isRecording.get(), message)
        }
    }

    override fun onPacketSent(totalPackets: Long, totalBytes: Long) {
        mainHandler.post { serviceStateListener?.onPacketProgress(totalPackets, totalBytes) }
    }

    override fun onDestroy() {
        stopCapture()
        super.onDestroy()
    }
}