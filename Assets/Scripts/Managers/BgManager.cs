#nullable enable


using MajdataViewX.Utils;
using System;
using Cysharp.Threading.Tasks;
using MajdataPlay.FFmpeg;
using UnityEngine;
using UnityEngine.UI;

using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class BgManager : MonoBehaviour
    {
        [SerializeField]
        private Sprite bgDummy = null!;
        [SerializeField]
        private Sprite defaultBg = null!;

        [SerializeField]
        private Material fullscreenBgMaterial = null!;
        [SerializeField]
        private Material circledBgMaterial = null!;
        private static readonly int RadiusID = Shader.PropertyToID("_Radius");

        /// <summary>Cover layer shown while no video frame is available.</summary>
        [SerializeField]
        private Image coverRenderer = null!;

        /// <summary>Video layer that receives the player output.</summary>
        [SerializeField]
        private RawImage videoRenderer = null!;

        public bool resizeBg;

        private RawImage _jacketImage = null!;
        private GameObject _songDetail = null!;
        private static readonly int ShowHash = Animator.StringToHash("show");
        private Animator _detailAnim = null!;
        private FFmpegVideoPlayer _videoPlayer = null!;

        private const float FULLSCREEN_SCALE_X = 1.777f;
        private const int VIDEO_LOAD_TIMEOUT_SECONDS = 15;

        private Sprite? Bg { get; set; }

        /// <summary>Invalidates pending video loads so a newer background, or none, owns the display.</summary>
        private int _videoLoadVersion;

        /// <summary>Cover layout captured at startup so the Inspector values stay the single source.</summary>
        private Vector3 _coverDefaultScale;
        private Vector2 _coverDefaultSize;

        public static bool HasBg;
        public static bool HasVideo;
        public bool IsBgLoaded => !HasBg || Bg != null;
        public bool IsVideoLoaded => !HasVideo || _videoPlayer.IsPrepared;

        private void Awake()
        {
            _bgManager = this;
        }

        private void Start()
        {
            _jacketImage = GameObject.Find("Jacket").GetComponent<RawImage>();
            _songDetail = GameObject.Find("CanvasSongDetail");
            _songDetail.SetActive(false);

            _videoPlayer = GetComponent<FFmpegVideoPlayer>();
            _videoPlayer.TextureChanged += OnVideoTextureChanged;
            _videoPlayer.ErrorReceived += OnVideoError;
            _videoClock = () => _timeProvider.AudioTime;
            _detailAnim = _songDetail.GetComponent<Animator>();

            _coverDefaultScale = coverRenderer.transform.localScale;
            _coverDefaultSize = coverRenderer.rectTransform.sizeDelta;

            DisableVideo();
        }

        /// <summary>视频时钟：主线程每帧更新的 AudioTime，解码线程读它不会碰 Unity API。</summary>
        private Func<double> _videoClock = null!;

        private void Update()
        {
            // 离线导出里游戏时间按帧推进、与墙钟脱钩，让播放器跟着游戏时间走；
            // 否则只能靠 PlaybackRate 追按墙钟推进的视频，误差一大就被压到 1/16，画面像停住。
            var offline = Time.captureFramerate != 0;
            _videoPlayer.MediaClock = offline ? _videoClock : null;

            if (_timeProvider.AudioTime < 0)
            {
                return;
            }

            var delta = (float)_videoPlayer.TimeSeconds - _timeProvider.AudioTime;
            // 录制时位置已由游戏时间推进，基准倍率是 1；闭环保留，用来吸收素材偏移与解码滞后。
            var speed = offline ? 1f : _timeProvider.CurrentSpeed;
            SetPlaybackRate(delta switch
            {
                < -0.1f => speed + 0.2f,
                > 0.1f => speed - 0.2f,
                _ => speed
            });

            // 只在明显不同步时打一条，正常导出不产生噪音。
            if (offline && Mathf.Abs(delta) > 0.5f)
            {
                Debug.Log($"[BgSync] audio={_timeProvider.AudioTime:F3} video={_videoPlayer.TimeSeconds:F3} delta={delta:F3} rate={_videoPlayer.PlaybackRate:F2}");
            }
        }

        /// <summary>Writes a clamped playback rate so the drift controller cannot leave the supported range.</summary>
        /// <param name="rate">The requested playback multiplier.</param>
        private void SetPlaybackRate(float rate)
        {
            var clamped = Mathf.Clamp(float.IsNaN(rate) ? 1f : rate, 0.0625f, 16f);
            if (!Mathf.Approximately(clamped, _videoPlayer.PlaybackRate))
            {
                _videoPlayer.PlaybackRate = clamped;
            }
        }

        public void PlaySongDetail()
        {
            _songDetail.SetActive(true);
            _detailAnim.SetTrigger(ShowHash);
        }

        public void LoadBg(string path)
        {
            DestroyLoadedBackground();
            Bg = TexLoader.LoadSprite(path);
        }

        private void DestroyLoadedBackground()
        {
            if (Bg == null)
            {
                return;
            }

            if (Bg.texture != null)
            {
                Destroy(Bg.texture);
            }

            Destroy(Bg);
            Bg = null;
        }

        public void ShowBg()
        {
            DisableVideo();

            if (Bg == null || !HasBg)
            {
                _jacketImage.texture = bgDummy.texture;
                return;
            }

            _jacketImage.texture = Bg.texture;
            coverRenderer.sprite = Bg;
            var texture = Bg.texture;
            var scale = 1140f / texture.width;
            coverRenderer.rectTransform.sizeDelta = new Vector2(texture.width, texture.height);
            coverRenderer.transform.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>Brings the cover back with its Inspector layout and hides the video layer.</summary>
        private void ShowCover()
        {
            ++_videoLoadVersion;
            coverRenderer.enabled = true;
            coverRenderer.sprite = defaultBg;
            coverRenderer.transform.localScale = _coverDefaultScale;
            coverRenderer.rectTransform.sizeDelta = _coverDefaultSize;
            videoRenderer.enabled = false;
            videoRenderer.texture = null;
        }

        /// <summary>Shows the cover and closes the player, invalidating any pending load.</summary>
        private void DisableVideo()
        {
            ShowCover();
            _videoPlayer.Close();
        }

        /// <summary>Starts loading a background video and keeps the cover until its first frame is ready.</summary>
        /// <param name="path">The local path or supported URL of the video to load.</param>
        public void LoadVideo(string path)
        {
            // ShowBg closes the previous video and invalidates its pending load.
            ShowBg();
            var loadVersion = _videoLoadVersion;
            LoadVideoAsync(path, loadVersion).Forget();
        }

        /// <summary>Preloads the video with a timeout and restores the cover when it fails.</summary>
        /// <param name="path">The video to preload.</param>
        /// <param name="loadVersion">The load generation captured before starting.</param>
        private async UniTaskVoid LoadVideoAsync(string path, int loadVersion)
        {
            try
            {
                // Preload presents the first frame without advancing the playback clock.
                await _videoPlayer.PreloadAsync(path).AsUniTask()
                    .Timeout(TimeSpan.FromSeconds(VIDEO_LOAD_TIMEOUT_SECONDS), DelayType.Realtime);
                if (loadVersion != _videoLoadVersion)
                {
                    return;
                }

                // The first frame exists now, so the video layer can take over from the cover.
                ShowVideoLayer();
            }
            catch (Exception e)
            {
                // A newer load, a picture change, or destruction owns the background now.
                if (loadVersion != _videoLoadVersion)
                {
                    return;
                }

                if (e is TimeoutException)
                {
                    Debug.LogError($"Background video preload timed out after {VIDEO_LOAD_TIMEOUT_SECONDS} s: {path}");
                }
                else
                {
                    Debug.LogException(e);
                }

                ShowBg();
            }
        }

        /// <summary>Switches the display to the video layer and fits the current frame.</summary>
        private void ShowVideoLayer()
        {
            var texture = _videoPlayer.Texture;
            coverRenderer.enabled = false;
            videoRenderer.texture = texture!;
            videoRenderer.enabled = true;
            // 纹理还没就绪时用播放器报告的视频尺寸，否则适配被跳过，缩放会停留在上一次的值。
            ApplyVideoFit(texture != null ? texture.width : (int)_videoPlayer.Width,
                texture != null ? texture.height : (int)_videoPlayer.Height);
        }

        /// <summary>Plays the loaded video in sync with the chart clock once the audio has started.</summary>
        public void ShowVideo()
        {
            if (!HasVideo)
            {
                return;
            }

            var loadVersion = _videoLoadVersion;
            ShowVideoAsync(loadVersion).Forget();
        }

        /// <summary>Waits for the chart clock and the prepared frame, then starts playback.</summary>
        /// <param name="loadVersion">The load generation captured when the video was shown.</param>
        private async UniTaskVoid ShowVideoAsync(int loadVersion)
        {
            try
            {
                // LoadVideo usually preloaded already; prepare again as a fallback for direct calls.
                if (!_videoPlayer.IsPrepared)
                {
                    await _videoPlayer.PrepareAsync();
                }

                var token = this.GetCancellationTokenOnDestroy();
                // AudioTime 在播放开始前是从共享内存读到的上一次播放的位置，不能拿它当本次起点去 seek；
                // 必须等到本次播放真正开始（IsStart）并且已经进入曲子时间。
                while (!_timeProvider.IsStart || _timeProvider.AudioTime <= 0)
                {
                    await UniTask.WaitForEndOfFrame(token);
                }

                // A failed load or a newer background owns the display now.
                if (loadVersion != _videoLoadVersion || !_videoPlayer.IsPrepared)
                {
                    return;
                }

                await _videoPlayer.SeekAsync(_timeProvider.AudioTime);
                _videoPlayer.Play();
                ShowVideoLayer();
            }
            catch (OperationCanceledException)
            {
                // The scene was left before the chart clock or the seek finished.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowBg();
            }
        }

        /// <summary>Shows each new video frame on the video layer.</summary>
        /// <param name="player">The player that presented the frame.</param>
        /// <param name="texture">The new output texture, or null when the video closed.</param>
        private void OnVideoTextureChanged(FFmpegVideoPlayer player, Texture? texture)
        {
            videoRenderer.texture = texture!;
            videoRenderer.enabled = texture != null && !coverRenderer.enabled;
        }

        /// <summary>Restores the cover when playback fails after the first frame was shown.</summary>
        /// <param name="player">The player that reported the error.</param>
        /// <param name="error">The reported error message.</param>
        private void OnVideoError(FFmpegVideoPlayer player, string error) => ShowBg();

        /// <summary>Fits the video layer to the source aspect and picks the matching material.</summary>
        /// <param name="width">The presented frame width in pixels.</param>
        /// <param name="height">The presented frame height in pixels.</param>
        private void ApplyVideoFit(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var aspect = (float)height / width;
            if (resizeBg)
            {
                var fullscreen = FULLSCREEN_SCALE_X;
                videoRenderer.transform.localScale = new Vector3(fullscreen, fullscreen * aspect, fullscreen);
                SetBackgroundMaterial(fullscreenBgMaterial);
                return;
            }

            // The placeholder size comes from the Inspector, so it stays the single source.
            var rect = videoRenderer.rectTransform.rect;
            // lossyScale 含上一次写入的 localScale，直接相乘会让每次适配都再乘一遍缩放（越算越偏），这里把它除掉。
            var localY = Mathf.Abs(videoRenderer.transform.localScale.y);
            var parentScaleY = videoRenderer.transform.lossyScale.y / Mathf.Max(localY, 1e-6f);
            var worldHeight = rect.height * parentScaleY;
            var circleDiameter = circledBgMaterial.GetFloat(RadiusID) * 2f;
            var fitScale = circleDiameter / (worldHeight * Mathf.Max(1f, aspect));
            SetBackgroundMaterial(circledBgMaterial);

            // localScale is written last so the measurement above reads the unscaled rect.
            videoRenderer.transform.localScale = new Vector3(fitScale, fitScale * aspect, fitScale);
        }

        /// <summary>Assigns one material to both background layers so they stay visually identical.</summary>
        /// <param name="material">The material to show; its own instance is not cloned.</param>
        private void SetBackgroundMaterial(Material material)
        {
            coverRenderer.material = material;
            videoRenderer.material = material;
        }

        public void ResetState()
        {
            // Keep the prepared media for the next song instead of closing it; ShowCover invalidates the version.
            if (_videoPlayer.IsPrepared && _videoPlayer.IsSeekable)
            {
                _videoPlayer.Pause();
                _videoPlayer.TimeSeconds = 0;
            }
            else
            {
                _videoPlayer.Stop();
            }

            ShowCover();
            SetBackgroundMaterial(circledBgMaterial);

            if (_songDetail != null)
            {
                _songDetail.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            ++_videoLoadVersion;
            _videoPlayer.TextureChanged -= OnVideoTextureChanged;
            _videoPlayer.ErrorReceived -= OnVideoError;
            _jacketImage.texture = null;
            DestroyLoadedBackground();
        }
    }
}
