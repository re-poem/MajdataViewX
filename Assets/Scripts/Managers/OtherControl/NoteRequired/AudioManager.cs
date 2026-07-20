#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using MajSimai;
using ManagedBass;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static MajCtx;

#endregion

public class AudioManager
{
    public const int SFX_COUNT = 16;

    [CanBeNull] private AudioSample TrackSample;
    [CanBeNull] private float[] TrackSampleData;
    private float TrackSampleVolume;
    public bool IsTrackLoaded => TrackSample != null && TrackSampleData != null;
    public bool IsTrackPlaying => TrackSample != null && TrackSample.IsPlaying;
    public double TrackCurrentSec => TrackSample != null ? TrackSample.CurrentSec : 0;

    //answer SFX
    List<AnswerTimingPoint> answerTimingPoints = new();
    private readonly object answerSfxLock = new();
    //note SFX
    //DO NOT USE THIS IN THIS FILE, MAY CAUSE AtomicSafetyHandle Exception, WE DONT CARE THIS
    public NativeArray<bool> noteSfxPlaybackRequests = new(SFX_COUNT, Allocator.Persistent);
    public unsafe bool* SfxRequestsPtr => (bool*)noteSfxPlaybackRequests.GetUnsafePtr();
    private unsafe bool* _sfxPtr;    // USE THIS INSTEAD

    List<AudioSample> NoteSfxs = new(SFX_COUNT);

    //for recording
    private List<float[]> noteSfxSamplesData = new(SFX_COUNT);
    private NativeArray<float> recordingBuffer;
    private int recordingSampleCount;
    private int recordingCommittedSampleCount;
    private int recordingPreviousTouchHoldCount;
    private float recordingInitialAudioTime;
    private float recordingSpeed = 1f;
    private float recordingInitialNoteTime;
    private int[][] sfxPlayPointers = new int[SFX_COUNT][]; //-1 is not playing

    public double GlobalAudioOffset { get; private set; }

    public bool IsShowingSongDetail => _timeProvider.AudioTime <= recordingInitialAudioTime + TimeProvider.SONG_DETAIL_OFFSET;
    const int SAMPLERATE = 44100;
    const int CHANNELS = 2;

    public const float TRACK_ANSWER_PLAYBACK_OFFSET_SEC = (16.66666f * 1) / 1000;

    public const int TAP_PERFECT = 0;
    public const int TAP_GREAT = 1;
    public const int TAP_GOOD = 2;
    public const int TAP_EX = 3;
    public const int BREAK_JUDGE = 4;
    public const int BREAK_SFX = 5;
    public const int SLIDE = 6;
    public const int BREAK_SLIDE = 7;
    public const int BREAK_SLIDE_JUDGE = 8;
    public const int TOUCH = 9;
    public const int TOUCHHOLD = 10;
    public const int FIREWORK = 11;
    public const int ANSWER = 12;
    public const int ANSWER_CLOCK = 13;
    public const int TRACK_START = 14;
    public const int ALL_PERFECT = 15;

    private bool waitingForTrackAudioStart;

    private bool isInited;

    public int ActiveTouchHoldCount { get; set; }
    private int _prevActiveTouchHoldCount;

    public unsafe AudioManager()
    {
        _audioManager = this;
        _sfxPtr = SfxRequestsPtr;
        Bass.Configure(Configuration.UpdatePeriod, 20);
        Bass.Configure(Configuration.PlaybackBufferLength, 40);
        Bass.Init(-1, 44100);

        //Note SFX
        var sfxPath = MajEnv.GetPath("SFX");
        int sfxIndex = 0;
        foreach (var filename in new[]
                {
                    "tap_perfect.wav",
                    "tap_great.wav",
                    "tap_good.wav",
                    "tap_ex.wav",
                    "break_tap.wav",
                    "break.wav",
                    "slide.wav",
                    "slide_break_start.wav",
                    "slide_break_slide.wav",
                    "touch.wav",
                    "touch_Hold_riser.wav",
                    "touch_hanabi.wav",
                    "answer.wav",
                    "answer_clock.wav",
                    "track_start.wav",
                    "all_perfect.wav"
                })
        {
            //sample
            var path = Path.Combine(sfxPath, filename);
            var type = filename switch
            {
                "track_start.wav" or "all_perfect.wav" => SampleType.Track,
                "answer.wav" or "answer_clock.wav" => SampleType.Answer,
                "tap_perfect.wav" or "tap_great.wav" or "tap_good.wav" or "tap_ex.wav" => SampleType.Tap,
                "break_tap.wav" or "break.wav" => SampleType.Break,
                "slide.wav" => SampleType.Slide,
                "slide_break_start.wav" or "slide_break_slide.wav" => SampleType.BreakSlide,
                "touch.wav" or "touch_Hold_riser.wav" => SampleType.Touch,
                "touch_hanabi.wav" => SampleType.Hanabi,
                _ => SampleType.Track
            };
            var maxNoOfPlaybacks = filename switch
            {
                "answer.wav" or "answer_clock.wav" => 65535,
                "tap_perfect.wav" or "tap_great.wav" or "tap_good.wav" or "tap_ex.wav" => 65535,
                "touch.wav" => 65535,
                _ => 1
            };
            var sample = new AudioSample(path, AudioMode.Sample, maxNoOfPlaybacks) { SampleType = type };
            sfxPlayPointers[sfxIndex] = new int[maxNoOfPlaybacks];
            sfxIndex++;

            NoteSfxs.Add(sample);

            //data
            noteSfxSamplesData.Add(GetSampleDataFromFile(path));
        }

        isInited = true;
    }

    public void Setting(double globalAudioOffset, MajVolumeSetting v)
    {
        GlobalAudioOffset = globalAudioOffset;

        foreach (var sample in NoteSfxs)
            sample.Volume = sample.SampleType switch
            {
                SampleType.Track => v.Track,
                SampleType.Answer => v.Answer,
                SampleType.Tap => v.Tap,
                SampleType.Slide => v.Slide,
                SampleType.Break => v.Break,
                SampleType.BreakSlide => v.BreakSlide,
                SampleType.Ex => v.Ex,
                SampleType.Touch => v.Touch,
                SampleType.Hanabi => v.Hanabi,
                _ => v.Track,
            };
        TrackSampleVolume = v.Track;
    }

    public unsafe void UpdateAnswerSfx()
    {
        lock (answerSfxLock)
        {
            for (var i = 0; i < answerTimingPoints.Count; i++)
            {
                var timing = answerTimingPoints[i];

                if (timing.IsPlayed) continue;

                var thisFrameSec = _timeProvider.NoteTime;

                var delta = thisFrameSec - (timing.Timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC);
                if (delta > 0)
                {
                    if (timing.IsClock) _sfxPtr[ANSWER_CLOCK] = true;
                    else _sfxPtr[ANSWER] = true;

                    timing.IsPlayed = true;
                }
            }
        }
    }

    public unsafe void OnUpdate()
    {
        if (!isInited || _timeProvider.IsRecord) return;

        UpdateAnswerSfx();

        for (var i = 0; i < SFX_COUNT; i++)
        {
            var isRequested = _sfxPtr[i];

            if (i != TOUCHHOLD && isRequested)
            {
                _sfxPtr[i] = false;
            }

            switch (i)
            {
                case TAP_PERFECT:
                    if (isRequested) NoteSfxs[TAP_PERFECT].PlayOneShot();
                    break;
                case TAP_GREAT:
                    if (isRequested) NoteSfxs[TAP_GREAT].PlayOneShot();
                    break;
                case TAP_GOOD:
                    if (isRequested) NoteSfxs[TAP_GOOD].PlayOneShot();
                    break;
                case TAP_EX:
                    if (isRequested) NoteSfxs[TAP_EX].PlayOneShot();
                    break;
                case BREAK_JUDGE:
                    if (isRequested) NoteSfxs[BREAK_JUDGE].PlayOneShot();
                    break;
                case BREAK_SFX:
                    if (isRequested) NoteSfxs[BREAK_SFX].PlayOneShot();
                    break;
                case SLIDE:
                    if (isRequested) NoteSfxs[SLIDE].PlayOneShot();
                    break;
                case BREAK_SLIDE:
                    if (isRequested) NoteSfxs[BREAK_SLIDE].PlayOneShot();
                    break;
                case BREAK_SLIDE_JUDGE:
                    if (isRequested)
                    {
                        NoteSfxs[BREAK_SLIDE_JUDGE].PlayOneShot();
                        // NoteSfxs[BREAK_SFX].PlayOneShot();   // blame @LeZi9916
                    }
                    break;
                case TOUCH:
                    if (isRequested) NoteSfxs[TOUCH].PlayOneShot();
                    break;
                case TOUCHHOLD:
                    // Handled at the end of OnUpdate based on ActiveTouchHoldCount
                    break;
                case FIREWORK:
                    if (isRequested) NoteSfxs[FIREWORK].PlayOneShot();
                    break;
                case ANSWER:
                    if (isRequested) NoteSfxs[ANSWER].PlayOneShot();
                    break;
                case ANSWER_CLOCK:
                    if (isRequested) NoteSfxs[ANSWER_CLOCK].PlayOneShot();
                    break;
                case TRACK_START:
                    if (isRequested) NoteSfxs[TRACK_START].PlayOneShot();
                    break;
                case ALL_PERFECT:
                    if (isRequested) NoteSfxs[ALL_PERFECT].PlayOneShot();
                    break;
            }
        }

        int currentCount = ActiveTouchHoldCount;
        if (currentCount > _prevActiveTouchHoldCount)
        {
            NoteSfxs[TOUCHHOLD].PlayOneShot();
        }
        else if (currentCount == 0 && _prevActiveTouchHoldCount > 0)
        {
            NoteSfxs[TOUCHHOLD].Stop();
        }
        _prevActiveTouchHoldCount = currentCount;
    }

    public void OnDestroy()
    {
        noteSfxPlaybackRequests.Dispose();
        ReleaseRecordingAudio();
        Bass.Stop();
        Bass.Free();
    }


    //track control

    public void LoadTrack(string path)
    {
        TrackSample?.Dispose();
        TrackSample = new AudioSample(path, AudioMode.Stream)
        {
            SampleType = SampleType.Track,
        };
        TrackSampleData = GetSampleDataFromFile(path);
    }

    public void PlayTrack()
    {
        if (TrackSample == null) return;
        TrackSample.Speed = _timeProvider.CurrentSpeed;
        TrackSample.Volume = TrackSampleVolume;

        waitingForTrackAudioStart = true;
        WaitForTrackAudioStart().Forget();

        async UniTask WaitForTrackAudioStart()
        {
            var offset = TRACK_ANSWER_PLAYBACK_OFFSET_SEC + GlobalAudioOffset;
            while (_timeProvider.AudioTime - offset < 0)
            {
                if (waitingForTrackAudioStart == false) return; //canceled
                await UniTask.Yield();
            }

            TrackSample!.Play();
            TrackSample!.CurrentSec = _timeProvider.AudioTime - offset;
            waitingForTrackAudioStart = false;
        }
    }

    public void PauseTrack() => TrackSample?.Pause();

    public void StopTrack()
    {
        waitingForTrackAudioStart = false;
        TrackSample?.Stop();
    }

    //for pause resume
    public void PauseTouchHoldSound()
    {
        if (_prevActiveTouchHoldCount > 0)
            NoteSfxs[TOUCHHOLD].Pause(); //seen as still playing
    }
    public void ResumeTouchHoldSound()
    {
        if (_prevActiveTouchHoldCount > 0)
            NoteSfxs[TOUCHHOLD].Play();
    }

    public unsafe void ResetState()
    {
        StopTrack();
        //StopTouchHoldSound();
        //_sfxPtr[TOUCHHOLD] = false;
        ActiveTouchHoldCount = 0;
        _prevActiveTouchHoldCount = 0;
        NoteSfxs[TOUCHHOLD].Stop();

        lock (answerSfxLock)
            answerTimingPoints.Clear();
        for (var i = 0; i < SFX_COUNT; i++)
            _sfxPtr[i] = false;
    }


    //Sfx control

    public void GenerateAnswerSFX(SimaiChart chart, double ignoreOffset, int clockCount = 0)
    {
        if (chart.NoteTimings.IsEmpty) return;

        //Generate ClockSounds
        var firstBpm = chart.NoteTimings[0].Bpm;

        lock (answerSfxLock)
        {
            answerTimingPoints.Clear();
            if (firstBpm > 0f)
            {
                var interval = 60 / firstBpm;
                for (var i = 0; i < clockCount; i++)
                {
                    var timing = i * interval;
                    answerTimingPoints.Add(new AnswerTimingPoint(timing, true));
                }
            }
        }

        //Generate AnswerSounds
        var rawTimings = new List<float>();

        foreach (var timingPoint in chart.NoteTimings)
        {
            var startTiming = (float)timingPoint.Timing;
            if (startTiming < ignoreOffset) continue;

            if (!timingPoint.Notes.All              //无头别叫
                            (o => o.Type is SimaiNoteType.Slide
                            && o.IsSlideNoHead == true))
            {
                rawTimings.Add(startTiming);
            }


            var holds = Array.FindAll(timingPoint.Notes,
                o => o.Type is SimaiNoteType.Hold or SimaiNoteType.TouchHold);

            foreach (var hold in holds)
            {
                var endTiming = (float)(timingPoint.Timing + hold.HoldTime);
                rawTimings.Add(endTiming);
            }
        }

        rawTimings.Sort();

        var lastAddedTime = -1f;
        var epsilon = 0.001f; // 1ms 阈值

        lock (answerSfxLock)
        {
            foreach (var t in rawTimings)
            {
                if (lastAddedTime < 0 || t - lastAddedTime > epsilon)
                {
                    answerTimingPoints.Add(new AnswerTimingPoint(t, false));
                    lastAddedTime = t;
                }
            }
        }
    }

    //recording control

    public void BeginRecordingAudio(float initialAudioTime, float speed)
    {
        recordingInitialAudioTime = initialAudioTime;
        recordingSpeed = Math.Max(speed, 0.01f);
        recordingInitialNoteTime = _timeProvider.NoteTime;
        var trackOffset = TRACK_ANSWER_PLAYBACK_OFFSET_SEC + (float)GlobalAudioOffset;
        var trackOutputStartTime = trackOffset - recordingInitialAudioTime;
        var leadAndTail = trackOutputStartTime
            + TimeProvider.SONG_DETAIL_OFFSET
            + NoteSfxs[ALL_PERFECT].Length;
        var totalLen = TrackSample!.Length / recordingSpeed + leadAndTail; // 留给开头演出和结尾AP音效
        var size = (int)Math.Ceiling(Math.Max(0.1, totalLen) * SAMPLERATE) * CHANNELS;
        if (recordingBuffer.IsCreated) recordingBuffer.Dispose();
        recordingBuffer = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        recordingSampleCount = size;
        recordingCommittedSampleCount = 0;
        recordingPreviousTouchHoldCount = 0;
        for (var i = 0; i < sfxPlayPointers.Length; i++)
        {
            if (sfxPlayPointers[i] != null)
            {
                for (var j = 0; j < sfxPlayPointers[i].Length; j++)
                    sfxPlayPointers[i][j] = -1; // 初始化指针
            }
        }
        // Mix sources whose timing is known before recording begins. During capture,
        // only the current dynamic-SFX interval remains mutable, so older intervals
        // can be encoded without retaining the whole song.
        MixStaticRecordingAudio();
    }

    public unsafe void UpdateRecordingAudioFrame(double frameStartTime, double frameEndTime)
    {
        if (!recordingBuffer.IsCreated)
            throw new InvalidOperationException("Recording audio has not been initialized.");
        if (frameEndTime < frameStartTime)
            throw new ArgumentOutOfRangeException(nameof(frameEndTime));

        if (!IsShowingSongDetail)
        {
            for (var i = 0; i < SFX_COUNT; i++)
            {
                if (i == TRACK_START || i == TOUCHHOLD || i == ANSWER || i == ANSWER_CLOCK)
                    continue;

                if (_sfxPtr[i])
                {
                    TriggerSfxRecording(i);
                    _sfxPtr[i] = false;
                }
            }

            var currentTouchHoldCount = ActiveTouchHoldCount;
            if (currentTouchHoldCount > recordingPreviousTouchHoldCount)
            {
                var newVoices = currentTouchHoldCount - recordingPreviousTouchHoldCount;
                for (var i = 0; i < newVoices; i++)
                    TriggerSfxRecording(TOUCHHOLD);
            }
            else if (currentTouchHoldCount == 0 && recordingPreviousTouchHoldCount > 0)
            {
                StopSfxRecording(TOUCHHOLD);
            }

            recordingPreviousTouchHoldCount = currentTouchHoldCount;
            UpdateSfxRecording(
                (float)(frameEndTime - frameStartTime),
                (float)frameStartTime);
        }

        // Static audio was mixed up front and the dynamic part for this frame is
        // now complete. Everything before frameEndTime is immutable and writable.
        CommitRecordingAudioThrough(frameEndTime);
    }

    public void EndRecordingAudio(float recordingElapsedTime)
    {
        var finalSampleCount = FinalizeRecordingBuffer(recordingElapsedTime);
        CommitRecordingSamples(finalSampleCount);
    }

    private void CommitRecordingAudioThrough(double recordingEndTime)
    {
        var targetSampleCount = Math.Min(
            (int)(recordingEndTime * SAMPLERATE) * CHANNELS,
            recordingBuffer.Length);
        CommitRecordingSamples(targetSampleCount);
    }

    private void CommitRecordingSamples(int targetSampleCount)
    {
        targetSampleCount = Math.Clamp(targetSampleCount, 0, recordingBuffer.Length);
        targetSampleCount -= targetSampleCount % CHANNELS;
        if (targetSampleCount <= recordingCommittedSampleCount)
            return;

        // Buffer ownership stays here. ScreenRecorder supplies only timestamps;
        // AudioManager submits the immutable NativeArray range to the encoder.
        ScreenRecorderBackendHub.WriteAudioSamples(
            recordingBuffer,
            recordingCommittedSampleCount,
            targetSampleCount - recordingCommittedSampleCount);
        recordingCommittedSampleCount = targetSampleCount;
    }

    private void TriggerSfxRecording(int index)
    {
        if (index < 0 || index >= noteSfxSamplesData.Count) return;
        var pointers = sfxPlayPointers[index];
        if (pointers == null || pointers.Length == 0) return;

        int bestIdx = 0;
        int maxProgress = -2;
        for (int i = 0; i < pointers.Length; i++)
        {
            if (pointers[i] == -1)
            {
                bestIdx = i;
                break;
            }
            if (pointers[i] > maxProgress)
            {
                maxProgress = pointers[i];
                bestIdx = i;
            }
        }
        pointers[bestIdx] = 0;
    }
    private void StopSfxRecording(int index)
    {
        if (index < 0 || index >= noteSfxSamplesData.Count) return;
        var pointers = sfxPlayPointers[index];
        if (pointers != null)
        {
            for (int i = 0; i < pointers.Length; i++)
                pointers[i] = -1;
        }
    }

    private void UpdateSfxRecording(float deltaTime, float recordingElapsedTime)
    {
        // 计算当前帧在 buffer 中的起始采样位置
        var bufferStartPos = (int)(recordingElapsedTime * SAMPLERATE) * CHANNELS;
        // 这一帧应该写入的采样长度
        // Absolute frame boundaries avoid cumulative rounding gaps at FPS values
        // such as 59, where sampleRate / fps is fractional.
        var bufferEndPos = (int)((recordingElapsedTime + deltaTime) * SAMPLERATE) * CHANNELS;
        var samplesToCopy = Math.Max(0, bufferEndPos - bufferStartPos);

        for (var i = 0; i < sfxPlayPointers.Length; i++)
        {
            if (i == TRACK_START || sfxPlayPointers[i] == null) continue;

            var pointers = sfxPlayPointers[i];
            var sfxData = noteSfxSamplesData[i];
            var vol = NoteSfxs[i].Volume;

            for (var p = 0; p < pointers.Length; p++)
            {
                if (pointers[p] == -1) continue;

                for (var j = 0; j < samplesToCopy; j++)
                {
                    var sfxIdx = pointers[p] + j;
                    if (sfxIdx < sfxData.Length)
                    {
                        var dstIdx = bufferStartPos + j;
                        if (dstIdx >= 0 && dstIdx < recordingBuffer.Length)
                        {
                            var mixed = recordingBuffer[dstIdx] + sfxData[sfxIdx] * vol;
                            recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
                        }
                    }
                    else
                    {
                        pointers[p] = -1;
                        break;
                    }
                }

                if (pointers[p] != -1)
                    pointers[p] += samplesToCopy;
            }
        }
    }

    private int FinalizeRecordingBuffer(float recordingElapsedTime)
    {
        // Flush remaining playing SFXs and calculate max required time
        float maxTime = recordingElapsedTime;

        var bufferStartPos = (int)(recordingElapsedTime * SAMPLERATE) * CHANNELS;
        for (var i = 0; i < sfxPlayPointers.Length; i++)
        {
            if (i == TRACK_START || sfxPlayPointers[i] == null) continue;
            var pointers = sfxPlayPointers[i];
            var sfxData = noteSfxSamplesData[i];
            var vol = NoteSfxs[i].Volume;

            for (var p = 0; p < pointers.Length; p++)
            {
                if (pointers[p] != -1)
                {
                    int remain = sfxData.Length - pointers[p];
                    float endTime = recordingElapsedTime + ((float)remain / CHANNELS / SAMPLERATE);
                    if (endTime > maxTime) maxTime = endTime;

                    for (int j = 0; j < remain; j++)
                    {
                        var dstIdx = bufferStartPos + j;
                        if (dstIdx >= 0 && dstIdx < recordingBuffer.Length)
                        {
                            var mixed = recordingBuffer[dstIdx] + sfxData[pointers[p] + j] * vol;
                            recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
                        }
                    }
                    pointers[p] = -1;
                }
            }
        }

        recordingSampleCount = Math.Min(
            (int)Math.Ceiling(maxTime * SAMPLERATE) * CHANNELS,
            recordingBuffer.Length);
        recordingSampleCount -= recordingSampleCount % CHANNELS;
        return recordingSampleCount;
    }

    private void MixStaticRecordingAudio()
    {
        // track start
        var trackStartSampleData = noteSfxSamplesData[TRACK_START];
        for (var i = 0; i < trackStartSampleData.Length; i++)
        {
            if (i < recordingBuffer.Length)
            {
                var mixed = recordingBuffer[i] + trackStartSampleData[i] * NoteSfxs[TRACK_START].Volume;
                recordingBuffer[i] = Math.Clamp(mixed, -1.0f, 1.0f);
            }
        }

        var trackOffset = TRACK_ANSWER_PLAYBACK_OFFSET_SEC + (float)GlobalAudioOffset;
        var initialTrackSec = recordingInitialAudioTime - trackOffset;

        // sample-accurate answers
        var answerData = noteSfxSamplesData[ANSWER];
        var answerClockData = noteSfxSamplesData[ANSWER_CLOCK];
        var answerVol = NoteSfxs[ANSWER].Volume;
        var answerClockVol = NoteSfxs[ANSWER_CLOCK].Volume;

        foreach (var timing in answerTimingPoints)
        {
            // Mirror the real-time trigger at Update():
            // NoteTime > timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC.
            var triggerNoteTime = timing.Timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC;
            float exactOutputSec = (triggerNoteTime - recordingInitialNoteTime) / recordingSpeed;
            if (exactOutputSec < 0) continue;

            int startSample = (int)(exactOutputSec * SAMPLERATE);
            int startIdx = startSample * CHANNELS;

            var sfxData = timing.IsClock ? answerClockData : answerData;
            var vol = timing.IsClock ? answerClockVol : answerVol;

            for (int i = 0; i < sfxData.Length; i++)
            {
                int dstIdx = startIdx + i;
                if (dstIdx >= 0 && dstIdx < recordingBuffer.Length)
                {
                    var mixed = recordingBuffer[dstIdx] + sfxData[i] * vol;
                    recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
                }
            }
        }
        var trackStartFrameCount = (int)((initialTrackSec + TimeProvider.SONG_DETAIL_OFFSET) * SAMPLERATE);
        var trackFrameCount = TrackSampleData.Length / CHANNELS;
        var recordingFrameCount = recordingBuffer.Length / CHANNELS;

        for (var dstFrame = 0; dstFrame < recordingFrameCount; dstFrame++)
        {
            var srcFrame = (initialTrackSec * SAMPLERATE) + dstFrame * recordingSpeed;
            if (srcFrame < 0 || srcFrame <= trackStartFrameCount) continue;
            if (srcFrame >= trackFrameCount - 1) break;

            var srcFrameFloor = (int)srcFrame;
            var t = srcFrame - srcFrameFloor;
            var srcIdx = srcFrameFloor * CHANNELS;
            var nextSrcIdx = srcIdx + CHANNELS;
            var dstIdx = dstFrame * CHANNELS;

            for (var ch = 0; ch < CHANNELS; ch++)
            {
                var sample = Mathf.Lerp(TrackSampleData[srcIdx + ch], TrackSampleData[nextSrcIdx + ch], t);
                var mixed = recordingBuffer[dstIdx + ch] + sample * TrackSampleVolume;
                recordingBuffer[dstIdx + ch] = Math.Clamp(mixed, -1.0f, 1.0f);
            }
        }

    }

    public void ReleaseRecordingAudio()
    {
        if (recordingBuffer.IsCreated) recordingBuffer.Dispose();
        recordingSampleCount = 0;
        recordingCommittedSampleCount = 0;
        recordingPreviousTouchHoldCount = 0;
    }

    public int SampleRate => SAMPLERATE;
    public int Channels => CHANNELS;



    private float[] GetSampleDataFromFile(string path)
    {
        var stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) return Array.Empty<float>();

        var info = Bass.ChannelGetInfo(stream);
        var lenBytes = Bass.ChannelGetLength(stream);
        var rawData = new float[lenBytes / 4];
        Bass.ChannelGetData(stream, rawData, (int)lenBytes);
        Bass.StreamFree(stream);

        unsafe
        {
            fixed (float* dataPtr = rawData)
            {
                using var sourceArray = rawData.AsUnsafeNativeArrayScope();

                var ratio = (float)info.Frequency / SAMPLERATE;
                var sourceFrames = rawData.Length / 2;
                var targetFrames = (int)(sourceFrames / ratio);
                var outputArray = new NativeArray<float>(targetFrames * 2, Allocator.TempJob);

                new AudioResampleJob
                {
                    Source = sourceArray.Array,
                    Output = outputArray,
                    Ratio = ratio,
                    SrcFrameLimit = (rawData.Length / 2) - 1
                }.Schedule(targetFrames, 64, default).Complete();

                var result = outputArray.ToArray();
                outputArray.Dispose();
                return result;
            }
        }
    }

    private class AnswerTimingPoint
    {
        public readonly float Timing;
        public readonly bool IsClock;
        public bool IsPlayed;

        public AnswerTimingPoint(float timing, bool isClock)
        {
            Timing = timing;
            IsClock = isClock;
            IsPlayed = false;
        }
    }

    [BurstCompile]
    public struct AudioResampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Source;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<float> Output;
        [ReadOnly] public float Ratio;
        [ReadOnly] public int SrcFrameLimit;

        public void Execute(int index)
        {
            float sourceIdx = index * Ratio;
            int i1 = (int)math.floor(sourceIdx);
            int i2 = (i1 < SrcFrameLimit) ? i1 + 1 : i1;

            float frac = sourceIdx - i1;
            int s1 = i1 << 1;
            int s2 = i2 << 1;
            int d = index << 1;

            Output[d] = math.lerp(Source[s1], Source[s2], frac);         // 左声道
            Output[d + 1] = math.lerp(Source[s1 + 1], Source[s2 + 1], frac); // 右声道
        }
    }
}