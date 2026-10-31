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

        private float _smoothRDelta;

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
            _detailAnim = _songDetail.GetComponent<Animator>();

            _coverDefaultScale = coverRenderer.transform.localScale;
            _coverDefaultSize = coverRenderer.rectTransform.sizeDelta;

            DisableVideo();
        }

        private void Update()
        {
            // 暂时不搞这个
            // if (HasVideo && _paused)
            // {
            //     _videoPlayer.SeekAsync(_timeProvider.AudioTime);
            //     _videoPlayer.Play();
            //     _videoPlayer.Pause();
            //     return;
            // }

            if (_timeProvider.AudioTime < 0)
            {
                return;
            }

            var delta = (float)_videoPlayer.TimeSeconds - _timeProvider.AudioTime;
            _smoothRDelta += (Time.unscaledDeltaTime - _smoothRDelta) * 0.01f;
            // The first frame has no smoothed delta yet.
            var realSpeed = _smoothRDelta > 0f ? Time.deltaTime / _smoothRDelta : _timeProvider.CurrentSpeed;

            if (Time.captureFramerate != 0)
            {
                SetPlaybackRate(realSpeed - delta);
                return;
            }

            SetPlaybackRate(delta switch
            {
                < -0.1f => _timeProvider.CurrentSpeed + 0.2f,
                > 0.1f => _timeProvider.CurrentSpeed - 0.2f,
                _ => _timeProvider.CurrentSpeed
            });
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
            ApplyVideoFit(texture);
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
                while (_timeProvider.AudioTime <= 0)
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
        /// <param name="texture">The presented frame supplying the source dimensions.</param>
        private void ApplyVideoFit(Texture? texture)
        {
            if (texture == null)
            {
                return;
            }

            // Texture dimensions are ints and non-zero here, so the aspect needs no extra guard.
            var aspect = (float)texture.height / texture.width;
            if (resizeBg)
            {
                var fullscreen = FULLSCREEN_SCALE_X;
                videoRenderer.transform.localScale = new Vector3(fullscreen, fullscreen * aspect, fullscreen);
                SetBackgroundMaterial(fullscreenBgMaterial);
                return;
            }

            // The placeholder size comes from the Inspector, so it stays the single source.
            var rect = videoRenderer.rectTransform.rect;
            var worldHeight = rect.height * videoRenderer.transform.lossyScale.y;
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
            _smoothRDelta = 0f;

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
