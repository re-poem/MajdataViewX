#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.MajWs;
using MajSimai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.Properties;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class PlayManager : MonoBehaviour
    {
        public Camera MainCamera;
        public Camera GameCamera;

        public static ViewSummary Summary => new()
        {
            State = _state,
            ErrMsg = _errMsg,
            Timeline = _thisFrameSec
        };

        public static bool IsReloading;

        private static SimaiChart _chart = SimaiChart.Empty;

        private static ViewStatus _state = ViewStatus.Idle;
        private static string _errMsg = string.Empty;
        private static float _thisFrameSec = 0f;

        private static Thread? _audioManagerThread;
        private static int _audioManagerThreadRunning;

        private static float? _speed;

        private static MajViewSetting _setting = new();

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
            MainCamera.targetDisplay = -1;
            GameCamera.targetDisplay = 0;

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
            _inputManager.CurrentCamera = GameCamera;


            SlideTableNeo.InitializeStandardSlideTable();

            _state = CheckIsLoaded() ? ViewStatus.Loaded : ViewStatus.Idle;
        }

        private bool CheckIsLoaded() => _audioManager.IsTrackLoaded &&
                                        _bgManager.IsBgLoaded &&
                                        _bgManager.IsVideoLoaded;

        public void Setting(MajViewSetting setting, MajVolumeSetting volumeSetting)
        {
            _setting = setting;
            _audioManager.Setting(setting.GlobalAudioOffset, volumeSetting);
        }

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

        public async UniTask<bool> PlayAsync(PlaybackMode playmode,
            double startAt, float speed,
            string title, string artist, float offset,
            string designer, string level, string fumen,
            IList<SimaiCommand> commands, int difficulty,
            string? maidataPath = null)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not ViewStatus.Loaded)
                return false;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                //chart
                _chart = await SimaiParser.ParseChartAsync(level, designer, fumen);

                var noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(_setting.TapSpeed + 0.9975f, -0.985558604f)));
                var touchSpeed = _setting.TouchSpeed;
                var ignoreOffset = startAt - offset;
                //simulate
                NoteHelper.AutoPlayModeSS.Data = _setting.AutoMode;
                _inputManager.ShowHand = _setting.ShowHand;
                //UI
                _objectCounter.StartOutput(_setting.ComboStatusType, _setting.UIType);
                //bg
                bgCover.color = new Color(0f, 0f, 0f, _setting.BackgroundDim);
                bgOutsideCover.color = new Color(0f, 0f, 0f, _setting.BackgroundOutsideDim);
                _bgManager.ShowBG();
                _bgManager.ShowVideo(_setting.ResizeBg);
                //sfx
                var clockCount = 0;
                if (playmode != PlaybackMode.Normal)
                {
                    var clockCommand = commands.FirstOrDefault(c => c.Prefix == "clock_count");
                    if (clockCommand != default) int.TryParse(clockCommand.Value, out clockCount);
                }
                _audioManager.GenerateAnswerSFX(_chart, ignoreOffset, clockCount);

                switch (playmode)
                {
                    case PlaybackMode.Normal:
                        await _dataLoader.Load(
                            _chart, ignoreOffset,
                            title, artist, difficulty,
                            noteSpeed, touchSpeed,
                            _setting.SmoothSlideAnime,
                            _setting.LegacySlideLayer,
                            _setting.MineAutoSlide);

                        _allPerfectManager.enabled = false;
                        _timeProvider.SetStartTime(startAt, offset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.IncludeOp:
                        await _dataLoader.Load(
                            _chart, ignoreOffset,
                            title, artist, difficulty,
                            noteSpeed, touchSpeed,
                            _setting.SmoothSlideAnime,
                            _setting.LegacySlideLayer,
                            _setting.MineAutoSlide);

                        _bgManager.PlaySongDetail();
                        _audioManager.noteSfxPlaybackRequests[AudioManager.TRACK_START] = true; //track_start

                        _allPerfectManager.enabled = true;
                        _timeProvider.SetStartTime(startAt, offset, speed, playmode);
                        _audioManager.PlayTrack();
                        break;
                    case PlaybackMode.Record:
                        canvasButtons.SetActive(false);
                        if (!Directory.Exists(maidataPath))
                        {
                            throw new InvalidPathException($"maidata path is required");
                        }

                        await _dataLoader.Load(
                            _chart, ignoreOffset,
                            title, artist, difficulty,
                            noteSpeed, touchSpeed,
                            _setting.SmoothSlideAnime,
                            _setting.LegacySlideLayer,
                            _setting.MineAutoSlide);

                        _bgManager.PlaySongDetail();

                        _allPerfectManager.enabled = true;
                        _state = ViewStatus.Playing;
                        _screenRecorder.StartRecording(maidataPath,
                            _setting.OutputFps, _setting.ExportQuality,
                            () =>
                            {
                                _timeProvider.SetStartTime(startAt, offset, speed, playmode, _setting.OutputFps);
                            }).ContinueWith(() =>
                        {
                            canvasButtons.SetActive(true);
                            _state = ViewStatus.Loaded;
                        }).Forget();
                        return true; //directly return
                }

                //save last speed for resume
                _speed = speed;

                _state = ViewStatus.Playing;
                return true;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
                return false;
            }
        }

        public async UniTask ResumeAsync()
        {
            await ResumeAsync(_speed!.Value);
        }

        public async UniTask ResumeAsync(float speed)
        {
            while (_state is ViewStatus.Busy)
                await UniTask.Yield();

            if (_state is not ViewStatus.Paused)
                return;

            _state = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _timeProvider.Resume(speed);

                _bgManager.ContinueVideo();

                _audioManager.PlayTrack();
                _audioManager.ResumeTouchHoldSound();

                _state = ViewStatus.Playing;
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
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
                //if not so, the last frame will be like after ResetAllManagers
                await UniTask.Yield();

                ResetAllManagers();
                // IsReloading = false;
                // in NoteManager, wait for notes cleared
            }
            catch (Exception ex)
            {
                _errMsg = ex.ToString();
                _state = ViewStatus.Error;
            }
        }

        private void ResetAllManagers()
        {
            _screenRecorder.ResetState();
            _objectCounter.ResetState();
            MajBurst.InputData.ResetState();
            _noteManager.ResetState();
            _timeProvider.ResetState();
            _audioManager.ResetState();
            _bgManager.ResetState();
            _effectManager.ResetState();
            _inputManager.ResetState();
            _allPerfectManager.ResetState();
            _dataLoader.ResetState();

            _state = CheckIsLoaded() ? ViewStatus.Loaded : ViewStatus.Idle;
            bgCover.color = new Color(0f, 0f, 0f, 0f);
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