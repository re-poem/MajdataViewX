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
    /// <summary>
    /// Background管理全链路非常简单：Load->Show
    /// Load不必说了，Show的时候会根据存在与否显示视频/图片
    /// Show分成ShowRenderer和Show本体两个部分，它们被安排在了
    /// Show本体部分中的正确位置，而不需要另外调用。
    /// </summary>
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

        private RawImage _jacketImage = null!;
        private GameObject _songDetail = null!;
        private static readonly int ShowHash = Animator.StringToHash("show");
        private Animator _songDetailAnim = null!;
        
        /// <summary>Cover layer shown while no video frame is available.</summary>
        [SerializeField]
        private Image coverRenderer = null!;
        private Sprite? _cover;
        private Vector3 _coverDefaultScale;
        private Vector2 _coverDefaultSize;
        
        /// <summary>Video layer that receives the player output.</summary>
        [SerializeField]
        private RawImage videoRenderer = null!;
        private FFmpegVideoPlayer _videoPlayer = null!;
        private Func<double> _videoClock = null!;
        public bool FullScreenBg { get; set; }
        private const float FULLSCREEN_SCALE_X = 1.777f;
        private const int VIDEO_LOAD_TIMEOUT_SECONDS = 15;
        /// <summary>Invalidates pending video loads so a newer background, or none, owns the display.</summary>
        private int _videoLoadVersion;

        
        public bool HasCover { get; set; }
        public bool HasVideo { get; set; }
        public bool IsCoverLoaded => !HasCover || _cover != null;
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
            _songDetailAnim = _songDetail.GetComponent<Animator>();
            
            _coverDefaultScale = coverRenderer.transform.localScale;
            _coverDefaultSize = coverRenderer.rectTransform.sizeDelta;
            
            _videoPlayer = GetComponent<FFmpegVideoPlayer>();
            _videoPlayer.TextureChanged += OnVideoTextureChanged;
            _videoPlayer.ErrorReceived += OnVideoError;
            _videoClock = () => _timeProvider.AudioTime;

            ShowCover();
        }

        /// <summary>视频时钟：主线程每帧更新的 AudioTime，解码线程读它不会碰 Unity API。</summary>

        private void Update()
        {
            // 离线导出里游戏时间按帧推进、与墙钟脱钩，让播放器跟着游戏时间走；
            // 否则只能靠 PlaybackRate 追按墙钟推进的视频，误差一大就被压到 1/16，画面像停住。
            var offline = Time.captureFramerate != 0;
            _videoPlayer.MediaClock = offline ? _videoClock : null;

            if (!_timeProvider.IsStart || _timeProvider.AudioTime < 0)
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
        }
        
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
            _songDetailAnim.SetTrigger(ShowHash);
        }

        
        
        
        public void LoadCover(string path)
        {
            DestroyLoadedCover();
            _cover = TexLoader.LoadSprite(path);
        }

        private void DestroyLoadedCover()
        {
            if (_cover == null)
            {
                return;
            }

            if (_cover.texture != null)
            {
                Destroy(_cover.texture);
            }

            Destroy(_cover);
            _cover = null;
        }

        private void ShowCoverRenderer()
        {
            ++_videoLoadVersion;
            
            coverRenderer.enabled = true;
            videoRenderer.enabled = false;

            coverRenderer.sprite = defaultBg;
            coverRenderer.transform.localScale = _coverDefaultScale;
            coverRenderer.rectTransform.sizeDelta = _coverDefaultSize;
        }
        
        private void ShowCover()
        {
            ShowCoverRenderer();
            if (_cover == null || !HasCover)
            {
                _jacketImage.texture = bgDummy.texture;
                return;
            }

            _jacketImage.texture = _cover.texture;
            coverRenderer.sprite = _cover;
            var texture = _cover.texture;
            var scale = 1140f / texture.width;
            coverRenderer.rectTransform.sizeDelta = new(texture.width, texture.height);
            coverRenderer.transform.localScale = new(scale, scale, scale);
        }
        
        
        

        public void LoadVideo(string path)
        {
            var loadVersion = _videoLoadVersion;
            LoadVideoAsync(path, loadVersion).Forget();
        }
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

                // 背景视频每轮都要按曲子时间定位，不可 seek 的素材在这里就当加载失败。
                if (!_videoPlayer.IsSeekable)
                {
                    throw new NotSupportedException($"Background video is not seekable: {path}");
                }

                // The first frame exists now, so the video layer can take over from the cover.
                ShowVideoRenderer();
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

                ShowCover();
            }
        }

        // 我们无需接管video player的close/destroy
        
        /// <summary>Switches the display to the video layer and fits the current frame.</summary>
        private void ShowVideoRenderer()
        {
            ApplyVideoFit();
            coverRenderer.enabled = false;
            videoRenderer.enabled = true;
        }

        /// <summary>Plays the loaded video in sync with the chart clock once the audio has started.</summary>
        private void ShowVideo()
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

                ShowVideoRenderer();
                await _videoPlayer.SeekAsync(_timeProvider.AudioTime);
                _videoPlayer.Play();
            }
            catch (OperationCanceledException)
            {
                // The scene was left before the chart clock or the seek finished.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowCover();
            }
        }
        
        private void ApplyVideoFit()
        {
            var width = _videoPlayer.Width;
            var height = _videoPlayer.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var aspect = (float)height / width;
            if (FullScreenBg)
            {
                videoRenderer.transform.localScale = new(FULLSCREEN_SCALE_X, FULLSCREEN_SCALE_X * aspect, FULLSCREEN_SCALE_X);
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



        public void Show()
        {
            if (HasVideo)
            {
                ShowVideo();
            }
            else
            {
                ShowCover();
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
        private void OnVideoError(FFmpegVideoPlayer player, string error) => ShowCover();
        
        /// <summary>Assigns one material to both background layers so they stay visually identical.</summary>
        /// <param name="material">The material to show; its own instance is not cloned.</param>
        private void SetBackgroundMaterial(Material material)
        {
            coverRenderer.material = material;
            videoRenderer.material = material;
        }

        public void ResetState()
        {
            if (_videoPlayer.IsPlaying)
                _videoPlayer.Pause();

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
            DestroyLoadedCover();
        }
    }
}
