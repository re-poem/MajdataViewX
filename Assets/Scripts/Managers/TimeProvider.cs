#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR
#define MMF_USABLE
#endif

using Cimai;
using MajdataViewX.Base;
using MajdataViewX.Types.Protocol;
using System;
using System.Diagnostics;
using System.IO;
#if MMF_USABLE
using System.IO.MemoryMappedFiles;
#endif
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
        public float CurrentSpeed => IsRecord ? Time.timeScale : _speed;

        private NativeList<(float time, float sVeloc)> _svList = new(20, Allocator.Persistent);
        private NativeArray<(float k, float b)> _svFuncArgs;

        private double _startRealtime; //the beginning of the program is 0
        private float _startAt; //the beginning of the audio is 0
        private float _offset;
        private float _speed;

        private readonly Stopwatch _playbackClock = new();



#if MMF_USABLE
        private MemoryMappedFile _mmfAudioTime;
        private MemoryMappedViewAccessor _mmvAudioTime;
#endif

        public const float SONG_DETAIL_OFFSET = 5f;

        private void Awake()
        {
            _timeProvider = this;
            ResetState();

#if MMF_USABLE
            var mmfAudioTimeFileStream = new FileStream(
                MajEnv.MmfAudioTimePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            );
            if (mmfAudioTimeFileStream.Length < sizeof(float))
                mmfAudioTimeFileStream.SetLength(sizeof(float));
            _mmfAudioTime = MemoryMappedFile.CreateFromFile(
                mmfAudioTimeFileStream,
                null,
                sizeof(float),
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                false
            );
            _mmvAudioTime = _mmfAudioTime.CreateViewAccessor();
#endif
        }

        private void Update()
        {
            TimeData.IsStart = IsStart;
            TimeData.deltaTime = IsRecord ? FRAME_LENGTH_SEC : Time.deltaTime;
            if (!IsStart)
            {
#if MMF_USABLE
                _mmvAudioTime.Read(0, out float time);
                AudioTime = time;
                NoteTime = AudioTime - _offset;
                TimeData.NoteTime = NoteTime;
#endif
                return;
            }

            if (IsRecord)
                AudioTime = (float)(_startAt + (Time.timeAsDouble - _startRealtime));
            else
                AudioTime = (float)(_startAt + _playbackClock.Elapsed.TotalSeconds * _speed);

#if MMF_USABLE
            _mmvAudioTime.Write(0, AudioTime);
#endif

            NoteTime = AudioTime - _offset;
            TimeData.NoteTime = NoteTime;
        }

        public unsafe void LoadSV(ReadOnlySpan<SimaiTiming> commaTimings)
        {
            _svList.Clear();
            if (_svFuncArgs.IsCreated) _svFuncArgs.Dispose();
            foreach (var timing in commaTimings)
            {
                if (_svList.Length == 0 || !Mathf.Approximately(_svList[^1].sVeloc, timing.SVeloc))
                {
                    _svList.Add(((float)timing.Time, timing.SVeloc));
                }
            }
            if (_svList.Length == 0)
            {
                return;
            }

            _svFuncArgs = new(_svList.Length + 1, Allocator.Persistent);

            var pos = 0f;
            var lastTime = 0f;
            var lastSpeed = 1f;

            _svFuncArgs[0] = (1, 0);

            for (var i = 0; i < _svList.Length; i++)
            {
                var (time, sveloc) = _svList[i];
                pos += lastSpeed * (time - lastTime);

                // 使用当前点的时间和当前新的速度构建往后的运动方程
                _svFuncArgs[i + 1] = (sveloc, pos - sveloc * time);
                lastTime = time;
                lastSpeed = sveloc;
            }

            TimeData.SVListPtr = _svList.GetUnsafeReadOnlyPtr();
            TimeData.SVListLength = _svList.Length;
            TimeData.SVFuncArgsPtr = _svFuncArgs.GetUnsafeReadOnlyPtr();
            TimeData.SVFuncArgsLength = _svFuncArgs.Length;
        }

        public void SetStartTime(double startAt, double offset, float speed, PlaybackMode mode, int fps = 60)
        {
            ResetState();

            _startAt = (float)startAt;
            _offset = (float)offset;
            _speed = speed;

            switch (mode)
            {
                case PlaybackMode.Normal:
                    {
                        this._speed = speed;
                        Time.captureFramerate = 0;
                        _playbackClock.Restart();
                    }
                    break;
                case PlaybackMode.IncludeOp:
                    {
                        this._startAt -= SONG_DETAIL_OFFSET;
                        this._speed = speed;
                        Time.captureFramerate = 0;
                        _playbackClock.Restart();
                    }
                    break;
                case PlaybackMode.Record:
                    {
                        IsRecord = true;
                        _startRealtime = Time.timeAsDouble;
                        this._startAt -= SONG_DETAIL_OFFSET;
                        Time.timeScale = speed;
                        Time.captureFramerate = fps;
                    }
                    break;
            }

            IsStart = true;
            //calculate immediately
            Update();
        }


        public void ResetState()
        {
            IsStart = false;
            IsRecord = false;
            AudioTime = 0f;
            NoteTime = 0f;
            _startRealtime = 0f;
            _startAt = 0f;
            _offset = 0f;
            _speed = 1f;
            _playbackClock.Reset();
            Time.timeScale = 1f;
            Time.captureFramerate = 0;
        }

        private void OnDestroy()
        {
#if MMF_USABLE
            _mmvAudioTime?.Dispose();
            _mmfAudioTime?.Dispose();
#endif

            if (_svList.IsCreated) _svList.Dispose();
            if (_svFuncArgs.IsCreated) _svFuncArgs.Dispose();
        }
    }

    [BurstCompile]
    public struct TimeDataB
    {
        public bool IsStart;
        public float NoteTime;
        public readonly float FakeNoteTime => GetPositionAtTime(NoteTime);

        /// <summary>
        /// 录制模式时恒为FRAME_LENGTH_SEC，因此无法与Time.xxx配合使用！！！
        /// </summary>
        public float deltaTime;

        public unsafe void* SVListPtr;
        public int SVListLength;
        public unsafe void* SVFuncArgsPtr;
        public int SVFuncArgsLength;

        private readonly unsafe ReadOnlySpan<(float time, float sVeloc)> SVList => new(SVListPtr, SVListLength);
        private readonly unsafe ReadOnlySpan<(float k, float b)> SVFuncArgs => new(SVFuncArgsPtr, SVFuncArgsLength);


        public readonly float GetPositionAtTime(float t)
        {
            if (SVList.Length == 0)
                return t;
            if (t < SVList[0].time)
                return t;
            if (t >= SVList[^1].time)
                return SVFuncArgs[^1].k * t + SVFuncArgs[^1].b;

            // 二分查找
            var low = 0;
            var high = SVList.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) >> 1;
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