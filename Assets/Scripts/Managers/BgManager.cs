#nullable enable


using MajdataViewX.Utils;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class BgManager : MonoBehaviour
    {
        [SerializeField]
        private Sprite bgDummy;
        [SerializeField]
        private Sprite defaultBg;

        [SerializeField]
        private Material fullscreenBgMaterial;
        [SerializeField]
        private Material circledBgMaterial;

        [SerializeField]
        private RawImage jacketImage;
        [SerializeField]
        private GameObject songDetail;

        private static readonly int ShowHash = Animator.StringToHash("show");
        private Animator detailAnim;
        private SpriteRenderer spriteRender;
        private VideoPlayer videoPlayer;

        private float smoothRDelta;

        const float CIRCLED_SCALE_X = 1.1f;
        const float FULLSCREEN_SCALE_X = 1.777f;

        private Sprite? Bg { get; set; }
        private string? VideoUrl { get; set; }

        public static bool hasBg;
        public static bool hasVideo;
        public bool IsBgLoaded => !hasBg || Bg != null;
        public bool IsVideoLoaded => !hasVideo || !string.IsNullOrWhiteSpace(VideoUrl);

        private static Sprite? _emptySprite;

        private void Awake()
        {
            _bgManager = this;
        }

        private void Start()
        {
            songDetail.SetActive(false);

            spriteRender = GetComponent<SpriteRenderer>();
            videoPlayer = GetComponent<VideoPlayer>();
            detailAnim = songDetail.GetComponent<Animator>();

            _emptySprite = Sprite.Create(new Texture2D(1080, 1080), new Rect(0, 0, 1080, 1080), new Vector2(0.5f, 0.5f));
        }

        private void Update()
        {
            var delta = (float)videoPlayer.clockTime - _timeProvider.AudioTime;
            smoothRDelta += (Time.unscaledDeltaTime - smoothRDelta) * 0.01f;
            if (_timeProvider.AudioTime < 0) return;
            var realSpeed = Time.deltaTime / smoothRDelta;

            if (Time.captureFramerate != 0)
            {
                videoPlayer.playbackSpeed = realSpeed - delta;
                return;
            }

            if (delta < -0.01f)
                videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed + 0.2f;
            else if (delta > 0.01f)
                videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed - 0.2f;
            else
                videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed;
        }

        public void PlaySongDetail()
        {
            songDetail.SetActive(true);
            detailAnim.SetTrigger(ShowHash);
        }

        public void LoadBG(string path)
        {
            DestroyLoadedBackground();
            Bg = TexLoader.LoadSprite(path);
        }

        private void DestroyLoadedBackground()
        {
            if (Bg != null)
            {
                if (Bg.texture != null)
                    Destroy(Bg.texture);

                Destroy(Bg);
                Bg = null;
            }
        }

        public void ShowBG()
        {
            if (Bg == null || !hasBg)
            {
                jacketImage.texture = bgDummy.texture;
                spriteRender.sprite = defaultBg;
                return;
            }

            jacketImage.texture = Bg.texture;
            spriteRender.sprite = Bg;
            var scale = 1140f / Bg.texture.width;
            gameObject.transform.localScale = new Vector3(scale, scale, scale);
        }

        public void LoadVideo(string path)
        {
            VideoUrl = "file://" + path;
        }

        public void ShowVideo(bool resizeBg)
        {
            if (!hasVideo) return;

            videoPlayer.url = VideoUrl;
            StartCoroutine(WaitFumenStart());
            IEnumerator WaitFumenStart()
            {
                videoPlayer.Prepare();

                //secret hack: if not so, the bg won't be set to defaultBg but full white
                spriteRender.sprite = _emptySprite;

                while (_timeProvider.AudioTime <= 0) yield return new WaitForEndOfFrame();
                while (!videoPlayer.isPrepared) yield return new WaitForEndOfFrame();
                videoPlayer.Play();
                videoPlayer.time = _timeProvider.AudioTime;

                var scale = videoPlayer.height / (float)videoPlayer.width;
                if (resizeBg)
                {
                    gameObject.transform.localScale = new Vector3(FULLSCREEN_SCALE_X, FULLSCREEN_SCALE_X * scale);
                    spriteRender.material = fullscreenBgMaterial;
                }
                else
                {
                    gameObject.transform.localScale = new Vector3(CIRCLED_SCALE_X, CIRCLED_SCALE_X * scale);
                    spriteRender.material = circledBgMaterial;
                }
            }
        }

        public void PauseVideo()
        {
            if (!hasVideo) return;
            videoPlayer.Pause();
        }

        public void ContinueVideo()
        {
            if (!hasVideo) return;
            videoPlayer.Play();
        }

        public void ResetState()
        {
            videoPlayer.Stop();
            // 销毁上一曲背景图(Texture2D/Sprite)，避免滞留到下次 LoadBG
            DestroyLoadedBackground();
            gameObject.transform.localScale = new Vector3(CIRCLED_SCALE_X, CIRCLED_SCALE_X, CIRCLED_SCALE_X);
            spriteRender.material = circledBgMaterial;
            spriteRender.sprite = defaultBg;
            smoothRDelta = 0f;

            if (songDetail != null)
                songDetail.SetActive(false);
        }

        private void OnDestroy()
        {
            DestroyLoadedBackground();
            if (_emptySprite != null)
            {
                var texture = _emptySprite.texture;
                Destroy(_emptySprite);
                if (texture != null)
                    Destroy(texture);
                _emptySprite = null;
            }
        }
    }
}