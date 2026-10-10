using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Input;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using Cimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UIElements;
using static MajdataViewX.Base.MajCtx;
using static UnityEngine.Rendering.HableCurve;

namespace MajdataViewX.Managers
{
    public partial class NoteManager
    {
        private readonly int[] _buttonOrderIndex = new int[BUTTON_COUNT];
        private readonly int[] _sensorOrderIndex = new int[SENSOR_COUNT];

        private unsafe SlideArea* slideAreaPool;
        private unsafe SlidePose* slidePosePool;
        private int areaPoolIndex = 0;
        private int posePoolIndex = 0;
        private readonly List<SlideArea[]> loadedSlideAreaArrays = new();
        private readonly List<SlidePose[]> loadedSlidePoseArrays = new();

        private readonly List<NoteRegister>[] loadedTouches = new List<NoteRegister>[SENSOR_COUNT];

        public unsafe void Load(SimaiChart chart)
        {
            _prevChain.Complete();

            // 防御性清空：正常流程下 Stop 时 ResetCur 已清空，
            // 此处防止"未 Stop 就重新 Load"导致 NativeList 累积
            taps.Clear();
            eachLines.Clear();
            holds.Clear();
            slides.Clear();
            touches.Clear();
            touchHolds.Clear();
            plays.Clear();
            _djAutoTouchInfosThisTiming.Clear();
            touchGroupTotalCounts.Clear();
            touchGroupJudgedCounts.Clear();
            touchHoldGroupTotalCounts.Clear();
            touchHoldGroupPressedCounts.Clear();

            if (chart.Timings.Length == 0) return; //涉及内存清空的就不干了没必要

            areaPoolIndex = 0;
            posePoolIndex = 0;
            Array.Fill(_buttonOrderIndex, 0);
            Array.Fill(_sensorOrderIndex, 0);
            if (slideAreaPool != null)
                UnsafeUtility.Free(slideAreaPool, Allocator.Persistent);
            if (slidePosePool != null)
                UnsafeUtility.Free(slidePosePool, Allocator.Persistent);
            for (var i = 0; i < SENSOR_COUNT; i++)
                if (loadedTouches[i] != null)
                    loadedTouches[i].Clear();
                else
                    loadedTouches[i] = new();


            ConfigureRenderCapacity(chart);


            foreach (var timing in chart.Timings)
            {
                LoadTiming(timing);
            }








            slideAreaPool = (SlideArea*)UnsafeUtility.Malloc(
                areaPoolIndex * sizeof(SlideArea),
                16, Allocator.Persistent);
            slidePosePool = (SlidePose*)UnsafeUtility.Malloc(
                posePoolIndex * sizeof(SlidePose),
                16, Allocator.Persistent);

            for (var i = 0; i < slides.Length; i++)
            {
                ref var slide = ref slides.ElementRef(i);
                slide.judgeQueue = slideAreaPool + slide.judgeQueueOffset;
                slide.judgeQueueL = slideAreaPool + slide.judgeQueueLOffset;
                slide.judgeQueueR = slideAreaPool + slide.judgeQueueROffset;
                slide.slideArrows = slidePosePool + slide.slideArrowsOffset;
            }
            for (var i = 0; i < plays.Length; i++)
            {
                if (plays[i].Type is DJAutoPlayType.Swipe)
                {
                    ref var play = ref plays.ElementRef(i);
                    play.BindSlide = slides.GetUnsafeReadOnlyPtr() + play.BindSlideIndex;
                }
            }


            var cur1 = 0;
            foreach (var areas in loadedSlideAreaArrays)
            {
                fixed (SlideArea* src = areas)
                {
                    UnsafeUtility.MemCpy(
                        slideAreaPool + cur1,
                        src, areas.Length * sizeof(SlideArea));
                }

                cur1 += areas.Length;
            }

            var cur2 = 0;
            foreach (var poses in loadedSlidePoseArrays)
            {
                fixed (SlidePose* src = poses)
                {
                    UnsafeUtility.MemCpy(
                        slidePosePool + cur2,
                        src, poses.Length * sizeof(SlidePose));
                }

                cur2 += poses.Length;
            }

            loadedSlideAreaArrays.Clear();
            loadedSlidePoseArrays.Clear();

            plays.Sort(new DJAutoPlayStartTimeComparer());
            BindSkippableHitsBySwipe();
            BindPlayPatterns();

            MajBurst.MultTouchHandler.Clear();
            MajBurst.MultTouchHandler.Load(loadedTouches);
        }


        /// <remarks>
        /// 本来isEach在isMine时应该被忽略，但实际上
        /// isEach对判定并无影响，主要取其skin的区别，
        /// 而且mine的tapline同样需要换为each line
        /// 因此全部丢进LoadSkin作特判
        /// </remarks>
        private unsafe void LoadTiming(in SimaiTiming timing)
        {
            int touchStartIdx = touches.Length;
            int touchHoldStartIdx = touchHolds.Length;

            var startPositions = stackalloc int[timing.Notes.Length];
            var nonMineCount = 0;

            var loadedStarIndex = stackalloc int[timing.Notes.Length];
            var loadedStarCount = 0;
            var loadedSlideCount = stackalloc int[8];
            var loadedSlideLength = stackalloc float[8];
            var loadedSlideTime = stackalloc double[8];

            bool eachLineIgnoreSV = false;

            ReadOnlySpan<byte> lastSlideContent = ReadOnlySpan<byte>.Empty;
            var sameTapCount = 0;
            var sameHoldCount = 0;
            var sameTouchCount = 0;
            var sameTouchHoldCount = 0;
            var sameSlideCount = 0;
            foreach (var note in timing.Notes)
            {
                try
                {
                    switch (note.Type)
                    {
                        case SimaiNoteType.TAP:
                            LoadTap(timing, note, ref sameTapCount);
                            if (!note.IsMine)
                            {
                                eachLineIgnoreSV |= note.IsIgnoreSV;
                                startPositions[nonMineCount++] = note.StartPos;
                            }
                            break;
                        case SimaiNoteType.HOLD:
                            LoadHold(timing, note, ref sameHoldCount);
                            if (!note.IsMine)
                            {
                                eachLineIgnoreSV |= note.IsIgnoreSV;
                                startPositions[nonMineCount++] = note.StartPos;
                            }
                            break;
                        case SimaiNoteType.TOUCH:
                            LoadTouch(timing, note, ref sameTouchCount);
                            break;
                        case SimaiNoteType.TOUCHHOLD:
                            LoadTouchHold(timing, note, ref sameTouchHoldCount);
                            break;
                        case SimaiNoteType.SLIDE:
                            lastSlideContent = LoadSlideChain(
                                timing,
                                note,
                                lastSlideContent,
                                ref sameTapCount,
                                ref sameSlideCount,
                                ref loadedSlideLength[note.StartPos - 1],
                                ref loadedSlideTime[note.StartPos - 1]);
                            loadedSlideCount[note.StartPos - 1]++;
                            break;
                    }
                }
                catch (Exception e)
                {
                    OnNoteLoadFailed(note, e);
                }
            }

            // each line
            if (nonMineCount > 1)
            {
                for (int i = 0; i < nonMineCount - 1; i++)
                {
                    var s = (float)timing.Time;
                    var spd = timing.HSpeed;
                    CreateEachLine(s, startPositions[i], startPositions[i + 1], spd, eachLineIgnoreSV);
                }
            }

            // star rotate
            // loadedStarIndex 按 star 创建顺序连续写入；pos 由 starTap.Key 反推（star 的 Key 必为 A1–A8 = 0–7），
            // 再用 per-pos 的 loadedStarCount/loadedSlideLength/loadedSlideTime 回填
            for (var i = 0; i < loadedStarCount; i++)
            {
                ref var starTap = ref taps.ElementRef(loadedStarIndex[i]);
                var pos = (int)starTap.Key;
                var cnt = loadedSlideCount[pos];
                var length = loadedSlideLength[pos];
                var time = loadedSlideTime[pos];
                if (time > 0)
                {
                    // RotateSpeed = 1 时是每秒转 180 度
                    // 官机算法是 转速 = 同头星星总长 / (总时间 * 15 * pi)
                    // 长度单位像素，时间单位ms，转速单位度/帧，转速最大是 18
                    // 这里 SlideLength 是 100ppu，SlideTime 是秒
                    if (cnt >= 2)
                    {
                        starTap.IsDouble = true;
                        // 解决贴图的方式有点丑陋。。。它工作
                        starTap.Init();
                    }
                    starTap.RotateSpeed = math.min(6f, length / ((float)time * 2 * math.PI));
                }
            }


            // touch group
            int touchCount = touches.Length - touchStartIdx;
            int thCount = touchHolds.Length - touchHoldStartIdx;
            // touch 头判与 touchhold 头判分开算 group；touchhold 按下 group 另算
            if (touchCount > 0)
            {
                ProcessTouchGroups(touchStartIdx, touchCount);
            }
            if (thCount > 0)
            {
                ProcessTouchHoldGroups(touchHoldStartIdx, thCount);
            }

            // djauto: 本 timing 的 touch hit 双圆预合并后写入 hits
            CombineTouchHitsThisTiming();
        }

        private struct TouchGroupBuild
        {
            public int[] MemberGroupIds;
        }

        /// <summary>
        /// 把同 timing 的一批 touch 类 note 按传感器邻接关系聚成判定 group。
        /// 算法：去重(同 sensor 合并) -> BFS 连通分量 -> 分量内 ≥5 个 sensor 才建 group(多数通过)。
        /// group 的 totalCount 等计数由调用方传入的 list 承接，groupId 即追加时的下标。
        /// </summary>
        private static TouchGroupBuild BuildTouchGroups(
            IReadOnlyList<SensorType> sensors,
            NativeList<int> totalCountsOut,
            NativeList<int> counterOut)
        {
            int count = sensors.Count;
            var memberGroupIds = new int[count];
            for (int i = 0; i < count; i++) memberGroupIds[i] = -1;

            // 1. 去重：同 sensor 合并为一个 unique
            var uniqueIndices = new List<int>();      // unique -> 该 sensor 的首个 member 索引
            var memberToUnique = new int[count];
            for (int i = 0; i < count; i++)
            {
                int found = -1;
                for (int j = 0; j < uniqueIndices.Count; j++)
                {
                    if (sensors[uniqueIndices[j]] == sensors[i])
                    {
                        found = j;
                        break;
                    }
                }
                memberToUnique[i] = found != -1 ? found : uniqueIndices.Count;
                if (found == -1) uniqueIndices.Add(i);
            }

            int uniqueCount = uniqueIndices.Count;
            if (uniqueCount == 0)
                return new TouchGroupBuild { MemberGroupIds = memberGroupIds };

            // 2. BFS 连通分量：TOUCH_GROUPS 邻接关系
            var visited = new bool[uniqueCount];
            var uniqueGroupIds = new int[uniqueCount];
            for (int i = 0; i < uniqueCount; i++) uniqueGroupIds[i] = -1;

            for (int i = 0; i < uniqueCount; i++)
            {
                if (visited[i]) continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    component.Add(curr);

                    var s1 = sensors[uniqueIndices[curr]];
                    for (int j = 0; j < uniqueCount; j++)
                    {
                        if (visited[j]) continue;
                        var s2 = sensors[uniqueIndices[j]];
                        if (TOUCH_GROUPS.TryGetValue(s1, out var adj) && adj.Contains(s2))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                // 3. ≥5 个 sensor 才算一个判定 group（多数通过阈值）
                if (component.Count < 5) continue;

                int groupId = totalCountsOut.Length;
                // total 含同 sensor 重复的 note（每个实际 note 都计入判定计数）
                int total = 0;
                for (int m = 0; m < count; m++)
                {
                    if (component.Contains(memberToUnique[m])) total++;
                }
                totalCountsOut.Add(total);
                counterOut.Add(0);

                for (int k = 0; k < component.Count; k++)
                {
                    int u = component[k];
                    uniqueGroupIds[u] = groupId;
                }
            }

            // 4. 回填每个 member 的 groupId
            for (int i = 0; i < count; i++)
                memberGroupIds[i] = uniqueGroupIds[memberToUnique[i]];

            return new TouchGroupBuild { MemberGroupIds = memberGroupIds };
        }

        /// <summary>
        /// 头判 group：仅 touch 之间多数通过。写 touch 的 groupId。
        /// </summary>
        private void ProcessTouchGroups(int startIdx, int count)
        {
            if (count == 0) return;

            var sensors = new List<SensorType>(count);
            var idx = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (touches[startIdx + i].isMine) continue;
                sensors.Add(touches[startIdx + i].sensor);
                idx.Add(i);
            }

            var build = BuildTouchGroups(sensors, touchGroupTotalCounts, touchGroupJudgedCounts);

            for (int k = 0; k < idx.Count; k++)
            {
                var t = touches[startIdx + idx[k]];
                t.groupId = build.MemberGroupIds[k];
                touches[startIdx + idx[k]] = t;
            }
        }

        /// <summary>
        /// touchhold 的头判 group 与按下 group 分开算（均仅 touchhold 之间，互不影响，也不与 touch 头判合并）：
        /// 头判 group 走 touchGroup 累计计数(多数通过带飞头判)，按下 group 走 touchHoldGroup 每帧重置计数(hold 期间多数按下)。
        /// </summary>
        private void ProcessTouchHoldGroups(int startIdx, int count)
        {
            if (count == 0) return;

            var sensors = new List<SensorType>(count);
            var idx = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (touchHolds[startIdx + i].isMine) continue;
                sensors.Add(touchHolds[startIdx + i].sensor);
                idx.Add(i);
            }

            // 头判 group：与 touch 头判分开，独立多数通过
            var headBuild = BuildTouchGroups(sensors, touchGroupTotalCounts, touchGroupJudgedCounts);
            // 按下 group：hold 期间多数按下
            var holdBuild = BuildTouchGroups(sensors, touchHoldGroupTotalCounts, touchHoldGroupPressedCounts);

            for (int k = 0; k < idx.Count; k++)
            {
                var t = touchHolds[startIdx + idx[k]];
                t.headGroupId = headBuild.MemberGroupIds[k];
                t.groupId = holdBuild.MemberGroupIds[k];
                touchHolds[startIdx + idx[k]] = t;
            }
        }

        private void CreateEachLine(float time, int startPosA, int startPosB, float speed, bool usingSV)
        {
            var startPos = startPosA;
            var endPos = startPosB;
            endPos -= startPos;
            if (endPos == 0) return;
            endPos = endPos < 0 ? endPos + 8 : endPos;
            endPos = endPos > 8 ? endPos - 8 : endPos;
            endPos++;

            if (endPos > 4)
            {
                startPos = startPosB;
                endPos = startPosA - startPosB;
                endPos = endPos < 0 ? endPos + 8 : endPos;
                endPos = endPos > 8 ? endPos - 8 : endPos;
                endPos++;
            }

            var el = new EachLineData
            {
                time = time,
                key = startPos - 1,
                curvLength = endPos - 1,
                hspeed = speed,
                usingSV = usingSV,

                isEnd = true
            };
            if (eachLines.Length > 0 && eachLines[^1].IsFoldable(el))
            {
                return;
            }
            el.Init();
            eachLines.Add(el);
        }

        private void LoadTap(
            in SimaiTiming timing,
            in SimaiNote note,
            ref int sameTapCount)
        {
            var key = (SensorType)(note.StartPos - 1);
            var tap = new TapData
            {
                Time = (float)timing.Time,
                Key = key,
                HSpeed = timing.HSpeed,
                ButtonOrderIndex = _buttonOrderIndex[(int)key]++,
                SensorOrderIndex = _sensorOrderIndex[(int)key]++,

                IsStar = note.IsStar,
                IsDouble = false,
                RotateSpeed = note.IsStarFakeRotate ? -3f : 0,    // (117.8)1-5[4:1] 的旋转速度

                IsEach = note.IsEach,
                IsEx = note.IsEx,
                IsBreak = note.IsBreak,
                IsMine = note.IsMine,
                IsIgnoreSV = note.IsIgnoreSV,

                IsEnd = true
            };
            if (note.CanBeFolded) sameTapCount++;
            if (sameTapCount > 3)
            {
                taps.ElementRef(taps.Length - 3).IsFolded = true;
            }
            tap.Init();
            taps.Add(tap);

            if (!note.IsMine)
                switch (NoteHelper.Settings.AutoPlayMode)
                {
                    case AutoPlayMode.DJAutoButton:
                        plays.Add(new DJAutoPlayData(
                            MajPos.RingPos(DJAUTO_BTN_DEFAULT_RADIUS, (int)tap.Key + 1, false),
                            DJAUTO_HAND_RADIUS,
                            tap.Time,
                            tap.Time + DJAUTO_TAP_RELEASE_TIME_SEC,
                            false));
                        break;
                    case AutoPlayMode.DJAutoSensor:
                        plays.Add(new DJAutoPlayData(
                            MajPos.GetSensorJudgePos(tap.Key),
                            DJAUTO_HAND_RADIUS,
                            tap.Time,
                            tap.Time + DJAUTO_TAP_RELEASE_TIME_SEC,
                            false));
                        break;
                }
        }

        private void LoadHold(
            in SimaiTiming timing,
            in SimaiNote note,
            ref int sameHoldCount)
        {
            var key = (SensorType)(note.StartPos - 1);
            var hold = new HoldData
            {
                time = (float)timing.Time,
                Key = key,
                hspeed = timing.HSpeed,
                LastFor = (float)note.Duration,
                ButtonOrderIndex = _buttonOrderIndex[(int)key]++,
                SensorOrderIndex = _sensorOrderIndex[(int)key]++,

                isEach = note.IsEach,
                isEx = note.IsEx,
                isBreak = note.IsBreak,
                isMine = note.IsMine,
                isIgnoreSV = note.IsIgnoreSV,

                isEnd = true
            };
            if (note.CanBeFolded) sameHoldCount++;
            if (sameHoldCount > 3)
            {
                holds.ElementRef(holds.Length - 3).isFolded = true;
            }
            hold.Init();
            holds.Add(hold);

            if (!note.IsMine)
                switch (NoteHelper.Settings.AutoPlayMode)
                {
                    case AutoPlayMode.DJAutoButton:
                        plays.Add(new DJAutoPlayData(
                            MajPos.RingPos(DJAUTO_BTN_DEFAULT_RADIUS, (int)hold.Key + 1, false),
                            DJAUTO_HAND_RADIUS,
                            hold.time,
                            hold.time + hold.LastFor + DJAUTO_HOLD_RELEASE_TIME_SEC,
                            false));
                        break;
                    case AutoPlayMode.DJAutoSensor:
                        plays.Add(new DJAutoPlayData(
                            MajPos.GetSensorJudgePos(hold.Key),
                            DJAUTO_HAND_RADIUS,
                            hold.time,
                            hold.time + hold.LastFor + DJAUTO_HOLD_RELEASE_TIME_SEC,
                            false));
                        break;
                }
        }

        private void LoadTouch(
            in SimaiTiming timing,
            in SimaiNote note,
            ref int sameTouchCount)
        {
            var sensor = GetSensor((char)note.TouchArea, note.StartPos);
            var touch = new TouchData
            {
                time = (float)timing.Time,
                sensor = sensor,
                hspeed = timing.HSpeed,
                sensorOrderIndex = _sensorOrderIndex[(int)sensor]++,

                isHanabi = note.IsHanabi,
                isEach = note.IsEach,
                isEx = note.IsEx,
                isBreak = note.IsBreak,
                isMine = note.IsMine,
                isIgnoreSV = note.IsIgnoreSV,

                isEnd = true
            };
            if (note.CanBeFolded) sameTouchCount++;
            if (sameTouchCount > 3)
            {
                touches.ElementRef(touches.Length - 3).isFolded = true;
            }
            touch.Init();
            touches.Add(touch);
            loadedTouches[(int)sensor].Add(new()
            {
                IsEach = note.IsEach,
                IsBreak = note.IsBreak,
                IsMine = note.IsMine
            });

            if (!note.IsMine &&
                NoteHelper.Settings.AutoPlayMode is AutoPlayMode.DJAutoButton or AutoPlayMode.DJAutoSensor)
                _djAutoTouchInfosThisTiming.Add(new DJAutoTouchInfo
                {
                    Sensor = touch.sensor,
                    Pos = touch.centerPos,
                    StartTime = touch.time,
                    EndTime = touch.time + DJAUTO_TOUCH_RELEASE_TIME_SEC,
                });
        }

        private void LoadTouchHold(
            in SimaiTiming timing,
            in SimaiNote note,
            ref int sameTouchHoldCount)
        {
            var sensor = GetSensor((char)note.TouchArea, note.StartPos);
            var th = new TouchHoldData
            {
                time = (float)timing.Time,
                sensor = sensor,
                hspeed = timing.HSpeed,
                sensorOrderIndex = _sensorOrderIndex[(int)sensor]++,
                LastFor = (float)note.Duration,

                isHanabi = note.IsHanabi,
                isEach = note.IsEach,
                isEx = note.IsEx,
                isBreak = note.IsBreak,
                isMine = note.IsMine,
                isIgnoreSV = note.IsIgnoreSV,

                isEnd = true
            };
            if (note.CanBeFolded) sameTouchHoldCount++;
            if (sameTouchHoldCount > 3)
            {
                touchHolds.ElementRef(touchHolds.Length - 3).isFolded = true;
            }
            th.Init();
            touchHolds.Add(th);

            if (!note.IsMine &&
                NoteHelper.Settings.AutoPlayMode is AutoPlayMode.DJAutoButton or AutoPlayMode.DJAutoSensor)
                _djAutoTouchInfosThisTiming.Add(new DJAutoTouchInfo
                {
                    Sensor = th.sensor,
                    Pos = th.centerPos,
                    StartTime = th.time,
                    EndTime = th.time + th.LastFor + DJAUTO_TOUCHHOLD_RELEASE_TIME_SEC,
                });
        }

        private ReadOnlySpan<byte> LoadSlideChain(
            in SimaiTiming timing,
            in SimaiNote note,
            ReadOnlySpan<byte> lastContent,
            ref int sameTapCount,
            ref int sameSlideCount,
            ref float loadedSlideLength,
            ref double loadedSlideTime)
        {
            var noteContent = note.SlideContent;

            SlideMetadata metadata;
            if (noteContent.Contains((byte)'w'))
            {
                metadata = SlideTableNeo.GetWifiSlide(Encoding.UTF8.GetString(noteContent[0..3]));

                var judgeQueueCount = metadata.JudgeAreaQueue.Length;
                loadedSlideAreaArrays.Add(metadata.JudgeAreaQueue);
                var judgeQueueLCount = metadata.JudgeAreaQueueL.Length;
                loadedSlideAreaArrays.Add(metadata.JudgeAreaQueueL);
                var judgeQueueRCount = metadata.JudgeAreaQueueR.Length;
                loadedSlideAreaArrays.Add(metadata.JudgeAreaQueueR);
                var slideArrowsCount = metadata.ArrowPoses.Length;
                loadedSlidePoseArrays.Add(metadata.ArrowPoses);

                var slide = new SlideData
                {
                    tapTime = (float)timing.Time,
                    shootTime = (float)(timing.Time + note.SlideShootDelay),
                    startPos = noteContent[0] - '0',
                    endPos = noteContent[2] - '0',
                    LastFor = (float)note.Duration,
                    hspeed = timing.HSpeed,

                    isWifi = true,

                    judgeQueueOffset = areaPoolIndex,
                    judgeQueueCount = judgeQueueCount,
                    judgeQueueLOffset = areaPoolIndex + judgeQueueCount,
                    judgeQueueLCount = judgeQueueLCount,
                    judgeQueueROffset = areaPoolIndex + judgeQueueCount + judgeQueueLCount,
                    judgeQueueRCount = judgeQueueRCount,
                    Const = metadata.SlideConst,
                    slideArrowsOffset = posePoolIndex,
                    slideArrowsCount = slideArrowsCount,
                    noLastArrow = metadata.ConditionalLastArrow,
                    okType = metadata.OkType,
                    okPose = metadata.OkPose,
                    unskippable1 = -1,
                    unskippable2 = -1,

                    isEach = note.IsEach,
                    isEx = false,
                    isBreak = note.IsBreak,
                    isMine = note.IsMine,

                    isEnd = true
                };
                ApplySlideFolding(ref slide, noteContent, lastContent, ref sameSlideCount);
                slide.Init();
                slides.Add(slide);

                if (!note.IsMine &&
                    NoteHelper.Settings.AutoPlayMode is AutoPlayMode.DJAutoButton or AutoPlayMode.DJAutoSensor &&
                    sameSlideCount == 1) // slide跟别人一样就不用鸟
                    unsafe
                    {
                        // wifi 双手：发射两个 play(side -1/+1)，FindNext 按偏移后中点就近分配，无强制左绑左右绑右
                        plays.Add(new DJAutoPlayData(
                            slides.Length - 1,
                            DJAUTO_WIFI_RADIUS,
                            slide.shootTime,
                            slide.shootTime + slide.LastFor,
                            isWifi: true,
                            wifiSide: -1));
                        plays.Add(new DJAutoPlayData(
                            slides.Length - 1,
                            DJAUTO_WIFI_RADIUS,
                            slide.shootTime,
                            slide.shootTime + slide.LastFor,
                            isWifi: true,
                            wifiSide: +1));
                    }

                areaPoolIndex += judgeQueueCount + judgeQueueLCount + judgeQueueRCount;
                posePoolIndex += slideArrowsCount;
            }
            else
            {
                var slideMetaDatas = GetSlidesFromRawContent(noteContent, out var startPos, out var endPos);
                metadata = slideMetaDatas.Count == 1 ? slideMetaDatas[0] : SlideTableNeo.MakeConnSlide(slideMetaDatas);

                var unskippable1 = -1;
                var unskippable2 = -1;
                switch (metadata.Flag)
                {
                    case SlideFlag.NormalV:
                        {
                            unskippable1 = 1;
                            break;
                        }
                    case SlideFlag.SpecialV:
                        {
                            unskippable1 = 1;
                            unskippable2 = 3;
                            break;
                        }
                    default:
                        {
                            if (metadata.JudgeAreaQueue.Length <= 3)
                                unskippable1 = metadata.JudgeAreaQueue.Length - 2;
                            break;
                        }
                }

                var judgeQueueCount = metadata.JudgeAreaQueue.Length;
                loadedSlideAreaArrays.Add(metadata.JudgeAreaQueue);
                var slideArrowsCount = metadata.ArrowPoses.Length;
                loadedSlidePoseArrays.Add(metadata.ArrowPoses);

                //ignore start/end pos
                var slide = new SlideData
                {
                    tapTime = (float)timing.Time,
                    shootTime = (float)(timing.Time + note.SlideShootDelay),
                    startPos = startPos,
                    endPos = endPos,
                    LastFor = (float)note.Duration,
                    hspeed = timing.HSpeed,

                    isWifi = false,

                    judgeQueueOffset = areaPoolIndex,
                    judgeQueueCount = judgeQueueCount,
                    Const = metadata.SlideConst,
                    slideArrowsOffset = posePoolIndex,
                    slideArrowsCount = slideArrowsCount,
                    noLastArrow = metadata.ConditionalLastArrow,
                    okType = metadata.OkType,
                    okPose = metadata.OkPose,
                    unskippable1 = unskippable1,
                    unskippable2 = unskippable2,

                    isEach = note.IsEach,
                    isEx = false,
                    isBreak = note.IsBreak,
                    isMine = note.IsMine,

                    isEnd = true
                };
                ApplySlideFolding(ref slide, noteContent, lastContent, ref sameSlideCount);
                slide.Init();
                slides.Add(slide);

                if (!note.IsMine &&
                    NoteHelper.Settings.AutoPlayMode is AutoPlayMode.DJAutoButton or AutoPlayMode.DJAutoSensor &&
                    sameSlideCount == 1) // slide跟别人一样就不用鸟
                    unsafe
                    {
                        plays.Add(new DJAutoPlayData(
                            slides.Length - 1,
                            DJAUTO_HAND_RADIUS,
                            slide.shootTime,
                            slide.shootTime + slide.LastFor,
                            false));
                    }

                areaPoolIndex += judgeQueueCount;
                posePoolIndex += slideArrowsCount;
            }

            loadedSlideLength += metadata.SlideLength;
            loadedSlideTime += note.Duration;

            return noteContent;
        }

        private void ApplySlideFolding(
            ref SlideData slide,
            ReadOnlySpan<byte> noteContent,
            ReadOnlySpan<byte> lastContent,
            ref int sameSlideCount)
        {
            var matchesPrevious =
                lastContent.SequenceEqual(noteContent) &&
                slides.Length > 0 &&
                slides[^1].IsFoldablePropOnly(slide);

            sameSlideCount = matchesPrevious ? sameSlideCount + 1 : 1;
            if (sameSlideCount > 3)
            {
                slides.ElementRef(slides.Length - 3).isFolded = true;
            }
        }

        private void OnNoteLoadFailed(SimaiNote note, Exception e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Note Load Failed] Exception: {e.Message}");
            if (e.InnerException != null)
            {
                sb.AppendLine($"  ---> Inner Exception: {e.InnerException.Message}");
            }
            sb.AppendLine($"  Stack Trace:");
            sb.AppendLine(e.StackTrace);
            sb.AppendLine("Note Properties:");
            try
            {
                foreach (var prop in typeof(SimaiNote).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    sb.AppendLine($"  {prop.Name}: {prop.GetValue(note)}");
                }
                foreach (var field in typeof(SimaiNote).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    sb.AppendLine($"  {field.Name}: {field.GetValue(note)}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  (Failed to reflect properties: {ex.Message})");
            }
            var errorMsg = sb.ToString();
            Debug.LogError(errorMsg);
            _wsServer.Error(errorMsg);
        }


        public void ResetLoadedNote(double ignore)
        {
            //重置一下sensor/button index
            MajBurst.InputData.ResetState();
            for (int i = 0; i < taps.Length; i++)
            {
                ref var tap = ref taps.ElementRef(i);
                if (tap.Time < ignore)
                {
                    tap.IsEnd = true;
                    MajBurst.InputData.NextTapHold(tap.Key);
                }
                else tap.Reset();
            }
            for (int i = 0; i < holds.Length; i++)
            {
                ref var hold = ref holds.ElementRef(i);
                if (hold.time < ignore)
                {
                    hold.isEnd = true;
                    MajBurst.InputData.NextTapHold(hold.Key);
                }
                else hold.Reset();
            }
            for (int i = 0; i < slides.Length; i++)
            {
                ref var slide = ref slides.ElementRef(i);
                if (slide.shootTime + slide.LastFor < ignore) slide.isEnd = true;
                else slide.Reset();
            }
            for (int i = 0; i < touches.Length; i++)
            {
                ref var touch = ref touches.ElementRef(i);
                if (touch.time < ignore)
                {
                    touch.isEnd = true;
                    MajBurst.InputData.NextTouch(touch.sensor);
                }
                else touch.Reset();
            }
            for (int i = 0; i < touchHolds.Length; i++)
            {
                ref var th = ref touchHolds.ElementRef(i);
                if (th.time < ignore)
                {
                    th.isEnd = true;
                    MajBurst.InputData.NextTouch(th.sensor);
                }
                else th.Reset();
            }
        }

        public void ResetLoadedPlay(double ignore)
        {
            for (int i = 0; i < plays.Length; i++)
            {
                ref var play = ref plays.ElementRef(i);
                play.IsHandClaimed = false;
            }
        }





        // ============== Slide shape detection ==============
        // TODO: 略显丑陋
        private static IList<SlideMetadata> GetSlidesFromRawContent(ReadOnlySpan<byte> rawContent,
            out int startPos, out int endPos)
        {
            startPos = endPos = rawContent[0] - '0';
            var slideMetadatas = new List<SlideMetadata>(rawContent.Length / 2);

            int lastKey = -1;
            ReadOnlySpan<byte> lastShape = ReadOnlySpan<byte>.Empty;
            bool isSlideCode = false;
            for (var i = 0; i < rawContent.Length; i++)
            {
                var c = rawContent[i];

                if (c is (byte)'[')
                {
                    var endIdx = rawContent[i..].IndexOf((byte)']');
                    if (endIdx == -1) return slideMetadatas;

                    i += endIdx;
                    continue;
                }

                if (c is >= (byte)'0' and <= (byte)'9')
                {
                    if (isSlideCode)
                    {
                        var curKey = c - '0';
                        if (lastKey != -1 && lastShape != ReadOnlySpan<byte>.Empty)
                        {
                            var shape = $"{lastKey}{Encoding.UTF8.GetString(lastShape)}{curKey}";
                            slideMetadatas.Add(SlideTableNeo.MakeCustomSlide(shape));
                            lastShape = ReadOnlySpan<byte>.Empty;
                        }
                        lastKey = curKey;
                        endPos = curKey;
                        isSlideCode = false;
                    }
                    else if (lastShape.Length == 1 && lastShape[0] == 'V')
                    {
                        if (i + 1 >= rawContent.Length)
                            return slideMetadatas;
                        var VKey = c - '0';
                        var curKey = rawContent[i + 1] - '0';
                        i++;
                        if (lastKey != -1 && lastShape != ReadOnlySpan<byte>.Empty)
                        {
                            var shape = $"{lastKey}{Encoding.UTF8.GetString(lastShape)}{VKey}{curKey}";
                            slideMetadatas.Add(SlideTableNeo.GetStandardSlide(shape));
                            lastShape = ReadOnlySpan<byte>.Empty;
                        }
                        lastKey = curKey;
                        endPos = curKey;
                    }
                    else
                    {
                        var curKey = c - '0';
                        if (lastKey != -1 && !lastShape.IsEmpty)
                        {
                            if (lastShape.Length == 1 && lastShape[0] == '^')
                                lastShape = new(new byte[] { TranslateAutoSlide(lastKey, curKey) });
                            var shape = $"{lastKey}{Encoding.UTF8.GetString(lastShape)}{curKey}";
                            slideMetadatas.Add(SlideTableNeo.GetStandardSlide(shape));
                            lastShape = ReadOnlySpan<byte>.Empty;
                        }
                        lastKey = curKey;
                    }
                }
                else if (c is (byte)'>' or (byte)'<' or (byte)'^' or (byte)'v' or (byte)'-' or (byte)'V' or (byte)'s' or (byte)'z')
                {
                    lastShape = rawContent[i..(i + 1)];
                }
                else if (c is (byte)'p' or (byte)'q')
                {
                    if (i + 1 < rawContent.Length && rawContent[i + 1] == c)
                    {
                        lastShape = rawContent[i..(i + 2)];
                        i++;
                    }
                    else
                    {
                        lastShape = rawContent[i..(i + 1)];
                    }
                }
                else if (SlideCodeParser.CommandChars.Contains((char)c))
                {
                    var endIdx = rawContent[i..].IndexOf((byte)'K');
                    if (endIdx == -1)
                        return slideMetadatas;

                    endIdx += i;
                    lastShape = rawContent[i..(endIdx + 1)];
                    isSlideCode = true;
                    i = endIdx;
                }
            }

            return slideMetadatas;


            static byte TranslateAutoSlide(int from, int to)
            {
                int cw = (to - from + 8) % 8;   // 顺时针距离
                int ccw = (from - to + 8) % 8;  // 逆时针距离

                if (from is 1 or 2 or 7 or 8)
                {
                    if (cw < ccw)
                        return (byte)'>';
                    else if (ccw < cw)
                        return (byte)'<';
                }
                else if (from is 3 or 4 or 5 or 6)
                {
                    if (cw < ccw)
                        return (byte)'<';
                    else if (ccw < cw)
                        return (byte)'>';
                }

                throw new Exception("CNM");
            }
        }
    }
}