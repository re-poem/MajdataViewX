using Cysharp.Threading.Tasks;
using MajSimai;
using Notes.SlideUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static MajCtx;

public partial class NoteManager
{
    public float NoteSpeed = 7f;
    public float TouchSpeed = 7.5f;
    public bool LegacySlideLayer = false;
    public bool SmoothSlideAnime = true;
    public bool MineAutoSlide = true;

    public double Ignore = 0f;

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
        if (chart.IsEmpty) return;
        ConfigureRenderCapacity(chart);

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

        foreach (var timing in chart.NoteTimings)
        {
            if (timing.Timing < Ignore)
                LoadIgnore(timing);
            else
                LoadTiming(timing);
        }

        MarkSlideGuideNotes();

        slideAreaPool = (SlideArea*)UnsafeUtility.Malloc(
            areaPoolIndex * sizeof(SlideArea),
            16, Allocator.Persistent);
        slidePosePool = (SlidePose*)UnsafeUtility.Malloc(
            posePoolIndex * sizeof(SlidePose),
            16, Allocator.Persistent);

        for (var i = 0; i < slides.Length; i++)
        {
            var slide = slides[i];
            slide.judgeQueue = slideAreaPool + slide.judgeQueueOffset;
            slide.judgeQueueL = slideAreaPool + slide.judgeQueueLOffset;
            slide.judgeQueueR = slideAreaPool + slide.judgeQueueROffset;
            slide.slideArrows = slidePosePool + slide.slideArrowsOffset;
            slides[i] = slide;
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

        MajBurst.MultTouchHandler.Load(loadedTouches);
    }

    private static int GetSlideGuideTimeKey(float timing) =>
        (int)math.round(timing * 1000f);

    private void MarkSlideGuideNotes()
    {
        var tapLookup = new Dictionary<(int Time, SensorType Sensor), List<int>>();
        var touchLookup = new Dictionary<(int Time, SensorType Sensor), List<int>>();

        for (int i = 0; i < taps.Length; i++)
        {
            var tap = taps[i];
            if (tap.IsMine) continue;

            var key = (GetSlideGuideTimeKey(tap.Time), tap.Key);
            if (!tapLookup.TryGetValue(key, out var indices))
                tapLookup.Add(key, indices = new List<int>());
            indices.Add(i);
        }

        for (int i = 0; i < touches.Length; i++)
        {
            var touch = touches[i];
            if (touch.isMine) continue;

            var key = (GetSlideGuideTimeKey(touch.time), touch.sensor);
            if (!touchLookup.TryGetValue(key, out var indices))
                touchLookup.Add(key, indices = new List<int>());
            indices.Add(i);
        }

        for (int i = 0; i < slides.Length; i++)
        {
            var slide = slides[i];
            if (slide.isMine) continue;

            var sensor = (SensorType)(slide.startPos - 1);
            var key = (GetSlideGuideTimeKey(slide.shootTime), sensor);

            if (tapLookup.TryGetValue(key, out var tapIndices))
            {
                slide.hasSlideGuide = true;
                slide.hasTapGuide = true;
                foreach (var tapIndex in tapIndices)
                {
                    var tap = taps[tapIndex];
                    tap.IsSlideGuide = true;
                    taps[tapIndex] = tap;
                }
            }

            if (touchLookup.TryGetValue(key, out var touchIndices))
            {
                slide.hasSlideGuide = true;
                foreach (var touchIndex in touchIndices)
                {
                    var touch = touches[touchIndex];
                    touch.isSlideGuide = true;
                    touches[touchIndex] = touch;
                }
            }

            slides[i] = slide;
        }
    }
    private void LoadIgnore(in SimaiTimingPoint timing)
    {
        var holdLength = 0d;
        foreach (var note in timing.Notes)
        {
            if (note.HoldTime > holdLength)
                holdLength = note.HoldTime;

            if (note.SlideStartTime + note.SlideTime > Ignore)
            {
                LoadTiming(timing);
                return;
            }
        }

        if (timing.Timing + holdLength > Ignore)
        {
            LoadTiming(timing);
            return;
        }

        _objectCounter.CountIgnoreNoteCountAsync(timing.Notes);
    }

    private void CalcEach(in SimaiTimingPoint timing, out bool isNoteEach, out bool isSlideEach)
    {
        var noteCount = 0;
        var slideCount = 0;

        foreach (var o in timing.Notes)
        {
            if (!o.IsMine)
            {
                if (o.Type == SimaiNoteType.Slide)
                {
                    if (!o.IsSlideNoHead)
                        noteCount++;
                }
                else
                {
                    noteCount++;
                }
            }

            if (o.Type == SimaiNoteType.Slide && !o.IsMineSlide)
            {
                slideCount++;
            }
        }

        isNoteEach = noteCount > 1;
        isSlideEach = slideCount > 1;
    }

    protected virtual void OnNoteLoadFailed(SimaiNote note, Exception e)
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

    private unsafe void LoadTiming(in SimaiTimingPoint timing)
    {
        int touchStartIdx = touches.Length;
        int touchHoldStartIdx = touchHolds.Length;

        CalcEach(timing, out var isNoteEach, out var isSlideEach);

        var nonMineCount = 0;
        var startPositions = stackalloc int[timing.Notes.Length];
        bool eachLineUsingSV = false;
        string lastSlideContent = string.Empty;
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
                    case SimaiNoteType.Tap:
                        LoadTap(timing, note, isNoteEach, ref sameTapCount);
                        if (!note.IsMine)
                        {
                            eachLineUsingSV |= note.UsingSV;
                            startPositions[nonMineCount++] = note.StartPosition;
                        }
                        break;
                    case SimaiNoteType.Hold:
                        LoadHold(timing, note, isNoteEach, ref sameHoldCount);
                        if (!note.IsMine)
                        {
                            eachLineUsingSV |= note.UsingSV;
                            startPositions[nonMineCount++] = note.StartPosition;
                        }
                        break;
                    case SimaiNoteType.Touch:
                        LoadTouch(timing, note, isNoteEach, ref sameTouchCount);
                        break;
                    case SimaiNoteType.TouchHold:
                        LoadTouchHold(timing, note, isNoteEach, ref sameTouchHoldCount);
                        break;
                    case SimaiNoteType.Slide:
                        lastSlideContent = LoadSlideChain(
                            timing,
                            note,
                            isNoteEach,
                            isSlideEach,
                            lastSlideContent,
                            ref sameTapCount,
                            ref sameSlideCount);
                        if (!note.IsMine && !note.IsSlideNoHead)
                            startPositions[nonMineCount++] = note.StartPosition;
                        break;
                }
            }
            catch (Exception e)
            {
                OnNoteLoadFailed(note, e);
            }
        }

        if (nonMineCount > 1)
        {
            for (int i = 0; i < nonMineCount - 1; i++)
            {
                var s = (float)timing.Timing;
                var spd = NoteSpeed * timing.HSpeed;
                CreateEachLine(s, startPositions[i], startPositions[i + 1], spd, eachLineUsingSV);
            }
        }

        int touchCount = touches.Length - touchStartIdx;
        if (touchCount > 0)
        {
            ProcessTouchGroups(touchStartIdx, touchCount);
        }

        int thCount = touchHolds.Length - touchHoldStartIdx;
        if (thCount > 0)
        {
            ProcessTouchHoldGroups(touchHoldStartIdx, thCount);
        }
    }

    private void ProcessTouchGroups(int startIdx, int count)
    {
        if (count == 0) return;

        List<int> uniqueIndices = new List<int>();
        int[] originalToUnique = new int[count];
        for (int i = 0; i < count; i++)
        {
            int foundUnique = -1;
            for (int j = 0; j < uniqueIndices.Count; j++)
            {
                if (touches[startIdx + i].sensor == touches[startIdx + uniqueIndices[j]].sensor)
                {
                    foundUnique = j;
                    break;
                }
            }
            if (foundUnique != -1)
            {
                originalToUnique[i] = foundUnique;
            }
            else
            {
                originalToUnique[i] = uniqueIndices.Count;
                uniqueIndices.Add(i);
            }
        }

        int uniqueCount = uniqueIndices.Count;
        bool[] visited = new bool[uniqueCount];
        var groups = new List<Group>();
        var uniqueGroupIds = new int[uniqueCount];
        for (int i = 0; i < uniqueCount; i++) uniqueGroupIds[i] = -1;

        for (int i = 0; i < uniqueCount; i++)
        {
            if (visited[i]) continue;

            // Find connected component
            List<int> component = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                component.Add(curr);

                for (int j = 0; j < uniqueCount; j++)
                {
                    if (visited[j]) continue;

                    SensorType s1 = touches[startIdx + uniqueIndices[curr]].sensor;
                    SensorType s2 = touches[startIdx + uniqueIndices[j]].sensor;

                    if (TouchGroupManager.TOUCH_GROUPS.TryGetValue(s1, out var adj) && adj.Contains(s2))
                    {
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            if (component.Count >= 5)
            {
                int groupId = touchGroupTotalCounts.Length;
                int totalTouchesInGroup = 0;
                for (int c = 0; c < count; c++)
                {
                    if (component.Contains(originalToUnique[c]))
                        totalTouchesInGroup++;
                }

                touchGroupTotalCounts.Add(totalTouchesInGroup);
                touchGroupJudgedCounts.Add(0);

                var groupDef = new Group { PointIndices = new int[component.Count] };

                for (int k = 0; k < component.Count; k++)
                {
                    int uIdx = component[k];
                    uniqueGroupIds[uIdx] = groupId;
                    groupDef.PointIndices[k] = uIdx;
                }

                groups.Add(groupDef);
            }
        }

        for (int i = 0; i < count; i++)
        {
            int uIdx = originalToUnique[i];
            if (uniqueGroupIds[uIdx] != -1)
            {
                var t = touches[startIdx + i];
                t.groupId = uniqueGroupIds[uIdx];
                touches[startIdx + i] = t;
            }
        }

        // Solve Coverage for UNIQUE touches in this cluster
        var points = new float2[uniqueCount];
        var pointRadii = new float[uniqueCount];
        for (int i = 0; i < uniqueCount; i++)
        {
            var sensor = touches[startIdx + uniqueIndices[i]].sensor;
            points[i] = MajPos.GetSensorWorldPos(sensor);
            pointRadii[i] = MajPos.GetSensorRadius(sensor);
        }

        var solverResult = CoverageSolver.Solve(points, pointRadii, groups, allowSlide: true);

        int coverageId = touchGroupCoverResults.Length;
        touchGroupCoverResults.Add(solverResult);

        for (int i = 0; i < count; i++)
        {
            var t = touches[startIdx + i];
            t.coverageId = coverageId;
            touches[startIdx + i] = t;
        }
    }

    private void ProcessTouchHoldGroups(int startIdx, int count)
    {
        if (count == 0) return;

        List<int> uniqueIndices = new List<int>();
        int[] originalToUnique = new int[count];
        for (int i = 0; i < count; i++)
        {
            int foundUnique = -1;
            for (int j = 0; j < uniqueIndices.Count; j++)
            {
                if (touchHolds[startIdx + i].sensor == touchHolds[startIdx + uniqueIndices[j]].sensor)
                {
                    foundUnique = j;
                    break;
                }
            }
            if (foundUnique != -1)
            {
                originalToUnique[i] = foundUnique;
            }
            else
            {
                originalToUnique[i] = uniqueIndices.Count;
                uniqueIndices.Add(i);
            }
        }

        int uniqueCount = uniqueIndices.Count;
        bool[] visited = new bool[uniqueCount];
        var groups = new List<Group>();
        var uniqueGroupIds = new int[uniqueCount];
        for (int i = 0; i < uniqueCount; i++) uniqueGroupIds[i] = -1;

        for (int i = 0; i < uniqueCount; i++)
        {
            if (visited[i]) continue;

            List<int> component = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                component.Add(curr);

                for (int j = 0; j < uniqueCount; j++)
                {
                    if (visited[j]) continue;

                    SensorType s1 = touchHolds[startIdx + uniqueIndices[curr]].sensor;
                    SensorType s2 = touchHolds[startIdx + uniqueIndices[j]].sensor;

                    if (TouchGroupManager.TOUCH_GROUPS.TryGetValue(s1, out var adj) && adj.Contains(s2))
                    {
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            if (component.Count >= 5)
            {
                int groupId = touchHoldGroupTotalCounts.Length;
                int totalTouchesInGroup = 0;
                for (int c = 0; c < count; c++)
                {
                    if (component.Contains(originalToUnique[c]))
                        totalTouchesInGroup++;
                }

                touchHoldGroupTotalCounts.Add(totalTouchesInGroup);
                touchHoldGroupPressedCounts.Add(0);

                var groupDef = new Group { PointIndices = new int[component.Count] };

                for (int k = 0; k < component.Count; k++)
                {
                    int uIdx = component[k];
                    uniqueGroupIds[uIdx] = groupId;
                    groupDef.PointIndices[k] = uIdx;
                }

                groups.Add(groupDef);
            }
        }

        for (int i = 0; i < count; i++)
        {
            int uIdx = originalToUnique[i];
            if (uniqueGroupIds[uIdx] != -1)
            {
                var t = touchHolds[startIdx + i];
                t.groupId = uniqueGroupIds[uIdx];
                touchHolds[startIdx + i] = t;
            }
        }

        // Solve Coverage for UNIQUE touch holds in this cluster
        var points = new float2[uniqueCount];
        var pointRadii = new float[uniqueCount];
        for (int i = 0; i < uniqueCount; i++)
        {
            var sensor = touchHolds[startIdx + uniqueIndices[i]].sensor;
            points[i] = MajPos.GetSensorWorldPos(sensor);
            pointRadii[i] = MajPos.GetSensorRadius(sensor);
        }

        var solverResult = CoverageSolver.Solve(points, pointRadii, groups);

        int coverageId = touchHoldGroupCoverResults.Length;
        touchHoldGroupCoverResults.Add(solverResult);

        for (int i = 0; i < count; i++)
        {
            var t = touchHolds[startIdx + i];
            t.coverageId = coverageId;
            touchHolds[startIdx + i] = t;
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
            speed = speed,
            usingSV = usingSV
        };
        if (eachLines.Length > 0 && eachLines[^1].IsFoldable(el))
        {
            return;
        }
        el.Init();
        eachLines.Add(el);
    }

    private void LoadTap(
        in SimaiTimingPoint timing,
        in SimaiNote note,
        bool isEach,
        ref int sameTapCount)
    {
        var key = (SensorType)(note.StartPosition - 1);
        var tap = new TapData
        {
            Time = (float)timing.Timing,
            Key = key,
            Speed = NoteSpeed * timing.HSpeed,
            ButtonOrderIndex = _buttonOrderIndex[(int)key]++,
            SensorOrderIndex = _sensorOrderIndex[(int)key]++,

            IsStar = note.IsForceStar,
            IsDouble = false,
            RotateSpeed = note.IsFakeRotate ? -3f : 0,    // (117.8)1-5[4:1] 的旋转速度

            IsEach = isEach,
            IsEx = note.IsEx,
            IsBreak = note.IsBreak,
            IsMine = note.IsMine,
            UsingSV = note.UsingSV
        };
        sameTapCount = taps.Length > 0 && taps[^1].IsFoldable(tap)
            ? sameTapCount + 1
            : 1;
        if (sameTapCount > 3)
        {
            taps.ElementRef(taps.Length - 3).IsFolded = true;
        }
        tap.Init();
        taps.Add(tap);
    }

    private void LoadHold(
        in SimaiTimingPoint timing,
        in SimaiNote note,
        bool isEach,
        ref int sameHoldCount)
    {
        var key = (SensorType)(note.StartPosition - 1);
        var hold = new HoldData
        {
            time = (float)timing.Timing,
            Key = key,
            speed = NoteSpeed * timing.HSpeed,
            LastFor = (float)note.HoldTime,
            ButtonOrderIndex = _buttonOrderIndex[(int)key]++,
            SensorOrderIndex = _sensorOrderIndex[(int)key]++,

            isEach = isEach,
            isEx = note.IsEx,
            isBreak = note.IsBreak,
            isMine = note.IsMine,
            usingSV = note.UsingSV
        };
        sameHoldCount = holds.Length > 0 && holds[^1].IsFoldable(hold)
            ? sameHoldCount + 1
            : 1;
        if (sameHoldCount > 3)
        {
            holds.ElementRef(holds.Length - 3).isFolded = true;
        }
        hold.Init();
        holds.Add(hold);
    }

    private void LoadTouch(
        in SimaiTimingPoint timing,
        in SimaiNote note,
        bool isEach,
        ref int sameTouchCount)
    {
        var sensor = GetSensor(note.TouchArea, note.StartPosition);
        var touch = new TouchData
        {
            time = (float)timing.Timing,
            sensor = sensor,
            speed = TouchSpeed * timing.HSpeed,
            sensorOrderIndex = _sensorOrderIndex[(int)sensor]++,

            isHanabi = note.IsHanabi,
            isEach = isEach,
            isEx = note.IsEx,
            isBreak = note.IsBreak,
            isMine = note.IsMine,
            usingSV = note.UsingSV
        };
        sameTouchCount = touches.Length > 0 && touches[^1].IsFoldable(touch)
            ? sameTouchCount + 1
            : 1;
        if (sameTouchCount > 3)
        {
            touches.ElementRef(touches.Length - 3).isFolded = true;
        }
        touch.Init();
        touches.Add(touch);
        loadedTouches[(int)sensor].Add(new()
        {
            IsEach = isEach,
            IsBreak = note.IsBreak,
            IsMine = note.IsMine
        });
    }

    private void LoadTouchHold(
        in SimaiTimingPoint timing,
        in SimaiNote note,
        bool isEach,
        ref int sameTouchHoldCount)
    {
        var sensor = GetSensor(note.TouchArea, note.StartPosition);
        var th = new TouchHoldData
        {
            time = (float)timing.Timing,
            sensor = sensor,
            speed = TouchSpeed * timing.HSpeed,
            sensorOrderIndex = _sensorOrderIndex[(int)sensor]++,
            LastFor = (float)note.HoldTime,

            isHanabi = note.IsHanabi,
            isEach = isEach,
            isEx = note.IsEx,
            isBreak = note.IsBreak,
            isMine = note.IsMine,
            usingSV = note.UsingSV
        };
        sameTouchHoldCount = touchHolds.Length > 0 && touchHolds[^1].IsFoldable(th)
            ? sameTouchHoldCount + 1
            : 1;
        if (sameTouchHoldCount > 3)
        {
            touchHolds.ElementRef(touchHolds.Length - 3).isFolded = true;
        }
        th.Init();
        touchHolds.Add(th);
    }

    private string LoadSlideChain(
        in SimaiTimingPoint timing,
        in SimaiNote note,
        bool isNoteEach,
        bool isSlideEach,
        string lastContent,
        ref int sameTapCount,
        ref int sameSlideCount)
    {
        var noteContent = note.RawContent;

        if (!note.IsSlideNoHead)
        {
            // 统计与当前 slide 同头的所有 slide 的总长和总时间
            var length = 0.0f;
            var time = 0.0D;
            var cnt = 0;

            // 有点丑陋，但能用
            // 考虑到这个 method 本来就套在一层遍历里，总之是多了很多不必要的遍历
            // TODO:使之不丑陋
            foreach (var sn in timing.Notes)
            {
                if (sn.Type != SimaiNoteType.Slide) continue;
                if (sn.IsMineSlide) continue;
                if (sn.StartPosition != note.StartPosition) continue;

                cnt++;
                var ct = note.RawContent;
                var meta = ct.Contains('w')
                    ? SlideTableNeo.GetWifiSlide(ct[..3])
                    : SlideTableNeo.MakeConnSlide(GetSlidesFromRawContent(ct, out _, out _));
                length += meta.SlideLength;
                time += sn.SlideTime;
            }
            // 注意上面的遍历也把当前正在处理的 slide 遍历到了
            var isDouble = cnt >= 2;

            // RotateSpeed = 1 时是每秒转 180 度
            // 官机算法是 转速 = 同头星星总长 / (总时间 * 15 * pi)
            // 长度单位像素，时间单位ms，转速单位度/帧，转速最大是 18
            // 这里 SlideLength 是 100ppu，SlideTime 是秒
            var rotateSpeed = math.min(6f, length / ((float)time * 2 * math.PI));

            var starTap = new TapData
            {
                Time = (float)timing.Timing,
                Key = (SensorType)(note.StartPosition - 1),
                Speed = NoteSpeed * timing.HSpeed,
                ButtonOrderIndex = _buttonOrderIndex[note.StartPosition - 1]++,
                SensorOrderIndex = _sensorOrderIndex[note.StartPosition - 1]++,
                IsStar = true,
                IsDouble = isDouble,
                RotateSpeed = rotateSpeed,
                IsEach = isNoteEach,
                IsEx = note.IsEx,
                IsBreak = note.IsBreak,
                IsMine = note.IsMine,
                UsingSV = note.UsingSV,
            };
            sameTapCount = taps.Length > 0 && taps[^1].IsFoldable(starTap)
                ? sameTapCount + 1
                : 1;
            if (sameTapCount > 3)
            {
                taps.ElementRef(taps.Length - 3).IsFolded = true;
            }
            starTap.Init();
            taps.Add(starTap);
        }

        if (noteContent.Contains('w'))
        {
            var metadata = SlideTableNeo.GetWifiSlide(noteContent[0..3]);

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
                tapTime = (float)timing.Timing,
                shootTime = (float)note.SlideStartTime,
                startPos = noteContent[0] - '0',
                endPos = noteContent[2] - '0',
                LastFor = (float)note.SlideTime,
                speed = NoteSpeed * timing.HSpeed,
                sensorOrderIndex = _sensorOrderIndex[note.StartPosition - 1],

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

                isEach = isSlideEach,
                isEx = false,
                isBreak = note.IsSlideBreak,
                isMine = note.IsMineSlide,
                usingSV = note.UsingSV,
                smoothSlideAnime = SmoothSlideAnime,
                legacySlideLayer = LegacySlideLayer,
                mineAutoSlide = MineAutoSlide,
            };
            ApplySlideFolding(ref slide, noteContent, lastContent, ref sameSlideCount);
            slide.Init();
            slides.Add(slide);

            areaPoolIndex += judgeQueueCount + judgeQueueLCount + judgeQueueRCount;
            posePoolIndex += slideArrowsCount;
        }
        else
        {
            var slideMetaDatas = GetSlidesFromRawContent(noteContent, out var startPos, out var endPos);
            var metadata = slideMetaDatas.Count == 1 ? slideMetaDatas[0] : SlideTableNeo.MakeConnSlide(slideMetaDatas);

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
                tapTime = (float)timing.Timing,
                shootTime = (float)note.SlideStartTime,
                startPos = startPos,
                endPos = endPos,
                LastFor = (float)note.SlideTime,
                speed = NoteSpeed * timing.HSpeed,
                sensorOrderIndex = _sensorOrderIndex[note.StartPosition - 1],

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

                isEach = isSlideEach,
                isEx = false,
                isBreak = note.IsSlideBreak,
                isMine = note.IsMineSlide,
                usingSV = note.UsingSV,
                smoothSlideAnime = SmoothSlideAnime,
                legacySlideLayer = LegacySlideLayer,
                mineAutoSlide = MineAutoSlide,
            };
            ApplySlideFolding(ref slide, noteContent, lastContent, ref sameSlideCount);
            slide.Init();
            slides.Add(slide);

            areaPoolIndex += judgeQueueCount;
            posePoolIndex += slideArrowsCount;
        }
        return noteContent;
    }

    private void ApplySlideFolding(
        ref SlideData slide,
        string noteContent,
        string lastContent,
        ref int sameSlideCount)
    {
        var matchesPrevious =
            lastContent == noteContent &&
            slides.Length > 0 &&
            slides[^1].IsFoldablePropOnly(slide);

        sameSlideCount = matchesPrevious ? sameSlideCount + 1 : 1;
        if (sameSlideCount > 3)
        {
            slides.ElementRef(slides.Length - 3).isFolded = true;
        }
    }

    // ============== Slide shape detection ==============
    public static string RemoveBracketContent(string s)
    {
        var sb = new StringBuilder(s.Length);
        int depth = 0;

        foreach (var c in s)
        {
            if (c == '[')
            {
                depth++;
                continue;
            }

            if (c == ']')
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (depth == 0)
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static IList<SlideMetadata> GetSlidesFromRawContent(ReadOnlySpan<char> rawContent,
        out int startPos, out int endPos)
    {
        startPos = endPos = rawContent[0] - '0';
        var slideMetadatas = new List<SlideMetadata>(rawContent.Length / 2);

        int lastKey = -1;
        ReadOnlySpan<char> lastShape = string.Empty;
        bool isSlideCode = false;
        for (var i = 0; i < rawContent.Length; i++)
        {
            var c = rawContent[i];

            if (c is '[')
            {
                var endIdx = rawContent[i..].IndexOf(']');
                if (endIdx == -1) return slideMetadatas;

                i += endIdx;
                continue;
            }

            if (c is >= '0' and <= '8')
            {
                if (isSlideCode)
                {
                    var curKey = c - '0';
                    if (lastKey != -1 && lastShape != string.Empty)
                    {
                        var shape = $"{lastKey}{lastShape.ToString()}{curKey}";
                        slideMetadatas.Add(SlideTableNeo.GetCustomSlide(shape));
                        lastShape = string.Empty;
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
                    if (lastKey != -1 && lastShape != string.Empty)
                    {
                        var shape = $"{lastKey}{lastShape.ToString()}{VKey}{curKey}";
                        slideMetadatas.Add(SlideTableNeo.GetStandardSlide(shape));
                        lastShape = string.Empty;
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
                            lastShape = TranslateAutoSlide(lastKey, curKey);
                        var shape = $"{lastKey}{lastShape.ToString()}{curKey}";
                        slideMetadatas.Add(SlideTableNeo.GetStandardSlide(shape));
                        lastShape = string.Empty;
                    }
                    lastKey = curKey;
                }
            }
            else if (c is '>' or '<' or '^' or 'v' or '-' or 'V' or 's' or 'z')
            {
                lastShape = c.ToString();
            }
            else if (c is 'p' or 'q')
            {
                if (i + 1 < rawContent.Length && rawContent[i + 1] == c)
                {
                    lastShape = new string(c, 2);
                    i++;
                }
                else
                {
                    lastShape = c.ToString();
                }
            }
            else if (SlideCodeParser.CommandChars.Contains(c))
            {
                var endIdx = rawContent[i..].IndexOf('K');
                if (endIdx == -1)
                    return slideMetadatas;

                endIdx += i;
                lastShape = rawContent[i..(endIdx + 1)];
                isSlideCode = true;
                i = endIdx;
            }
        }

        return slideMetadatas;


        static string TranslateAutoSlide(int from, int to)
        {
            int cw = (to - from + 8) % 8;   // 顺时针距离
            int ccw = (from - to + 8) % 8;  // 逆时针距离

            if (from is 1 or 2 or 7 or 8)
            {
                if (cw < ccw)
                    return ">";
                else if (ccw < cw)
                    return "<";
            }
            else if (from is 3 or 4 or 5 or 6)
            {
                if (cw < ccw)
                    return "<";
                else if (ccw < cw)
                    return ">";
            }

            throw new Exception("CNM");
        }
    }
}
