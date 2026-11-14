using MajdataViewX.Base;
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

namespace MajdataViewX.Notes.Updaters
{
    [BurstCompile]
    public unsafe struct TouchUpdateJob : IJobParallelFor
    {
        // 渲染时要向左回看同组的折叠兄弟（只读），属于跨 index 访问
        [NativeDisableParallelForRestriction]
        public NativeArray<TouchData> touches;

        [NativeDisableParallelForRestriction]
        public NativeArray<SimpleRenderData> touchesRender;

        [NativeDisableUnsafePtrRestriction]
        public int* TouchesWriteCountPtr;

        [NativeDisableUnsafePtrRestriction]
        public bool* SfxRequests;
        [NativeDisableUnsafePtrRestriction]
        public EffectData* JudgeEffectRequests;
        public NativeList<ReportResultEntry>.ParallelWriter ReportResults;

        [ReadOnly] public NativeArray<int> touchGroupTotalCounts;
        [NativeDisableParallelForRestriction] public NativeArray<int> touchGroupJudgedCounts;

        public void Execute(int index)
        {
            if (!TimeData.IsStart)
            {
                SeeOnlyUpdate(touches[index], index);
                return;
            }
            ref var touch = ref touches.ElementRef(index);
            TransformUpdate(ref touch, index);
            AutoplayUpdate(ref touch);
            CheckUpdate(ref touch);
        }
        private void SeeOnlyUpdate(TouchData touch, int index)
        {
            if (touch.isFolded) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, touch.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index, ignoreEnd: true);

            var timing = touch.isIgnoreSV
                ? TimeData.NoteTime - touch.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(touch.time);
            if (timing > 0) return;
            var pow = -math.exp(8f * (timing * 0.43f / touch.moveDuration) - 0.85f) + 0.42f;
            var fanDist = math.clamp(pow, 0f, 0.4f);

            if (-timing > touch.wholeDuration)
            {
                return;
            }
            if (-timing < touch.wholeDuration && -timing >= touch.moveDuration)
            {
                touch.fanAlpha = 1 - math.saturate((-timing - touch.moveDuration) / touch.displayDuration);
                fanDist = 0.4f;
            }
            else if (-timing <= touch.moveDuration)
            {
                touch.fanAlpha = 1f;
            }

            var centerPos = touch.centerPos;

            var fanPositions = stackalloc float2[4]
            {
                centerPos + new float2(0.226f + fanDist, 0),
                centerPos + new float2(0, 0.226f + fanDist),
                centerPos + new float2(-(0.226f + fanDist), 0),
                centerPos + new float2(0, -(0.226f + fanDist)),
            };
            for (int i = 0; i < 4; i++)
            {
                var tIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
                touchesRender[tIdx] = new SimpleRenderData
                {
                    pos = fanPositions[i],
                    angRad = math.radians(90f * (i + 1)),
                    scale = new float2(1, 1),
                    spriteId = touch.fanSprite,
                    color = new float4(1, 1, 1, touch.fanAlpha),
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x3,
                    foldedCount = foldedCount,
                };
            }

            var ptIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
            touchesRender[ptIdx] = new SimpleRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = touch.pointSprite,
                color = new float4(1, 1, 1, touch.fanAlpha),
                brightness = 1f,
                sort = (sortTime << 2) | 0x2,
                foldedCount = foldedCount,
            };

            if (timing > -0.02f)
            {
                var justIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
                touchesRender[justIdx] = new SimpleRenderData
                {
                    pos = centerPos,
                    angRad = 0,
                    scale = new float2(1, 1),
                    spriteId = touch.justSprite,
                    color = new float4(1),
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x1,
                    foldedCount = foldedCount,
                };
            }
        }

        private void TransformUpdate(ref TouchData touch, int index)
        {
            if (touch.isFolded) return;
            if (touch.isEnd) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, touch.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index);

            var timing = touch.isIgnoreSV
                ? TimeData.NoteTime - touch.time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(touch.time);
            var pow = -math.exp(8f * (timing * 0.43f / touch.moveDuration) - 0.85f) + 0.42f;
            var fanDist = math.clamp(pow, 0f, 0.4f);

            if (-timing > touch.wholeDuration)
            {
                return;
            }

            if (!touch.isAppeared)
            {
                touch.isAppeared = true;
                MajBurst.MultTouchHandler.RegisterActive(touch.sensor);
            }

            if (-timing < touch.wholeDuration && -timing >= touch.moveDuration)
            {
                touch.fanAlpha = 1 - math.saturate((-timing - touch.moveDuration) / touch.displayDuration);
                fanDist = 0.4f;
            }
            else if (-timing <= touch.moveDuration)
            {
                touch.fanAlpha = 1f;
            }

            var centerPos = touch.centerPos;

            var fanPositions = stackalloc float2[4]
            {
                centerPos + new float2(0.226f + fanDist, 0),
                centerPos + new float2(0, 0.226f + fanDist),
                centerPos + new float2(-(0.226f + fanDist), 0),
                centerPos + new float2(0, -(0.226f + fanDist)),
            };
            for (var i = 0; i < 4; i++)
            {
                var tIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
                touchesRender[tIdx] = new SimpleRenderData
                {
                    pos = fanPositions[i],
                    angRad = math.radians(90f * (i + 1)),
                    scale = new float2(1, 1),
                    spriteId = touch.fanSprite,
                    color = new float4(1, 1, 1, touch.fanAlpha),
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x3,
                    foldedCount = foldedCount,
                };
            }

            var ptIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
            touchesRender[ptIdx] = new SimpleRenderData
            {
                pos = centerPos,
                angRad = 0,
                scale = new float2(1, 1),
                spriteId = touch.pointSprite,
                color = new float4(1, 1, 1, touch.fanAlpha),
                brightness = 1f,
                sort = (sortTime << 2) | 0x2,
                foldedCount = foldedCount,
            };

            if (timing > -0.02f)
            {
                var justIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
                touchesRender[justIdx] = new SimpleRenderData
                {
                    pos = centerPos,
                    angRad = 0,
                    scale = new float2(1, 1),
                    spriteId = touch.justSprite,
                    color = new float4(1),
                    brightness = 1f,
                    sort = (sortTime << 2) | 0x1,
                    foldedCount = foldedCount,
                };
            }

            if (-timing < touch.wholeDuration &&
                MajBurst.MultTouchHandler.CanShowBorder(touch.sensor, out var sprite))
            {
                var borderIdx = Interlocked.Increment(ref *TouchesWriteCountPtr) - 1;
                touchesRender[borderIdx] = new SimpleRenderData
                {
                    pos = centerPos,
                    angRad = 0,
                    scale = new float2(1, 1),
                    spriteId = sprite,
                    color = new float4(1, 1, 1, touch.fanAlpha),
                    brightness = 1f,
                    sort = sortTime << 2,
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
                if (!touches[i].isFolded) break;
                if (ignoreEnd || touches[i].isEnd)
                    count++;
            }
            return count;
        }

        private void AutoplayUpdate(ref TouchData touch)
        {
            if (touch.isEnd) return;
            if (NoteHelper.Settings.AutoPlayMode is AutoPlayMode.Disable) return;

            var timing = TimeData.NoteTime - touch.time;
            var autoplayStart = InputManager.AUTOPLAY_START_SEC;

            if (timing < autoplayStart) return;

            switch (NoteHelper.Settings.AutoPlayMode)
            {
                case AutoPlayMode.Enable:
                    touch.judgeGrade = JudgeGrade.LateCritical;
                    touch.isJudged = true;
                    touch.diff = 0;
                    EndNote(ref touch);
                    break;
                case AutoPlayMode.Random:
                    var grade = (JudgeGrade)GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss); ;
                    touch.judgeGrade = touch.isMine
                        ? (grade < JudgeGrade.FastPerfect3rd ? JudgeGrade.Miss : JudgeGrade.LateCritical)
                        : grade;
                    touch.isJudged = true;
                    touch.diff = grade >= JudgeGrade.LateCritical ? 11.4514f : -11.4514f;
                    EndNote(ref touch);
                    break;
            }
        }

        private void CheckUpdate(ref TouchData touch)
        {
            if (touch.isJudged || touch.isEnd) return;
            if (!NoteHelper.IsSimulated) return;

            var noteTime = TimeData.NoteTime;
            var diffSec = noteTime - touch.time;

            if (touch.isMine)
            {
                var mineOn = InputData.GetSensorState(touch.sensor).IsPadDown;
                if (mineOn && diffSec >= -NoteHelper.TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    touch.judgeGrade = JudgeGrade.Miss;
                    touch.isJudged = true;
                    touch.diff = diffSec;
                    EndNote(ref touch);
                    return;
                }
                if (diffSec >= NoteHelper.MINE_END_SEC)
                {
                    touch.judgeGrade = JudgeGrade.LateCritical;
                    touch.isJudged = true;
                    EndNote(ref touch);
                }
                return;
            }

            if (diffSec > NoteHelper.TOUCH_JUDGE_GOOD_AREA_MSEC / 1000f)
            {
                touch.judgeGrade = JudgeGrade.Miss;
                touch.isJudged = true;
                EndNote(ref touch);
                return;
            }

            var clicked = InputData.GetSensorState(touch.sensor).IsPadDown;

            if (touch.groupId != -1)
            {
                if (touchGroupJudgedCounts[touch.groupId] * 2 > touchGroupTotalCounts[touch.groupId])
                {
                    clicked = true;
                }
            }

            if (!clicked) return;

            var diffMSec = diffSec * 1000;
            if (diffMSec < -NoteHelper.TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC) return;
            if (!InputData.CanJudgeSensor(touch.sensor, touch.sensorOrderIndex)) return;

            touch.judgeGrade = NoteHelper.GetTouchJudge(diffSec);
            touch.isJudged = true;
            touch.diff = diffSec;
            EndNote(ref touch);
        }

        private void EndNote(ref TouchData touch)
        {
            if (touch.isBreak)
                NoteHelper.PlayTapSound(SfxRequests,
                    touch.judgeGrade,
                    true,
                    touch.isEx,
                    false,
                    touch.diff
                );
            else
                NoteHelper.PlayTouchSound(SfxRequests,
                    touch.judgeGrade,
                    touch.isMine,
                    touch.isHanabi
                );
            NoteHelper.PlayTouchEffect(JudgeEffectRequests,
                (int)touch.sensor + 8,
                touch.judgeGrade,
                touch.isBreak,
                touch.isHanabi,
                touch.isMine
            );
            NoteHelper.ReportResult(ReportResults,
                touch.judgeGrade,
                touch.isBreak,
                SimaiNoteType.TOUCH
            );

            InputData.NextTouch(touch.sensor);
            MajBurst.MultTouchHandler.Unregister(touch.sensor);
            if (touch.isAppeared)
            {
                touch.isAppeared = false;
                MajBurst.MultTouchHandler.UnregisterActive(touch.sensor);
            }

            if (touch.groupId != -1 && touch.judgeGrade != JudgeGrade.Miss)
            {
                unsafe
                {
                    var ptr = (int*)touchGroupJudgedCounts.GetUnsafePtr();
                    Interlocked.Increment(ref ptr[touch.groupId]);
                }
            }

            touch.isEnd = true;
        }
    }
}