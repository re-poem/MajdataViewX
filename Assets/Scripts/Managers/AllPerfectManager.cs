#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Protocol;
using UnityEngine;

using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class AllPerfectManager : MonoBehaviour
    {
        private static readonly int PlayAllPerfectHash = Animator.StringToHash("playAllPerfect");
        [SerializeField]
        private Animator allPerfect = null!;

        private bool _isPlayed;

        private void Awake()
        {
            _allPerfectManager = this;
        }

        private void Start()
        {
            allPerfect.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (PlayManager.State is not ViewStatus.Playing)
                return;

            if (!_objectCounter.AllFinished) return;
            if (_isPlayed)
            {
                if (allPerfect.gameObject.activeSelf) return;
                _playManager.PauseAsync().Forget();
            }
            else
            {
                allPerfect.gameObject.SetActive(true);
                allPerfect.SetTrigger(PlayAllPerfectHash);
                _audioManager.noteSfxPlaybackRequests[AudioManager.ALL_PERFECT] = true;
                _isPlayed = true;
            }
        }

        public void ResetState()
        {
            allPerfect.gameObject.SetActive(false);
            _isPlayed = false;
        }
    }
}