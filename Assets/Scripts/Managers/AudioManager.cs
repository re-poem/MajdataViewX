using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using MajdataViewX.Base;
using MajdataViewX.Types.Audio;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Utils.Extensions;
using Cimai;
using ManagedBass;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;


namespace MajdataViewX.Managers
{
    public class AudioManager
    {
        private const int SFX_COUNT = 16;

        [CanBeNull] private AudioSample _trackSample;
        [CanBeNull] private string _trackPath;
        private float _trackSampleVolume;
        [CanBeNull] private float[] _trackSampleData;
        // TrackSampleData 仅导出(Record)模式混音用，普通播放无需加载，
        // 故 IsTrackLoaded 只取决于 Bass 流是否就绪
        public bool IsTrackLoaded => _trackSample != null;

        // answer SFX gen
        private readonly List<AnswerTimingPoint> _answerTimingPoints = new();
        private readonly object _answerSfxLock = new();
        // note SFX
        // DO NOT USE THIS WHEN PLAY STARTED, MAY CAUSE AtomicSafetyHandle Exception, WE DONT CARE THIS
        public NativeArray<bool> noteSfxPlaybackRequests = new(SFX_COUNT, Allocator.Persistent);
        // USE THIS
        public readonly unsafe bool* SfxRequestsPtr;

        private readonly List<AudioSample> _noteSfx = new(SFX_COUNT);

        //for recording
        private readonly List<float[]> _noteSfxSamplesData = new(SFX_COUNT);
        private NativeArray<float> _recordingBuffer;
        private int _recordingSampleCount;
        private int _recordingPreviousTouchHoldCount;
        private float _recordingInitialAudioTime;
        private float _recordingSpeed = 1f;
        private float _recordingInitialNoteTime;
        private readonly int[][] _sfxPlayPointers = new int[SFX_COUNT][]; //-1 is not playing

        /// <summary>
        /// 只用于调整音频的延迟，视频/note/时间轴显示均不变化
        /// </summary>
        public double GlobalAudioOffset { get; private set; }
        /// <summary>
        /// Note的起始时间
        /// </summary>
        public double NoteOffset { get; set; }

        public const int SAMPLE_RATE = 44100;
        public const int CHANNELS = 2;

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

        private bool _waitingForTrackAudioStart;
        private bool IsShowingSongDetail => _timeProvider.AudioTime <= _recordingInitialAudioTime + TimeProvider.SONG_DETAIL_OFFSET;

        public int ActiveTouchHoldCount { get; set; }
        private int _prevActiveTouchHoldCount;


        private readonly bool _isInitialized;

        public unsafe AudioManager()
        {
            _audioManager = this;
            SfxRequestsPtr = (bool*)noteSfxPlaybackRequests.GetUnsafePtr();
            MajEnv.InitBass(SAMPLE_RATE);

            //Note SFX
            var sfxPath = MajEnv.GetPath("SFX");
            var sfxIndex = 0;
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
                    "tap_perfect.wav" or "tap_great.wav" or "tap_good.wav" => SampleType.Tap,
                    "break_tap.wav" or "break.wav" => SampleType.Break,
                    "slide.wav" => SampleType.Slide,
                    "slide_break_start.wav" or "slide_break_slide.wav" => SampleType.BreakSlide,
                    "tap_ex.wav" => SampleType.Ex,
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
                var sample = new AudioSample(path, maxNoOfPlaybacks) { SampleType = type };
                _sfxPlayPointers[sfxIndex] = new int[maxNoOfPlaybacks];
                sfxIndex++;

                _noteSfx.Add(sample);

                //data
                _noteSfxSamplesData.Add(GetSampleDataFromFile(path));
            }

            _isInitialized = true;
        }

        public void Setting(double globalAudioOffset, MajVolumeSetting v)
        {
            GlobalAudioOffset = globalAudioOffset;

            foreach (var sample in _noteSfx)
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
            _trackSampleVolume = v.Track;
        }

        public unsafe void UpdateAnswerSfx()
        {
            lock (_answerSfxLock)
            {
                for (var i = 0; i < _answerTimingPoints.Count; i++)
                {
                    var timing = _answerTimingPoints[i];

                    if (timing.IsPlayed) continue;

                    var thisFrameSec = _timeProvider.NoteTime;

                    var delta = thisFrameSec - (timing.Timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC);
                    if (delta > 0)
                    {
                        if (timing.IsClock) SfxRequestsPtr[ANSWER_CLOCK] = true;
                        else SfxRequestsPtr[ANSWER] = true;

                        timing.IsPlayed = true;
                    }
                }
            }
        }

        public unsafe void OnUpdate()
        {
            if (!_isInitialized || _timeProvider.IsRecord) return;

            // 拖动预览时不要有声音
            if (_timeProvider.IsStart)
                UpdateAnswerSfx();

            for (var i = 0; i < SFX_COUNT; i++)
            {
                var isRequested = SfxRequestsPtr[i];

                if (i != TOUCHHOLD && isRequested)
                {
                    SfxRequestsPtr[i] = false;
                }

                switch (i)
                {
                    case TAP_PERFECT:
                        if (isRequested) _noteSfx[TAP_PERFECT].PlayOneShot();
                        break;
                    case TAP_GREAT:
                        if (isRequested) _noteSfx[TAP_GREAT].PlayOneShot();
                        break;
                    case TAP_GOOD:
                        if (isRequested) _noteSfx[TAP_GOOD].PlayOneShot();
                        break;
                    case TAP_EX:
                        if (isRequested) _noteSfx[TAP_EX].PlayOneShot();
                        break;
                    case BREAK_JUDGE:
                        if (isRequested) _noteSfx[BREAK_JUDGE].PlayOneShot();
                        break;
                    case BREAK_SFX:
                        if (isRequested) _noteSfx[BREAK_SFX].PlayOneShot();
                        break;
                    case SLIDE:
                        if (isRequested) _noteSfx[SLIDE].PlayOneShot();
                        break;
                    case BREAK_SLIDE:
                        if (isRequested) _noteSfx[BREAK_SLIDE].PlayOneShot();
                        break;
                    case BREAK_SLIDE_JUDGE:
                        if (isRequested)
                        {
                            _noteSfx[BREAK_SLIDE_JUDGE].PlayOneShot();
                            // NoteSfxs[BREAK_SFX].PlayOneShot();   // blame @LeZi9916
                        }
                        break;
                    case TOUCH:
                        if (isRequested) _noteSfx[TOUCH].PlayOneShot();
                        break;
                    case TOUCHHOLD:
                        // Handled at the end of OnUpdate based on ActiveTouchHoldCount
                        break;
                    case FIREWORK:
                        if (isRequested) _noteSfx[FIREWORK].PlayOneShot();
                        break;
                    case ANSWER:
                        if (isRequested) _noteSfx[ANSWER].PlayOneShot();
                        break;
                    case ANSWER_CLOCK:
                        if (isRequested) _noteSfx[ANSWER_CLOCK].PlayOneShot();
                        break;
                    case TRACK_START:
                        if (isRequested) _noteSfx[TRACK_START].PlayOneShot();
                        break;
                    case ALL_PERFECT:
                        if (isRequested) _noteSfx[ALL_PERFECT].PlayOneShot();
                        break;
                }
            }

            var currentCount = ActiveTouchHoldCount;
            if (currentCount > _prevActiveTouchHoldCount)
            {
                _noteSfx[TOUCHHOLD].PlayOneShot();
            }
            else if (currentCount == 0 && _prevActiveTouchHoldCount > 0)
            {
                _noteSfx[TOUCHHOLD].Stop();
            }
            _prevActiveTouchHoldCount = currentCount;
        }

        public void OnDestroy()
        {
            _trackSample?.Dispose();
            _trackSample = null;
            _trackSampleData = null;
            noteSfxPlaybackRequests.Dispose();
            ReleaseRecordingAudio();
            Bass.Stop();
            Bass.Free();
        }


        //track control

        public void LoadTrack(string path)
        {
            _trackPath = path;
            _trackSample?.Dispose();
            _trackSample = new AudioSample(path, 1)
            {
                SampleType = SampleType.Track,
            };
            // 整首PCM(数十MB,落LOH)仅导出模式混音用，延迟到 BeginRecordingAudio 按需加载，
            // 避免普通播放模式持有大数组、切歌时靠GC延迟回收(表现为内存几十秒后才回落)
            _trackSampleData = null;
        }

        public void PlayTrack()
        {
            if (_trackSample == null) return;
            _trackSample.Speed = _timeProvider.CurrentSpeed;
            _trackSample.Volume = _trackSampleVolume;

            _waitingForTrackAudioStart = true;
            WaitForTrackAudioStart().Forget();

            async UniTask WaitForTrackAudioStart()
            {
                var offset = TRACK_ANSWER_PLAYBACK_OFFSET_SEC + GlobalAudioOffset;
                while (_timeProvider.AudioTime - offset < 0)
                {
                    if (_waitingForTrackAudioStart == false) return; //canceled
                    await UniTask.Yield();
                }

                _trackSample!.Play();
                _trackSample!.CurrentSec = _timeProvider.AudioTime - offset;
                _waitingForTrackAudioStart = false;
            }
        }


        public unsafe void ResetState()
        {
            _waitingForTrackAudioStart = false;
            _trackSample?.Pause();
            // 不释放上一曲的音频资源，下次load时自然会被释放
            //StopTouchHoldSound();
            //SfxRequestsPtr[TOUCHHOLD] = false;
            ActiveTouchHoldCount = 0;
            _prevActiveTouchHoldCount = 0;
            _noteSfx[TOUCHHOLD].Stop();

            for (var i = 0; i < SFX_COUNT; i++)
                SfxRequestsPtr[i] = false;
        }


        //Sfx control

        public void GenerateAnswerSFX(SimaiChart chart, int clockCount = 0)
        {
            //Generate ClockSounds
            if (chart.Timings.IsEmpty)
            {
                _answerTimingPoints.Clear();
                return;
            }
            var firstBpm = chart.Timings[0].Bpm;

            lock (_answerSfxLock)
            {
                _answerTimingPoints.Clear();
                if (firstBpm > 0f)
                {
                    var interval = 60 / firstBpm;
                    for (var i = 0; i < clockCount; i++)
                    {
                        var timing = i * interval;
                        _answerTimingPoints.Add(new AnswerTimingPoint(timing, true));
                    }
                }
            }

            //Generate AnswerSounds
            var rawTimings = new List<float>();

            foreach (var timingPoint in chart.Timings)
            {
                var startTime = (float)timingPoint.Time;
                var addedStart = false;
                var addedEnd = false;

                foreach (var note in chart.Notes.Slice((int)timingPoint.NotesFirst, (int)timingPoint.NotesCount))
                {
                    switch (note.Type)
                    {
                        case SimaiNoteType.TAP:
                        case SimaiNoteType.TOUCH:
                            if (!addedStart)
                            {
                                rawTimings.Add(startTime);
                                addedStart = true;
                            }
                            break;
                        case SimaiNoteType.HOLD:
                        case SimaiNoteType.TOUCHHOLD:
                            if (!addedStart)
                            {
                                rawTimings.Add(startTime);
                                addedStart = true;
                            }
                            if (!addedEnd)
                            {
                                var endTiming = (float)(timingPoint.Time + note.Duration);
                                rawTimings.Add(endTiming);
                                addedEnd = true;
                            }
                            break;
                    }

                    if (addedStart && addedEnd)
                        break;
                }
            }

            rawTimings.Sort();

            var lastAddedTime = -1f;
            var epsilon = 0.001f; // 1ms 阈值

            lock (_answerSfxLock)
            {
                foreach (var t in rawTimings)
                {
                    if (lastAddedTime < 0 || t - lastAddedTime > epsilon)
                    {
                        _answerTimingPoints.Add(new AnswerTimingPoint(t, false));
                        lastAddedTime = t;
                    }
                }
            }
        }

        public void ResetAnswerSFX(double ignoreOffset)
        {
            foreach (var tp in _answerTimingPoints)
            {
                if (tp.Timing < ignoreOffset) tp.IsPlayed = true;
                else tp.IsPlayed = false;
            }
        }

        //recording control

        public void BeginRecordingAudio(float initialAudioTime, float speed)
        {
            _recordingInitialAudioTime = initialAudioTime;
            _recordingSpeed = Math.Max(speed, 0.01f);
            _recordingInitialNoteTime = _timeProvider.NoteTime;
            var trackOffset = TRACK_ANSWER_PLAYBACK_OFFSET_SEC;
            var trackOutputStartTime = trackOffset - _recordingInitialAudioTime;
            var leadAndTail = trackOutputStartTime
                + TimeProvider.SONG_DETAIL_OFFSET
                + _noteSfx[ALL_PERFECT].Length;
            var totalLen = _trackSample!.Length / _recordingSpeed + leadAndTail; // 留给开头演出和结尾AP音效
            var size = (int)Math.Ceiling(Math.Max(0.1, totalLen) * SAMPLE_RATE) * CHANNELS;
            if (_recordingBuffer.IsCreated) _recordingBuffer.Dispose();
            _recordingBuffer = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _recordingSampleCount = size;
            _recordingPreviousTouchHoldCount = 0;
            for (var i = 0; i < _sfxPlayPointers.Length; i++)
            {
                if (_sfxPlayPointers[i] != null)
                {
                    for (var j = 0; j < _sfxPlayPointers[i].Length; j++)
                        _sfxPlayPointers[i][j] = -1; // 初始化指针
                }
            }
            // 按需加载整首PCM：普通播放模式下 TrackSampleData 保持为null，仅导出模式走到这里
            _trackSampleData ??= GetSampleDataFromFile(_trackPath);
            // Mix sources whose timing is known before recording begins. Dynamic SFX
            // are added frame by frame, then the completed buffer is muxed at the end.
            MixStaticRecordingAudio();
        }

        public unsafe void UpdateRecordingAudioFrame(double frameStartTime, double frameEndTime)
        {
            if (!_recordingBuffer.IsCreated)
                throw new InvalidOperationException("Recording audio has not been initialized.");
            if (frameEndTime < frameStartTime)
                throw new ArgumentOutOfRangeException(nameof(frameEndTime));

            if (!IsShowingSongDetail)
            {
                for (var i = 0; i < SFX_COUNT; i++)
                {
                    if (i == TRACK_START || i == TOUCHHOLD || i == ANSWER || i == ANSWER_CLOCK)
                        continue;

                    if (SfxRequestsPtr[i])
                    {
                        TriggerSfxRecording(i);
                        SfxRequestsPtr[i] = false;
                    }
                }

                var currentTouchHoldCount = ActiveTouchHoldCount;
                if (currentTouchHoldCount > _recordingPreviousTouchHoldCount)
                {
                    var newVoices = currentTouchHoldCount - _recordingPreviousTouchHoldCount;
                    for (var i = 0; i < newVoices; i++)
                        TriggerSfxRecording(TOUCHHOLD);
                }
                else if (currentTouchHoldCount == 0 && _recordingPreviousTouchHoldCount > 0)
                {
                    StopSfxRecording(TOUCHHOLD);
                }

                _recordingPreviousTouchHoldCount = currentTouchHoldCount;
                UpdateSfxRecording(
                    (float)(frameEndTime - frameStartTime),
                    (float)frameStartTime);
            }

        }

        public void EndRecordingAudio(float recordingElapsedTime)
        {
            _recordingSampleCount = FinalizeRecordingBuffer(recordingElapsedTime);
        }

        public NativeArray<float> GetRecordingBuffer(out int sampleCount)
        {
            if (!_recordingBuffer.IsCreated)
                throw new InvalidOperationException("Recording audio has not been initialized.");

            // The native muxer copies this data synchronously, so AudioManager keeps
            // ownership and releases the buffer only after video_encoder_free returns.
            sampleCount = _recordingSampleCount;
            return _recordingBuffer;
        }

        private void TriggerSfxRecording(int index)
        {
            if (index < 0 || index >= _noteSfxSamplesData.Count) return;
            var pointers = _sfxPlayPointers[index];
            if (pointers == null || pointers.Length == 0) return;

            var bestIdx = 0;
            var maxProgress = -2;
            for (var i = 0; i < pointers.Length; i++)
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
            if (index < 0 || index >= _noteSfxSamplesData.Count) return;
            var pointers = _sfxPlayPointers[index];
            if (pointers != null)
            {
                for (var i = 0; i < pointers.Length; i++)
                    pointers[i] = -1;
            }
        }

        private void UpdateSfxRecording(float deltaTime, float recordingElapsedTime)
        {
            // 计算当前帧在 buffer 中的起始采样位置
            var bufferStartPos = (int)(recordingElapsedTime * SAMPLE_RATE) * CHANNELS;
            // 这一帧应该写入的采样长度
            // Absolute frame boundaries avoid cumulative rounding gaps at FPS values
            // such as 59, where sampleRate / fps is fractional.
            var bufferEndPos = (int)((recordingElapsedTime + deltaTime) * SAMPLE_RATE) * CHANNELS;
            var samplesToCopy = Math.Max(0, bufferEndPos - bufferStartPos);

            for (var i = 0; i < _sfxPlayPointers.Length; i++)
            {
                if (i == TRACK_START || _sfxPlayPointers[i] == null) continue;

                var pointers = _sfxPlayPointers[i];
                var sfxData = _noteSfxSamplesData[i];
                var vol = _noteSfx[i].Volume;

                for (var p = 0; p < pointers.Length; p++)
                {
                    if (pointers[p] == -1) continue;

                    for (var j = 0; j < samplesToCopy; j++)
                    {
                        var sfxIdx = pointers[p] + j;
                        if (sfxIdx < sfxData.Length)
                        {
                            var dstIdx = bufferStartPos + j;
                            if (dstIdx >= 0 && dstIdx < _recordingBuffer.Length)
                            {
                                var mixed = _recordingBuffer[dstIdx] + sfxData[sfxIdx] * vol;
                                _recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
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
            var maxTime = recordingElapsedTime;

            var bufferStartPos = (int)(recordingElapsedTime * SAMPLE_RATE) * CHANNELS;
            for (var i = 0; i < _sfxPlayPointers.Length; i++)
            {
                if (i == TRACK_START || _sfxPlayPointers[i] == null) continue;
                var pointers = _sfxPlayPointers[i];
                var sfxData = _noteSfxSamplesData[i];
                var vol = _noteSfx[i].Volume;

                for (var p = 0; p < pointers.Length; p++)
                {
                    if (pointers[p] != -1)
                    {
                        var remain = sfxData.Length - pointers[p];
                        var endTime = recordingElapsedTime + ((float)remain / CHANNELS / SAMPLE_RATE);
                        if (endTime > maxTime) maxTime = endTime;

                        for (var j = 0; j < remain; j++)
                        {
                            var dstIdx = bufferStartPos + j;
                            if (dstIdx >= 0 && dstIdx < _recordingBuffer.Length)
                            {
                                var mixed = _recordingBuffer[dstIdx] + sfxData[pointers[p] + j] * vol;
                                _recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
                            }
                        }
                        pointers[p] = -1;
                    }
                }
            }

            _recordingSampleCount = Math.Min(
                (int)Math.Ceiling(maxTime * SAMPLE_RATE) * CHANNELS,
                _recordingBuffer.Length);
            _recordingSampleCount -= _recordingSampleCount % CHANNELS;
            return _recordingSampleCount;
        }

        private void MixStaticRecordingAudio()
        {
            // track start
            var trackStartSampleData = _noteSfxSamplesData[TRACK_START];
            for (var i = 0; i < trackStartSampleData.Length; i++)
            {
                if (i < _recordingBuffer.Length)
                {
                    var mixed = _recordingBuffer[i] + trackStartSampleData[i] * _noteSfx[TRACK_START].Volume;
                    _recordingBuffer[i] = Math.Clamp(mixed, -1.0f, 1.0f);
                }
            }
            
            var initialTrackSec = _recordingInitialAudioTime - TRACK_ANSWER_PLAYBACK_OFFSET_SEC;

            // sample-accurate answers
            var answerData = _noteSfxSamplesData[ANSWER];
            var answerClockData = _noteSfxSamplesData[ANSWER_CLOCK];
            var answerVol = _noteSfx[ANSWER].Volume;
            var answerClockVol = _noteSfx[ANSWER_CLOCK].Volume;

            foreach (var timing in _answerTimingPoints)
            {
                // 播放song detail时不要有answer音
                if (timing.Timing + NoteOffset < initialTrackSec + TimeProvider.SONG_DETAIL_OFFSET)
                    continue;
                
                // Mirror the real-time trigger at Update():
                // NoteTime > timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC.
                var triggerNoteTime = timing.Timing + TRACK_ANSWER_PLAYBACK_OFFSET_SEC;
                var exactOutputSec = (triggerNoteTime - _recordingInitialNoteTime) / _recordingSpeed;
                if (exactOutputSec < 0) continue;

                var startSample = (int)(exactOutputSec * SAMPLE_RATE);
                var startIdx = startSample * CHANNELS;

                var sfxData = timing.IsClock ? answerClockData : answerData;
                var vol = timing.IsClock ? answerClockVol : answerVol;

                for (var i = 0; i < sfxData.Length; i++)
                {
                    var dstIdx = startIdx + i;
                    if (dstIdx >= 0 && dstIdx < _recordingBuffer.Length)
                    {
                        var mixed = _recordingBuffer[dstIdx] + sfxData[i] * vol;
                        _recordingBuffer[dstIdx] = Math.Clamp(mixed, -1.0f, 1.0f);
                    }
                }
            }
            var trackStartFrameCount = (int)((initialTrackSec + TimeProvider.SONG_DETAIL_OFFSET) * SAMPLE_RATE);
            var trackFrameCount = _trackSampleData.Length / CHANNELS;
            var recordingFrameCount = _recordingBuffer.Length / CHANNELS;

            for (var dstFrame = 0; dstFrame < recordingFrameCount; dstFrame++)
            {
                var srcFrame = (initialTrackSec * SAMPLE_RATE) + dstFrame * _recordingSpeed;
                if (srcFrame < 0 || srcFrame <= trackStartFrameCount) continue;
                if (srcFrame >= trackFrameCount - 1) break;

                var srcFrameFloor = (int)srcFrame;
                var t = srcFrame - srcFrameFloor;
                var srcIdx = srcFrameFloor * CHANNELS;
                var nextSrcIdx = srcIdx + CHANNELS;
                var dstIdx = dstFrame * CHANNELS;

                for (var ch = 0; ch < CHANNELS; ch++)
                {
                    var sample = Mathf.Lerp(_trackSampleData[srcIdx + ch], _trackSampleData[nextSrcIdx + ch], t);
                    var mixed = _recordingBuffer[dstIdx + ch] + sample * _trackSampleVolume;
                    _recordingBuffer[dstIdx + ch] = Math.Clamp(mixed, -1.0f, 1.0f);
                }
            }

        }

        public void ReleaseRecordingAudio()
        {
            if (_recordingBuffer.IsCreated) _recordingBuffer.Dispose();
            _recordingSampleCount = 0;
            _recordingPreviousTouchHoldCount = 0;
        }

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

                    var ratio = (float)info.Frequency / SAMPLE_RATE;
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
                IsPlayed = true; //先设为true，等reset
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
                var sourceIdx = index * Ratio;
                var i1 = (int)math.floor(sourceIdx);
                var i2 = (i1 < SrcFrameLimit) ? i1 + 1 : i1;

                var frac = sourceIdx - i1;
                var s1 = i1 << 1;
                var s2 = i2 << 1;
                var d = index << 1;

                Output[d] = math.lerp(Source[s1], Source[s2], frac);         // 左声道
                Output[d + 1] = math.lerp(Source[s1 + 1], Source[s2 + 1], frac); // 右声道
            }
        }
    }
}