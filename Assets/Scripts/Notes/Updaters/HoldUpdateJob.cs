using MajdataViewX.Managers;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Notes;
using MajdataViewX.Types.Notes.RenderData;
using MajdataViewX.Utils.Extensions;
using Cimai;
using System;
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
    public unsafe struct HoldUpdateJob : IJobParallelFor
    {
        // 渲染时要向左回看同组的折叠兄弟（只读），属于跨 index 访问
        [NativeDisableParallelForRestriction]
        public NativeArray<HoldData> holds;

        [NativeDisableParallelForRestriction]
        public NativeArray<LineRenderData> tapLinesRender;
        [NativeDisableParallelForRestriction]
        public NativeArray<NotesRenderData> notesRender;

        [NativeDisableUnsafePtrRestriction]
        public int* TapLinesWriteCountPtr;
        [NativeDisableUnsafePtrRestriction]
        public int* NotesWriteCountPtr;

        [NativeDisableUnsafePtrRestriction]
        public bool* SfxRequests;
        [NativeDisableUnsafePtrRestriction]
        public EffectData* JudgeEffectRequests;
        public NativeList<ReportResultEntry>.ParallelWriter ReportResults;

        public void Execute(int index)
        {
            if (!TimeData.IsStart)
            {
                SeeOnlyUpdate(holds[index], index);
                return;
            }
            ref var hold = ref holds.ElementRef(index);
            TransformUpdate(ref hold, index);
            AutoplayUpdate(ref hold);
            CheckUpdate(ref hold);
        }

        private void SeeOnlyUpdate(HoldData hold, int index)
        {
            if (hold.isFolded) return;

            // ---- body ----
            var headTiming = hold.isIgnoreSV
                ? TimeData.NoteTime - hold.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(hold.time);
            var headDistance = headTiming * hold.speed + 4.8f;
            var clampedDistance = math.max(headDistance, 1.225f);

            var destScale = math.min(headDistance * 0.4f + 0.51f, 1f);
            var lineScale = math.min(clampedDistance / 4.8f, 1f);

            // ---- Tail (hold end) ----
            var tailTiming = hold.isIgnoreSV
                ? TimeData.NoteTime - (hold.time + hold.LastFor)
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(hold.time + hold.LastFor);
            var tailDistance = tailTiming * hold.speed + 4.8f;

            if (tailTiming > 0) return;

            // ---- Invisible ----
            if (destScale < 0f) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, hold.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index, ignoreEnd: true);

            // show line
            if (destScale > 0.3f)
            {
                var lineIdx = Interlocked.Increment(ref *TapLinesWriteCountPtr) - 1;
                tapLinesRender[lineIdx] = new LineRenderData
                {
                    angRad = math.radians(hold.ang),
                    scale = lineScale,
                    spriteId = hold.lineSprite,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // ---- body ----
            if (headDistance < 1.225f)
            {
                NoteHelper.GetPosFromDistance(1.225f, hold.Key, out var pos);
                hold.pos = pos;
                hold.scale = destScale;
                hold.stretchY = -0.58f; //原图带有一定高度
                hold.holdEndScale = 0f;
            }
            else
            {
                var headClamped = math.min(headDistance, 4.8f);
                var tailClamped = math.clamp(tailDistance, 1.225f, 4.8f);
                var barLen = math.max(headClamped - tailClamped, 0f);
                var midDist = (headClamped + tailClamped) * 0.5f;

                NoteHelper.GetPosFromDistance(midDist, hold.Key, out var pos);
                hold.pos = pos;
                hold.scale = 1;
                hold.stretchY = barLen - 0.58f;

                if (tailDistance >= 1.225f)
                {
                    NoteHelper.GetPosFromDistance(math.min(tailDistance, 4.8f), hold.Key, out var endPos);
                    hold.holdEndPos = endPos;
                    hold.holdEndScale = 1f;
                }
                else
                {
                    hold.holdEndScale = 0f;
                }
            }

            // ---- Write body ----
            {
                var noteIdx = Interlocked.Increment(ref *NotesWriteCountPtr) - 1;
                notesRender[noteIdx] = new NotesRenderData
                {
                    pos = hold.pos,
                    angRad = math.radians(hold.ang),
                    scale = hold.scale,
                    stretchY = hold.stretchY,
                    spriteId = hold.bodySprite,
                    color = new float4(1, 1, 1, 1),
                    brightness = 1f,
                    exSprite = hold.isEx ? hold.exSprite : 0u,
                    exColor = hold.exColor,
                    sliceBorder = hold.sliceBorder,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // ---- Write holdEnd
            if (hold.holdEndScale > 0f)
            {
                var endIdx = Interlocked.Increment(ref *NotesWriteCountPtr) - 1;
                notesRender[endIdx] = new NotesRenderData
                {
                    pos = hold.holdEndPos,
                    angRad = math.radians(hold.ang),
                    scale = 1f,
                    stretchY = 0,
                    spriteId = hold.endSprite,
                    color = new float4(1, 1, 1, 1),
                    brightness = 1f,
                    exSprite = 0,
                    exColor = float4.zero,
                    sliceBorder = float2.zero,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }
        }
        private void TransformUpdate(ref HoldData hold, int index)
        {
            if (hold.isFolded) return;
            if (hold.isEnd) return;

            // ---- body ----
            var headTiming = hold.isIgnoreSV
                ? TimeData.NoteTime - hold.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(hold.time);
            var headDistance = headTiming * hold.speed + 4.8f;
            var clampedDistance = math.max(headDistance, 1.225f);

            var destScale = math.min(headDistance * 0.4f + 0.51f, 1f);
            var lineScale = math.min(clampedDistance / 4.8f, 1f);

            // ---- Tail (hold end) ----
            var tailTiming = hold.isIgnoreSV
                ? TimeData.NoteTime - (hold.time + hold.LastFor)
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(hold.time + hold.LastFor);
            var tailDistance = tailTiming * hold.speed + 4.8f;

            // ---- Invisible ----
            if (destScale < 0f) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, hold.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index);

            // ---- shine ----
            if (hold.isBreak)           // break shine
            {
                var extra = math.max(math.sin(TimeData.GetFrame() * 0.17f) * 0.5f, 0f);
                hold.brightness = 0.95f + extra;
            }
            else if (hold.isHolding)    // on shine
            {
                var frame = TimeData.GetFrame() % 16;
                var extra = (1f - math.abs(frame - 8f) / 8f) * 0.5f; //0->0.5->0
                hold.brightness = 1f + extra;
            }
            else
            {
                hold.brightness = 1f;
            }

            // ---- hold effect ----
            if (headTiming > 0) // 防止顶掉前面hold的特效
                NoteHelper.SetHoldEffect(JudgeEffectRequests,
                    (int)hold.Key,
                    hold.judgeGrade,
                    hold.isHolding
                );

            // ---- hold on/off skin ----
            // if (hold.LastFor > (NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC + NoteHelper.HOLD_TAIL_IGNORE_LENGTH_SEC) && // 忽略短hold
            //     headTiming >= NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC &&  // 忽略头6帧
            //     !hold.isMine)          // 忽略mine
            if (!hold.isMine && headTiming >= 0.01f)
            {
                if (hold.isHolding)
                {
                    if (hold.isBreak)
                    {
                        hold.bodySprite = NoteSp.HOLD_BREAK_ON;
                    }
                    else if (hold.isEach)
                    {
                        hold.bodySprite = NoteSp.HOLD_EACH_ON;
                    }
                    else if (hold.isMine)
                    {
                        if (hold.isBreak)
                            hold.bodySprite = NoteSp.HOLD_BREAK_MINE_ON;
                        else
                            hold.bodySprite = NoteSp.HOLD_MINE_ON;
                    }
                    else
                    {
                        hold.bodySprite = NoteSp.HOLD_ON;
                    }
                }
                else
                {
                    hold.bodySprite = NoteSp.HOLD_OFF;
                }
            }

            // show line
            if (destScale > 0.3f)
            {
                var lineIdx = Interlocked.Increment(ref *TapLinesWriteCountPtr) - 1;
                tapLinesRender[lineIdx] = new LineRenderData
                {
                    angRad = math.radians(hold.ang),
                    scale = lineScale,
                    spriteId = hold.lineSprite,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // ---- body ----
            if (headDistance < 1.225f)
            {
                NoteHelper.GetPosFromDistance(1.225f, hold.Key, out var pos);
                hold.pos = pos;
                hold.scale = destScale;
                hold.stretchY = -0.58f; //原图带有一定高度
                hold.holdEndScale = 0f;
            }
            else
            {
                var headClamped = math.min(headDistance, 4.8f);
                var tailClamped = math.clamp(tailDistance, 1.225f, 4.8f);
                var barLen = math.max(headClamped - tailClamped, 0f);
                var midDist = (headClamped + tailClamped) * 0.5f;

                NoteHelper.GetPosFromDistance(midDist, hold.Key, out var pos);
                hold.pos = pos;
                hold.scale = 1;
                hold.stretchY = barLen - 0.58f;

                if (tailDistance >= 1.225f)
                {
                    NoteHelper.GetPosFromDistance(math.min(tailDistance, 4.8f), hold.Key, out var endPos);
                    hold.holdEndPos = endPos;
                    hold.holdEndScale = 1f;
                }
                else
                {
                    hold.holdEndScale = 0f;
                }
            }

            // ---- Write body ----
            {
                var noteIdx = Interlocked.Increment(ref *NotesWriteCountPtr) - 1;
                notesRender[noteIdx] = new NotesRenderData
                {
                    pos = hold.pos,
                    angRad = math.radians(hold.ang),
                    scale = hold.scale,
                    stretchY = hold.stretchY,
                    spriteId = hold.bodySprite,
                    color = new float4(1, 1, 1, 1),
                    brightness = hold.brightness,
                    exSprite = hold.isEx ? hold.exSprite : 0u,
                    exColor = hold.exColor,
                    sliceBorder = hold.sliceBorder,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // ---- Write holdEnd
            if (hold.holdEndScale > 0f)
            {
                var endIdx = Interlocked.Increment(ref *NotesWriteCountPtr) - 1;
                notesRender[endIdx] = new NotesRenderData
                {
                    pos = hold.holdEndPos,
                    angRad = math.radians(hold.ang),
                    scale = 1f,
                    stretchY = 0,
                    spriteId = hold.endSprite,
                    color = new float4(1, 1, 1, 1),
                    brightness = 1f,
                    exSprite = 0,
                    exColor = float4.zero,
                    sliceBorder = float2.zero,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }
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
                if (!holds[i].isFolded) break;
                if (ignoreEnd || holds[i].isEnd)
                    count++;
            }
            return count;
        }

        private void AutoplayUpdate(ref HoldData hold)
        {
            if (hold.isEnd) return;
            if (NoteHelper.Settings.AutoPlayMode is AutoPlayMode.Disable) return;

            var timing = TimeData.NoteTime - hold.time;
            if (timing < InputManager.AUTOPLAY_START_SEC) return;

            switch (NoteHelper.Settings.AutoPlayMode)
            {
                case AutoPlayMode.Enable:
                    if (!hold.isHeadJudged)
                    {
                        hold.judgeGrade = JudgeGrade.LateCritical;
                        hold.isHeadJudged = true;
                        hold.isHolding = true;
                        hold.headDiff = 0;
                        NoteHelper.PlayHoldSound(SfxRequests,
                            hold.judgeGrade,
                            hold.isBreak,
                            hold.isEx,
                            hold.isMine,
                            hold.headDiff
                        );
                    }
                    if (hold.isHeadJudged && math.max(hold.LastFor - timing, 0) <= 0)
                    {
                        hold.holdPercent = 1f;
                        EndNote(ref hold);
                    }
                    break;
                case AutoPlayMode.Random:
                    if (!hold.isHeadJudged)
                    {
                        var grade = (JudgeGrade)GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss);
                        hold.judgeGrade = hold.isMine
                            ? (grade < JudgeGrade.FastPerfect3rd ? JudgeGrade.Miss : JudgeGrade.LateCritical)
                            : grade;
                        hold.isHeadJudged = true;
                        hold.isHolding = true;
                        hold.headDiff = grade >= JudgeGrade.LateCritical ? 11.4514f : -11.4514f;
                        NoteHelper.PlayHoldSound(SfxRequests,
                            hold.judgeGrade,
                            hold.isBreak,
                            hold.isEx,
                            hold.isMine,
                            hold.headDiff
                        );
                    }
                    if (hold.isHeadJudged && hold.LastFor - timing <= 0)
                    {
                        hold.holdPercent = 1f;
                        EndNote(ref hold);
                    }
                    break;
            }
        }

        private void CheckUpdate(ref HoldData hold)
        {
            if (hold.isEnd) return;
            if (!NoteHelper.IsSimulated) return;

            var noteTime = TimeData.NoteTime;
            var timing = noteTime - hold.time;

            if (hold.isMine)
            {
                var buttonClicked = InputData.GetButtonState(hold.Key).IsPadDown;
                var sensorClicked = InputData.GetSensorState(hold.Key).IsPadDown;
                if ((buttonClicked || sensorClicked) &&
                    timing >= -NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    hold.judgeGrade = JudgeGrade.Miss;
                    hold.isHeadJudged = true;
                    hold.headDiff = timing;
                    EndNote(ref hold);
                    return;
                }
                if (timing >= hold.LastFor)
                {
                    hold.judgeGrade = JudgeGrade.LateCritical;
                    hold.isHeadJudged = true;
                    hold.holdPercent = 1f;
                    EndNote(ref hold);
                }
                return;
            }

            // ---- Head judgment ----
            if (!hold.isHeadJudged)
            {
                var buttonClicked = InputData.GetButtonState(hold.Key).IsPadDown;
                var sensorClicked = InputData.GetSensorState(hold.Key).IsPadDown;

                var clicked = sensorClicked || buttonClicked;

                if (timing > NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    hold.judgeGrade = JudgeGrade.Miss;
                    hold.isHeadJudged = true;
                    hold.headDiff = NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f;
                    // NOTE: DO NOT EndNote here. A missed head keeps tracking the body and
                    // can still be recovered to LateGood by the release-percent mapping below.
                    return;
                }

                if (!clicked) return;
                if (timing < -NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f) return;
                var canJudge =
                    (buttonClicked && InputData.CanJudgeButton(hold.Key, hold.ButtonOrderIndex)) ||
                    (sensorClicked && InputData.CanJudgeSensor(hold.Key, hold.SensorOrderIndex));
                if (!canJudge) return;

                hold.judgeGrade = NoteHelper.GetTapJudge(timing, hold.isEx);
                hold.isHeadJudged = true;
                hold.headDiff = timing;
                NoteHelper.PlayHoldSound(SfxRequests,
                    hold.judgeGrade,
                    hold.isBreak,
                    hold.isEx,
                    hold.isMine,
                    hold.headDiff
                );
                return;
            }

            // ---- Hold tracking ----
            var remainingTime = math.max(hold.LastFor - timing, 0);
            if (remainingTime <= 0)
            {
                var realityHT = hold.LastFor - (NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC + NoteHelper.HOLD_TAIL_IGNORE_LENGTH_SEC) - math.max(hold.headDiff, 0f);
                var pct = math.clamp((realityHT - hold.playerIdleTimeSec) / math.max(realityHT, 0.001f), 0f, 1f);
                hold.holdPercent = pct;
                if (!hold.isMine)
                    hold.judgeGrade = NoteHelper.GetHoldFinalGrade(hold.judgeGrade, pct, realityHT);
                EndNote(ref hold);
                return;
            }

            if (!TimeData.IsStart) return;

            var btnStatus = InputData.GetButtonState(hold.Key);
            var senStatus = InputData.GetSensorState(hold.Key);
            var on = btnStatus.Status || senStatus.Status;

            if (on)
            {
                hold.lastReleaseTimeSec = 0f;
                hold.isHolding = true;
            }
            else
            {
                if (hold.lastReleaseTimeSec <= NoteHelper.DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC)
                {
                    hold.lastReleaseTimeSec += TimeData.deltaTime;
                    hold.isHolding = true;
                }
                else
                {
                    hold.isHolding = false;
                    if (timing > NoteHelper.HOLD_HEAD_IGNORE_LENGTH_SEC && remainingTime > NoteHelper.HOLD_TAIL_IGNORE_LENGTH_SEC)
                        hold.playerIdleTimeSec += TimeData.deltaTime;
                }
            }
        }

        private void EndNote(ref HoldData hold)
        {
            NoteHelper.PlayTapSound(SfxRequests,
                hold.judgeGrade,
                false,
                false,
                hold.isMine,
                0
            );
            NoteHelper.PlayTapEffect(JudgeEffectRequests,
                (int)hold.Key,
                hold.judgeGrade,
                hold.isBreak,
                hold.isMine
            );
            NoteHelper.ReportResult(ReportResults,
                hold.judgeGrade,
                hold.isBreak,
                SimaiNoteType.HOLD
            );
            InputData.NextTapHold(
                hold.Key
            );
            hold.isEnd = true;
        }
    }
}