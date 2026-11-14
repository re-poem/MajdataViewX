using MajdataViewX.Managers;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Notes;
using MajdataViewX.Types.Notes.RenderData;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using Cimai;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static MajdataViewX.Base.MajBurst;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Notes.Updaters
{
    [BurstCompile]
    public unsafe struct TouchHoldUpdateJob : IJobParallelFor
    {
        // 渲染时要向左回看同组的折叠兄弟（只读），属于跨 index 访问
        [NativeDisableParallelForRestriction]
        public NativeArray<TouchHoldData> touchHolds;

        [NativeDisableParallelForRestriction]
        public NativeArray<SimpleRenderData> simpleRender;

        [NativeDisableUnsafePtrRestriction]
        public int* SimpleWriteCountPtr;

        [NativeDisableParallelForRestriction]
        public NativeArray<MaskRenderData> maskRender;

        [NativeDisableUnsafePtrRestriction]
        public int* MaskWriteCountPtr;

        [NativeDisableUnsafePtrRestriction]
        public bool* SfxRequests;
        [NativeDisableUnsafePtrRestriction]
        public EffectData* JudgeEffectRequests;
        public NativeList<ReportResultEntry>.ParallelWriter ReportResults;

        [ReadOnly] public NativeArray<int> touchGroupTotalCounts;
        [NativeDisableParallelForRestriction] public NativeArray<int> touchGroupJudgedCounts;
        [ReadOnly] public NativeArray<int> touchHoldGroupTotalCounts;
        [ReadOnly] public NativeArray<int> touchHoldGroupPressedCounts;

        public void Execute(int index)
        {
            if (!TimeData.IsStart)
            {
                SeeOnlyUpdate(touchHolds[index], index);
                return;
            }
            ref var th = ref touchHolds.ElementRef(index);
            TransformUpdate(ref th, index);
            AutoplayUpdate(ref th);
            CheckUpdate(ref th);
        }

        private void SeeOnlyUpdate(TouchHoldData th, int index)
        {
            if (th.isFolded) return;

            var timing = th.isIgnoreSV
                ? TimeData.NoteTime - th.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(th.time);
            var lastFor = th.isIgnoreSV
                ? th.LastFor
                : TimeData.GetPositionAtTime(th.time + th.LastFor) - TimeData.GetPositionAtTime(th.time);
            if (timing > lastFor) return;

            var pow = -math.exp(8f * (timing * 0.43f / th.moveDuration) - 0.85f) + 0.42f;
            var fanDist = math.clamp(pow, 0f, 0.4f);

            if (-timing > th.wholeDuration)
            {
                return;
            }
            else if (-timing <= th.wholeDuration && -timing > th.moveDuration)
            {
                var fadeT = (-timing - th.moveDuration) / th.displayDuration;
                th.fanAlpha = math.saturate(1f - fadeT);
            }
            else if (-timing <= th.moveDuration)
            {
                th.fanAlpha = 1f;
            }

            if (timing >= 0)
            {
                th.maskProgress = math.clamp(timing / lastFor, 0f, 1f);
            }

            var centerPos = th.centerPos;
            var color = new float4(1, 1, 1, th.fanAlpha);

            var radius = 0.226f + fanDist;
            var c = math.SQRT2 / 2f;
            var fanPositions = stackalloc float2[4]
            {
                centerPos + new float2(radius * c, radius * c),
                centerPos + new float2(radius * c, -radius * c),
                centerPos + new float2(-radius * c, -radius * c),
                centerPos + new float2(-radius * c, radius * c),
            };

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, th.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index, ignoreEnd: true);

            for (int i = 0; i < 4; i++)
            {
                var tIdx = Interlocked.Increment(ref *SimpleWriteCountPtr) - 1;
                simpleRender[tIdx] = new SimpleRenderData
                {
                    pos = fanPositions[i],
                    angRad = math.radians(135f - 90f * i),
                    scale = new float2(1, 1),
                    spriteId = th.fanSprite + (uint)i,
                    color = color,
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x3,
                    foldedCount = foldedCount,
                };
            }

            var ptIdx = Interlocked.Increment(ref *SimpleWriteCountPtr) - 1;
            simpleRender[ptIdx] = new SimpleRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = th.pointSprite,
                color = color,
                brightness = 1f,
                sort = (sortTime << 2) | 0x2,
                foldedCount = foldedCount,
            };

            var borderIdx = Interlocked.Increment(ref *MaskWriteCountPtr) - 1;
            maskRender[borderIdx] = new MaskRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = th.borderSprite,
                color = color,
                maskCutoff = th.maskProgress,
                sort = sortTime,
                foldedCount = foldedCount,
            };
        }

        private void TransformUpdate(ref TouchHoldData th, int index)
        {
            if (th.isFolded) return;
            if (th.isEnd) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, th.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index);

            var timing = th.isIgnoreSV
                ? TimeData.NoteTime - th.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(th.time);
            var lastFor = th.isIgnoreSV
                ? th.LastFor
                : TimeData.GetPositionAtTime(th.time + th.LastFor) - TimeData.GetPositionAtTime(th.time);

            var wholeDuration = 3.209385682f * math.pow(th.speed, -0.9549621752f);
            var moveDuration = 0.8f * wholeDuration;
            var displayDuration = 0.2f * wholeDuration;

            var pow = -math.exp(8f * (timing * 0.43f / moveDuration) - 0.85f) + 0.42f;
            var fanDist = math.clamp(pow, 0f, 0.4f);

            if (-timing > wholeDuration)
            {
                return;
            }
            else if (-timing <= wholeDuration && -timing > moveDuration)
            {
                var fadeT = (-timing - moveDuration) / displayDuration;
                th.fanAlpha = math.saturate(1f - fadeT);
            }
            else if (-timing <= moveDuration)
            {
                th.fanAlpha = 1f;
            }

            if (timing >= 0)
            {
                th.maskProgress = math.clamp(timing / lastFor, 0f, 1f);
            }

            // ---- hold effect ----
            if (timing > 0) // 防止顶掉前面hold的特效
                NoteHelper.SetHoldEffect(JudgeEffectRequests,
                    (int)th.sensor + 8,
                    th.judgeGrade,
                    th.isHolding
                );
            // NoteHelper.SetTouchHoldSound(SfxRequests, th.isHolding);

            // ---- hold on/off skin ----
            if (th.LastFor > (NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC + NoteHelper.HOLD_TAIL_IGNORE_LENGTH_SEC) && // 忽略短hold
                timing >= NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC &&    // 忽略头6帧
                !th.isMine)          // 忽略mine
            {
                if (th.isHolding)
                {
                    th.borderSprite = th._borderOnSpriteCache;
                }
                else
                {
                    th.borderSprite = NoteSp.TOUCH_HOLD_BORDER_MISS;
                }
            }

            var centerPos = th.centerPos;
            var color = new float4(1, 1, 1, th.fanAlpha);

            var radius = 0.226f + fanDist;
            var c = math.SQRT2 / 2f;
            var fanPositions = stackalloc float2[4]
            {
            centerPos + new float2(radius * c, radius * c),
            centerPos + new float2(radius * c, -radius * c),
            centerPos + new float2(-radius * c, -radius * c),
            centerPos + new float2(-radius * c, radius * c),
        };

            for (int i = 0; i < 4; i++)
            {
                var tIdx = Interlocked.Increment(ref *SimpleWriteCountPtr) - 1;
                simpleRender[tIdx] = new SimpleRenderData
                {
                    pos = fanPositions[i],
                    angRad = math.radians(135f - 90f * i),
                    scale = new float2(1, 1),
                    spriteId = th.fanSprite + (uint)i,
                    color = color,
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x3,
                    foldedCount = foldedCount,
                };
            }

            var ptIdx = Interlocked.Increment(ref *SimpleWriteCountPtr) - 1;
            simpleRender[ptIdx] = new SimpleRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = th.pointSprite,
                color = color,
                brightness = 1f,
                sort = (sortTime << 2) | 0x2,
                foldedCount = foldedCount,
            };

            var borderIdx = Interlocked.Increment(ref *MaskWriteCountPtr) - 1;
            maskRender[borderIdx] = new MaskRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = th.borderSprite,
                color = color,
                maskCutoff = th.maskProgress,
                sort = sortTime,
                foldedCount = foldedCount,
            };
        }

        /// <summary>
        /// 向前回看紧挨着的折叠兄弟（CanBeFolded 保证同组 note 连续存放），
        /// 得到本实例应当代表的叠加层数（含自身）。
        /// </summary>
        /// <param name="ignoreEnd">
        /// 预览（SeeOnly）模式不按 isEnd 截断：加载后到播放前所有 note 的 isEnd 都还是 true，
        /// 而预览本来就不过滤已判定 note。
        /// </param>
        private int CountFoldedCover(int index, bool ignoreEnd = false)
        {
            var count = 1;
            for (var i = index - 1; i >= 0; i--)
            {
                if (!touchHolds[i].isFolded) break;
                if (ignoreEnd || touchHolds[i].isEnd)
                    count++;
            }
            return count;
        }

        private void AutoplayUpdate(ref TouchHoldData th)
        {
            if (th.isEnd) return;
            if (NoteHelper.Settings.AutoPlayMode is AutoPlayMode.Disable) return;

            var timing = TimeData.NoteTime - th.time;
            if (timing < InputManager.AUTOPLAY_START_SEC) return;

            switch (NoteHelper.Settings.AutoPlayMode)
            {
                case AutoPlayMode.Enable:
                    if (!th.isHeadJudged)
                    {
                        th.judgeGrade = JudgeGrade.LateCritical;
                        th.isHeadJudged = true;
                        th.isHolding = true;
                        th.headDiff = 0;
                    }
                    if (th.isHeadJudged)
                    {
                        var remaining = math.max(th.LastFor - timing, 0);
                        if (remaining <= 0)
                        {
                            th.holdPercent = 1f;
                            EndNote(ref th);
                            return;
                        }
                    }
                    break;
                case AutoPlayMode.Random:
                    if (!th.isHeadJudged)
                    {
                        var grade = (JudgeGrade)GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss);
                        th.judgeGrade = th.isMine
                            ? (grade < JudgeGrade.FastPerfect3rd ? JudgeGrade.Miss : JudgeGrade.LateCritical)
                            : grade;
                        th.isHeadJudged = true;
                        th.isHolding = true;
                        th.headDiff = grade >= JudgeGrade.LateCritical ? 11.4514f : -11.4514f;
                    }
                    if (th.isHeadJudged)
                    {
                        var remaining = math.max(th.LastFor - timing, 0);
                        if (remaining <= 0)
                        {
                            th.holdPercent = 1f;
                            EndNote(ref th);
                            return;
                        }
                    }
                    break;
            }
        }

        private void CheckUpdate(ref TouchHoldData th)
        {
            if (th.isEnd) return;
            if (!NoteHelper.IsSimulated) return;

            var noteTime = TimeData.NoteTime;
            var timing = noteTime - th.time;

            if (th.isMine)
            {
                var mineClicked = InputData.GetSensorState(th.sensor).IsPadDown;
                if (mineClicked &&
                    timing >= -NoteHelper.TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    th.judgeGrade = JudgeGrade.Miss;
                    th.isHeadJudged = true;
                    th.headDiff = timing;
                    EndNote(ref th);
                    return;
                }
                if (timing >= th.LastFor)
                {
                    th.judgeGrade = JudgeGrade.LateCritical;
                    th.isHeadJudged = true;
                    th.holdPercent = 1f;
                    EndNote(ref th);
                }
                return;
            }

            if (!th.isHeadJudged)
            {
                if (timing > NoteHelper.TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    th.judgeGrade = JudgeGrade.Miss;
                    th.isHeadJudged = true;
                    th.headDiff = NoteHelper.TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f;
                    return;
                }

                var clicked = InputData.GetSensorState(th.sensor).IsPadDown;
                if (th.headGroupId != -1)
                {
                    if (touchGroupJudgedCounts[th.headGroupId] * 2 > touchGroupTotalCounts[th.headGroupId])
                    {
                        clicked = true;
                    }
                }

                if (!clicked) return;
                var diffMSec = timing * 1000;
                if (diffMSec < -NoteHelper.TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC) return;
                if (!InputData.CanJudgeSensor(th.sensor, th.sensorOrderIndex)) return;

                th.judgeGrade = NoteHelper.GetTouchJudge(timing);
                th.isHeadJudged = true;
                th.headDiff = timing;

                if (th.headGroupId != -1 && th.judgeGrade != JudgeGrade.Miss)
                {
                    unsafe
                    {
                        var ptr = (int*)touchGroupJudgedCounts.GetUnsafePtr();
                        Interlocked.Increment(ref ptr[th.headGroupId]);
                    }
                }

                return;
            }

            var remainingTime = math.max(th.LastFor - timing, 0);
            if (remainingTime <= 0)
            {
                var realityHT = th.LastFor - (NoteHelper.TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC + NoteHelper.TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC) - math.max(th.headDiff, 0f);
                var pct = math.clamp((realityHT - th.playerIdleTimeSec) / math.max(realityHT, 0.001f), 0f, 1f);
                th.holdPercent = pct;
                if (!th.isMine)
                    th.judgeGrade = NoteHelper.GetHoldFinalGrade(th.judgeGrade, pct, realityHT);
                EndNote(ref th);
                return;
            }

            if (!TimeData.IsStart) return;

            var on = InputData.GetSensorState(th.sensor).Status;

            if (th.groupId != -1)
            {
                if (touchHoldGroupPressedCounts[th.groupId] * 2 > touchHoldGroupTotalCounts[th.groupId])
                {
                    on = true;
                }
            }

            if (on)
            {
                th.lastReleaseTimeSec = 0f;
                th.isHolding = true;

                // touchHoldGroupPressedCount在外部处理
            }
            else
            {
                if (th.lastReleaseTimeSec <= NoteHelper.DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC)
                {
                    th.lastReleaseTimeSec += TimeData.deltaTime;
                    th.isHolding = true;
                }
                else
                {
                    th.isHolding = false;
                    if (timing > NoteHelper.TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC && remainingTime > NoteHelper.TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC)
                        th.playerIdleTimeSec += TimeData.deltaTime;
                }
            }
        }

        private void EndNote(ref TouchHoldData th)
        {
            // NoteHelper.SetTouchHoldSound(SfxRequests, false);
            th.isHolding = false;

            if (th.isBreak)
                NoteHelper.PlayTapSound(SfxRequests,
                    th.judgeGrade,
                    true,
                    th.isEx,
                    false,
                    th.headDiff
                );
            else
                NoteHelper.PlayTouchSound(SfxRequests,
                    th.judgeGrade,
                    th.isMine,
                    th.isHanabi
                );
            NoteHelper.PlayTouchEffect(JudgeEffectRequests,
                (int)th.sensor + 8,
                th.judgeGrade,
                th.isBreak,
                th.isHanabi,
                th.isMine
            );
            NoteHelper.ReportResult(ReportResults,
                th.judgeGrade,
                th.isBreak,
                SimaiNoteType.TOUCHHOLD
            );

            InputData.NextTouch(th.sensor);
            th.isEnd = true;
        }
    }
}