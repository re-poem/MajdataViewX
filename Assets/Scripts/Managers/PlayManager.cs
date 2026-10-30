#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.Protocol;
using Cimai;
using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class PlayManager : MonoBehaviour
    {
        public static ViewStatus State { get; private set; } = ViewStatus.Idle;
        public static string ErrMsg { get; private set; } = string.Empty;
        public static double Position => _timeProvider?.AudioTime ?? 0;


        private static Thread? _audioManagerThread;
        private static int _audioManagerThreadRunning;

        private static MajViewSetting _setting = new();
        private static float _currentOffset;
        private static SimaiChart? _chart;

        private SpriteRenderer _bgCover = null!;
        private SpriteRenderer _bgOutsideCover = null!;
        private GameObject _canvasButtons = null!;

        private void Awake()
        {
            _playManager = this;
        }

        // 这里是游戏内部的东西的启动初始化
        private void Start()
        {
            _bgCover = GameObject.Find("BgCover").GetComponent<SpriteRenderer>();
            _bgOutsideCover = GameObject.Find("BgOutsideCover").GetComponent<SpriteRenderer>();
            _canvasButtons = GameObject.Find("CanvasButtons");

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

        public async UniTask<bool> LoadAsync(string audioPath, string bgPath, string? pvPath)
        {
            while (State is ViewStatus.Busy)
                await UniTask.Yield();
            State = ViewStatus.Busy;

            try
            {
                await UniTask.SwitchToMainThread();

                //audio
                _audioManager.LoadTrack(audioPath);

                //bg
                if (File.Exists(bgPath))
                {
                    BgManager.HasBg = true;
                    _bgManager.LoadBg(bgPath);
                }
                else
                {
                    BgManager.HasBg = false;
                }

                //video
                if (pvPath is not null && File.Exists(pvPath))
                {
                    BgManager.HasVideo = true;
                    _bgManager.LoadVideo(pvPath);
                }
                else
                {
                    BgManager.HasVideo = false;
                }

                State = ViewStatus.Loaded;
                return true;
            }
            catch (Exception ex)
            {
                ErrMsg = ex.ToString();
                State = ViewStatus.Error;
                return false;
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
            _bgCover.color = new Color(0f, 0f, 0f, _setting.BackgroundDim);
            _bgOutsideCover.color = new Color(0f, 0f, 0f, _setting.BackgroundOutsideDim);
            _bgManager.resizeBg = _setting.ResizeBg;
        }

        public async UniTask<bool> ParseAsync(
            string? chartText,
            float offset,
            string title,
            string artist,
            string level,
            string designer,
            int difficulty)
        {
            while (State is ViewStatus.Busy)
                await UniTask.Yield();

            var previousState = State;
            State = ViewStatus.Busy;

            var source = Encoding.UTF8.GetBytes(chartText ?? string.Empty);
            SimaiChart chart;
            unsafe
            {
                fixed (byte* sourcePtr = source)
                    chart = SimaiChart.Parse(sourcePtr, source.Length);
            }

            _chart?.Dispose();
            _chart = chart;
            _currentOffset = offset;

            //answer
            // TODO: MajProtocol 2.0 的 SimaiRequest 没有 clock_count 字段，这里先固定为 4，
            // 之后改为从 Simai 内容的 &clock_count 指令解析。
            _audioManager.GenerateAnswerSFX(chart, 4);

            //counter
            _objectCounter.ResetLoaded();
            _objectCounter.CountNoteSum(chart);
            _objectCounter.ReportMeterBpm(chart);

            await _dataLoader.Load(chart, title, artist, level, designer, difficulty);

            State = previousState;
            return true;
        }

        public async UniTask<bool> PlayAsync(PlaybackMode playmode, double startAt, float speed)
        {
            while (State is ViewStatus.Busy)
                await UniTask.Yield();

            if (State is not (ViewStatus.Loaded or ViewStatus.Paused))
                return false;

            // metadata 与谱面装载都在 Parse 阶段完成，这里只管启动。
            State = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                var ignoreOffset = startAt - _currentOffset;

                //bg
                _bgManager.ShowBg();
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
                        var recordPath = MajEnv.RecordingDirectory;
                        Directory.CreateDirectory(recordPath);

                        _canvasButtons.SetActive(false);
                        _allPerfectManager.enabled = true;

                        _bgManager.PlaySongDetail();
                        _screenRecorder.StartRecording(recordPath,
                            _setting.OutputFps, _setting.ExportQuality,
                            () =>
                            {
                                _timeProvider.SetStartTime(startAt, _currentOffset, speed, playmode, _setting.OutputFps);
                            }).ContinueWith(() =>
                        {
                            _canvasButtons.SetActive(true);
                            State = ViewStatus.Loaded;
                        }).Forget();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(playmode), playmode, null);
                }

                State = ViewStatus.Playing;
                return true;
            }
            catch (Exception ex)
            {
                ErrMsg = ex.ToString();
                State = ViewStatus.Error;
                return false;
            }
        }

        public async UniTask<bool> PauseAsync()
        {
            while (State is ViewStatus.Busy)
                await UniTask.Yield();

            if (State is not ViewStatus.Playing)
                return false;

            State = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _screenRecorder.StopRecording();
                await UniTask.Yield();

                _timeProvider.ResetState();
                _audioManager.ResetState();
                _bgManager.ResetState();
                _effectManager.ResetState();
                _allPerfectManager.ResetState();

                State = CheckIsLoaded() ? ViewStatus.Loaded : ViewStatus.Idle;
                return true;
            }
            catch (Exception ex)
            {
                ErrMsg = ex.ToString();
                State = ViewStatus.Error;
                return false;
            }
        }

        public async UniTask<bool> UnloadAsync()
        {
            while (State is ViewStatus.Busy)
                await UniTask.Yield();

            if (State is ViewStatus.Playing or ViewStatus.Paused)
            {
                if (!await PauseAsync()) return false;
            }

            State = ViewStatus.Busy;
            try
            {
                await UniTask.SwitchToMainThread();

                _chart?.Dispose();
                _chart = null;
                _currentOffset = 0;

                _objectCounter.ResetLoaded();

                State = ViewStatus.Idle;
                return true;
            }
            catch (Exception ex)
            {
                ErrMsg = ex.ToString();
                State = ViewStatus.Error;
                return false;
            }
        }


        private void OnDestroy()
        {
            _chart?.Dispose();
            _chart = null;
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