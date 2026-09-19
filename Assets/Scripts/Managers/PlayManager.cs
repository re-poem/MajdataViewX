#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.MajWs;
using Cimai;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.Properties;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class PlayManager : MonoBehaviour
    {
        public static ViewSummary Summary => new()
        {
            State = _state,
            ErrMsg = _errMsg,
        };

        private static ViewStatus _state = ViewStatus.Idle;
        private static string _errMsg = string.Empty;

        private static Thread? _audioManagerThread;
        private static int _audioManagerThreadRunning;

        private static MajViewSetting _setting = new();
        private static float _currentOffset;
        private static SimaiChart? _chart;

        private SpriteRenderer bgCover;
        private SpriteRenderer bgOutsideCover;
        private GameObject canvasButtons;

        private void Awake()
        {
            _playManager = this;
        }

        // 这里是游戏内部的东西的启动初始化
        private void Start()
        {
            bgCover = GameObject.Find("BgCover").GetComponent<SpriteRenderer>();
            bgOutsideCover = GameObject.Find("BgOutsideCover").GetComponent<SpriteRenderer>();
            canvasButtons = GameObject.Find("CanvasButtons");

            _ = new AudioManager();
            Volatile.Write(ref _audioManagerThreadRunning, 1);
            _audioManagerThread = new Thread(() =>
            {
                while (Volatile.Read(ref _audioManagerThreadRunning) != 0)
                {
                    _audioManager.OnUpdate();
                    Thread.Sleep(1);
                }
            })
            {
                IsBackground = true,
                Name = "Majdata SFX Trigger",
                Priority = System.Threading.ThreadPriority.AboveNormal,
            };
            _audioManagerThread.Start();

            MajBurst.__DataSS.Data = new MajBurstData
            {
                TimeData = new(),
                InputData = new(),
                MultTouchHandler = new(),
                GlobalRandom = new((uint)"MajdataX".GetHashCode()),
            };
            //MajBurst.TimeData.Init();
            MajBurst.InputData.Init();
            MajBurst.MultTouchHandler.Init();

            _ = new InputManager();


            SlideTableNeo.InitializeStandardSlideTable();
        }

        private bool CheckIsLoaded() => _audioManager.IsTrackLoaded &&
                                        _bgManager.IsBgLoaded &&
                                        _bgManager.IsVideoLoaded;

        public async UniTask LoadAsync(string audioPath, string bgPath, string? pvPath)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();
            _state = ViewStatus.Busy;

            try
            {
                await UniTask.SwitchToMainThread();

                //audio
                _audioManager.LoadTrack(audioPath);

                //bg
                if (File.Exists(bgPath))
                {
                    BgManager.hasBg = true;
                    _bgManager.LoadBG(bgPath);
                }
                else
                {
                    BgManager.hasBg = false;
                }

                //video
                if (pvPath is not null && File.Exists(pvPath))
                {
                    BgManager.hasVideo = true;
                    _bgManager.LoadVideo(pvPath);
                }
                else
                {
                    BgManager.hasVideo = false;
                }

                _state = ViewStatus.Loaded;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        public void Setting(MajViewSetting setting, MajVolumeSetting volumeSetting)
        {
            _setting = setting;

            NoteHelper.NoteSettingsSS.Data = new NoteSettings()
            {
                AutoPlayMode = _setting.AutoMode,
                TapSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(_setting.TapSpeed + 0.9975f, -0.985558604f))),
                TouchSpeed = _setting.TouchSpeed,
                LegacySlideLayer = _setting.LegacySlideLayer,
                SmoothSlideAnime = _setting.SmoothSlideAnime,
                MineAutoSlide = _setting.MineAutoSlide,
            };
            //audio
            _audioManager.Setting(setting.GlobalAudioOffset, volumeSetting);
            //simulate
            _inputManager.ShowHand = _setting.ShowHand;
            //counter
            _objectCounter.Setting(_setting.ComboStatusType, _setting.UIType);
            //bg
            bgCover.color = new Color(0f, 0f, 0f, _setting.BackgroundDim);
            bgOutsideCover.color = new Color(0f, 0f, 0f, _setting.BackgroundOutsideDim);
            _bgManager.ResizeBg = _setting.ResizeBg;
        }

        public async UniTask UpdateAsync(
            string? chartText,
            int selectedDifficulty,
            string title,
            string artist,
            string level,
            string designer,
            float offset,
            int clockCount)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            var previousState = _state;
            _state = ViewStatus.Busy;
            
            var chart = SimaiChart.Parse(chartText ?? string.Empty);

            _chart?.Dispose();
            _chart = chart;
            _currentOffset = offset;
            _timeProvider.offset = offset;

            //answer
            _audioManager.GenerateAnswerSFX(chart, clockCount);

            //counter
            _objectCounter.ResetLoaded();
            _objectCounter.CountNoteSum(chart);
            _objectCounter.ReportMeterBpm(chart);

            await _dataLoader.Load(chart, title, artist, level, designer, selectedDifficulty);

            _state = previousState;
        }

        public async UniTask PlayAsync(PlaybackMode playmode, double startAt, float speed, string recordPath)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not (ViewStatus.Loaded or ViewStatus.Paused))
                return;

            if (playmode is PlaybackMode.Record && !ScreenRecorder.IsSupported)
            {
                _wsServer.Error("Video recording is currently supported only on Windows.");
                return;
            }

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();
                var ignoreOffset = startAt - _currentOffset;

                //bg
                _bgManager.ShowBG();
                _bgManager.ShowVideo();
                //sfx
                _audioManager.ResetAnswerSFX(ignoreOffset);
                //counter
                _objectCounter.ResetCur();
                _objectCounter.CountIgnoreNoteCountAsync(_chart, ignoreOffset);
                //notes
                //MajBurst.InputData.ResetState(); in ResetLoadedNote
                _noteManager.ResetState(); //reset DJAuto hands (PlayUpdateJob is still running when IsStart==false)
                _noteManager.ResetLoadedNote(ignoreOffset);
                _noteManager.ResetLoadedPlay(ignoreOffset);
                MajBurst.MultTouchHandler.ResetMultTouchState();

                switch (playmode)
                {
                    case PlaybackMode.Normal:
                        _allPerfectManager.enabled = false;

                        _timeProvider.SetStartTime(startAt, _currentOffset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.IncludeOp:
                        _allPerfectManager.enabled = true;

                        _bgManager.PlaySongDetail();
                        _audioManager.noteSfxPlaybackRequests[AudioManager.TRACK_START] = true; //track_start

                        _timeProvider.SetStartTime(startAt, _currentOffset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.Record:
                        if (!Directory.Exists(recordPath))
                        {
                            throw new InvalidPathException($"maidata path is required");
                        }

                        canvasButtons.SetActive(false);
                        _allPerfectManager.enabled = true;

                        _bgManager.PlaySongDetail();
                        _screenRecorder.StartRecording(recordPath,
                            _setting.OutputFps, _setting.ExportQuality,
                            () =>
                            {
                                _timeProvider.SetStartTime(startAt, _currentOffset, speed, playmode, _setting.OutputFps);
                            }).ContinueWith(() =>
                        {
                            canvasButtons.SetActive(true);
                            _state = ViewStatus.Loaded;
                        }).Forget();
                        break;
                    case PlaybackMode.Preview:
                        _allPerfectManager.enabled = false;
                        _state = ViewStatus.Paused;
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(playmode), playmode, null);
                }

                _state = ViewStatus.Playing;
                return;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
                return;
            }
        }

        public async UniTask PauseAsync()
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not ViewStatus.Playing)
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _timeProvider.Pause();

                _bgManager.PauseVideo();

                _audioManager.PauseTrack();
                _audioManager.PauseTouchHoldSound();

                _state = ViewStatus.Paused;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        public async UniTask StopAsync()
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not (ViewStatus.Playing or ViewStatus.Paused or ViewStatus.Error))
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _screenRecorder.StopRecording();
                await UniTask.Yield();

                //_objectCounter.ResetCur();
                _timeProvider.ResetState();
                _audioManager.ResetState();
                _bgManager.ResetState();
                _effectManager.ResetState();
                _allPerfectManager.ResetState();

                _state = CheckIsLoaded() ? ViewStatus.Loaded : ViewStatus.Idle;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        private void OnDestroy()
        {
            Volatile.Write(ref _audioManagerThreadRunning, 0);
            if (_audioManagerThread is { IsAlive: true } &&
                _audioManagerThread != Thread.CurrentThread)
            {
                _audioManagerThread.Join();
            }
            _audioManagerThread = null;

            _audioManager.OnDestroy();
            _inputManager.OnDestroy();
            MajBurst.InputData.Dispose();
        }
    }
}
