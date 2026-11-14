using MajdataViewX.Managers;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Notes;
using MajdataViewX.Types.Notes.RenderData;
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
    public unsafe struct TapUpdateJob : IJobParallelFor
    {
        // 渲染时要向左回看同组的折叠兄弟（只读），属于跨 index 访问
        [NativeDisableParallelForRestriction]
        public NativeArray<TapData> taps;

        [NativeDisableParallelForRestriction]
        public NativeArray<LineRenderData> tapLinesRender;
        [NativeDisableParallelForRestriction]
        public NativeArray<NotesRenderData> notesRender;

        [NativeDisableUnsafePtrRestriction]
        public int* tapLinesWriteCountPtr;
        [NativeDisableUnsafePtrRestriction]
        public int* notesWriteCountPtr;

        [NativeDisableUnsafePtrRestriction]
        public bool* SfxRequests;
        [NativeDisableUnsafePtrRestriction]
        public EffectData* JudgeEffectRequests;
        public NativeList<ReportResultEntry>.ParallelWriter ReportResults;

        public void Execute(int index)
        {
            if (!TimeData.IsStart)
            {
                SeeOnlyUpdate(taps[index], index);
                return;
            }
            ref var tap = ref taps.ElementRef(index);
            TransformUpdate(ref tap, index);
            AutoplayUpdate(ref tap);
            CheckUpdate(ref tap);
        }
        private void SeeOnlyUpdate(TapData tap, int index)
        {
            if (tap.IsFolded) return;

            var timing = tap.IsIgnoreSV
                ? TimeData.NoteTime - tap.Time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(tap.Time);
            if (timing > 0) return;

            var rawDistance = timing * tap.Speed + 4.8f;
            var clampedDistance = math.max(rawDistance, 1.225f);

            var destScale = math.min(rawDistance * 0.4f + 0.51f, 1f);
            var lineScale = clampedDistance / 4.8f;

            if (destScale < 0f) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, tap.Time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index, ignoreEnd: true);

            // show line
            if (destScale > 0.3f)
            {
                var lineIdx = Interlocked.Increment(ref *tapLinesWriteCountPtr) - 1;
                tapLinesRender[lineIdx] = new LineRenderData()
                {
                    angRad = math.radians(tap.AngleKey),
                    scale = lineScale,
                    spriteId = tap.LineSprite,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // show tap
            NoteHelper.GetPosFromDistance(clampedDistance, tap.Key, out var pos);
            tap.Pos = pos;
            tap.Scale = destScale;

            var tapIdx = Interlocked.Increment(ref *notesWriteCountPtr) - 1;
            notesRender[tapIdx] = new NotesRenderData()
            {
                pos = tap.Pos,
                angRad = math.radians(tap.AngleRot),
                scale = tap.Scale,
                stretchY = 0,
                spriteId = tap.TapSprite,
                color = new float4(1, 1, 1, 1),
                brightness = 1f,

                exSprite = tap.IsEx ? tap.ExSprite : 0,
                exColor = tap.ExColor,
                sliceBorder = float2.zero,

                sort = sortTime,
                foldedCount = foldedCount,
            };
        }

        private void TransformUpdate(ref TapData tap, int index)
        {
            if (tap.IsFolded) return;
            if (tap.IsEnd) return;

            var timing = tap.IsIgnoreSV
                ? TimeData.NoteTime - tap.Time
                : TimeData.FakeNoteTime - TimeData.GetPositionAtTime(tap.Time);

            var rawDistance = timing * tap.Speed + 4.8f;
            var clampedDistance = math.max(rawDistance, 1.225f);

            var destScale = math.min(rawDistance * 0.4f + 0.51f, 1f);
            var lineScale = clampedDistance / 4.8f;

            if (destScale < 0f) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, tap.Time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            var foldedCount = (uint)CountFoldedCover(index);

            // show line
            if (destScale > 0.3f)
            {
                var lineIdx = Interlocked.Increment(ref *tapLinesWriteCountPtr) - 1;
                tapLinesRender[lineIdx] = new LineRenderData()
                {
                    angRad = math.radians(tap.AngleKey),
                    scale = lineScale,
                    spriteId = tap.LineSprite,
                    sort = sortTime,
                    foldedCount = foldedCount,
                };
            }

            // show tap
            NoteHelper.GetPosFromDistance(clampedDistance, tap.Key, out var pos);
            tap.Pos = pos;
            tap.Scale = destScale;

            if (tap.IsBreak)
            {
                var extra = math.max(math.sin(TimeData.GetFrame() * 0.17f) * 0.5f, 0f);
                tap.Brightness = 0.95f + extra;
            }
            if (tap.IsStar && tap.RotateSpeed != 0)
            {
                var deltaRot = -180f * tap.RotateSpeed * TimeData.deltaTime;
                tap.AngleRot += deltaRot;
            }

            var tapIdx = Interlocked.Increment(ref *notesWriteCountPtr) - 1;
            notesRender[tapIdx] = new NotesRenderData()
            {
                pos = tap.Pos,
                angRad = math.radians(tap.AngleRot),
                scale = tap.Scale,
                stretchY = 0,
                spriteId = tap.TapSprite,
                color = new float4(1, 1, 1, 1),
                brightness = tap.Brightness,

                exSprite = tap.IsEx ? tap.ExSprite : 0,
                exColor = tap.ExColor,
                sliceBorder = float2.zero,

                sort = sortTime,
                foldedCount = foldedCount,
            };
        }

        /// <summary>
        /// 向前回看紧挨着的折叠兄弟（CanBeFolded 保证同组 note 连续存放），
        /// 得到本实例应当代表的叠加层数（含自身）。
        /// </summary>
        /// <param name="ignoreEnd">
        /// 预览（SeeOnly）模式不按 IsEnd 截断：加载后到播放前所有 note 的 IsEnd 都还是 true，
        /// 而预览本来就不过滤已判定 note。
        /// </param>
        private int CountFoldedCover(int index, bool ignoreEnd = false)
        {
            var count = 1;
            for (var i = index - 1; i >= 0; i--)
            {
                if (!taps[i].IsFolded) break;
                if (ignoreEnd || taps[i].IsEnd)
                    count++;
            }
            return count;
        }

        private void AutoplayUpdate(ref TapData tap)
        {
            if (tap.IsEnd) return;
            if (NoteHelper.Settings.AutoPlayMode is AutoPlayMode.Disable) return;

            var timing = TimeData.NoteTime - tap.Time;
            if (timing < InputManager.AUTOPLAY_START_SEC) return;
            switch (NoteHelper.Settings.AutoPlayMode)
            {
                case AutoPlayMode.Enable:
                    tap.JudgeGrade = JudgeGrade.LateCritical;
                    tap.IsJudged = true;
                    tap.Diff = 0;
                    EndNote(ref tap);
                    break;
                case AutoPlayMode.Random:
                    var grade = (JudgeGrade)GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss);
                    tap.JudgeGrade = tap.IsMine
                        ? (grade < JudgeGrade.FastPerfect3rd ? JudgeGrade.Miss : JudgeGrade.LateCritical)
                        : grade;
                    tap.IsJudged = true;
                    tap.Diff = grade >= JudgeGrade.LateCritical ? 11.4514f : -11.4514f;
                    EndNote(ref tap);
                    break;
            }
        }

        private void CheckUpdate(ref TapData tap)
        {
            if (tap.IsEnd) return;
            if (!NoteHelper.IsSimulated) return;

            var diffSec = TimeData.NoteTime - tap.Time;

            var buttonClicked = InputData.GetButtonState(tap.Key).IsPadDown;
            var sensorClicked = InputData.GetSensorState(tap.Key).IsPadDown;

            var clicked = sensorClicked || buttonClicked;

            // ---- Mine: touched within window -> Miss, otherwise Perfect once survived.
            //      Resolved independently of the sensor-order gate so it never softlocks. ----
            if (tap.IsMine)
            {
                if (clicked && diffSec >= -NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f)
                {
                    tap.JudgeGrade = JudgeGrade.Miss;
                    tap.IsJudged = true;
                    tap.Diff = diffSec;
                    EndNote(ref tap);
                    return;
                }
                if (diffSec >= NoteHelper.MINE_END_SEC)
                {
                    tap.JudgeGrade = JudgeGrade.LateCritical;
                    tap.IsJudged = true;
                    EndNote(ref tap);
                }
                return;
            }

            // ---- Late timeout (independent of sensor, so an untouched tap still misses) ----
            if (diffSec > NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f)
            {
                tap.JudgeGrade = JudgeGrade.Miss;
                tap.IsJudged = true;
                EndNote(ref tap);
                return;
            }

            // ---- Sensor input judgment ----
            if (!clicked) return;
            if (diffSec < -NoteHelper.TAP_JUDGE_GOOD_AREA_MSEC / 1000f) return;
            var canJudge =
                (buttonClicked && InputData.CanJudgeButton(tap.Key, tap.ButtonOrderIndex)) ||
                (sensorClicked && InputData.CanJudgeSensor(tap.Key, tap.SensorOrderIndex));
            if (!canJudge) return;

            tap.JudgeGrade = NoteHelper.GetTapJudge(diffSec, tap.IsEx);
            tap.IsJudged = true;
            tap.Diff = diffSec;
            EndNote(ref tap);
        }

        private void EndNote(ref TapData tap)
        {
            NoteHelper.PlayTapSound(SfxRequests,
                tap.JudgeGrade,
                tap.IsBreak,
                tap.IsEx,
                tap.IsMine,
                tap.Diff
            );
            NoteHelper.PlayTapEffect(JudgeEffectRequests,
                (int)tap.Key,
                tap.JudgeGrade,
                tap.IsBreak,
                tap.IsMine
            );
            NoteHelper.ReportResult(ReportResults,
                tap.JudgeGrade,
                tap.IsBreak,
                SimaiNoteType.TAP
            );
            InputData.NextTapHold(
                tap.Key
            );
            tap.IsEnd = true;
        }
    }
}