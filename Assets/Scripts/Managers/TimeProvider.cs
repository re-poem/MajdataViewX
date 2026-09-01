using MajdataViewX.Base;
using MajdataViewX.Types.Enums;
using MajSimai;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static MajdataViewX.Base.MajBurst;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class TimeProvider : MonoBehaviour
    {
        public bool IsStart { get; private set; }
        public bool IsRecord { get; private set; }

        //audio get this value
        public float AudioTime { get; private set; }
        //notes get this value
        public float NoteTime { get; private set; }
        public float FakeNoteTime => TimeData.GetPositionAtTime(NoteTime);
        public float CurrentSpeed => IsRecord ? Time.timeScale : speed;

        private static NativeList<(float time, float sVeloc)> SVList = new(20, Allocator.Persistent);
        private static NativeArray<(float k, float b)> SVFuncArgs;

        private double startRealtime; //the beginning of the program is 0
        private float startAt; //the beginning of the audio is 0
        public float offset;
        private float speed;
        //for pause and resume
        private double accumulated;

        private readonly Stopwatch playbackClock = new();



        private MemoryMappedFile mmfAudioTime;
        private MemoryMappedViewAccessor mmvAudioTime;

        public const float SONG_DETAIL_OFFSET = 5f;

        private void Awake()
        {
            _timeProvider = this;
            ResetState();

            Directory.CreateDirectory(MajEnv.SharedMemoryPath);
            var mmfAudioTimeFileStream = new FileStream(
                MajEnv.MmfAudioTimePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
            if (mmfAudioTimeFileStream.Length < sizeof(float))
                mmfAudioTimeFileStream.SetLength(sizeof(float));
            mmfAudioTime = MemoryMappedFile.CreateFromFile(
                mmfAudioTimeFileStream,
                null,
                sizeof(float),
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                false
            );
            mmvAudioTime = mmfAudioTime.CreateViewAccessor();
        }

        private void Update()
        {
            TimeData.IsStart = IsStart;
            TimeData.deltaTime = IsRecord ? FRAME_LENGTH_SEC : Time.deltaTime;
            if (!IsStart)
            {
                mmvAudioTime.Read(0, out float time);
                AudioTime = time;
                NoteTime = AudioTime - offset;
                TimeData.NoteTime = NoteTime;
                return;
            }

            if (IsRecord)
            {
                AudioTime = (float)(startAt + accumulated + (Time.timeAsDouble - startRealtime));
                NoteTime = AudioTime - offset;
            }
            else
            {
                AudioTime = (float)(startAt + accumulated + playbackClock.Elapsed.TotalSeconds * speed);
                NoteTime = AudioTime - offset;
            }

            mmvAudioTime.Write(0, AudioTime);

            TimeData.NoteTime = NoteTime;
        }

        public unsafe void LoadSV(ReadOnlySpan<SimaiTimingPoint> commaTimings)
        {
            SVList.Clear();
            if (SVFuncArgs.IsCreated) SVFuncArgs.Dispose();
            foreach (var timing in commaTimings)
            {
                if (SVList.Length == 0 || SVList[^1].sVeloc != timing.SVeloc)
                {
                    SVList.Add(((float)timing.Timing, timing.SVeloc));
                }
            }
            if (SVList.Length == 0)
            {
                return;
            }

            SVFuncArgs = new(SVList.Length + 1, Allocator.Persistent);

            float pos = 0f;
            float lastTime = 0f;
            float lastSpeed = 1f;

            SVFuncArgs[0] = (1, 0);

            for (var i = 0; i < SVList.Length; i++)
            {
                var (time, sveloc) = SVList[i];
                pos += lastSpeed * (time - lastTime);

                // 使用当前点的时间和当前新的速度构建往后的运动方程
                SVFuncArgs[i + 1] = (sveloc, pos - sveloc * time);
                lastTime = time;
                lastSpeed = sveloc;
            }

            TimeData.__SVListPtr = SVList.GetUnsafeReadOnlyPtr();
            TimeData.__SVListLength = SVList.Length;
            TimeData.__SVFuncArgsPtr = SVFuncArgs.GetUnsafeReadOnlyPtr();
            TimeData.__SVFuncArgsLength = SVFuncArgs.Length;
        }

        public void SetStartTime(double _startAt, double _offset, float _speed, PlaybackMode mode, int fps = 60)
        {
            IsRecord = false;
            AudioTime = 0f;
            NoteTime = 0f;
            accumulated = 0f;
            playbackClock.Reset();
            Time.timeScale = 1f;

            startAt = (float)_startAt;
            offset = (float)_offset;
            speed = _speed;

            switch (mode)
            {
                case PlaybackMode.Normal:
                    {
                        speed = _speed;
                        Time.captureFramerate = 0;
                        playbackClock.Restart();
                    }
                    break;
                case PlaybackMode.IncludeOp:
                    {
                        startAt -= SONG_DETAIL_OFFSET;
                        speed = _speed;
                        Time.captureFramerate = 0;
                        playbackClock.Restart();
                    }
                    break;
                case PlaybackMode.Record:
                    {
                        IsRecord = true;
                        startRealtime = Time.timeAsDouble;
                        startAt -= SONG_DETAIL_OFFSET;
                        Time.timeScale = _speed;
                        Time.captureFramerate = fps;
                    }
                    break;
            }

            IsStart = true;
            //calculate immediately
            Update();
        }

        public void Pause()
        {
            if (!IsStart) return;

            var now = IsRecord ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
            accumulated += IsRecord
                ? now - startRealtime
                : playbackClock.Elapsed.TotalSeconds * speed;

            playbackClock.Reset();
            IsStart = false;
        }

        public void Resume(float? _speed)
        {
            if (_speed != null) speed = _speed.Value;
            if (IsStart) return;

            if (IsRecord)
                startRealtime = Time.timeAsDouble;
            else
                playbackClock.Restart();

            IsStart = true;
        }

        public void ResetState()
        {
            IsStart = false;
            IsRecord = false;
            AudioTime = 0f;
            NoteTime = 0f;
            startRealtime = 0f;
            startAt = 0f;
            offset = 0f;
            accumulated = 0f;
            speed = 1f;
            playbackClock.Reset();
            Time.timeScale = 1f;
            Time.captureFramerate = 0;
        }

        private void OnDestroy()
        {
            mmvAudioTime?.Dispose();
            mmfAudioTime?.Dispose();

            if (SVList.IsCreated) SVList.Dispose();
            if (SVFuncArgs.IsCreated) SVFuncArgs.Dispose();
        }
    }

    [BurstCompile]
    public struct TimeDataB
    {
        public bool IsStart;
        public float NoteTime;
        public readonly float FakeNoteTime => GetPositionAtTime(NoteTime);
        // 存在减少late的隐秘指令
        public readonly float DJAutoTime => TimeData.NoteTime + 0.01f;

        /// <summary>
        /// 录制模式时恒为FRAME_LENGTH_SEC，因此无法与Time.xxx配合使用！！！
        /// </summary>
        public float deltaTime;

        // public NativeList<(float time, float sVeloc)> SVList;
        // public NativeArray<(float k, float b)> SVFuncArgs;
        public unsafe void* __SVListPtr;
        public int __SVListLength;
        public unsafe void* __SVFuncArgsPtr;
        public int __SVFuncArgsLength;

        readonly unsafe ReadOnlySpan<(float time, float sVeloc)> SVList => new(__SVListPtr, __SVListLength);
        readonly unsafe ReadOnlySpan<(float k, float b)> SVFuncArgs => new(__SVFuncArgsPtr, __SVFuncArgsLength);


        public readonly float GetPositionAtTime(float t)
        {
            if (SVList.Length == 0)
                return t;
            if (t < SVList[0].time)
                return t;
            if (t >= SVList[^1].time)
                return SVFuncArgs[^1].k * t + SVFuncArgs[^1].b;

            // 二分查找
            int low = 0;
            int high = SVList.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (t < SVList[mid].time)
                {
                    if (mid == 0 || t >= SVList[mid - 1].time) return SVFuncArgs[mid].k * t + SVFuncArgs[mid].b;
                    high = mid - 1;
                }
                else low = mid + 1;
            }
            return SVFuncArgs[^1].k * t + SVFuncArgs[^1].b;
        }

        public readonly float GetFrame() => NoteTime * 1000 / 16.6667f;
    }
}