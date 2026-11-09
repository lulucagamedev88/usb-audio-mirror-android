package com.localcast.audio

import android.media.AudioFormat

/**
 * Configurações de áudio, rede e constantes do protocolo para a Fase 1.
 */
object AudioConfig {
    const val SAMPLE_RATE = 48000
    const val ENCODING = AudioFormat.ENCODING_PCM_16BIT
    const val CHANNEL_MASK = AudioFormat.CHANNEL_IN_STEREO
    const val CHANNELS = 2
    const val BYTES_PER_SAMPLE = 2 // 16-bit
    const val BYTES_PER_FRAME = CHANNELS * BYTES_PER_SAMPLE // 4 bytes

    const val CHUNK_DURATION_MS = 20
    const val FRAMES_PER_CHUNK = (SAMPLE_RATE * CHUNK_DURATION_MS) / 1000 // 960 frames
    const val CHUNK_SIZE_BYTES = FRAMES_PER_CHUNK * BYTES_PER_FRAME // 3840 bytes

    // Byte de versão inicial enviado no handshake TCP (Fase 1 = 0x01)
    const val PROTOCOL_VERSION: Byte = 0x01

    const val TCP_HOST = "127.0.0.1"
    const val TCP_PORT = 5000

    const val QUEUE_CAPACITY = 10
    const val SOCKET_SO_TIMEOUT_MS = 200

    const val RECONNECT_INITIAL_DELAY_MS = 500L
    const val RECONNECT_MAX_DELAY_MS = 5000L
    const val RECONNECT_MULTIPLIER = 1.5f
}