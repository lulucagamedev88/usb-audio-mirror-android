package com.localcast.audio

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat

class MainActivity : AppCompatActivity(), AudioCaptureService.Companion.ServiceStateListener {
    private lateinit var tvStatus: TextView
    private lateinit var tvStreamDetails: TextView
    private lateinit var tvPacketsSent: TextView
    private lateinit var btnToggleCapture: Button

    private var isCapturing = false
    private lateinit var projectionManager: MediaProjectionManager

    private var captureService: AudioCaptureService? = null
    private var isServiceBound = false
    private var pendingProjection: MediaProjection? = null

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            val binder = service as? AudioCaptureService.LocalBinder
            captureService = binder?.getService()
            isServiceBound = true

            pendingProjection?.let { projection ->
                captureService?.startCaptureWithProjection(projection)
                pendingProjection = null
            }
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            captureService = null
            isServiceBound = false
        }
    }

    private val projectionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK && result.data != null) {
            val projection = projectionManager.getMediaProjection(result.resultCode, result.data!!)
            if (projection != null) {
                if (isServiceBound && captureService != null) {
                    captureService?.startCaptureWithProjection(projection)
                } else {
                    pendingProjection = projection
                }
                setUiCapturing(true)
            } else {
                setUiCapturing(false)
            }
        } else {
            setUiCapturing(false)
            stopAudioService()
        }
    }

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val recordAudioGranted = permissions[Manifest.permission.RECORD_AUDIO] ?: false
        if (recordAudioGranted) {
            startServiceAndRequestProjection()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        tvStatus = findViewById(R.id.tvStatus)
        tvStreamDetails = findViewById(R.id.tvStreamDetails)
        tvPacketsSent = findViewById(R.id.tvPacketsSent)
        btnToggleCapture = findViewById(R.id.btnToggleCapture)

        projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager

        btnToggleCapture.setOnClickListener {
            if (isCapturing) stopAudioService()
            else checkPermissionsAndProceed()
        }
    }

    override fun onResume() {
        super.onResume()
        AudioCaptureService.serviceStateListener = this
    }

    override fun onPause() {
        super.onPause()
        if (AudioCaptureService.serviceStateListener === this) {
            AudioCaptureService.serviceStateListener = null
        }
    }

    private fun checkPermissionsAndProceed() {
        val neededPermissions = mutableListOf(Manifest.permission.RECORD_AUDIO)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            neededPermissions.add(Manifest.permission.POST_NOTIFICATIONS)
        }
        val allGranted = neededPermissions.all {
            ContextCompat.checkSelfPermission(this, it) == PackageManager.PERMISSION_GRANTED
        }
        if (allGranted) startServiceAndRequestProjection()
        else permissionLauncher.launch(neededPermissions.toTypedArray())
    }

    private fun startServiceAndRequestProjection() {
        val serviceIntent = Intent(this, AudioCaptureService::class.java).apply {
            action = AudioCaptureService.ACTION_START_FOREGROUND
        }
        ContextCompat.startForegroundService(this, serviceIntent)
        bindService(serviceIntent, serviceConnection, Context.BIND_AUTO_CREATE)

        val captureIntent = projectionManager.createScreenCaptureIntent()
        projectionLauncher.launch(captureIntent)
    }

    private fun stopAudioService() {
        if (isServiceBound) {
            captureService?.stopCapture()
            try { unbindService(serviceConnection) } catch (_: Exception) {}
            isServiceBound = false
            captureService = null
        }
        val stopIntent = Intent(this, AudioCaptureService::class.java).apply {
            action = AudioCaptureService.ACTION_STOP
        }
        startService(stopIntent)
        setUiCapturing(false)
    }

    private fun setUiCapturing(active: Boolean) {
        isCapturing = active
        if (active) {
            btnToggleCapture.text = getString(R.string.stop_capture)
            btnToggleCapture.setBackgroundColor(ContextCompat.getColor(this, R.color.accent_red))
        } else {
            btnToggleCapture.text = getString(R.string.start_capture)
            btnToggleCapture.setBackgroundColor(ContextCompat.getColor(this, R.color.purple_500))
            tvStatus.text = getString(R.string.status_idle)
        }
    }

    override fun onStateChanged(isRunning: Boolean, status: String) {
        runOnUiThread {
            setUiCapturing(isRunning)
            tvStatus.text = status
        }
    }

    override fun onPacketProgress(totalPackets: Long, totalBytes: Long) {
        runOnUiThread {
            val megaBytes = totalBytes / (1024.0 * 1024.0)
            tvPacketsSent.text = String.format("Pacotes enviados: %,d (%.2f MB)", totalPackets, megaBytes)
        }
    }

    override fun onDestroy() {
        if (isServiceBound) {
            try { unbindService(serviceConnection) } catch (_: Exception) {}
            isServiceBound = false
        }
        super.onDestroy()
    }
}