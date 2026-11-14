using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Types.Notes.RenderData;
using MajdataViewX.Utils.Extensions;
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
    public unsafe struct EachLineUpdateJob : IJobParallelFor
    {
        public NativeArray<EachLineData> eachLines;

        [NativeDisableParallelForRestriction]
        public NativeArray<LineRenderData> eachLinesRender;
        [NativeDisableUnsafePtrRestriction]
        public int* EachLinesWriteCountPtr;

        public void Execute(int index)
        {
            ref var el = ref eachLines.ElementRef(index);
            if (el.isEnd && TimeData.IsStart) return;

            var timing = el.usingSV
                ? TimeData.FakeNoteTime - TimeData.GetPositionAtTime(el.time)
                : TimeData.NoteTime - el.time;

            if (timing >= 0)
            {
                if (TimeData.IsStart)
                    el.isEnd = true;
                return;
            }

            var rawDistance = timing * NoteHelper.Settings.TapSpeed * el.hspeed + 4.8f;
            var clampedDistance = math.max(rawDistance, 1.225f);
            var destScale = math.min(rawDistance * 0.4f + 0.51f, 1f);

            if (destScale < 0f) return;

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timePart = ((uint)math.max(0f, el.time * 100f)) & 0x7FFFF;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;

            var idx = Interlocked.Increment(ref *EachLinesWriteCountPtr) - 1;
            eachLinesRender[idx] = new LineRenderData
            {
                angRad = math.radians(el.ang),
                scale = clampedDistance / 4.8f,
                spriteId = el.lineSprite,
                sort = sortTime,
                foldedCount = 1,   // eachLine 不去重渲染，恒为单层
            };
        }
    }
}