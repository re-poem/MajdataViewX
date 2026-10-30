#nullable enable


using MajdataViewX.Utils;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

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

        [FormerlySerializedAs("ResizeBg")] public bool resizeBg;

        private RawImage _jacketImage = null!;
        private GameObject _songDetail = null!;
        private static readonly int ShowHash = Animator.StringToHash("show");
        private Animator _detailAnim = null!;
        private SpriteRenderer _spriteRender = null!;
        private VideoPlayer _videoPlayer = null!;

        private float _smoothRDelta;

        private const float CIRCLED_SCALE_X = 1.1f;
        private const float FULLSCREEN_SCALE_X = 1.777f;

        private Sprite? Bg { get; set; }
        private string? VideoUrl { get; set; }

        public static bool HasBg;
        public static bool HasVideo;
        public bool IsBgLoaded => !HasBg || Bg != null;
        public bool IsVideoLoaded => !HasVideo || !string.IsNullOrWhiteSpace(VideoUrl);

        private static Sprite? _emptySprite;
        private bool _videoPaused;

        private void Awake()
        {
            _bgManager = this;
        }

        private void Start()
        {
            _jacketImage = GameObject.Find("Jacket").GetComponent<RawImage>();
            _songDetail = GameObject.Find("CanvasSongDetail");
            _songDetail.SetActive(false);

            _spriteRender = GetComponent<SpriteRenderer>();
            _videoPlayer = GetComponent<VideoPlayer>();
            _detailAnim = _songDetail.GetComponent<Animator>();

            _emptySprite = Sprite.Create(new Texture2D(1080, 1080), new Rect(0, 0, 1080, 1080), new Vector2(0.5f, 0.5f));
        }

        private void Update()
        {
            if (HasVideo && _videoPaused)
            {
                _videoPlayer.time = _timeProvider.AudioTime;
                _videoPlayer.Play();
                _videoPlayer.Pause();
                return;
            }
            var delta = (float)_videoPlayer.clockTime - _timeProvider.AudioTime;
            _smoothRDelta += (Time.unscaledDeltaTime - _smoothRDelta) * 0.01f;
            if (_timeProvider.AudioTime < 0) return;
            var realSpeed = Time.deltaTime / _smoothRDelta;

            if (Time.captureFramerate != 0)
            {
                _videoPlayer.playbackSpeed = realSpeed - delta;
                return;
            }

            if (delta < -0.01f)
                _videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed + 0.2f;
            else if (delta > 0.01f)
                _videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed - 0.2f;
            else
                _videoPlayer.playbackSpeed = _timeProvider.CurrentSpeed;
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
            if (Bg == null) return;

            if (Bg.texture != null)
                Destroy(Bg.texture);

            Destroy(Bg);
            Bg = null;
        }

        public void ShowBg()
        {
            if (Bg == null || !HasBg)
            {
                _jacketImage.texture = bgDummy.texture;
                _spriteRender.sprite = defaultBg;
                return;
            }

            _jacketImage.texture = Bg.texture;
            _spriteRender.sprite = Bg;
            var scale = 1140f / Bg.texture.width;
            gameObject.transform.localScale = new Vector3(scale, scale, scale);
        }

        public void LoadVideo(string path)
        {
            VideoUrl = "file://" + path;
        }

        public void ShowVideo()
        {
            if (!HasVideo) return;

            _videoPlayer.url = VideoUrl;
            StartCoroutine(WaitFumenStart());
            IEnumerator WaitFumenStart()
            {
                _videoPlayer.Prepare();

                //secret hack: if not so, the bg won't be set to defaultBg but full white
                _spriteRender.sprite = _emptySprite;

                while (_timeProvider.AudioTime <= 0) yield return new WaitForEndOfFrame();
                while (!_videoPlayer.isPrepared) yield return new WaitForEndOfFrame();
                _videoPlayer.Play();
                _videoPlayer.time = _timeProvider.AudioTime;
                _videoPaused = false;

                var scale = _videoPlayer.height / (float)_videoPlayer.width;
                if (resizeBg)
                {
                    gameObject.transform.localScale = new Vector3(FULLSCREEN_SCALE_X, FULLSCREEN_SCALE_X * scale);
                    _spriteRender.material = fullscreenBgMaterial;
                }
                else
                {
                    var circleDiameter = circledBgMaterial.GetFloat(RadiusID) * 2f;
                    var spriteSize = _spriteRender.sprite!.bounds.size;
                    var longestSide = Mathf.Max(spriteSize.x, spriteSize.y * scale);
                    var fitScale = circleDiameter / longestSide;
                    gameObject.transform.localScale = new Vector3(fitScale, fitScale * scale, fitScale);
                    _spriteRender.material = circledBgMaterial;
                }
            }
        }


        public void ResetState()
        {
            _videoPlayer.Stop();
            _videoPaused = false;
            // 不销毁上一曲背景图(Texture2D/Sprite)，下次 LoadBG 销毁
            gameObject.transform.localScale = new(CIRCLED_SCALE_X, CIRCLED_SCALE_X, CIRCLED_SCALE_X);
            _spriteRender.material = circledBgMaterial;
            _spriteRender.sprite = defaultBg;
            _smoothRDelta = 0f;

            if (_songDetail != null)
                _songDetail.SetActive(false);
        }

        private void OnDestroy()
        {
            DestroyLoadedBackground();
            if (_emptySprite == null) return;

            var texture = _emptySprite.texture;
            Destroy(_emptySprite);
            if (texture != null)
                Destroy(texture);
            _emptySprite = null;
        }
    }
}