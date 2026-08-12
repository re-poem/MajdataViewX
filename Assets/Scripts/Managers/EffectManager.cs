#nullable enable

using MajdataViewX.Base;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Notes;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class EffectManager : MonoBehaviour
    {
        public const int EFFECT_COUNT = BUTTON_COUNT + SENSOR_COUNT;

        private static readonly int PerfectHash = Animator.StringToHash("perfect");
        private static readonly int GreatHash = Animator.StringToHash("great");
        private static readonly int GoodHash = Animator.StringToHash("good");
        private static readonly int BPerfectHash = Animator.StringToHash("bPerfect");
        private static readonly int BGreatHash = Animator.StringToHash("bGreat");
        private static readonly int BGoodHash = Animator.StringToHash("bGood");
        private static readonly int FireHash = Animator.StringToHash("fire");

        [SerializeField]
        GameObject effectPrefab;

        public NativeArray<EffectData> judgeEffectRequests = new(EFFECT_COUNT, Allocator.Persistent);
        public unsafe EffectData* JudgeEffectRequestsPtr => (EffectData*)judgeEffectRequests.GetUnsafePtr();

        private readonly Animator[] tapAnimators = new Animator[EFFECT_COUNT];

        private readonly GameObject[] holdEffects = new GameObject[EFFECT_COUNT];
        private readonly Material[] holdMaterials = new Material[EFFECT_COUNT];

        private readonly GameObject[] touchEffects = new GameObject[EFFECT_COUNT];
        private readonly Animator[] touchAnimators = new Animator[EFFECT_COUNT];

        private readonly Animator[] judgeAnimators = new Animator[EFFECT_COUNT];
        private readonly SpriteRenderer[] judgeRenderers = new SpriteRenderer[EFFECT_COUNT];

        private readonly SpriteRenderer[] fastLateRenderers = new SpriteRenderer[EFFECT_COUNT];

        private GameObject fireworkEffect;
        private Animator fireworkAnimator;

        private void Awake()
        {
            _effectManager = this;
        }

        private void Start()
        {
            var parent = GameObject.Find("NoteEffects");

            for (var i = 0; i < EFFECT_COUNT; i++)
            {
                float2 pos;
                if (i < BUTTON_COUNT)
                {
                    pos = MajPos.GetBtnPos(i);
                }
                else
                {
                    pos = MajPos.GetAreaPos((SensorType)i - 8);
                }
                float ang = 0f;
                if (i - 8 < 16)       // 1~8, A1~B8
                    ang = -45f * (i % 8) - 22.5f;
                else if (i - 8 > 16)  // D1~E8
                    ang = -45f * (i % 8 - 1) - 22.5f;

                var effect = Instantiate(effectPrefab, parent.transform, false);
                effect.transform.SetLocalPositionAndRotation(new Vector3(pos.x, pos.y, 0), Quaternion.Euler(new Vector3(0, 0, ang)));

                var tapEffect = effect.transform.GetChild(0).gameObject;
                tapAnimators[i] = tapEffect.GetComponent<Animator>();
                if (i > 7) tapEffect.SetActive(false); // touch 部分的不要了 

                holdEffects[i] = effect.transform.GetChild(1).gameObject;
                holdMaterials[i] = holdEffects[i].GetComponent<ParticleSystemRenderer>().material;
                holdEffects[i].SetActive(false);

                touchEffects[i] = effect.transform.GetChild(2).gameObject;
                touchEffects[i].transform.localEulerAngles = new Vector3(0, 0, -ang); // 回正
                touchAnimators[i] = touchEffects[i].GetComponent<Animator>();
                if (i <= 7) touchEffects[i].SetActive(false); // tap 部分的不要了 

                var judgeEffect = effect.transform.GetChild(3).gameObject;
                judgeAnimators[i] = judgeEffect.GetComponent<Animator>();
                judgeRenderers[i] = judgeEffect.transform.GetChild(0).GetChild(0).gameObject.GetComponent<SpriteRenderer>();
                judgeEffect.transform.GetChild(0).GetChild(1).gameObject.GetComponent<SpriteRenderer>().sprite = _noteSkinManager.JudgeText_BPerfect;
                fastLateRenderers[i] = judgeEffect.transform.GetChild(1).GetChild(0).GetComponent<SpriteRenderer>();

                fireworkEffect = GameObject.Find("FireworkEffect");
                fireworkAnimator = fireworkEffect.GetComponent<Animator>();
            }
        }

        private void Update()
        {
            ProcessEffectRequests();
        }

        private void OnDestroy()
        {
            if (judgeEffectRequests.IsCreated) judgeEffectRequests.Dispose();
        }

        public void ProcessEffectRequests()
        {
            for (var i = 0; i < judgeEffectRequests.Length; i++)
            {
                var req = judgeEffectRequests[i];

                if (!req.IsMine ||
                    (req.IsMine && req.JudgeGrade is (JudgeGrade.Miss or JudgeGrade.TooFast)))
                {
                    if (req.Effect.HasFlag(EffectType.Tap))
                    {
                        PlayTapEffect(i, req.JudgeGrade, req.IsBreak);
                    }
                    if (req.Effect.HasFlag(EffectType.Touch))
                    {
                        PlayTouchEffect(i, req.JudgeGrade, req.IsBreak);
                    }
                    if (req.Effect.HasFlag(EffectType.Firework))
                    {
                        PlayFireworkEffect(i);
                    }
                }

                holdEffects[i].SetActive(req.HasHolding);
                if (req.HasHolding)
                {
                    holdMaterials[i].SetColor("_Color", req.HoldingColor);
                }
            }

            for (var i = 0; i < judgeEffectRequests.Length; i++)
                judgeEffectRequests[i] = default;
        }

        private void PlayTapEffect(int pos, JudgeGrade judge, bool isBreak)
        {
            // Effect & Judge Text
            switch (judge)
            {
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[1];
                    if (isBreak)
                    {
                        tapAnimators[pos].speed = 0.9f;
                        tapAnimators[pos].SetTrigger(BGoodHash);
                    }
                    else
                    {
                        tapAnimators[pos].speed = 1f;
                        tapAnimators[pos].SetTrigger(GoodHash);
                    }
                    break;
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat1st:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat1st:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[2];
                    if (isBreak)
                    {
                        tapAnimators[pos].speed = 0.9f;
                        tapAnimators[pos].SetTrigger(BGreatHash);
                    }
                    else
                    {
                        tapAnimators[pos].speed = 1f;
                        tapAnimators[pos].SetTrigger(GreatHash);
                    }
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.FastPerfect2nd:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[3];
                    if (isBreak)
                    {
                        tapAnimators[pos].speed = 0.9f;
                        tapAnimators[pos].SetTrigger(BPerfectHash);
                    }
                    else
                    {
                        tapAnimators[pos].speed = 1f;
                        tapAnimators[pos].SetTrigger(PerfectHash);
                    }
                    break;
                case JudgeGrade.LateCritical:
                case JudgeGrade.FastCritical:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[4];
                    if (isBreak)
                    {
                        tapAnimators[pos].speed = 0.9f;
                        tapAnimators[pos].SetTrigger(BPerfectHash);
                    }
                    else
                    {
                        tapAnimators[pos].speed = 1f;
                        tapAnimators[pos].SetTrigger(PerfectHash);
                    }
                    break;
                default:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[0];
                    break;
            }

            // Judge Anim
            if (isBreak && (judge is JudgeGrade.LateCritical or JudgeGrade.FastCritical))
                judgeAnimators[pos].SetTrigger(BPerfectHash);
            else
                judgeAnimators[pos].SetTrigger(PerfectHash);

            // Fast / Late
            if (judge is JudgeGrade.Miss or JudgeGrade.LateCritical or JudgeGrade.FastCritical)
            {
                fastLateRenderers[pos].sprite = null;
            }
            else
            {
                var isFast = judge <= JudgeGrade.FastCritical;
                if (isFast)
                    fastLateRenderers[pos].sprite = _noteSkinManager.FastText;
                else
                    fastLateRenderers[pos].sprite = _noteSkinManager.LateText;
            }
        }

        private void PlayTouchEffect(int pos, JudgeGrade judge, bool isBreak)
        {
            // Effect & Judge Text
            switch (judge)
            {
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[1];
                    touchAnimators[pos].SetTrigger(GoodHash);
                    break;
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat1st:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat1st:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[2];
                    touchAnimators[pos].SetTrigger(GreatHash);
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.FastPerfect2nd:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[3];
                    touchAnimators[pos].SetTrigger(PerfectHash);
                    break;
                case JudgeGrade.LateCritical:
                case JudgeGrade.FastCritical:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[4];
                    touchAnimators[pos].SetTrigger(PerfectHash);
                    break;
                default:
                    judgeRenderers[pos].sprite = _noteSkinManager.JudgeText[0];
                    break;
            }

            // Judge Anim
            if (isBreak && (judge is JudgeGrade.LateCritical or JudgeGrade.FastCritical))
                judgeAnimators[pos].SetTrigger(BPerfectHash);
            else
                judgeAnimators[pos].SetTrigger(PerfectHash);

            // Fast / Late
            if (judge is JudgeGrade.Miss or JudgeGrade.LateCritical or JudgeGrade.FastCritical)
            {
                fastLateRenderers[pos].sprite = null;
            }
            else
            {
                var isFast = judge <= JudgeGrade.FastCritical;
                if (isFast)
                    fastLateRenderers[pos].sprite = _noteSkinManager.FastText;
                else
                    fastLateRenderers[pos].sprite = _noteSkinManager.LateText;
            }
        }

        public void PlayFireworkEffect(int pos)
        {
            float2 worldPos;
            if (pos is < 0 or > EFFECT_COUNT) return;
            else if (pos < BUTTON_COUNT) worldPos = MajPos.GetBtnPos(pos);
            else worldPos = MajPos.GetAreaPos((SensorType)(pos - 8));
            fireworkEffect.transform.position = new float3(worldPos, 0);
            fireworkAnimator.SetTrigger(FireHash);
        }

        public void ResetState()
        {
            for (var i = 0; i < judgeEffectRequests.Length; i++)
                judgeEffectRequests[i] = default;
        }
    }

    public struct EffectData
    {
        public EffectType Effect;
        public JudgeGrade JudgeGrade;
        public bool IsBreak;
        public bool IsMine;
        public bool HasHolding;
        public Color HoldingColor;
    }

    [Flags]
    public enum EffectType
    {
        None = 0,
        Tap = 1 << 0,
        Touch = 1 << 1,
        Firework = 1 << 2
    }
}