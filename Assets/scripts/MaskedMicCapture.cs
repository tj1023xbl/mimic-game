using Dissonance.Audio.Capture;
using JetBrains.Annotations;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Replaces Dissonance's default microphone capture (BasicMicrophoneCapture).
///
/// Dissonance finds this component because it sits on the same GameObject as DissonanceComms
/// and implements IMicrophoneCapture. Dissonance then calls into it every frame, and whatever
/// audio we hand to its subscribers is what gets encoded and sent to other players.
///
/// Two modes (toggled with the maskingEnabled checkbox / MaskingEnabled property):
///
///   MASKING ON:   real mic -> simple VAD -> speech segment -> Whisper (STT) -> TTS -> Dissonance
///                 The real voice never leaves this machine. Dissonance only receives silence
///                 while idle and the synthesized voice while "speaking".
///
///   MASKING OFF:  real mic -> Dissonance (plain pass-through, like the default capture).
/// </summary>
public class MaskedMicCapture : MonoBehaviour, IMicrophoneCapture
{
    // =====================================================================
    // Inspector settings
    // (Field names are unchanged so existing inspector assignments and tuned values survive.)
    // =====================================================================

    /// <summary>Component that wraps WhisperManager and turns audio into text.</summary>
    [SerializeField] Transcriber transcriber;
    [SerializeField] TtsController tts;

    [Tooltip("On: STT -> TTS masking. Off: send the real mic audio.")]
    [SerializeField] bool maskingEnabled = true;

    [Header("Simple energy VAD (placeholder for Silero)")]

    /// <summary>Loudness (RMS) above which a chunk of mic audio counts as speech.</summary>
    [SerializeField] float speechThreshold = 0.015f;

    /// <summary>How long it must stay quiet before we decide the speaker has finished.</summary>
    [SerializeField] float hangoverSeconds = 0.6f;

    /// <summary>Segments with less actual speech than this are ignored (coughs, clicks, etc.).</summary>
    [SerializeField] float minSpeechSeconds = 0.3f;

    /// <summary>Force-send a segment to Whisper if someone talks for this long without pausing.</summary>
    [SerializeField] float maxSegmentSeconds = 15f;

    // =====================================================================
    // Constants
    // =====================================================================

    const int OutputSampleRate = 48000;       // Sample rate of the audio we hand to Dissonance.
    const int WhisperSampleRate = 16000;      // Sample rate Whisper expects.
    const int SamplesPerFrame = 960;          // One 20 ms frame at 48 kHz (48000 * 0.02).
    const float FrameDurationSeconds = 0.02f; // Dissonance wants audio in 20 ms frames.
    const int TtsSampleRate = 24000;          // Sample rate of the placeholder TTS output.

    // =====================================================================
    // Private state
    // =====================================================================

    // --- Dissonance-facing ---
    /// <summary>Objects inside Dissonance that want our audio. We push every frame to each of them.</summary>
    readonly List<IMicrophoneSubscriber> _micSubscribers = new List<IMicrophoneSubscriber>();

    /// <summary>Audio format we promise Dissonance: 48 kHz, mono.</summary>
    readonly WaveFormat _outputFormat = new WaveFormat(OutputSampleRate, 1);

    /// <summary>
    /// Queue of ready-to-send 20 ms frames. In masking mode it holds synthesized speech;
    /// in pass-through mode it holds real mic audio. Accessed under lock.
    /// </summary>
    readonly Queue<float[]> _outgoingFrames = new Queue<float[]>();

    /// <summary>Reusable frame of zeros, sent whenever there is nothing to say.</summary>
    readonly float[] _silenceFrame = new float[SamplesPerFrame];

    // --- Microphone ---
    /// <summary>Unity's looping recording buffer for the real mic.</summary>
    AudioClip _micClip;

    /// <summary>Mic name in the form Unity's Microphone API wants (null = default device).</summary>
    string _unityMicDeviceName;

    /// <summary>Mic name exactly as Dissonance gave it to us; reported back through the Device property.</summary>
    string _dissonanceMicName;

    /// <summary>Actual sample rate of the mic (the device may not honor our 48 kHz request).</summary>
    int _micSampleRate;

    /// <summary>Position in the mic buffer up to which we've already read.</summary>
    int _lastReadPosition;

    // --- Voice activity detection / segmentation (masking mode) ---
    /// <summary>Mic samples collected for the utterance in progress (plus a short pre-roll before speech starts).</summary>
    readonly List<float> _speechSegmentSamples = new List<float>();
    /// <summary>True once speech has been detected and we're collecting an utterance.</summary>
    bool _isInSpeechSegment;
    /// <summary>How long it has been quiet since the last loud chunk (counts toward hangoverSeconds).</summary>
    float _silenceDurationSeconds;
    /// <summary>Total amount of actual speech heard in the current segment (compared to minSpeechSeconds).</summary>
    float _speechDurationSeconds;

    // --- Mode handling / pacing ---
    /// <summary>The mode we're actually running; may lag behind maskingEnabled for one frame until SwitchMode runs.</summary>
    bool _isMaskingActive;

    /// <summary>
    /// Incremented on every mode switch. Background STT/TTS work remembers the value it started with
    /// and discards its result if the number has changed, so stale audio can't leak out.
    /// </summary>
    int _modeGeneration;

    /// <summary>Wall-clock time accumulated toward the next 20 ms frame (masking-mode pacing).</summary>
    float _frameTimeAccumulator;

    /// <summary>Leftover real-mic samples (already at 48 kHz) that don't yet fill a whole 20 ms frame.</summary>
    readonly List<float> _passThroughLeftoverSamples = new List<float>();

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>Turn masking on or off at runtime. Takes effect on the next UpdateSubscribers call.</summary>
    public bool MaskingEnabled { get => maskingEnabled; set => maskingEnabled = value; }

    // =====================================================================
    // IMicrophoneCapture implementation (called by Dissonance)
    // =====================================================================

    /// <summary>Tells Dissonance whether we are currently capturing.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>
    /// Estimated delay between sound hitting the mic and being passed on. Dissonance's docs say
    /// to leave this at zero if unknown. (Masking adds seconds of delay, but that isn't mic latency.)
    /// </summary>
    public TimeSpan Latency => TimeSpan.Zero;

    /// <summary>
    /// Name of the mic in use. Dissonance compares this against the mic name you configure, so it
    /// must return the same value Dissonance passed to StartCapture.
    /// </summary>
    public string Device => _dissonanceMicName;

    /// <summary>
    /// Called by Dissonance when it wants audio to start flowing. Opens the real microphone and
    /// returns the format we will deliver audio in. Returning null tells Dissonance we could not start.
    /// </summary>
    public WaveFormat StartCapture([CanBeNull] string micDeviceName)
    {
        // Keep the raw name for Dissonance, and a normalized one for Unity (null means default mic).
        _dissonanceMicName = micDeviceName;
        _unityMicDeviceName = string.IsNullOrEmpty(micDeviceName) ? null : micDeviceName;

        // Start a looping 10 second recording buffer. Unity keeps writing into it continuously
        // and we read new samples out of it each frame (see ReadNewMicSamples).
        _micClip = Microphone.Start(_unityMicDeviceName, true, 10, OutputSampleRate);
        if (_micClip == null) return null;

        _micSampleRate = _micClip.frequency;   // the device may not give exactly 48 kHz
        _lastReadPosition = 0;
        _frameTimeAccumulator = 0;
        _isMaskingActive = maskingEnabled;
        IsRecording = true;
        return _outputFormat;
    }

    /// <summary>Called by Dissonance to stop capturing. Closes the mic and throws away any pending audio.</summary>
    public void StopCapture()
    {
        Microphone.End(_unityMicDeviceName);
        _micClip = null;
        IsRecording = false;
        _speechSegmentSamples.Clear();
        _passThroughLeftoverSamples.Clear();
        _isInSpeechSegment = false;
        lock (_outgoingFrames) _outgoingFrames.Clear();
    }

    /// <summary>Dissonance registers a listener that wants to receive our audio frames.</summary>
    public void Subscribe(IMicrophoneSubscriber subscriber) => _micSubscribers.Add(subscriber);

    /// <summary>Dissonance removes a listener it previously registered.</summary>
    public bool Unsubscribe(IMicrophoneSubscriber subscriber) => _micSubscribers.Remove(subscriber);

    /// <summary>
    /// Called by Dissonance once per frame. This is the heart of the class:
    ///   1. Apply a pending masking on/off change.
    ///   2. Read any new audio from the real mic (and run VAD / pass-through on it).
    ///   3. Deliver 20 ms frames of audio to Dissonance.
    /// </summary>
    /// <returns>Return true only to ask Dissonance to reset the capture pipeline (we never need to).</returns>
    public bool UpdateSubscribers()
    {
        // 1. React to the inspector checkbox / property changing.
        if (maskingEnabled != _isMaskingActive) 
            SwitchMode(maskingEnabled);

        // 2. Pull in whatever the real mic recorded since last frame.
        ReadNewMicSamples();

        // 3. Hand audio to Dissonance.
        if (_isMaskingActive)
        {
            // Masking mode: Dissonance expects audio at real-time speed, so we send exactly one
            // 20 ms frame per 20 ms of wall-clock time. If a frame hitches, the loop catches up.
            // When no synthesized speech is queued we send silence to keep the stream continuous.
            _frameTimeAccumulator += Time.unscaledDeltaTime;
            while (_frameTimeAccumulator >= FrameDurationSeconds)
            {
                _frameTimeAccumulator -= FrameDurationSeconds;
                DeliverFrame(TryDequeueFrame() ?? GetSilenceFrame());
            }
        }
        else
        {
            // Pass-through mode: the mic hardware already paces itself, so just forward every
            // complete frame we've built from it.
            float[] frame;
            while ((frame = TryDequeueFrame()) != null) DeliverFrame(frame);
        }

        return false;
    }

    // =====================================================================
    // Frame delivery helpers
    // =====================================================================

    /// <summary>Takes the next ready frame from the queue, or returns null if the queue is empty.</summary>
    float[] TryDequeueFrame()
    {
        lock (_outgoingFrames) return _outgoingFrames.Count > 0 ? _outgoingFrames.Dequeue() : null;
    }

    /// <summary>Returns the shared silence frame, cleared to zeros in case a subscriber modified it.</summary>
    float[] GetSilenceFrame()
    {
        Array.Clear(_silenceFrame, 0, _silenceFrame.Length);
        return _silenceFrame;
    }

    /// <summary>Sends one 20 ms frame to every Dissonance subscriber.</summary>
    void DeliverFrame(float[] frame)
    {
        var frameSegment = new ArraySegment<float>(frame);
        foreach (var subscriber in _micSubscribers)
            subscriber.ReceiveMicrophoneData(frameSegment, _outputFormat);
    }

    /// <summary>
    /// Applies a change between masking and pass-through. Resets all in-progress state so audio
    /// from the old mode can't bleed into the new one, including results from background STT/TTS
    /// work that is still running (those are invalidated via the generation counter).
    /// </summary>
    void SwitchMode(bool maskingNowEnabled)
    {
        _isMaskingActive = maskingNowEnabled;
        _modeGeneration++;   // in-flight STT/TTS results from the old mode will be discarded
        _speechSegmentSamples.Clear();
        _passThroughLeftoverSamples.Clear();
        _isInSpeechSegment = false;
        _silenceDurationSeconds = 0;
        _speechDurationSeconds = 0;
        _frameTimeAccumulator = 0;
        lock (_outgoingFrames) _outgoingFrames.Clear();
    }

    // =====================================================================
    // Reading the real microphone
    // =====================================================================

    /// <summary>
    /// Reads whatever the mic has recorded since the last call and passes it to ProcessMicChunk.
    /// Unity's mic recording is a looping buffer: it writes forward and wraps back to the start,
    /// so "new audio" is everything between our last read position and the current write position,
    /// which may require reading the end of the buffer and then the beginning.
    /// </summary>
    void ReadNewMicSamples()
    {
        if (_micClip == null) return;

        int currentPosition = Microphone.GetPosition(_unityMicDeviceName);

        if (currentPosition < 0 || currentPosition == _lastReadPosition) 
            return; // no new audio

        float[] newSamples;
        if (currentPosition > _lastReadPosition)
        {
            // Simple case: the new audio is one contiguous block.
            newSamples = new float[currentPosition - _lastReadPosition];
            _micClip.GetData(newSamples, _lastReadPosition);
        }
        else
        {
            // The write position wrapped around. Read from our last position to the end of the
            // buffer, then from the start of the buffer to the current position, and join them.
            var endOfBuffer = new float[_micClip.samples - _lastReadPosition];
            _micClip.GetData(endOfBuffer, _lastReadPosition);

            var startOfBuffer = new float[currentPosition];
            if (currentPosition > 0) _micClip.GetData(startOfBuffer, 0);

            newSamples = new float[endOfBuffer.Length + startOfBuffer.Length];
            endOfBuffer.CopyTo(newSamples, 0);
            startOfBuffer.CopyTo(newSamples, endOfBuffer.Length);
        }

        _lastReadPosition = currentPosition;
        ProcessMicChunk(newSamples);
    }

    /// <summary>
    /// Handles a fresh chunk of real mic audio according to the current mode.
    ///
    /// Masking mode: a very simple voice activity detector (VAD). It measures loudness, starts
    /// collecting an utterance when the mic gets loud, and ends it after enough quiet time.
    /// Before speech starts, the most recent ~300 ms are kept as "pre-roll" so the first syllable
    /// isn't cut off. When the utterance ends it is sent off for transcription (see FlushSpeechSegment).
    ///
    /// Pass-through mode: forwards the audio untouched (see PassThrough).
    /// </summary>
    void ProcessMicChunk(float[] micChunk)
    {
        if (!_isMaskingActive) { PassThrough(micChunk); return; }

        // Loudness of this chunk as RMS (root mean square) of the samples.
        float rmsLoudness = 0;

        for (int i = 0; i < micChunk.Length; i++)
        {
            rmsLoudness += micChunk[i] * micChunk[i];
        }
        
        rmsLoudness = Mathf.Sqrt(rmsLoudness / Mathf.Max(1, micChunk.Length));

        float chunkDurationSeconds = micChunk.Length / (float)_micSampleRate;

        // Always collect audio; we trim it below if no speech has started yet.
        _speechSegmentSamples.AddRange(micChunk);

        if (rmsLoudness > speechThreshold)
        {
            // Loud enough: we're in speech, and the quiet-time counter restarts.
            _isInSpeechSegment = true;
            _silenceDurationSeconds = 0;
            _speechDurationSeconds += chunkDurationSeconds;
        }
        else if (_isInSpeechSegment)
        {
            // Quiet, but we were mid-utterance: count how long the pause has lasted.
            _silenceDurationSeconds += chunkDurationSeconds;
        }

        if (!_isInSpeechSegment)
        {
            // No speech yet: keep only the latest ~300 ms as pre-roll and discard older audio.
            int preRollSampleCount = _micSampleRate * 3 / 10;
            if (_speechSegmentSamples.Count > preRollSampleCount)
                _speechSegmentSamples.RemoveRange(0, _speechSegmentSamples.Count - preRollSampleCount);
            return;
        }

        // The utterance is over if the pause was long enough, or it has run too long.
        bool segmentTooLong = _speechSegmentSamples.Count > maxSegmentSeconds * _micSampleRate;
        if (_silenceDurationSeconds >= hangoverSeconds || segmentTooLong)
            FlushSpeechSegment();
    }

    /// <summary>
    /// Pass-through mode: resamples the real mic audio to 48 kHz, cuts it into 20 ms frames, and
    /// queues them for delivery. Samples that don't fill a whole frame are kept for the next call.
    /// </summary>
    void PassThrough(float[] micChunk)
    {
        _passThroughLeftoverSamples.AddRange(Resample(micChunk, _micSampleRate, OutputSampleRate));

        lock (_outgoingFrames)
        {
            while (_passThroughLeftoverSamples.Count >= SamplesPerFrame)
            {
                _outgoingFrames.Enqueue(_passThroughLeftoverSamples.GetRange(0, SamplesPerFrame).ToArray());
                _passThroughLeftoverSamples.RemoveRange(0, SamplesPerFrame);
            }
        }
    }

    /// <summary>
    /// Called when VAD decides an utterance has finished. Takes the collected audio, resets the
    /// VAD state for the next utterance, and (if there was enough real speech) starts the
    /// transcribe-then-synthesize work in the background.
    /// </summary>
    void FlushSpeechSegment()
    {
        var utteranceSamples = _speechSegmentSamples.ToArray();
        bool hasEnoughSpeech = _speechDurationSeconds >= minSpeechSeconds;

        _speechSegmentSamples.Clear();
        _isInSpeechSegment = false;
        _silenceDurationSeconds = 0;
        _speechDurationSeconds = 0;

        // Fire and forget: MaskAsync handles its own errors.
        if (hasEnoughSpeech) _ = MaskAsync(utteranceSamples);
    }

    // =====================================================================
    // Masking pipeline: speech -> text -> synthesized speech -> queued frames
    // =====================================================================

    /// <summary>
    /// Runs the masking pipeline for one finished utterance:
    ///   1. Resample to 16 kHz and transcribe with Whisper.
    ///   2. Synthesize speech from the text.
    ///   3. Resample to 48 kHz and queue it as 20 ms frames; UpdateSubscribers then plays them out.
    /// If the masking mode changed while this was running, the result is thrown away.
    /// </summary>
    async Task MaskAsync(float[] utteranceSamples)
    {
        int generationAtStart = _modeGeneration;
        try
        {
            // Step 1: speech to text.
            float[] samplesForWhisper = Resample(utteranceSamples, _micSampleRate, WhisperSampleRate);
            string transcript = await transcriber.TranscribeAsync(samplesForWhisper, WhisperSampleRate);
            if (generationAtStart != _modeGeneration || string.IsNullOrWhiteSpace(transcript)) return;
            Debug.Log($"[Mask] heard: {transcript}");

            // Step 2: text to speech.
            float[] ttsAudio = await SynthesizeAsync(transcript);
            if (generationAtStart != _modeGeneration) return;

            // Step 3: convert to Dissonance's format and cut into 20 ms frames.
            float[] outputAudio = Resample(ttsAudio, TtsSampleRate, OutputSampleRate);
            lock (_outgoingFrames)
            {
                for (int offset = 0; offset < outputAudio.Length; offset += SamplesPerFrame)
                {
                    // The last frame may be short; the rest of it stays zero (silence).
                    var frame = new float[SamplesPerFrame];
                    int samplesToCopy = Math.Min(SamplesPerFrame, outputAudio.Length - offset);
                    Array.Copy(outputAudio, offset, frame, 0, samplesToCopy);
                    _outgoingFrames.Enqueue(frame);
                }
            }
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>
    /// PLACEHOLDER: replace with a real TTS. Emits one short beep per word so you can test the
    /// wiring. Must return mono samples at TtsSampleRate.
    /// </summary>
    async Task<float[]> SynthesizeAsync(string text)
    {
        float[] ttsAudio = await tts.GenerateTtsAsync(text);
        return ttsAudio;
    }

    // =====================================================================
    // Utility
    // =====================================================================

    /// <summary>
    /// Converts audio from one sample rate to another.
    ///   Downsampling (e.g. 48 kHz -> 16 kHz): averages each group of source samples, which
    ///   reduces the aliasing you'd get from just dropping samples.
    ///   Upsampling (e.g. 24 kHz -> 48 kHz): linear interpolation between neighboring samples.
    /// Simple and good enough for a prototype; a proper resampler would sound cleaner.
    /// </summary>
    static float[] Resample(float[] sourceSamples, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || sourceSamples.Length == 0) return sourceSamples;

        int targetLength = (int)((long)sourceSamples.Length * targetRate / sourceRate);
        var resampled = new float[targetLength];

        // How many source samples correspond to one output sample (>1 means downsampling).
        float sourceStepPerOutputSample = (float)sourceRate / targetRate;

        for (int i = 0; i < targetLength; i++)
        {
            // Where this output sample falls in the source audio (fractional index).
            float sourcePosition = i * sourceStepPerOutputSample;

            if (sourceStepPerOutputSample > 1f)
            {
                // Downsampling: average the window of source samples this output sample covers.
                int windowStart = (int)sourcePosition;
                int windowEnd = Mathf.Min((int)(sourcePosition + sourceStepPerOutputSample), sourceSamples.Length);
                float windowSum = 0;
                for (int k = windowStart; k < windowEnd; k++) windowSum += sourceSamples[k];

                resampled[i] = windowEnd > windowStart
                    ? windowSum / (windowEnd - windowStart)
                    : sourceSamples[Mathf.Min(windowStart, sourceSamples.Length - 1)];
            }
            else
            {
                // Upsampling: blend the two source samples on either side of the position.
                int lowerIndex = (int)sourcePosition;
                int upperIndex = Mathf.Min(lowerIndex + 1, sourceSamples.Length - 1);
                resampled[i] = Mathf.Lerp(sourceSamples[lowerIndex], sourceSamples[upperIndex], sourcePosition - lowerIndex);
            }
        }
        return resampled;
    }
}
