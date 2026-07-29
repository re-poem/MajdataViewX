using MajdataViewX.Base;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Notes.SlideUtils
{
    /// <summary>
    /// slide 判定段信息
    /// </summary>
    public readonly struct SlideArea
    {
        /// <summary>
        /// 判定段按下以后应当消除的箭头总数，起点已被扣除，最后一个区会比实际箭头数多
        /// </summary>
        public readonly int ArrowProgressPush;

        /// <summary>
        /// 判定段完成以后应当消除的箭头总数，起点已被扣除，最后一个区会比实际箭头数多
        /// </summary>
        public readonly int ArrowProgressFinish;

        /// <summary>
        /// 判定段包含的第一个判定区
        /// </summary>
        public readonly SensorType SensorA;

        /// <summary>
        /// 判定段包含的第二个判定区
        /// </summary>
        public readonly SensorType SensorB;

        public SlideArea(int progressPush, int progressFinish, SensorType sensorA, SensorType sensorB)
        {
            ArrowProgressPush = progressPush;
            ArrowProgressFinish = progressFinish;
            SensorA = sensorA;
            SensorB = sensorB;
        }
    }

    /// <summary>
    /// slide 箭头信息
    /// </summary>
    public readonly struct SlidePose
    {
        public readonly float X, Y, RotZ, L;

        public SlidePose(float x, float y, float rotZ, float l)
        {
            X = x;
            Y = y;
            RotZ = rotZ;
            L = l;
        }
    }

    public enum SlideOkType
    {
        StraightL,
        StraightR,
        CircleL,
        CircleR,
        WifiU,
        WifiD,
    }

    public enum SlideFlag
    {
        None,
        NormalV,
        SpecialV
    }

    public readonly struct SlideMetadata
    {
        /// <summary>slide 最后一个区占全长的比例，0~1 间的小数</summary>
        public readonly float SlideConst;
        /// <summary>slide 路径全长，单位取决于<c>MajGeometry.MainRadius</c></summary>
        public readonly float SlideLength;
        /// <summary>这个参数为 true 表示最后一个箭头距离终点太近，只在 conn-slide 的非最终段显示</summary>
        public readonly bool ConditionalLastArrow;
        /// <summary>用来标记是不是大V</summary>
        public readonly SlideFlag Flag;
        /// <summary>判定队列，若为 Wifi 则表示中间一支的判定队列</summary>
        public readonly SlideArea[] JudgeAreaQueue;

        /// <summary>
        /// <para>slide 每个箭头的位置与方向</para>
        /// <para>注意数组中包含了路径起点和终点，真正应该显示的箭头需要去掉这两项
        /// （对<c>ConditionalLastArrow = true</c>的情况还要视情况去掉倒数第二项）</para>
        /// <para>普通 slide 可以直接拿这个数组来更新引导星星，Wifi 就别用这个了</para>
        /// </summary>
        public readonly SlidePose[] ArrowPoses;

        /// <summary>slideOK 的位置与方向，以图片中点为锚点。
        /// 请注意素材的尺寸：直线和圆弧形 slideOK 为 410x140，Wifi 为 668x200</summary>
        public readonly SlidePose OkPose;
        /// <summary>slideOK 的种类，告诉你该用哪张素材</summary>
        public readonly SlideOkType OkType;

        /// <summary>Wifi 左边一支的判定队列（终点在中间支的顺时针方向）</summary>
        public readonly SlideArea[] JudgeAreaQueueL;
        /// <summary>Wifi 右边一支的判定队列（终点在中间支的逆时针方向）</summary>
        public readonly SlideArea[] JudgeAreaQueueR;

        public SlideMetadata(
            SlideArea[] judgeAreaQueue,
            float slideConst,
            float slideLength,
            SlidePose[] arrowPoses,
            bool conditionalLastArrow,
            SlidePose okPose,
            SlideOkType okType,
            SlideFlag flag
            )
        {
            SlideConst = slideConst;
            SlideLength = slideLength;
            JudgeAreaQueue = judgeAreaQueue;
            ArrowPoses = arrowPoses;
            OkPose = okPose;
            ConditionalLastArrow = conditionalLastArrow;
            OkType = okType;
            JudgeAreaQueueL = null;
            JudgeAreaQueueR = null;
            Flag = flag;
        }

        public SlideMetadata(
            SlideArea[] judgeAreaQueueL,
            SlideArea[] judgeAreaQueueC,
            SlideArea[] judgeAreaQueueR,
            float slideConst,
            float slideLength,
            SlidePose[] arrowPoses,
            bool conditionalLastArrow,
            SlidePose okPose,
            SlideOkType okType,
            SlideFlag flag
            )
        {
            SlideConst = slideConst;
            SlideLength = slideLength;
            JudgeAreaQueue = judgeAreaQueueC;
            ArrowPoses = arrowPoses;
            OkPose = okPose;
            ConditionalLastArrow = conditionalLastArrow;
            OkType = okType;
            JudgeAreaQueueL = judgeAreaQueueL;
            JudgeAreaQueueR = judgeAreaQueueR;
            Flag = flag;
        }
    }

    public static class SlideTableNeo
    {
        public static SlidePose CalcArrowPose(SlideArrowRawData rawData)
        {
            var x = (float)rawData.Point.Real;
            var y = (float)rawData.Point.Imaginary;
            var phase = rawData.Direction.Phase;  // -pi ~ pi

            // rawData 中 phase 是弧度，0 度是箭头朝正右，逆时针为正
            // View 中 rotZ 是角度，0 度是箭头朝正左，逆时针为正
            var rotZ = (float)(180 + phase * 180 / Math.PI);    // should be 0 ~ 360

            return new SlidePose(x, y, rotZ, (float)rawData.PathLength);
        }

        /// <summary>需要贴图尺寸 410x140</summary>
        public const double CircleOkRadius = MajGeometry.MainRadius * 462.0 / 480.0;
        /// <summary>需要贴图尺寸 410x140</summary>
        public const double StraightOkDistance = MajGeometry.MainRadius * 205.0 / 480.0;

        /// <summary>
        /// 获取圆弧 slide 的 slideOK 姿势
        /// </summary>
        /// <param name="endButton">1-based idx，1~8</param>
        /// <param name="isCcw">true 表示是逆时针 slide</param>
        public static SlidePose CalcCircleOkPose(int endButton, bool isCcw)
        {
            if (isCcw)
            {
                var rotZ = (float)(360 - 45 * endButton);
                var pos = Complex.FromPolarCoordinates(CircleOkRadius, Math.PI * (2 - endButton) / 4.0);
                return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
            }
            else
            {
                var rotZ = (float)(405 - 45 * endButton);
                var pos = Complex.FromPolarCoordinates(CircleOkRadius, Math.PI * (3 - endButton) / 4.0);
                return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
            }
        }

        /// <summary>
        /// 获取直线 slide 的 slideOK 姿势
        /// </summary>
        /// <param name="finalArrow">直接把生成的最后一个箭头数据代进来</param>
        /// <param name="isLeft">true 表示是朝左的 Ok</param>
        public static SlidePose CalcStraightOkPose(SlideArrowRawData finalArrow, bool isLeft)
        {
            var pos = finalArrow.Point - finalArrow.Direction * StraightOkDistance;
            var rotZ = (float)(finalArrow.Direction.Phase * 180.0 / Math.PI);    // should be -180 ~ 180
            if (isLeft)
            {
                rotZ += 180f;
            }
            return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
        }

        /// <summary>
        /// 使用指定的 slide 路径打表
        /// </summary>
        public static SlideMetadata CreateSlideEntry(ParametricSlidePath slidePath, SlideFlag flag = SlideFlag.None)
        {
            var arrowData = SlideDataBuilder.BuildArrowData(slidePath);
            var areaData = SlideDataBuilder.BuildSlideAreas(slidePath);
            return CreateSlideEntry(arrowData, areaData, slidePath.GetEndShape(), flag);
        }

        /// <summary>
        /// 使用指定的 slide 路径打表，如果需要对原始参数做调整的话用这个
        /// </summary>
        public static SlideMetadata CreateSlideEntry(
            SlideArrowRawData[] arrowRawData,
            SlideAreaRawData[] areaRawData,
            SlideEndShape endShape,
            SlideFlag flag = SlideFlag.None)
        {
            // ========== ========== ========== ========== ========== ========== ==========
            // 整理箭头数据

            var arrowPoseList = new List<SlidePose>();

            for (var i = 0; i < arrowRawData.Length; i++)
            {
                arrowPoseList.Add(CalcArrowPose(arrowRawData[i]));
            }

            var arrowCount = arrowPoseList.Count;

            // 如果最后一个箭头距离终点太近，就只在 conn-slide 里显示
            var conditionalLastArrow =
                arrowRawData[^1].PathLength - arrowRawData[^2].PathLength <= MajGeometry.DefaultDistance / 2.0;

            // ========== ========== ========== ========== ========== ========== ==========
            // 整理判定区数据

            var areaList = new List<SlideArea>();

            var arrowIdx = 1;
            var lastLength = 0.0;
            var firstAreaMinIdx = 2;    // 第一个区至少删两个箭头
            var finalAreaMaxIdx = conditionalLastArrow ? arrowCount - 7 : arrowCount - 6;  // 最后一个区至少留四个箭头
            SensorType sensorA;
            SensorType sensorB;
            for (var i = 0; i <= areaRawData.Length - 2; i++) // 最后一个判定段要特殊处理
            {
                var targetLength = lastLength + 0.33 * (areaRawData[i].LengthAfterPush - lastLength);
                while (arrowRawData[arrowIdx].PathLength <= targetLength) arrowIdx++;
                lastLength = areaRawData[i].LengthAfterPush;
                var push = Math.Max(arrowIdx - 1, firstAreaMinIdx);   // 扣掉本来就不显示的路径起点

                targetLength = lastLength + 0.33 * (areaRawData[i].LengthAfterFinish - lastLength);
                while (arrowRawData[arrowIdx].PathLength <= targetLength) arrowIdx++;
                var finish = Math.Min(arrowIdx - 1, finalAreaMaxIdx);   // 扣掉本来就不显示的路径起点
                lastLength = areaRawData[i].LengthAfterFinish;

                sensorA = (SensorType)areaRawData[i].SensorA;
                sensorB = (SensorType)areaRawData[i].SensorB;
                areaList.Add(new SlideArea(push, finish, sensorA, sensorB));
            }

            sensorA = (SensorType)areaRawData[^1].SensorA;
            sensorB = (SensorType)areaRawData[^1].SensorB;
            areaList.Add(new SlideArea(arrowCount, arrowCount, sensorA, sensorB));

            var slideLength = arrowRawData[^1].PathLength;
            var slideConst = (float)(1.0 - areaRawData[^2].LengthAfterFinish / slideLength);

            // ========== ========== ========== ========== ========== ========== ==========
            // 生成 slideOk

            var endButton = areaRawData[^1].SensorA + 1;   // 直接从判定队列里抠出最后一个区的键位
            SlideOkType okType;
            SlidePose okPose;

            switch (endShape)
            {
                case SlideEndShape.CircleCCW:
                    {
                        okType = SlideOkType.CircleL;
                        okPose = CalcCircleOkPose(endButton, true);
                        break;
                    }
                case SlideEndShape.CircleCW:
                    {
                        okType = SlideOkType.CircleR;
                        okPose = CalcCircleOkPose(endButton, false);
                        break;
                    }
                case SlideEndShape.Straight:
                default:
                    {
                        var isLeft = (endButton > 4);
                        okType = isLeft ? SlideOkType.StraightL : SlideOkType.StraightR;
                        okPose = CalcStraightOkPose(arrowRawData[^1], isLeft);
                        break;
                    }
            }

            return new SlideMetadata(
                areaList.ToArray(), slideConst, (float)slideLength,
                arrowPoseList.ToArray(), conditionalLastArrow,
                okPose, okType, flag);
        }

        public static readonly double[] WifiPos =
        {
            4.8, 4.279, 3.658, 3.010, 2.337, 1.637, 0.911, 0.158, -0.621, -1.426, -2.257, -3.115, -4.8
        };

        public static readonly int[] WifiArrow =
        {
            1, 2, 4, 5, 7, 8, 11
        };

        /// <summary>需要贴图尺寸 668x200</summary>
        public const double WifiOkRadius = MajGeometry.MainRadius * 424.0 / 480.0;

        /// <summary>
        /// Wifi 打表
        /// </summary>
        /// <param name="start">起点键位，1-based，1~8</param>
        public static SlideMetadata CreateWifiEntry(int start)
        {
            var arrowPoseList = new List<SlidePose>();
            var startPoint = MajGeometry.PointGroupA(start);
            var phase = startPoint.Phase;

            for (var i = 0; i < WifiPos.Length; i++)
            {
                // // magic
                // var l = 57.63636 + 23.13636 * i + 0.5 * i * i;
                // var y = 5.79371 - 2.62793 * l / 100;
                // var radius = y / 4.8 * MajGeometry.MainRadius;
                var radius = WifiPos[i] / 4.8 * MajGeometry.MainRadius;
                var length = MajGeometry.MainRadius - radius;
                var pos = Complex.FromPolarCoordinates(radius, phase);
                var rotZ = (float)(360 - 45 * start);    // should be 0 ~ 360
                arrowPoseList.Add(new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, (float)length));
            }

            var okType = (start is 3 or 4 or 5 or 6) ? SlideOkType.WifiU : SlideOkType.WifiD;
            var okPos = Complex.FromPolarCoordinates(-WifiOkRadius, phase);
            var okRotZ = 202.5f - 45 * start;
            if (okType == SlideOkType.WifiD) okRotZ += 180;
            var okPose = new SlidePose((float)okPos.Real, (float)okPos.Imaginary, okRotZ, 0);

            var judgeC = new SlideArea[]    // 1w5 的 1-5 部分，注意 start 是 1~8 而不是 0~7
            {
                new(WifiArrow[0], WifiArrow[1],
                    (SensorType)((start - 1) & 7), SensorType.Invalid), // A1=0
                new(WifiArrow[2], WifiArrow[3],
                    (SensorType)((start - 1) & 7 | 8), SensorType.Invalid), // B1=8
                new(WifiArrow[4], WifiArrow[5],
                    SensorType.C, SensorType.Invalid),
                new(WifiArrow[6], WifiArrow[6],
                    (SensorType)((start + 3) & 7), (SensorType)((start + 3) & 7 | 8)), // B5=12, A5=4
            };
            var judgeR = new SlideArea[]    // 1w5 的 1-4 部分
            {
                new(WifiArrow[0], WifiArrow[1],
                    (SensorType)((start - 1) & 7), SensorType.Invalid), // A1=0
                new(WifiArrow[2], WifiArrow[3],
                    (SensorType)(start & 7 | 8), SensorType.Invalid), // B2=9
                new(WifiArrow[4], WifiArrow[5],
                    (SensorType)((start + 1) & 7 | 8), SensorType.Invalid), // B3=10
                new(WifiArrow[6], WifiArrow[6],
                    (SensorType)((start + 2) & 7), (SensorType)(((start + 3) & 7) + 17)), // A4=3, D5=21
            };
            var judgeL = new SlideArea[]    // 1w5 的 1-6 部分
            {
                new(WifiArrow[0], WifiArrow[1],
                    (SensorType)((start - 1) & 7), SensorType.Invalid), // A1=0
                new(WifiArrow[2], WifiArrow[3],
                    (SensorType)((start - 2) & 7 | 8), SensorType.Invalid), // B8=15
                new(WifiArrow[4], WifiArrow[5],
                    (SensorType)((start - 3) & 7 | 8), SensorType.Invalid), // B7=14
                new(WifiArrow[6], WifiArrow[6],
                    (SensorType)((start - 4) & 7), (SensorType)(((start - 4) & 7) + 17)), // A6=5, D6=22
            };

            return new SlideMetadata(
                judgeL, judgeC, judgeR, 0.162870f, (float)(MajGeometry.MainRadius * 2),
                arrowPoseList.ToArray(), false,
                okPose, okType, SlideFlag.None);
        }

        /// <summary>获取除了 wifi 以外的标准 slide</summary>
        /// <remarks>没有做任何参数校验，获取前记得初始化</remarks>
        public static SlideMetadata GetStandardSlide(string key) => SLIDE_TABLE[key];

        /// <summary>获取 wifi</summary>
        /// <remarks>没有做任何参数校验，获取前记得初始化</remarks>
        public static SlideMetadata GetWifiSlide(string key) => WIFI_TABLE[key];

        /// <summary>用 slide-code 直接生成一条完整的自定义 slide</summary>
        public static SlideMetadata GetCustomSlide(string slideCode)
        {
            var path = SlideCodeParser.Parse(slideCode);
            return CreateSlideEntry(path);
        }

        /// <summary>把多条 slide 直接拼接成一条 conn-slide，然后就可以和单一 slide 一样处理了</summary>
        /// <remarks>没有做任何 wifi 的特判，如果待连接的 slide 里有 wifi 是 UB</remarks>
        public static SlideMetadata MakeConnSlide(IList<SlideMetadata> slides)
        {
            if (slides.Count == 1) return slides[0];

            var judgeAreas = new List<SlideArea>();
            var arrowPoses = new List<SlidePose>();

            // 起点单独处理
            arrowPoses.Add(slides[0].ArrowPoses[0]);

            var totalLength = 0f;
            var arrowProgress = 0;
            foreach (var slide in slides)
            {
                // 去掉路径起点和终点，只添加真实的箭头
                // 注意到这一步直接忽略了 ConditionalLastArrow，因为 conn-slide 里非最终段就是不考虑这个的
                for (var i = 1; i <= slide.ArrowPoses.Length - 2; i++)
                {
                    var pose = slide.ArrowPoses[i];
                    // 照搬坐标和取向，累积长度要改
                    arrowPoses.Add(new SlidePose(pose.X, pose.Y, pose.RotZ, pose.L + totalLength));
                }

                // 去掉最后一个判定区，留待下一段开头补上
                for (var i = 0; i <= slide.JudgeAreaQueue.Length - 2; i++)
                {
                    var area = slide.JudgeAreaQueue[i];
                    // 这里注意非最后一个区的 ArrowProgress 是比实际显示的箭头数多 1 个的
                    // 这个偏差需要原样传递到新的 SlideArea 里
                    // 所以 arrowProgress 需要是之前所有片段实际显示的箭头数量
                    judgeAreas.Add(new SlideArea(
                        arrowProgress + area.ArrowProgressPush,
                        arrowProgress + area.ArrowProgressFinish,
                        area.SensorA,
                        area.SensorB));
                    //{ IsSkippable = area.IsSkippable }
                    //此处忽略所有星星的判定保护，单条大V星星会直接返回原对象不受影响
                    //1-3这种在NoteManager->Loader中赋值完成后处理
                }

                totalLength += slide.SlideLength;
                // ArrowPoses 数组天生比实际显示的箭头数量多 2 个
                arrowProgress += slide.ArrowPoses.Length - 2;
            }

            var lastSlide = slides[^1];
            // 补上路径终点
            var lastPose = lastSlide.ArrowPoses[^1];
            arrowPoses.Add(new SlidePose(lastPose.X, lastPose.Y, lastPose.RotZ, totalLength));
            var arrowCount = arrowPoses.Count;

            if (arrowCount != arrowProgress + 2)    // assertion
            {
                throw new InvalidOperationException("Assertion failed: arrowCount == arrowProgress + 2");
            }

            // 补上最后一个区
            var lastArea = lastSlide.JudgeAreaQueue[^1];
            judgeAreas.Add(new SlideArea(arrowCount, arrowCount, lastArea.SensorA, lastArea.SensorB));

            var slideConst = lastSlide.SlideConst * lastSlide.SlideLength / totalLength;

            return new SlideMetadata(
                judgeAreas.ToArray(),
                slideConst,
                totalLength,
                arrowPoses.ToArray(),
                lastSlide.ConditionalLastArrow,
                lastSlide.OkPose,
                lastSlide.OkType,
                SlideFlag.None
                );
        }

        // ReSharper disable once InconsistentNaming
        public static readonly Dictionary<string, SlideMetadata> SLIDE_TABLE = new();
        // ReSharper disable once InconsistentNaming
        public static readonly Dictionary<string, SlideMetadata> WIFI_TABLE = new();

        /// <summary>
        /// 打表所有标准 slide 以及 Wifi
        /// </summary>
        public static void InitializeStandardSlideTable()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();

            SLIDE_TABLE.Clear();
            WIFI_TABLE.Clear();

            for (var diff = 2; diff <= 6; diff++)
            {
                // 直线形
                var path = SlidePathConstructor.BeginAt((int)SensorType.A1)
                    .LineToPoint(diff)
                    .GeneratePath();

                for (var i = 0; i <= 7; i++)
                {
                    var start = 1 + i;
                    var end = 1 + ((i + diff) & 7);
                    var key = $"{start}-{end}";
                    path.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path));
                }
            }

            for (var diff = 0; diff <= 7; diff++)
            {
                // 大圆逆时针
                var phaseNudge = ((diff is 1 or 5 or 7) ? -1 : 1) * MajGeometry.EpsilonRad / 2;
                // 官机上 1<2 1<6 1<8 会在 conn-slide 里有缝，剩下 5 种无缝，还原一下
                var pathCircle = SlidePathConstructor.BeginAt((int)SensorType.A1)
                    .ArcToAngle(9, MajGeometry.GetPoint(diff).Phase + phaseNudge, true, false)
                    .GeneratePath();

                // p slide
                ParametricSlidePath pathP;
                if (diff == 5)
                {
                    // 1p6
                    pathP = SlidePathConstructor.BeginAt((int)SensorType.A1)
                        .TangentToCircle(0, true)
                        .FullCircle(0, true)
                        .LineToPoint(diff)
                        .GeneratePath();
                }
                else if (diff == 2)
                {
                    // 1p3 直接生成的话最后一个箭头离终点太近
                    pathP = SlidePathConstructor.BeginAt((int)SensorType.A1)
                        .TangentToCircle(0, true)
                        .ArcToTangentTowards(diff, 0, true)
                        .LineToPoint(MajGeometry.GetPoint(diff) + new Complex(-MajGeometry.MainRadius / 960f, 0))
                        .GeneratePath();
                }
                else
                {
                    pathP = SlidePathConstructor.BeginAt((int)SensorType.A1)
                        .TangentToCircle(0, true)
                        .ArcToTangentTowards(diff, 0, true)
                        .LineToPoint(diff)
                        .GeneratePath();
                }

                // pp slide
                ParametricSlidePath pathPP;
                if (diff == 3 || diff == 4)
                {
                    pathPP = SlidePathConstructor.BeginAt((int)SensorType.A1)
                        .TangentToCircle(3, true)
                        .FullCircle(3, true)
                        .ArcToTangentTowards(diff, 3, true)
                        .LineToPoint(diff)
                        .GeneratePath();
                }
                else
                {
                    pathPP = SlidePathConstructor.BeginAt((int)SensorType.A1)
                        .TangentToCircle(3, true)
                        .ArcToTangentTowards(diff, 3, true)
                        .LineToPoint(diff)
                        .GeneratePath();
                }

                for (var i = 0; i <= 7; i++)
                {
                    var start = 1 + i;
                    var end = 1 + ((i + diff) & 7);

                    var key = (start is 3 or 4 or 5 or 6) ? $"{start}>{end}" : $"{start}<{end}";
                    pathCircle.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathCircle));

                    key = $"{start}p{end}";
                    pathP.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathP));

                    key = $"{start}pp{end}";
                    pathPP.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathPP));

                    end = 1 + ((i - diff) & 7);

                    key = (start is 3 or 4 or 5 or 6) ? $"{start}<{end}" : $"{start}>{end}";
                    pathCircle.SetRotoreflection(true, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathCircle));

                    key = $"{start}q{end}";
                    pathP.SetRotoreflection(true, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathP));

                    key = $"{start}qq{end}";
                    pathPP.SetRotoreflection(true, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(pathPP));
                }
            }

            for (var diff = 0; diff <= 7; diff++)
            {
                if (diff == 4) continue;
                // v slide
                var path = SlidePathConstructor.BeginAt((int)SensorType.A1)
                    .LineToPoint((int)SensorType.C)
                    .LineToPoint(diff)
                    .GeneratePath();

                for (var i = 0; i <= 7; i++)
                {
                    var start = 1 + i;
                    var end = 1 + ((i + diff) & 7);
                    var key = $"{start}v{end}";
                    path.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path));
                }
            }

            {
                // zigzag
                var path = SlidePathConstructor.BeginAt((int)SensorType.A1)
                    .LineToPoint((int)SensorType.B7)
                    .LineToPoint((int)SensorType.B3)
                    .LineToPoint((int)SensorType.A5)
                    .GeneratePath();

                for (var i = 0; i <= 7; i++)
                {
                    var start = 1 + i;
                    var end = 1 + ((i + 4) & 7);
                    var key = $"{start}s{end}";
                    path.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path));
                    key = $"{start}z{end}";
                    path.SetRotoreflection(true, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path));
                }
            }

            for (var diff = 1; diff <= 4; diff++)
            {
                // L slide
                var path = SlidePathConstructor.BeginAt((int)SensorType.A1)
                    .LineToPoint((int)SensorType.A7)
                    .LineToPoint(diff)
                    .GeneratePath();
                var isSpecial = diff == 4;

                for (var i = 0; i <= 7; i++)
                {
                    var start = 1 + i;
                    var mid = 1 + ((i - 2) & 7);
                    var end = 1 + ((i + diff) & 7);
                    var key = $"{start}V{mid}{end}";
                    path.SetRotoreflection(false, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path, isSpecial ? SlideFlag.SpecialV : SlideFlag.NormalV));

                    mid = 1 + ((i + 2) & 7);
                    end = 1 + ((i - diff) & 7);
                    key = $"{start}V{mid}{end}";
                    path.SetRotoreflection(true, i);
                    SLIDE_TABLE.Add(key, CreateSlideEntry(path, isSpecial ? SlideFlag.SpecialV : SlideFlag.NormalV));
                }
            }

            // Wifi
            for (var i = 0; i <= 7; i++)
            {
                var start = 1 + i;
                var end = 1 + ((i + 4) & 7);
                var key = $"{start}w{end}";
                WIFI_TABLE.Add(key, CreateWifiEntry(start));
            }
        }
    }
}