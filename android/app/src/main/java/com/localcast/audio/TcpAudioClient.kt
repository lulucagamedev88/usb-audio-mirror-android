package com.localcast.audio

import android.util.Log
import java.io.BufferedOutputStream
import java.io.IOException
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

class TcpAudioClient(
    private val host: String = AudioConfig.TCP_HOST,
    private val port: Int = AudioConfig.TCP_PORT,
    private val listener: StatusListener? = null
) {
    private val tag = "TcpAudioClient"

    interface StatusListener {
        fun onStatusChanged(status: ConnectionStatus, message: String)
        fun onPacketSent(totalPackets: Long, totalBytes: Long)
    }

    enum class ConnectionStatus {
        IDLE, CONNECTING, CONNECTED, RECONNECTING, DISCONNECTED
    }

    private val isRunning = AtomicBoolean(false)
    private val isConnected = AtomicBoolean(false)
    private val audioQueue = ArrayBlockingQueue<ByteArray>(AudioConfig.QUEUE_CAPACITY)

    private var senderThread: Thread? = null
    private var connectionThread: Thread? = null
    private var socket: Socket? = null
    private var outputStream: OutputStream? = null

    private var totalPacketsSent: Long = 0
    private var totalBytesSent: Long = 0

    fun start() {
        if (isRunning.compareAndSet(false, true)) {
            startSenderThread()
            triggerConnect(AudioConfig.RECONNECT_INITIAL_DELAY_MS)
        }
    }

    fun enqueueChunk(data: ByteArray, length: Int) {
        if (!isRunning.get()) return
        val chunkCopy = data.copyOf(length)
        while (!audioQueue.offer(chunkCopy)) {
            audioQueue.poll() // Descarta bloco mais antigo
        }
    }

    private fun startSenderThread() {
        senderThread = Thread({
            while (isRunning.get()) {
                try {
                    val chunk = audioQueue.poll(100, TimeUnit.MILLISECONDS) ?: continue
                    if (isConnected.get()) {
                        val out = outputStream
                        if (out != null) {
                            out.write(chunk)
                            out.flush()
                            totalPacketsSent++
                            totalBytesSent += chunk.size
                            if (totalPacketsSent % 50L == 0L) {
                                listener?.onPacketSent(totalPacketsSent, totalBytesSent)
                            }
                        }
                    }
                } catch (e: IOException) {
                    handleConnectionFailure()
                } catch (e: InterruptedException) {
                    break
                }
            }
        }, "TcpAudioSenderThread")
        senderThread?.start()
    }

    private fun triggerConnect(delayMs: Long) {
        if (!isRunning.get()) return
        connectionThread = Thread({
            try {
                if (delayMs > 0) Thread.sleep(delayMs)
                if (!isRunning.get()) return@Thread

                val newSocket = Socket()
                newSocket.tcpNoDelay = true
                newSocket.soTimeout = AudioConfig.SOCKET_SO_TIMEOUT_MS
                newSocket.sendBufferSize = AudioConfig.CHUNK_SIZE_BYTES * 4
                newSocket.connect(InetSocketAddress(host, port), 2000)

                val out = BufferedOutputStream(newSocket.getOutputStream(), AudioConfig.CHUNK_SIZE_BYTES * 2)
                out.write(byteArrayOf(AudioConfig.PROTOCOL_VERSION))
                out.flush()

                synchronized(this) {
                    socket = newSocket
                    outputStream = out
                    isConnected.set(true)
                }

                listener?.onStatusChanged(ConnectionStatus.CONNECTED, "Conectado ao PC ($host:$port)")
            } catch (e: Exception) {
                handleConnectionFailure(delayMs)
            }
        }, "TcpAudioConnectorThread")
        connectionThread?.start()
    }

    private fun handleConnectionFailure(lastDelayMs: Long = AudioConfig.RECONNECT_INITIAL_DELAY_MS) {
        synchronized(this) {
            isConnected.set(false)
            try { outputStream?.close() } catch (_: Exception) {}
            try { socket?.close() } catch (_: Exception) {}
            outputStream = null
            socket = null
        }
        if (isRunning.get()) {
            val nextDelay = (lastDelayMs * AudioConfig.RECONNECT_MULTIPLIER).toLong()
                .coerceAtMost(AudioConfig.RECONNECT_MAX_DELAY_MS)
            triggerConnect(nextDelay)
        }
    }

    fun stop() {
        isRunning.set(false)
        isConnected.set(false)
        audioQueue.clear()
        try { senderThread?.interrupt() } catch (_: Exception) {}
        try { connectionThread?.interrupt() } catch (_: Exception) {}
        synchronized(this) {
            try { outputStream?.close() } catch (_: Exception) {}
            try { socket?.close() } catch (_: Exception) {}
            outputStream = null
            socket = null
        }
        listener?.onStatusChanged(ConnectionStatus.DISCONNECTED, "Desconectado")
    }

    fun isCurrentlyConnected(): Boolean = isConnected.get()
}