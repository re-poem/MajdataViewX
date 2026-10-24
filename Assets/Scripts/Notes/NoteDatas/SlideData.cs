using MajdataViewX.Base;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Notes;
using Unity.Burst;
using Unity.Mathematics;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Notes.NoteDatas
{
    [BurstCompile]
    public struct SlideData
    {
        public float tapTime;
        public float shootTime;

        /// <summary>
        /// 在最后一个区停留的时间
        /// </summary>
        /// <remarks>在isJudged（实际上已判定）后，递减以用于延迟slideok显示</remarks>
        public float lastStayTime;
        /// <summary>
        /// 正解帧
        /// </summary>
        public float judgeTiming;
        /// <summary>
        /// 显示slideok的时间（实际被判定的时间 + lastStayTime）
        /// </summary>
        public float finishJudgeTiming;

        // FOR WIFI STARS CALCULATE ONLY
        public int startPos;
        public int endPos;

        public float LastFor;
        public float hspeed;
        public readonly float speed => NoteHelper.Settings.TapSpeed * hspeed;

        public bool isEach;
        public bool isEx;
        public bool isBreak;
        public bool isMine;
        // slide不适合SV！
        //public bool usingSV;

        public bool isWifi;

        public bool isFolded;
        public bool hasSlideGuide;
        public bool hasTapGuide;

        public int judgeQueueOffset;
        public unsafe SlideArea* judgeQueue;
        public int judgeQueueCount;
        public int judgeQueueLOffset;
        public unsafe SlideArea* judgeQueueL;
        public int judgeQueueLCount;
        public int judgeQueueROffset;
        public unsafe SlideArea* judgeQueueR;
        public int judgeQueueRCount;
        public float Const;
        public int slideArrowsOffset;

        public int unskippable1;
        public int unskippable2;
        public SensorType currentOn;    // Invalid就是没按，否则就是正在按的区
        public SensorType currentOnL;
        public SensorType currentOnR;

        public unsafe SlidePose* slideArrows;
        // 注意第一个是起点最后一个是终点不需要画箭头
        public int slideArrowsCount;
        public bool noLastArrow;
        public SlideOkType okType;
        public SlidePose okPose;

        public bool isSoundPlayed;
        public bool isJudged;  // 是否实际上已被判定
        public JudgeGrade judgeGrade;
        public bool isSlideEnd;// 是否表现为已判定（显示slideok和报告判定结果）
        public bool isEnd;     // note是否完全结束（slideok消失后）

        //slide
        public NoteSp slideSprite;
        public int eaten;
        //star
        public float process;
        public int processIdx; // 标记现在引导星星走到哪儿了（引导星星之后的第一个箭头idx）
        public float starAlpha;
        public float starScale;
        public NoteSp starSprite;
        //FOR WIFI
        public float2 starPosStart;
        public float2 starPosConstC; //for wifi only
        public float2 starPosConstL;
        public float2 starPosConstR;

        public float2 starPos;
        public float2 starPosL;
        public float2 starPosR;

        public int judgeCurrent; //SLide / Wifi Center
        public int judgeL_Current; //Wifi Left
        public int judgeR_Current; //Wifi Right

        // Animation state
        public float fadeInStartTiming;
        public float fadeInDuration;
        public float slideAlpha; // 0->1

        public float judgeTime; //被判定时
        public float slideOKAlpha; // 1->0
        public float brightness;

        public void Init()
        {
            starAlpha = 0;
            starScale = 0;
            process = 0;
            processIdx = 1;
            slideAlpha = 0;
            slideOKAlpha = 1f;
            brightness = 1f;
            judgeTime = float.MinValue;
            currentOn = SensorType.Invalid;
            currentOnL = SensorType.Invalid;
            currentOnR = SensorType.Invalid;

            // 如果是 mine slide 就不要播放音效
            if (isMine)
            {
                isSoundPlayed = true;
            }

            // 计算Slide淡入时机
            // 8.0速时应当提前300ms显示Slide
            fadeInStartTiming = -3.926913f / speed;
            // Slide完全淡入时机
            // 正常情况下应为负值；速度过高将忽略淡入      
            var fadeInFinishTiming = math.min(fadeInStartTiming + 0.2f, 0);
            //淡入时机与正解帧间隔小于200ms时，加快淡入动画的播放速度，这个过程现在是自然实现的        
            fadeInDuration = fadeInFinishTiming - fadeInStartTiming;

            // Calc Skippable (V slides calc is in SlideTableNeo)
            // in NoteManager->Loader

            // Load Skin
            if (!isWifi)
            {
                slideSprite = NoteSp.SLIDE;
                starSprite = NoteSp.STAR;
                if (isEach)
                {
                    slideSprite = NoteSp.SLIDE_EACH;
                    starSprite = NoteSp.STAR_EACH;
                }
                if (isBreak)
                {
                    slideSprite = NoteSp.SLIDE_BREAK;
                    starSprite = NoteSp.STAR_BREAK;
                }
                if (isMine)
                {
                    if (isBreak)
                    {
                        slideSprite = NoteSp.SLIDE_BREAK_MINE;
                        starSprite = NoteSp.STAR_BREAK_MINE;
                    }
                    else
                    {
                        slideSprite = NoteSp.SLIDE_MINE;
                        starSprite = NoteSp.STAR_MINE;
                    }
                }
            }
            else
            {
                slideSprite = NoteSp.WIFI_0;
                starSprite = NoteSp.STAR;
                if (isEach)
                {
                    slideSprite = NoteSp.WIFI_EACH_0;
                    starSprite = NoteSp.STAR_EACH;
                }
                if (isBreak)
                {
                    slideSprite = NoteSp.WIFI_BREAK_0;
                    starSprite = NoteSp.STAR_BREAK;
                }
                if (isMine)
                {
                    if (isBreak)
                    {
                        slideSprite = NoteSp.WIFI_BREAK_MINE_0;
                        starSprite = NoteSp.STAR_BREAK_MINE;
                    }
                    else
                    {
                        slideSprite = NoteSp.WIFI_MINE_0;
                        starSprite = NoteSp.STAR_MINE;
                    }
                }

                //FOR WIFI STARS
                var endPosC = endPos;
                var endPosL = endPosC + 1 > 8 ? 1 : endPosC + 1;
                var endPosR = endPosC - 1 < 1 ? 8 : endPosC - 1;

                starPosStart = MajPos.GetBtnPos(startPos - 1);
                starPosConstC = MajPos.GetBtnPos(endPosC - 1) - starPosStart;
                starPosConstL = MajPos.GetBtnPos(endPosL - 1) - starPosStart;
                starPosConstR = MajPos.GetBtnPos(endPosR - 1) - starPosStart;
            }

            lastStayTime = LastFor * Const;
            judgeTiming = shootTime + LastFor * (1f - Const);
        }

        public void Reset()
        {
            currentOn = SensorType.Invalid;
            currentOnL = SensorType.Invalid;
            currentOnR = SensorType.Invalid;

            isSoundPlayed = default;
            isJudged = default;
            judgeGrade = default;
            isSlideEnd = default;
            isEnd = default;
            eaten = default;

            process = default;
            processIdx = 1;
            starScale = default;
            judgeCurrent = default;
            judgeL_Current = default;
            judgeR_Current = default;


            judgeTime = default;
        }

        /// <summary>
        /// 路径由 SlideParts 与起点比较；此处检查剩余的时间与音符属性。
        /// </summary>
        public readonly bool IsFoldablePropOnly(SlideData other) =>
            tapTime == other.tapTime &&
            shootTime == other.shootTime &&
            LastFor == other.LastFor &&
            speed == other.speed &&

            isEach == other.isEach &&
            isEx == other.isEx &&
            isBreak == other.isBreak &&
            isMine == other.isMine;
    }
}