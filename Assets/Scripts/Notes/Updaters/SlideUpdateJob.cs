using MajdataViewX.Base;
using MajdataViewX.Managers;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Input;
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
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Notes.Updaters
{
    [BurstCompile]
    public unsafe struct SlideUpdateJob : IJobParallelFor
    {
        // 渲染时要向左回看同组的折叠兄弟（只读），属于跨 index 访问
        [NativeDisableParallelForRestriction]
        public NativeArray<SlideData> slides;

        [NativeDisableParallelForRestriction]
        public NativeArray<SimpleRenderData> slidesRender;
        [NativeDisableParallelForRestriction]
        public NativeArray<NotesRenderData> notesRender;

        [NativeDisableUnsafePtrRestriction]
        public int* SlidesWriteCountPtr;
        [NativeDisableUnsafePtrRestriction]
        public int* NotesWriteCountPtr;

        [NativeDisableUnsafePtrRestriction]
        public bool* SfxRequests;
        public NativeList<ReportResultEntry>.ParallelWriter ReportResults;

        public const float SlideOKKeepDuration = 17 * MajCtx.FRAME_LENGTH_SEC;
        public const float SlideOKFadeOutDuration = 8 * MajCtx.FRAME_LENGTH_SEC;
        public void Execute(int index)
        {
            if (!TimeData.IsStart)
            {
                SeeOnlyUpdate(slides[index], index);
                return;
            }
            ref var slide = ref slides.ElementRef(index);
            TransformUpdate(ref slide, index);
            AutoplayUpdate(ref slide);
            CheckUpdate(ref slide);
        }

        private void SeeOnlyUpdate(SlideData slide, int index)
        {
            if (slide.isFolded) return;
            if (TimeData.NoteTime < slide.tapTime ||
                TimeData.NoteTime > slide.shootTime + slide.LastFor) return;

            var tapTiming = TimeData.NoteTime - slide.tapTime;
            var timing = TimeData.NoteTime - slide.shootTime;
            slide.process = math.saturate(timing / math.max(slide.LastFor, 0.001f));

            // 播放期 processIdx 只增不减，暂停查看需按当前时间重建
            if (!slide.isWifi)
            {
                var idxLast = slide.slideArrowsCount - 1;
                var distance = slide.process * slide.slideArrows[idxLast].L;
                slide.processIdx = 1;
                while (slide.slideArrows[slide.processIdx].L < distance && slide.processIdx < idxLast)
                    slide.processIdx++;
            }
            else
            {
                slide.processIdx = math.max((int)(slide.process * (slide.slideArrowsCount - 1)), 1);
            }

            slide.brightness = 1f;
            RenderArrows(ref slide, index, tapTiming, slide.processIdx - 1);
            RenderStar(ref slide, index, timing, tapTiming);
        }
        // 注意：RenderXXX都是需要每帧调用的
        private void TransformUpdate(ref SlideData slide, int index)
        {
            //if (slide.isFolded) return;
            //slide的判TransformUpdate和生命周期有点耦合，把folded移到各个render去比较好
            if (slide.isEnd) return;

            var tapTiming = TimeData.NoteTime - slide.tapTime;
            var timing = TimeData.NoteTime - slide.shootTime;
            slide.process = math.saturate(timing / math.max(slide.LastFor, 0.001f));

            if (slide.isSlideEnd)
            {
                //正常需要等待slideok显示完才可以死
                //folded不需要显示直接死
                if (slide.isFolded) EndNote(ref slide);
                else RenderSlideOK(ref slide, index);
                return;
            }

            // 模拟模式下，实际已判定仍然要更新process，等待表现已判定，此时star仍需渲染
            // 非模拟模式下，isJudged和isSlideEnd是同步进行的
            if (slide.isJudged)
            {
                RenderStar(ref slide, index, timing, tapTiming);
                if (slide.lastStayTime <= 0)
                {
                    // render slideok不能用sv影响的时间
                    slide.finishJudgeTiming = TimeData.NoteTime;
                    EndSlide(ref slide);
                }
                slide.lastStayTime -= TimeData.deltaTime;
                return;
            }

            // 负 SV 可能让 tapTiming 在音符判定后重新回到淡入区间。
            // 这里只裁剪尚未判定的渲染，不能阻断 SlideOK 和判定上报的生命周期。
            if (tapTiming - slide.fadeInStartTiming < 0) return;

            RenderArrows(ref slide, index, tapTiming, slide.eaten);
            RenderStar(ref slide, index, timing, tapTiming);
        }

        private void RenderArrows(ref SlideData slide, int index, float tapTiming, int eaten)
        {
            if (slide.isFolded) return;

            // =====Arrows样式逻辑=====
            if (tapTiming <= 0)
            {
                slide.slideAlpha = math.clamp((tapTiming - slide.fadeInStartTiming) / slide.fadeInDuration, 0f, 1f);
            }
            else
            {
                slide.slideAlpha = 1f;
            }

            if (slide.isBreak) // break shine
            {
                var extra = math.max(math.sin(TimeData.GetFrame() * 0.17f) * 0.5f, 0f);
                slide.brightness = 0.95f + extra;
            }

            if (slide.slideAlpha <= 0) return;
            // =====渲染逻辑=====
            var cnt = slide.slideArrowsCount;
            var color = new float4(1, 1, 1, slide.slideAlpha);

            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timeVal = ((uint)math.max(0f, slide.tapTime * 100f)) & 0x7FFFF;
            var timePart = NoteHelper.Settings.LegacySlideLayer ? (0x7FFFFu - timeVal) : timeVal;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;

            // 第一个是路径起点，最后一个是路径终点，忽略不画，倒数第二个要看情况
            var startIdx = eaten + 1;
            var endIdx = slide.noLastArrow ? cnt - 2 : cnt - 1;
            var writeCount = math.max(0, endIdx - startIdx);

            if (writeCount <= 0) return;

            var idx = Interlocked.Add(ref *SlidesWriteCountPtr, writeCount) - writeCount;
            var foldedCount = (uint)CountFoldedCover(index);
            for (var i = startIdx; i < endIdx; i++)
            {
                var p = slide.slideArrows[i];

                slidesRender[idx + i - startIdx] = new SimpleRenderData
                {
                    pos = new float2(p.X, p.Y),
                    angRad = math.radians(p.RotZ),
                    scale = new float2(1, 1),
                    spriteId = slide.isWifi ? slide.slideSprite.Offset(i - 1) : slide.slideSprite,
                    color = color,
                    brightness = slide.brightness,
                    // sort (32 bits): [19 bits: time] + [5 bits: slide tie-breaker (32 wrap)] + [8 bits: arrow path i (256 wrap)]
                    sort = (timePart << 13) | ((uint)(index & 0x1F) << 8) | ((uint)i & 0xFF),
                    foldedCount = foldedCount,
                };
            }
        }

        private void RenderStar(ref SlideData slide, int index, float timing, float tapTiming)
        {
            if (slide.isFolded) return;

            var foldedCount = (uint)CountFoldedCover(index);

            // =====Star样式逻辑=====
            if (timing <= 0)
            {
                slide.starAlpha = math.saturate(tapTiming / (slide.shootTime - slide.tapTime));
                slide.starScale = slide.starAlpha + 0.5f;
            }
            else
            {
                slide.starAlpha = 1f;
                slide.starScale = 1.5f;
            }

            if (slide.starAlpha <= 0) return;
            // =====渲染逻辑=====
            // sortTime (30 bits): [19 bits: time (87 mins wrap)] + [11 bits: index tie-breaker (2048 wrap)]
            var timeVal = ((uint)math.max(0f, slide.tapTime * 100f)) & 0x7FFFF;
            var timePart = NoteHelper.Settings.LegacySlideLayer ? (0x7FFFFu - timeVal) : timeVal;
            var sortTime = ((timePart << 11) | (uint)(index & 0x7FF)) & 0x3FFFFFFF;
            if (!slide.isWifi)
            {
                var idxLast = slide.slideArrowsCount - 1; //这里借助路径起终点画star

                var distance = slide.process * slide.slideArrows[idxLast].L;
                while (slide.slideArrows[slide.processIdx].L < distance && slide.processIdx < idxLast) slide.processIdx++;
                // processIdx 初值是 1 所以一定不会下溢，然后循环条件保证了不会上溢
                var idx0 = slide.processIdx - 1;
                var idx1 = slide.processIdx;
                var p0 = slide.slideArrows[idx0];
                var p1 = slide.slideArrows[idx1];
                var t = math.unlerp(p0.L, p1.L, distance);

                var starPosX = math.lerp(p0.X, p1.X, t);
                var starPosY = math.lerp(p0.Y, p1.Y, t);
                var deltaRot = math.fmod(p1.RotZ - p0.RotZ + 540f, 360f) - 180f;
                var starRot = p0.RotZ + deltaRot * t;

                var nIdx = Interlocked.Increment(ref *NotesWriteCountPtr) - 1;
                slide.starPos = new float2(starPosX, starPosY);
                notesRender[nIdx] = new NotesRenderData
                {
                    pos = slide.starPos,
                    angRad = math.radians(starRot + 90),
                    scale = slide.starScale,
                    stretchY = 0,
                    spriteId = slide.starSprite,
                    color = new float4(1, 1, 1, slide.starAlpha),
                    brightness = 1f,
                    exSprite = 0,
                    exColor = float4.zero,
                    sliceBorder = new float2(0, 0),
                    sort = 0x40000000u | sortTime,
                    foldedCount = foldedCount,
                };
            }
            else
            {
                // 让 wifi 也可以使用 processIdx 来计算 eaten
                slide.processIdx = math.max((int)(slide.process * (slide.slideArrowsCount - 1)), 1);

                var starPos = stackalloc float2[3]; //C, L, R   //这里不借助slideArrows，提供不了另两条的信息
                slide.starPos = starPos[0] = slide.starPosConstC * slide.process + slide.starPosStart;
                slide.starPosL = starPos[1] = slide.starPosConstL * slide.process + slide.starPosStart;
                slide.starPosR = starPos[2] = slide.starPosConstR * slide.process + slide.starPosStart;
                var nIdx = Interlocked.Add(ref *NotesWriteCountPtr, 3) - 3;
                for (var i = 0; i < 3; i++)
                {
                    var rotZ = slide.slideArrows[0].RotZ - 22.5f * (i - 1);
                    notesRender[nIdx + i] = new NotesRenderData
                    {
                        pos = starPos[i],
                        angRad = math.radians(rotZ + 90),
                        scale = slide.starScale,
                        stretchY = 0,
                        spriteId = slide.starSprite,
                        color = new float4(1, 1, 1, slide.starAlpha),
                        brightness = 1f,
                        exSprite = 0,
                        exColor = float4.zero,
                        sliceBorder = new float2(0, 0),
                        sort = 0x40000000u | sortTime,
                        foldedCount = foldedCount,
                    };
                }
            }
        }

        private void AutoplayUpdate(ref SlideData slide)
        {
            if (slide.isEnd || slide.isSlideEnd) return;
            var timing = TimeData.NoteTime - slide.shootTime;
            if (timing < InputManager.AUTOPLAY_START_SEC) return;
            switch (NoteHelper.Settings.AutoPlayMode)
            {
                // 非模拟模式下星星可以正常走到尾再显示slideok并销毁
                case AutoPlayMode.Enable:
                case AutoPlayMode.Random:
                    {
                        if (NoteHelper.Settings.SmoothSlideAnime)
                        {
                            // 先前 RenderStar 的时候计算过 processIdx 可以直接拿来用
                            slide.eaten = slide.processIdx - 1;
                        }
                        else
                        {
                            // slide 各判定区长度差异很大（conn slide更严重）所以直接 lerp 不是很好看
                            // 这里借用一下 judgeCurrent 存储目前到哪个区了
                            if (slide.processIdx > slide.judgeQueue[slide.judgeCurrent].ArrowProgressFinish)
                            {
                                slide.eaten = slide.judgeQueue[slide.judgeCurrent].ArrowProgressFinish;
                                slide.judgeCurrent++;
                            }
                            else if (slide.processIdx > slide.judgeQueue[slide.judgeCurrent].ArrowProgressPush)
                            {
                                slide.eaten = slide.judgeQueue[slide.judgeCurrent].ArrowProgressPush;
                            }
                        }

                        if (!slide.isSoundPlayed)
                        {
                            NoteHelper.PlaySlideSound(SfxRequests,
                                slide.isBreak
                            );
                            slide.isSoundPlayed = true;
                        }

                        if (slide.process >= 1)
                        {
                            if (NoteHelper.Settings.AutoPlayMode is AutoPlayMode.Enable)
                            {
                                slide.judgeGrade = JudgeGrade.LateCritical;
                            }
                            else
                            {
                                // 这里起始点不用TooFast，兼容一下普通slide
                                var grade = (JudgeGrade)GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss);
                                slide.judgeGrade = slide.isMine
                                    ? (grade < JudgeGrade.FastPerfect3rd ? JudgeGrade.TooFast : JudgeGrade.LateCritical)
                                    : grade;
                            }
                            // 非模拟模式下需要自行赋值finishJudgeTiming
                            slide.finishJudgeTiming = TimeData.NoteTime;
                            FinishJudgeSlide(ref slide);
                            EndSlide(ref slide);
                            if (slide.isFolded) EndNote(ref slide);
                        }
                        break;
                    }
                // 模拟模式下需要等待星星完全结束（isSlideEnd），但因为isJudged所以并不会把手黏在这里
                case AutoPlayMode.DJAutoButton:
                case AutoPlayMode.DJAutoSensor:
                case AutoPlayMode.Disable: // disable也要处理mine情况
                    {
                        if (!slide.isMine || !NoteHelper.Settings.MineAutoSlide) break;

                        // 目前判定到哪个区
                        var idx = slide.judgeCurrent;
                        if (slide.isWifi)
                        {
                            // wifi 的情况，取三支里剩的最长的
                            idx = math.min(slide.judgeCurrent, math.min(slide.judgeL_Current, slide.judgeR_Current));
                        }

                        // 剩一个区就不动了，留给check表演
                        if (idx >= slide.judgeQueueCount - 1) break;

                        var newEaten = 0;
                        // wifi 三支判定队列的 ArrowProgress 是一样的
                        if (slide.processIdx > slide.judgeQueue[idx].ArrowProgressFinish)
                        {
                            // 如果引导星星已经走完这个区了，就推进一个区
                            newEaten = slide.judgeQueue[idx].ArrowProgressFinish;

                            if (slide.isWifi)
                            {
                                // wifi 的情况要分别检查三支各自是否需要推进
                                if (slide.judgeCurrent <= idx)
                                {
                                    slide.judgeCurrent = idx + 1;
                                    slide.currentOn = SensorType.Invalid;
                                }

                                if (slide.judgeL_Current <= idx)
                                {
                                    slide.judgeL_Current = idx + 1;
                                    slide.currentOnL = SensorType.Invalid;
                                }

                                if (slide.judgeR_Current <= idx)
                                {
                                    slide.judgeR_Current = idx + 1;
                                    slide.currentOnR = SensorType.Invalid;
                                }
                            }
                            else
                            {
                                // 普通slide肯定需要推进了
                                slide.currentOn = SensorType.Invalid;
                                slide.judgeCurrent++;
                            }
                        }
                        else if (slide.processIdx > slide.judgeQueue[idx].ArrowProgressPush)
                        {
                            newEaten = slide.judgeQueue[idx].ArrowProgressPush;
                        }

                        if (NoteHelper.Settings.SmoothSlideAnime)
                        {
                            newEaten = slide.processIdx - 1;
                        }

                        if (newEaten > slide.eaten)
                        {
                            slide.eaten = newEaten;
                        }

                        break;
                    }
            }
        }

        private void CheckUpdate(ref SlideData slide)
        {
            if (!NoteHelper.IsSimulated) return;
            if (slide.isEnd || slide.isSlideEnd || slide.isJudged) return;

            // slide的正解帧是 星星进入最后一个判定区的时间，所以判定部分受SV影响
            var tapTiming = TimeData.NoteTime - slide.tapTime;
            var timing = TimeData.NoteTime - slide.shootTime;

            if (tapTiming < -NoteHelper.SLIDE_CHECK_AHEAD_TIME_MSEC / 1000f) return; // 提前100ms接受判定
            var remaining = slide.LastFor - timing;

            // 星星 miss 的时间点在结束后 +550ms
            var forceJudge = timing - slide.LastFor - NoteHelper.SLIDE_FORCE_MISS / 1000f;
            // mine 星星 perfect 的时间点在slide结束
            // mineAutoSlide开着时直到倒数第二个区都会被滚木摸掉，此时最后一个区如果蹭到了就miss
            bool timeout = slide.isMine ? (TimeData.NoteTime >= slide.judgeTiming) : (forceJudge >= 0);

            if (timeout)
            {
                slide.judgeGrade = slide.isMine
                    ? JudgeGrade.LateCritical
                    : (CanLeaveTailAsGood(slide) ? JudgeGrade.LateGood : JudgeGrade.Miss);
                // 此处将lastStayTime置0，去除slideok显示延迟
                slide.lastStayTime = 0;
                FinishJudgeSlide(ref slide);
                return;
            }

            if (!slide.isWifi)
            {
                ProcessAreas(ref slide, slide.judgeQueue, slide.judgeQueueCount, ref slide.judgeCurrent, ref slide.currentOn);
            }
            else
            {
                ProcessAreas(ref slide, slide.judgeQueue, slide.judgeQueueCount, ref slide.judgeCurrent, ref slide.currentOn);
                ProcessAreas(ref slide, slide.judgeQueueL, slide.judgeQueueLCount, ref slide.judgeL_Current, ref slide.currentOnL);
                ProcessAreas(ref slide, slide.judgeQueueR, slide.judgeQueueRCount, ref slide.judgeR_Current, ref slide.currentOnR);
            }

            var newEaten = 0;
            if (!slide.isWifi)
            {
                if (slide.judgeCurrent >= slide.judgeQueueCount) //按完了
                {
                    slide.judgeGrade = CalcSlideJudgeGrade(ref slide);
                    FinishJudgeSlide(ref slide);
                    return;
                }

                if (slide.currentOn >= SensorType.A1) //有按下
                {
                    newEaten = slide.judgeQueue[slide.judgeCurrent].ArrowProgressPush;
                }
                else if (slide.judgeCurrent > 0) //有完成
                {
                    newEaten = slide.judgeQueue[slide.judgeCurrent - 1].ArrowProgressFinish;
                }
                else
                {
                    newEaten = 0; //啥也没
                }
            }
            else
            {
                if (slide.judgeCurrent >= slide.judgeQueueCount &&
                    slide.judgeL_Current >= slide.judgeQueueLCount &&
                    slide.judgeR_Current >= slide.judgeQueueRCount)
                {
                    slide.judgeGrade = CalcSlideJudgeGrade(ref slide);
                    FinishJudgeSlide(ref slide);
                    return;
                }

                var eatenC = (slide.currentOn >= SensorType.A1)
                    ? slide.judgeQueue[slide.judgeCurrent].ArrowProgressPush
                    : (slide.judgeCurrent > 0)
                        ? slide.judgeQueue[slide.judgeCurrent - 1].ArrowProgressFinish
                        : 0;
                var eatenL = (slide.currentOnL >= SensorType.A1)
                    ? slide.judgeQueueL[slide.judgeL_Current].ArrowProgressPush
                    : (slide.judgeL_Current > 0)
                        ? slide.judgeQueueL[slide.judgeL_Current - 1].ArrowProgressFinish
                        : 0;
                var eatenR = (slide.currentOnR >= SensorType.A1)
                    ? slide.judgeQueueR[slide.judgeR_Current].ArrowProgressPush
                    : (slide.judgeR_Current > 0)
                        ? slide.judgeQueueR[slide.judgeR_Current - 1].ArrowProgressFinish
                        : 0;
                newEaten = math.min(eatenC, math.min(eatenL, eatenR));
            }

            // 这个检查是为了 mine slide，如果自动推进已经经过了 ArrowProgressPush 但第一个区没有按下，
            // newEaten 就会比 slide.eaten 小
            if (newEaten > slide.eaten)
            {
                slide.eaten = newEaten;
            }
        }

        // 检查 area 队列，更新 sensor On/Off 状态并推进游标
        private void ProcessAreas(ref SlideData slide, SlideArea* queue, int queueCount, ref int cur, ref SensorType currentOn)
        {
            if (cur >= queueCount) return;

            var changed = false;
            do
            {
                changed = false;

                var first = queue[cur];
                var hasSecond = cur + 1 < queueCount;

                // 先看当前第一个区
                if (currentOn <= SensorType.Invalid)  // 第一个区还没按
                {
                    if (InputData.GetSensorState(first.SensorA).Status)
                    {
                        currentOn = first.SensorA;
                        changed = true;
                        if (!hasSecond) cur++;  // 最后一个区不需要松手
                    }
                    else if (first.SensorB >= SensorType.A1 && InputData.GetSensorState(first.SensorB).Status)
                    {
                        currentOn = first.SensorB;
                        changed = true;
                        if (!hasSecond) cur++;  // 最后一个区不需要松手
                    }
                }
                else // 第一个区已经按下了
                {
                    if (!InputData.GetSensorState(currentOn).Status)
                    {
                        currentOn = SensorType.Invalid;
                        changed = true;
                        cur++;
                    }
                }

                // 然后看当前第二个区，注意当第一个区已经按下时一定可以跳区
                var skippable = (cur != slide.unskippable1 && cur != slide.unskippable2 || currentOn >= SensorType.A1);
                if (!changed && hasSecond && skippable)
                {
                    var second = queue[cur + 1];
                    var isSecondLast = cur + 2 >= queueCount;
                    var sensorState = InputData.GetSensorState(second.SensorA);
                    if (sensorState.Status || sensorState.IsPadUp)  // 计算跳区时本帧刚刚松开的区被认为依然按下
                    {
                        currentOn = second.SensorA;
                        changed = true;
                        cur++;
                        if (isSecondLast) cur++;  // 最后一个区不需要松手
                    }
                    else if (second.SensorB >= SensorType.A1)
                    {
                        sensorState = InputData.GetSensorState(second.SensorB);
                        if (sensorState.Status || sensorState.IsPadUp)
                        {
                            currentOn = second.SensorB;
                            changed = true;
                            cur++;
                            if (isSecondLast) cur++;  // 最后一个区不需要松手
                        }
                    }
                }

                if (changed && !slide.isSoundPlayed)
                {
                    NoteHelper.PlaySlideSound(SfxRequests,
                        slide.isBreak
                    );
                    slide.isSoundPlayed = true;
                }
            } while (changed && cur < queueCount);

            if (cur >= queueCount)
            {
                currentOn = SensorType.Invalid;
                cur = (byte)queueCount;
            }
        }
        /// <summary>
        /// 留尾判绿：slide 因 timeout 判 Miss 时，若剩余判定段满足条件则提升为 LateGood。
        /// Slide：剩余 ≤ 1 段。
        /// Wifi：三支各自剩余 ≤ 1 段。其中中间支(judgeQueue)末段是两段拼合的 OR 段，
        /// 官机在留尾检查时将其算作两段（bug），故中间支只要未完全清空就 ≥ 2 段，无法判绿。
        /// </summary>
        private bool CanLeaveTailAsGood(SlideData slide)
        {
            if (!slide.isWifi)
            {
                return slide.judgeQueueCount - slide.judgeCurrent <= 1;
            }
            else
            {
                var cRemaining = slide.judgeQueueCount - slide.judgeCurrent;
                // 中间支末段为合并段，官机留尾检查时按2段计：未清空则 +1
                if (slide.judgeCurrent < slide.judgeQueueCount)
                    cRemaining += 1;
                return cRemaining <= 1
                    && slide.judgeQueueLCount - slide.judgeL_Current <= 1
                    && slide.judgeQueueRCount - slide.judgeR_Current <= 1;
            }
        }

        private JudgeGrade CalcSlideJudgeGrade(ref SlideData slide)
        {
            if (slide.isMine)
            {
                return JudgeGrade.TooFast;
            }

            var triggerTime = TimeData.NoteTime;

            const float totalInterval = 36f / 60; // 秒
            const float nPInterval = 14f / 60; // Perfect基础区间
            const float gr1Interval = 21f / 60;
            const float gr2Interval = 25f / 60;
            const float gr3Interval = 29f / 60;
            const float gdInterval = 36f / 60;

            var ext = slide.lastStayTime; // 额外区间T
            var pInterval = math.min(nPInterval + ext / 4f, totalInterval); // Perfect总区间

            var diff = slide.judgeTiming - triggerTime;
            var isFast = diff > 0;
            diff = math.abs(diff);

            if (diff <= pInterval)
                return isFast ? JudgeGrade.FastCritical : JudgeGrade.LateCritical;
            if (diff <= gr1Interval)
                return isFast ? JudgeGrade.FastGreat1st : JudgeGrade.LateGreat1st;
            if (diff <= gr2Interval)
                return isFast ? JudgeGrade.FastGreat2nd : JudgeGrade.LateGreat2nd;
            if (diff <= gr3Interval)
                return isFast ? JudgeGrade.FastGreat3rd : JudgeGrade.LateGreat3rd;
            if (diff <= gdInterval)
                return isFast ? JudgeGrade.FastGood : JudgeGrade.LateGood;
            if (!isFast)
            {
                // 超出了自然的late good区间，官机上是先判定成too late miss再提升到late good
                // 此处将lastStayTime置0，去除slideok显示延迟
                slide.lastStayTime = 0;
                return JudgeGrade.LateGood;
            }
            // too fast good
            return JudgeGrade.FastGood;
        }

        /// <summary>
        /// 星星实际上已判定
        /// </summary>
        private void FinishJudgeSlide(ref SlideData slide)
        {
            slide.judgeTime = TimeData.NoteTime;
            slide.isJudged = true;
        }
        /// <summary>
        /// 星星表现为已判定
        /// </summary>
        /// <remarks>非模拟模式下，CompleteSlide在完全结束时才调用，因此EndSlide紧随CompleteSlide之后</remarks>
        private void EndSlide(ref SlideData slide)
        {
            slide.isSlideEnd = true;
            NoteHelper.PlaySlideEndSound(SfxRequests,
                slide.judgeGrade,
                slide.isMine,
                slide.isBreak
            );
            NoteHelper.ReportResult(ReportResults,
                slide.judgeGrade,
                slide.isBreak,
                SimaiNoteType.SLIDE
            );
        }

        private void RenderSlideOK(ref SlideData slide, int index)
        {
            var foldedCount = (uint)CountFoldedCover(index);
            var ok = slide.okPose;

            var baseJ = slide.okType switch
            {
                SlideOkType.StraightL => NoteSp.JUST_STR_L,
                SlideOkType.StraightR => NoteSp.JUST_STR_R,
                SlideOkType.CircleL => NoteSp.JUST_CURV_L,
                SlideOkType.CircleR => NoteSp.JUST_CURV_R,
                SlideOkType.WifiU => NoteSp.JUST_WIFI_U,
                SlideOkType.WifiD => NoteSp.JUST_WIFI_D,
                _ => NoteSp.JUST_STR_L,
            };
            var off = slide.judgeGrade switch
            {
                >= JudgeGrade.FastPerfect3rd and <= JudgeGrade.LatePerfect3rd => 0,
                JudgeGrade.FastGreat1st or JudgeGrade.FastGreat2nd or JudgeGrade.FastGreat3rd => 6,
                JudgeGrade.FastGood => 12,
                JudgeGrade.LateGreat1st or JudgeGrade.LateGreat2nd or JudgeGrade.LateGreat3rd => 18,
                JudgeGrade.LateGood => 24,
                JudgeGrade.Miss => 30,
                JudgeGrade.TooFast => 36,
                _ => 30,
            };

            // SlideOK fade-out animation (Just_curv animator equivalent):
            // fade out from 1→0 over SlideOKFadeOutDuration
            var timing = TimeData.NoteTime - slide.finishJudgeTiming;

            slide.slideOKAlpha = timing switch
            {
                < 0 => 0f,
                < 2 * MajCtx.FRAME_LENGTH_SEC => math.saturate(timing / (2 * MajCtx.FRAME_LENGTH_SEC)),
                < 17 * MajCtx.FRAME_LENGTH_SEC => 1f,
                < 25 * MajCtx.FRAME_LENGTH_SEC => math.saturate(1f - (timing - 17 * MajCtx.FRAME_LENGTH_SEC) / (8 * MajCtx.FRAME_LENGTH_SEC)),
                _ => 0f,
            };

            if (timing > 25 * MajCtx.FRAME_LENGTH_SEC)
                EndNote(ref slide);

            if (slide.isBreak && off == 0) // break perfect
            {
                bool flag = ((int)(timing / MajCtx.FRAME_LENGTH_SEC) / 2) % 2 == 0;
                if (flag) off = 42; // 偏移到Break Skin
            }

            var idx = Interlocked.Increment(ref *SlidesWriteCountPtr) - 1;
            slidesRender[idx] = new SimpleRenderData
            {
                pos = new float2(ok.X, ok.Y),
                angRad = math.radians(ok.RotZ),
                scale = new float2(1, 1),
                spriteId = baseJ.Offset(off),
                color = new float4(1, 1, 1, slide.slideOKAlpha),
                brightness = slide.brightness,
                sort = 0u,
                foldedCount = foldedCount,
            };
        }

        /// <summary>
        /// 向前回看紧挨着的折叠兄弟（CanBeFolded 保证同组 note 连续存放），
        /// 得到本实例应当代表的叠加层数（含自身）。
        /// </summary>
        private int CountFoldedCover(int index)
        {
            var count = 1;
            for (var i = index - 1; i >= 0; i--)
            {
                if (!slides[i].isFolded) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 星星彻底结束
        /// </summary>
        private void EndNote(ref SlideData slide)
        {
            slide.isEnd = true;
        }
    }
}