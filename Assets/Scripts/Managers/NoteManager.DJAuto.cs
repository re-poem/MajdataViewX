using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Notes.SlideUtils;
using MajdataViewX.Types.Enums;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Rendering;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using MajSimai;
using NUnit;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static MajdataViewX.Base.MajBurst;
using static UnityEditor.Rendering.ShadowCascadeGUI;

namespace MajdataViewX.Managers
{
    public partial class NoteManager
    {
        // 同一 timing 内收集到的 touch 类引用（指向 touches/touchHolds 的 index），LoadTiming 末尾做双圆预合并后写入 infos。
        private NativeList<DJAutoTouchInfo> _djAutoTouchInfosThisTiming = new(64, Allocator.Persistent);
        // touch 组合(sensor 子集掩码) -> 双圆手法的缓存，重复 touch 模式只算一次
        private readonly Dictionary<ulong, TwoCircleBest> _touchComboCache = new();

        private NativeArray<float> __weights = new(3, Allocator.Persistent);
        private const int DJAUTO_BEAM_DEPTH = 4;
        private const int DJAUTO_MAX_BIND_CHAIN_LENGTH = 16;


        NativeArray<DJAutoHand> _djAutoHands = new(2, Allocator.Persistent);
        NativeArray<DJAutoPlayData> _leftHandPlays;
        NativeArray<DJAutoPlayData> _rightHandPlays;

        private const int DJAUTO_CURVE_RESOLUTION = 2048;
        private static NativeArray<float> _djAutoMoveCurve;

        public const int DJAUTO_LOOKBACK_COUNT = 3;
        public const int DJAUTO_LOOKAHEAD_COUNT = 12;

        // ===== 放手时机 =====
        public const float DJAUTO_TAP_RELEASE_TIME_SEC = 0.022f;
        public const float DJAUTO_HOLD_RELEASE_TIME_SEC = 0.022f;
        public const float DJAUTO_TOUCH_RELEASE_TIME_SEC = 0.022f;
        public const float DJAUTO_TOUCHHOLD_RELEASE_TIME_SEC = 0.022f;
        /// <summary>DJAuto打星星的放手时机（判定后）</summary>
        public const float DJAUTO_SLIDE_RELEASE_DELAY_SEC = 6 * MajCtx.FRAME_LENGTH_SEC;

        // ===== 击打参数 =====
        /// <summary>Tap/Hold外键默认击打半径</summary>
        public const float DJAUTO_BTN_DEFAULT_RADIUS = MajPos.MAIN_RADIUS + DJAUTO_HAND_RADIUS * 2 + 0.5f;

        /// <summary>Tap/Hold/Slide默认尺寸</summary>
        public const float DJAUTO_HAND_RADIUS = 0.45f;
        /// <summary>Wifi默认尺寸</summary>
        public const float DJAUTO_WIFI_RADIUS = 1.00f;
        /// <summary>所有 DJAuto 手最大半径</summary>
        public const float DJAUTO_HAND_MAX_RADIUS = 1.80f;

        /// <summary>手提前移动到 hit/swipe 的时间（线性插值窗口）</summary>
        public const float DJAUTO_HAND_PREADVANCE_SEC = 30f * MajCtx.FRAME_LENGTH_SEC;
        /// <summary>手位移速度上限，超此距离来不及就Miss</summary>
        //TODO:扫键还没考虑到该情况
        public const float DJAUTO_HAND_MAX_SPEED = 9.6f / (4f * MajCtx.FRAME_LENGTH_SEC);

        // ===== CombineTouchThisTiming 覆盖圆计算系数 =====
        /// <summary>长 touchhold 重算手位的间隔阈值：相邻 endtime gap >= 此值则断开新段、重新算圆。</summary>
        private const float TOUCH_HIT_RESIZE_HAND_THRESHOLD = 100f * MajCtx.FRAME_LENGTH_SEC;
        /// <summary>一组touch(hold)中，当存在end time大于start+此值的项时，重算一次手位。之后的重算有所不同</summary>
        private const float TOUCH_HIT_SHORT_SPLIT_THRESHOLD = 5f * MajCtx.FRAME_LENGTH_SEC + (float)MajGeo.Epsilon;
        /// <summary>双圆枚举的 3 点最小覆盖圆候选仅在 n ≤ 此值时启用。大 n 降级为 1/2 点候选以保全性能。</summary>
        private const int TWO_CIRCLE_3POINT_CANDIDATE_MAX_N = 16;



        // ===== BindPlayPatterns 硬绑定系数(待实测调参) =====
        // 扫键绑定
        /// <summary>hit 到 hit 允许预绑定的最大时间差。</summary>
        public const float DJAUTO_HIT_HIT_CHAIN_SEC = 6f * MajCtx.FRAME_LENGTH_SEC;
        /// <summary>hit 到 hit 允许预绑定的最大辐角。</summary>
        public const float DJAUTO_HIT_HIT_CHAIN_ANG = math.PI2 / 8; // 一个键左右
        // 拍划绑定
        /// <summary>hit 结束到 swipe 开始允许预绑定的最大时间差。</summary>
        public const float DJAUTO_HIT_SWIPE_CHAIN_SEC = 0.01f;
        /// <summary>hit 位置到 swipe 起点允许预绑定的最大距离。</summary>
        public const float DJAUTO_HIT_SWIPE_CHAIN_DIST = 0.1f;
        // 一笔画绑定
        /// <summary>swipe 结束到 swipe 开始允许预绑定的最大时间差。</summary>
        public const float DJAUTO_SWIPE_SWIPE_CHAIN_SEC = 0.078f;
        /// <summary>swipe 结束到 swipe 起点允许预绑定的最大距离。</summary>
        public const float DJAUTO_SWIPE_SWIPE_CHAIN_DIST = 0.05f;

        // ===== FindNext 权重系数（归一化后，常用权重约为 0.5~2） =====
        /// <summary>位置代价：手到分配参考点的距离²，以场地直径²归一化后乘此系数。</summary>
        public float DJAUTO_COST_POS = 1f;
        /// <summary>未来认领代价：当前服务结束后到目标开始的空档，以此时间尺度归一化后乘此系数。</summary>
        public float DJAUTO_COST_TIME = 1f;
        /// <summary>转向代价：按手当前位置的左右符号，惩罚朝该手负方向的横向移动。</summary>
        public float DJAUTO_COST_TURN = 0.5f;
        /// <summary>未来认领代价的归一化时间尺度。</summary>
        private const float DJAUTO_COST_TIME_SCALE_SEC = 0.25f;
        /// <summary>时间可达硬约束的迟到容差:允许手在 StartTime 之后这么多秒内仍认领。</summary>
        private const float DJAUTO_COST_REACH_TOL = 0.05f;

        /// <summary>准备按下时，要写入DJAutoHand以播放动画，这个取决于动画长度</summary>
        private const float DJAUTO_PENDING_HIT = 0.15f;

        /// <summary>
        /// plays 排序键：StartTime 升序，同 StartTime 下 Hit 优先于 Swipe
        /// （为 FindEarliestTarget 的 hit 优先规则铺路，也让 BindSkippableHitsBySwipe 的窗口上界 break 成立）。
        /// </summary>
        private struct DJAutoPlayStartTimeComparer : IComparer<DJAutoPlayData>
        {
            [BurstCompile]
            public int Compare(DJAutoPlayData a, DJAutoPlayData b)
            {
                int c = a.StartTime.CompareTo(b.StartTime);
                if (c != 0) return c;
                // Hit=1 < Swipe=2，升序即 Hit 优先；NoneOrFinished=0 不会出现在 plays 中
                return ((byte)a.Type).CompareTo((byte)b.Type);
            }
        }



        #region CombineTouchThisTiming

        /// <summary>
        /// 本 timing 的 touch hit 双圆预合并：双手只有两只，用最多两个覆盖圆覆盖本 timing 尽量多的 touch 落点。
        /// 第一圆候选 = 每 1/2/3 点的最小覆盖圆；剩余点交给第二圆同样「尽量多管」（≤MAX 覆盖最多点的单圆）。
        /// 半径上限 DJAUTO_HAND_MAX_RADIUS、下限 DJAUTO_HAND_RADIUS；
        /// 选优：合计覆盖点数最多 -> max(r1,r2) 最小 -> |r1-r2| 最小。
        /// 超上限废弃；覆盖不到的点不生成 hit -> Miss 看命。计算由 Burst 直接把结果 Add 进 infos。
        /// </summary>
        private void CombineTouchHitsThisTiming()
        {
            var infos = _djAutoTouchInfosThisTiming;
            int n = infos.Length;
            if (n == 0) return;
            if (n == 1)
            {
                var r = infos[0];
                var sensor = r.Sensor;
                var pos = MajPos.GetSensorJudgePos(sensor);
                plays.Add(new DJAutoPlayData(
                    pos,
                    DJAUTO_HAND_RADIUS,
                    r.StartTime,
                    r.EndTime,
                    true));
                infos.Clear();
                return;
            }

            // 四圆：双圆 C1/C2 覆盖最多点，剩余点 S' 再算双圆 C3/C4（hashmap 缓存复用）。
            var four = ComputeFourCircle(infos.AsArray());
            CombineTouchHitsBurst(infos.AsArray(),
                four,
                DJAUTO_HAND_RADIUS, DJAUTO_HAND_MAX_RADIUS,
                ref plays);
            infos.Clear();
        }


        /// <summary>四圆 = 两次双圆（缓存复用）：first 覆盖最多点，second 覆盖 first 之外的剩余点。</summary>
        private FourCircleBest ComputeFourCircle(NativeArray<DJAutoTouchInfo> infos)
        {
            var first = GetOrComputeTwoCircle(infos);
            var four = new FourCircleBest { First = first };

            int n = infos.Length;
            var remaining = new NativeList<DJAutoTouchInfo>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
            {
                var r = infos[i];
                var pos = r.Pos;
                bool covered = (first.HasC1 && math.distance(pos, first.C1) <= first.R1 + MajGeo.Epsilon)
                            || (first.HasC2 && math.distance(pos, first.C2) <= first.R2 + MajGeo.Epsilon);
                if (!covered) remaining.Add(r);
            }
            if (remaining.Length > 0)
                four.Second = GetOrComputeTwoCircle(remaining.AsArray());
            remaining.Dispose();
            return four;
        }
        /// <summary>sensor 子集掩码池化：重复 touch 模式直接复用双圆手法，只算新的。</summary>
        private TwoCircleBest GetOrComputeTwoCircle(NativeArray<DJAutoTouchInfo> infos)
        {
            ulong mask = ComputeSensorMask(infos);
            if (_touchComboCache.TryGetValue(mask, out var best))
                return best;
            best = ComputeTwoCircle(infos, DJAUTO_HAND_RADIUS, DJAUTO_HAND_MAX_RADIUS);
            _touchComboCache[mask] = best;
            return best;
        }


        [BurstCompile]
        private static ulong ComputeSensorMask(NativeArray<DJAutoTouchInfo> infos)
        {
            ulong mask = 0;
            for (int i = 0; i < infos.Length; i++)
            {
                var r = infos[i];
                var sensor = r.Sensor;
                mask |= 1UL << (int)sensor;
            }
            return mask;
        }

        [BurstCompile]
        private static void CombineTouchHitsBurst(
            NativeArray<DJAutoTouchInfo> infos,
            FourCircleBest four,
            float handRadius,
            float maxRadius,
            ref NativeList<DJAutoPlayData> plays)
        {
            int n = infos.Length;
            // 同一 timing 内所有 touch 的 start time 相同，直接取。
            float startMin = infos[0].StartTime;
            float maxEnd = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var end = infos[i].EndTime;
                if (end > maxEnd) maxEnd = end;
            }

            float split = startMin + TOUCH_HIT_SHORT_SPLIT_THRESHOLD;

            // 短段：四圆扫过（C1->C3 / C2->C4）或双圆静态（覆盖全部时）
            if (maxEnd <= split)
            {
                EmitShortSegment(ref plays, four, startMin, maxEnd);
                return;
            }

            // 长段：仅双圆静态（剩余 Miss）。第一段短按用 first.C1/C2。
            EmitCircles(ref plays, four.First, startMin, startMin + DJAUTO_TOUCH_RELEASE_TIME_SEC);

            // 长 touchhold（endtime > 阈值）按 endtime 升序排序。遍历中 gap >= TOUCH_HIT_RESIZE_HAND_THRESHOLD 断开：
            // 对「当前段及之后全部未结束的 hit」重新算一次双圆（= 前面遍历过的 + 后面未遍历的），
            // start 取上一段末（第一段为 split），end 取本段末（最后一个遍历到的最大 endtime）。如此往复到最后一个。
            var longs = new NativeList<DJAutoTouchInfo>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
                if (infos[i].EndTime > split) longs.Add(infos[i]);
            if (longs.Length > 0)
            {
                SortByEndTime(longs);
                int m = longs.Length;
                int segIndex = 0;
                float segBegin = split;
                for (int i = 1; i <= m; i++)
                {
                    float endIm1 = longs[i - 1].EndTime;
                    bool breakHere = i == m
                        || (longs[i].EndTime - endIm1) >= TOUCH_HIT_RESIZE_HAND_THRESHOLD;
                    if (!breakHere) continue;

                    int remLen = m - segIndex;
                    var rem = new NativeArray<DJAutoTouchInfo>(remLen, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int j = 0; j < remLen; j++) rem[j] = longs[segIndex + j];
                    var segBest = ComputeTwoCircle(rem, handRadius, maxRadius);
                    EmitCircles(ref plays, segBest, segBegin, endIm1);
                    rem.Dispose();
                    segBegin = endIm1;
                    segIndex = i;
                }
            }
            longs.Dispose();
        }


        /// <summary>短段 emit：双圆覆盖全部则静态 hit，否则四圆扫过 = 每手两个端点 hit（零时长），updater 连按 On 移动实现扫过。</summary>
        [BurstCompile]
        private static void EmitShortSegment(
            ref NativeList<DJAutoPlayData> plays,
            FourCircleBest four, float startTime, float endTime)
        {
            var first = four.First;
            var second = four.Second;
            // 双圆已覆盖全部 -> 静态 hit
            if (!second.HasC1)
            {
                EmitCircles(ref plays, first, startTime, endTime);
                return;
            }
            // 四圆扫过：C1->C3 一条轨迹（两端零时长 hit，中间靠连按 On 移动扫过覆盖剩余点）
            if (first.HasC1)
            {
                plays.Add(new DJAutoPlayData(first.C1, first.R1, startTime, startTime + DJAUTO_TOUCH_RELEASE_TIME_SEC, false));
                plays.Add(new DJAutoPlayData(second.C1, second.R1, endTime, endTime + DJAUTO_TOUCH_RELEASE_TIME_SEC, false));
            }
            // C2->C4（有 C4 则扫过，否则 C2 静态）
            if (first.HasC2)
            {
                if (second.HasC2)
                {
                    plays.Add(new DJAutoPlayData(first.C2, first.R2, startTime, startTime + DJAUTO_TOUCH_RELEASE_TIME_SEC, false));
                    plays.Add(new DJAutoPlayData(second.C2, second.R2, endTime, endTime + DJAUTO_TOUCH_RELEASE_TIME_SEC, false));
                }
                else
                    plays.Add(new DJAutoPlayData(first.C2, first.R2, startTime, endTime, false));
            }
        }
        /// <summary>把最优两圆以统一的 startTime/endTime 写入 infos（0/1/2 个）。</summary>
        [BurstCompile]
        private static void EmitCircles(ref NativeList<DJAutoPlayData> plays, TwoCircleBest best, float startTime, float endTime)
        {
            if (best.HasC1)
                plays.Add(new DJAutoPlayData(best.C1, best.R1, startTime, endTime, false));
            if (best.HasC2)
                plays.Add(new DJAutoPlayData(best.C2, best.R2, startTime, endTime, false));
        }


        // pure math...



        /// <summary>对给定子集做双圆枚举（1/2/3 点候选 + 第二圆尽量多管），返回最优两圆，不写 infos。</summary>
        [BurstCompile]
        private static TwoCircleBest ComputeTwoCircle(
            NativeArray<DJAutoTouchInfo> infos,
            float handRadius, float maxRadius)
        {
            int n = infos.Length;
            var best = new TwoCircleBest { Max = float.MaxValue, Diff = float.MaxValue };
            if (n == 0) return best;

            // 预计算各点 Pos（从 touch/touchhold sensor）
            var posArr = new NativeArray<float2>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
                posArr[i] = infos[i].Pos;

            for (int i = 0; i < n; i++)
                ConsiderFirst(posArr, handRadius, maxRadius, new Circle { C = posArr[i], R = 0f }, ref best);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    ConsiderFirst(posArr, handRadius, maxRadius, MinEnclosing2(posArr[i], posArr[j]), ref best);
            if (n <= TWO_CIRCLE_3POINT_CANDIDATE_MAX_N)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        for (int k = j + 1; k < n; k++)
                            ConsiderFirst(posArr, handRadius, maxRadius, MinEnclosing3(posArr[i], posArr[j], posArr[k]), ref best);

            posArr.Dispose();
            return best;
        }

        /// <summary>按 EndTime 升序插入排序（长 touchhold 通常很少，Burst 友好）。</summary>
        [BurstCompile]
        private static void SortByEndTime(NativeList<DJAutoTouchInfo> list)
        {
            for (int i = 1; i < list.Length; i++)
            {
                var key = list[i];
                float keyEnd = key.EndTime;
                int j = i - 1;
                while (j >= 0 && list[j].EndTime > keyEnd)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }

        [BurstCompile]
        private static void ConsiderFirst(
            NativeArray<float2> posArr, float handRadius, float maxRadius,
            Circle first, ref TwoCircleBest best)
        {
            float r1 = math.max(first.R, handRadius);
            if (r1 > maxRadius) return;

            float2 cc = first.C;
            int n = posArr.Length;
            var remaining = new NativeList<float2>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
                if (math.distance(posArr[i], cc) > r1 + MajGeo.Epsilon)
                    remaining.Add(posArr[i]);

            int covered1 = n - remaining.Length;
            int covered;
            float maxR, diff, r2 = 0f;
            float2 c2 = default;
            bool hasC2;
            if (remaining.Length == 0)
            {
                covered = n;
                maxR = r1; diff = r1; hasC2 = false;
            }
            else
            {
                var sb = BestSingleCircle(remaining, handRadius, maxRadius);
                r2 = sb.R;
                c2 = sb.C;
                covered = covered1 + sb.Cov;
                maxR = math.max(r1, r2);
                diff = math.abs(r1 - r2);
                hasC2 = true;
            }
            remaining.Dispose();

            // 同覆盖点数时优先单圆(hasC2=false)：一只手大圆覆盖 优于 两只手小圆覆盖，
            // 避免双圆的 r2 hit 在双手状态机里抢不到手被忽略、其覆盖的 touch miss
            if (covered > best.Covered
                || (covered == best.Covered && !hasC2 && best.HasC2)
                || (covered == best.Covered && hasC2 == best.HasC2 && (maxR < best.Max
                    || (math.abs(maxR - best.Max) < 1e-6f && diff < best.Diff))))
            {
                best.Covered = covered;
                best.Max = maxR;
                best.Diff = diff;
                best.HasC1 = true;
                best.C1 = cc; best.R1 = r1;
                best.C2 = c2; best.R2 = r2;
                best.HasC2 = hasC2;
            }
        }

        /// <summary>在 pts 中选 1 个 ≤MAX 的圆（1/2/3 点候选）覆盖 pts 中最多点。</summary>
        [BurstCompile]
        private static SingleBest BestSingleCircle(NativeList<float2> pts, float handRadius, float maxRadius)
        {
            float2 bestC = default;
            float bestR = float.MaxValue;
            int bestCov = 0;
            int n = pts.Length;

            for (int i = 0; i < n; i++)
                ConsiderSingle(pts, handRadius, maxRadius, new Circle { C = pts[i], R = 0f }, ref bestC, ref bestR, ref bestCov);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    ConsiderSingle(pts, handRadius, maxRadius, MinEnclosing2(pts[i], pts[j]), ref bestC, ref bestR, ref bestCov);
            if (n <= TWO_CIRCLE_3POINT_CANDIDATE_MAX_N)
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        for (int k = j + 1; k < n; k++)
                            ConsiderSingle(pts, handRadius, maxRadius, MinEnclosing3(pts[i], pts[j], pts[k]), ref bestC, ref bestR, ref bestCov);

            return new SingleBest { C = bestC, R = bestR, Cov = bestCov };
        }

        [BurstCompile]
        private static void ConsiderSingle(
            NativeList<float2> pts, float handRadius, float maxRadius, Circle cand,
            ref float2 bestC, ref float bestR, ref int bestCov)
        {
            float r = math.max(cand.R, handRadius);
            if (r > maxRadius) return;
            int cov = 0;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
                if (math.distance(pts[i], cand.C) <= r + MajGeo.Epsilon) cov++;
            if (cov > bestCov || (cov == bestCov && r < bestR))
            {
                bestCov = cov;
                bestC = cand.C;
                bestR = r;
            }
        }

        [BurstCompile]
        private static Circle MinEnclosing2(float2 a, float2 b)
        {
            var c = (a + b) * 0.5f;
            return new Circle { C = c, R = math.distance(a, b) * 0.5f };
        }

        /// <summary>3 点最小覆盖圆：钝角/共线退化为最长边直径圆，否则外接圆。</summary>
        [BurstCompile]
        private static Circle MinEnclosing3(float2 a, float2 b, float2 c)
        {
            float d2_01 = math.distancesq(a, b);
            float d2_12 = math.distancesq(b, c);
            float d2_20 = math.distancesq(c, a);

            int longest = 0; float maxD2 = d2_01;
            if (d2_12 > maxD2) { longest = 1; maxD2 = d2_12; }
            if (d2_20 > maxD2) { longest = 2; maxD2 = d2_20; }

            float sumOther = longest switch
            {
                0 => d2_12 + d2_20,
                1 => d2_01 + d2_20,
                _ => d2_01 + d2_12,
            };
            if (maxD2 >= sumOther || maxD2 < 1e-12f)
            {
                return longest switch
                {
                    0 => MinEnclosing2(a, b),
                    1 => MinEnclosing2(b, c),
                    _ => MinEnclosing2(c, a),
                };
            }

            float ax = a.x, ay = a.y, bx = b.x, by = b.y, cx = c.x, cy = c.y;
            float d = 2f * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (math.abs(d) < 1e-12f)
                return longest == 0 ? MinEnclosing2(a, b)
                    : longest == 1 ? MinEnclosing2(b, c) : MinEnclosing2(c, a);

            float a2 = ax * ax + ay * ay;
            float b2 = bx * bx + by * by;
            float c2 = cx * cx + cy * cy;
            float ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            float uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            var center = new float2(ux, uy);
            return new Circle { C = center, R = math.distance(center, a) };
        }

        [BurstCompile]
        private struct Circle { public float2 C; public float R; }
        [BurstCompile]
        private struct SingleBest
        {
            public float2 C;
            public float R;
            public int Cov;
        }
        [BurstCompile]
        private struct TwoCircleBest
        {
            public int Covered;
            public float Max, Diff;
            public bool HasC1, HasC2;
            public float2 C1, C2;
            public float R1, R2;
        }
        /// <summary>四圆 = 两次双圆：First(C1/C2) 覆盖最多点，Second(C3/C4) 覆盖剩余；Second.HasC1=false 表示双圆已覆盖全部。</summary>
        [BurstCompile]
        private struct FourCircleBest
        {
            public TwoCircleBest First;
            public TwoCircleBest Second;
        }

        [BurstCompile]
        internal struct DJAutoTouchInfo
        {
            public SensorType Sensor;
            public float2 Pos;
            public float StartTime;
            public float EndTime;
        }

        #endregion





        #region BindSkippableHitsBySwipe

        /// <summary>target 到 swipe 路径采样点的最近顶点（arrows 间隔短，免段内投影）：返回最近距离与 swipe 手到达该点的时刻。</summary>
        [BurstCompile]
        private static unsafe void SwipePathNearest(in DJAutoPlayData play, float2 target,
            out float nearestDist, out float nearestTime)
        {
            var arrows = play.BindSlide->slideArrows;
            var count = play.BindSlide->slideArrowsCount;
            var startTime = play.StartTime;
            var endTime = play.EndTime;

            nearestDist = float.MaxValue;
            nearestTime = startTime;
            if (count <= 1 || arrows == null) return;
            float totalL = arrows[count - 1].L;
            float duration = endTime - startTime;
            float bestL = 0f;
            for (int k = 0; k < count; k++)
            {
                var p = arrows[k];
                // 0.3f的神秘常数是取自最小判定区E的半径的再一半，
                // 因为实际上不需要完全摸到那个区的中点
                // 这里用不上ComputeSwipePosAt，没必要那么精细
                var d = math.distance(new float2(p.X, p.Y), target) - 0.3f;
                if (d < nearestDist)
                {
                    nearestDist = d;
                    bestL = p.L;
                }
            }
            nearestTime = totalL > 0f ? startTime + (bestL / totalL) * duration : startTime;
        }

        private static unsafe void InsideWifi(in DJAutoPlayData play, float2 target,
            out float nearestDist, out float nearestTime)
        {
            nearestDist = DJAUTO_WIFI_RADIUS;
            var arrows = play.BindSlide->slideArrows;
            var count = play.BindSlide->slideArrowsCount;
            var startPos = new float2(arrows[0].X, arrows[0].Y);
            var midEndPos = new float2(arrows[count - 1].X, arrows[count - 1].Y);
            var offset = midEndPos - startPos;
            var rad = math.radians(22.5f);
            var cos = math.cos(rad);
            var sin = math.sin(rad);
            var cwEndPos = startPos + new float2(
                offset.x * cos - -offset.y * sin,
                -offset.x * sin + offset.y * cos);
            var ccwEndPos = startPos + new float2(
                offset.x * cos - offset.y * sin,
                offset.x * sin + offset.y * cos);
            var c1 = mathx.cross(cwEndPos - startPos, target - startPos);
            var c2 = mathx.cross(ccwEndPos - startPos, target - startPos);
            if (c1 > 0 && c2 < 0)
            {
                nearestTime = play.StartTime + math.dot(target - startPos, midEndPos - startPos) / 9.6f / 9.6f * (play.EndTime - play.StartTime);
            }
            else
            {
                nearestTime = float.MaxValue;
            }
        }


        /// <summary>
        /// 标记可被 swipe 顺带覆盖的 hit：swipe 路径经过 hit 附近，且手到达最近点时 hit 仍在触发窗口内，
        /// 则运行时跳过绑定 hit
        /// </summary>
        [BurstCompile]
        private unsafe void BindSkippableHitsBySwipe()
        {
            for (int i = 0; i < plays.Length; i++)
            {
                ref var hit = ref plays.ElementRef(i);
                if (hit.Type is not DJAutoPlayType.Hit) continue;
                if (!hit.IsAllowSkipBySwipe) continue;  // 不允许被 swipe 顺带覆盖（tap/hold）
                var bestReq = float.MaxValue;
                var bestSwipe = -1;
                //只有touch是允许跳过的，先这样写死
                var perfectStart = hit.StartTime - NoteHelper.TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC / 1000;
                var perfectEnd = hit.StartTime + NoteHelper.TOUCH_JUDGE_SEG_3RD_PERFECT_MSEC / 1000;
                for (int j = 0; j < plays.Length; j++)
                {
                    ref var swipe = ref plays.ElementRef(j);
                    // plays 已按 StartTime 升序：超过 perfect 窗口上界后，后面的只会更晚，不可能相交
                    if (swipe.StartTime > perfectEnd) break;
                    if (swipe.Type is not DJAutoPlayType.Swipe) continue;
                    var arrows = swipe.BindSlide->slideArrows;
                    var count = swipe.BindSlide->slideArrowsCount;
                    if (count <= 0 || arrows == null) continue;
                    // 廉价时间筛选下界：swipe 整段已早于窗口（EndTime 非单调，只能 continue 不能 break）
                    if (swipe.EndTime < perfectStart) continue;
                    float dist, time;
                    if (!swipe.IsWifi)
                    {
                        // 路径上离 hit 最近的点：swipe 手经过该点时若 hit 仍在 perfect 区间内，即可顺带覆盖
                        SwipePathNearest(swipe, hit.Pos, out dist, out time);
                    }
                    else
                    {
                        InsideWifi(swipe, hit.Pos, out dist, out time);
                    }
                    if (time < perfectStart || time > perfectEnd) continue;  // 到达时 touch 已经不在 perfect 区间了
                    if (math.all(hit.Pos == 0f) &&
                        dist <= MajGeo.GroupBRadius &&
                        !swipe.IsWifi) // 非wifi，差一点可以蹭到C区touch的情况
                    {
                        hit.IsReserved |= true;
                        swipe.SkipCTime = time;
                        swipe.Radius += 0.2f;
                        break;
                    }
                    if (dist > DJAUTO_HAND_MAX_RADIUS) continue;  // 扩到最大半径也够不着
                    if (dist < bestReq)
                    {
                        bestReq = dist;
                        bestSwipe = j;
                    }
                }
                if (bestSwipe >= 0)
                {
                    hit.IsReserved |= true;
                    ref var swipe = ref plays.ElementRef(bestSwipe);
                    swipe.Radius = math.max(swipe.Radius, bestReq);
                }
            }
        }

        #endregion


        #region BindPlayPatterns

        private void BindPlayPatterns()
        {
            for (var i = 0; i < plays.Length; i++)
            {
                ref var play = ref plays.ElementRef(i);
                if (play.Type is DJAutoPlayType.NoneOrFinished) continue;

                for (ushort j = 1; j <= 8; j++)
                {
                    if (i + j >= plays.Length) break;
                    ref var play2 = ref plays.ElementRef(i + j);

                    // 扫键绑定：hit跟随最近的（辐角为一个键左右）的hit，前hit经过排序一定在后hit前面，不考虑间隔超多plays的情况
                    if (play.Type is DJAutoPlayType.Hit &&
                        play2.Type is DJAutoPlayType.Hit)
                    {
                        var delta = math.abs(play.StartTime - play2.StartTime);
                        if (
                        delta is > 0.01f and < DJAUTO_HIT_HIT_CHAIN_SEC &&
                        math.abs(math.abs(math.atan2(
                            mathx.cross(play.Pos, play2.Pos),
                            math.dot(play.Pos, play2.Pos))) - DJAUTO_HIT_HIT_CHAIN_ANG) < 0.1f)
                        {
                            // hit 不按满 0.022 直接转
                            play.EndTime = play.StartTime;
                            play.BindPlayOffset = j;
                            play2.IsReserved |= true;
                            break;
                        }
                    }


                    // 拍划绑定：swipe跟随最近的hit，hit经过排序一定在swipe前面
                    if (play.Type is DJAutoPlayType.Hit &&
                        play2.Type is DJAutoPlayType.Swipe &&
                        math.abs(play.StartTime - play2.StartTime) < DJAUTO_HIT_SWIPE_CHAIN_SEC)
                    {
                        if ( // 内屏
                        math.distancesq(play.Pos, play2.GetEntryPos()) < math.pow(DJAUTO_HIT_SWIPE_CHAIN_DIST, 2)) //相对宽松，适配大touch hit
                        {
                            // hit 不按满 0.022 直接转
                            play.EndTime = play.StartTime;
                            play.BindPlayOffset = j;
                            play2.IsReserved |= true;
                            break;
                        }
                        else if ( // 外键
                        math.lengthsq(play.Pos) > math.pow(MajGeo.MainRadius, 2f) &&
                        math.abs(math.atan2(
                            mathx.cross(play.Pos, play2.Pos),
                            math.dot(play.Pos, play2.Pos))) < 0.1f)
                        {
                            // hit 不按满 0.022 直接转
                            play.EndTime = play.StartTime;
                            play.BindPlayOffset = j;
                            play2.IsReserved |= true;
                            break;
                        }
                    }

                    // 一笔画绑定：swipe跟随最近结束的swipe，前swipe经过排序一定在后swipe前面，不考虑间隔超多plays的情况
                    if (play.Type is DJAutoPlayType.Swipe && !play.IsWifi &&
                        play2.Type is DJAutoPlayType.Swipe &&
                        math.abs(play.EndTime - play2.StartTime) < DJAUTO_SWIPE_SWIPE_CHAIN_SEC &&
                        math.distancesq(play.GetEndPos(), play2.GetEntryPos()) < math.pow(DJAUTO_SWIPE_SWIPE_CHAIN_DIST, 2))
                    {
                        play.BindPlayOffset = j;
                        play2.IsReserved |= true;
                        break;
                    }
                }
            }
        }

        #endregion


        internal enum DJAutoHandState { Off, On, Moving, OnMovingToBoundNext }
        [BurstCompile]
        internal struct DJAutoHand
        {
            public float2 Pos;
            public readonly float Radius => Current.Radius;
            public DJAutoHandState State;
            public bool IsPendingHit;

            public int CurrentIdx;               // 当前目标的 play 的索引，供释放用，实际调用Current更快
            public DJAutoPlayData Current;       // 当前目标的 play

            public float MoveStart;
            public float MoveEnd;
            public float2 MoveFrom;
            public float2 MoveTo;
            public float ServeEnd;
        }

        /// <summary>一条尚未提交的 DJAuto 软规划链，同时保存两只手在假设分配后的状态。</summary>
        [BurstCompile]
        private struct DJAutoHandClip
        {
            public float2 Pos;
            public float ServeEnd;
            public int CurrentIdx;
        }

        [BurstCompile]
        private struct DJAutoPlan
        {
            public DJAutoHandClip left;
            public DJAutoHandClip right;
            public int FirstPlayIdx;
            public int FirstHandIdx;
            public float Cost;
            public bool Valid;
        }

        /// <summary>重置双手到初始位（半径 2.4 = 主圆一半，平行 x 轴直径两端），状态 Off，CurrentIdx=-1 无目标，ServeEnd=-∞ 确保初始可达。</summary>
        private void ResetDJAutoHands()
        {
            _djAutoHands[0] = new DJAutoHand
            {
                Pos = new float2(-2.4f, 0f),
                CurrentIdx = -1,
                Current = default,
                ServeEnd = float.NegativeInfinity
            };
            _djAutoHands[1] = new DJAutoHand
            {
                Pos = new float2(2.4f, 0f),
                CurrentIdx = -1,
                Current = default,
                ServeEnd = float.NegativeInfinity
            };
        }

        [BurstCompile]
        private unsafe struct PlayUpdateJob : IJob
        {
            public NativeArray<float> _djAutoMoveCurve;
            private float DJAutoMoveEvaluate(float t)
            {
                if (t < 0) return 0f; else if (t > 1) return 1f;
                return _djAutoMoveCurve[(int)(t * (DJAUTO_CURVE_RESOLUTION - 1))];
            }

            public NativeArray<DJAutoPlayData> plays;
            public NativeArray<DJAutoHand> hands;          // [0]=left [1]=right
            public NativeArray<float> __weights;          // [0]=位置 [1]=时间 [2]=转向

            public void Execute()
            {
                var time = TimeData.DJAutoTime;
                // 先各自更新状态，再统一按权重分配尚未占用的 data。
                UpdateHandState(0, time);
                UpdateHandState(1, time);
                FindNext(time);

                RenderAndTrigger(0);
                RenderAndTrigger(1);
            }

            /// <summary>只推进单手已锁目标的状态，不做目标分配。返回值表示本帧刚从 On 释放，可在统一分配时保持连按。</summary>
            private bool UpdateHandState(int handIdx, float time)
            {
                if (hands[handIdx].Current.Type is DJAutoPlayType.NoneOrFinished) return false;

                ref var hand = ref hands.ElementRef(handIdx);
                switch (hand.State)
                {
                    case DJAutoHandState.On:
                        {
                            hand.Pos = hand.Current.GetCurPos(time);
                            var released = hand.Current.IsReleased(time);
                            if (released)
                            {
                                if (hand.Current.BindPlayOffset != 0)
                                {
                                    plays[hand.CurrentIdx] = default;
                                    hand.CurrentIdx += hand.Current.BindPlayOffset;
                                    var next = plays[hand.CurrentIdx];
                                    hand.Current = next;
                                    hand.ServeEnd = next.EndTime;

                                    hand.MoveFrom = hand.Pos;
                                    hand.MoveTo = hand.Current.GetEntryPos();
                                    hand.MoveStart = time;
                                    hand.MoveEnd = hand.Current.StartTime;
                                    hand.State = DJAutoHandState.OnMovingToBoundNext;

                                    return false;
                                }
                                hand.State = DJAutoHandState.Off;
                                plays[hand.CurrentIdx] = default;
                                hand.Current = default;
                                return true;
                            }
                            break;
                        }
                    case DJAutoHandState.OnMovingToBoundNext:
                        {
                            var t = math.saturate((time - hand.MoveStart) / math.max(hand.MoveEnd - hand.MoveStart, 1e-5f));
                            hand.Pos = math.lerp(hand.MoveFrom, hand.MoveTo, t);
                            if (time >= hand.Current.StartTime)
                            {
                                hand.State = DJAutoHandState.On;
                                hand.Pos = hand.Current.GetEntryPos();
                            }
                            break;
                        }
                    case DJAutoHandState.Off:
                        {
                            var startTime = hand.Current.StartTime;
                            var moveStart = math.max(hand.ServeEnd, startTime - DJAUTO_HAND_PREADVANCE_SEC);
                            if (time >= moveStart)
                            {
                                hand.State = DJAutoHandState.Moving;
                                hand.MoveFrom = hand.Pos;
                                hand.MoveTo = hand.Current.GetEntryPos();
                                hand.MoveStart = moveStart;
                                hand.MoveEnd = startTime;
                            }
                            break;
                        }
                    case DJAutoHandState.Moving:
                        {
                            var t = math.saturate((time - hand.MoveStart) / math.max(hand.MoveEnd - hand.MoveStart, 1e-5f));
                            hand.Pos = math.lerp(hand.MoveFrom, hand.MoveTo, DJAutoMoveEvaluate(t));
                            if (time >= hand.Current.StartTime)
                            {
                                hand.IsPendingHit = false;
                                hand.State = DJAutoHandState.On;
                                hand.Pos = hand.Current.GetEntryPos();
                                hand.ServeEnd = hand.Current.EndTime;
                            }
                            else if (time >= hand.Current.StartTime - DJAUTO_PENDING_HIT)
                            {
                                hand.IsPendingHit = true;
                            }
                            break;
                        }
                }
                return false;
            }

            private void FindNext(float time)
            {
                var plan = BuildBestPlan(time);
                if (plan.Valid) ClaimPlay(plan.FirstHandIdx, plan.FirstPlayIdx);
            }

            /// <summary>
            /// 从当前状态分别假设首项交给左手或右手，并让后续每一项在两只手之间选择较低的增量代价。
            /// 规划结果只用于比较当前首项，真实状态每帧都会重新计算。
            /// </summary>
            private DJAutoPlan BuildBestPlan(float time)
            {
                var best = default(DJAutoPlan);
                for (int firstHand = 0; firstHand < 2; firstHand++)
                {
                    var first = hands[firstHand];
                    if (first.Current.Type is not DJAutoPlayType.NoneOrFinished) continue;

                    var firstBegin = math.max(first.CurrentIdx - DJAUTO_LOOKBACK_COUNT, 0);
                    var firstEnd = math.min(first.CurrentIdx + DJAUTO_LOOKAHEAD_COUNT + 1, plays.Length);
                    for (int firstIdx = firstBegin; firstIdx < firstEnd; firstIdx++)
                    {
                        if (!TryAppendClip(
                            CreateClip(first), firstIdx, time, time,
                            out var firstClip, out var firstCost)) continue;

                        var left = CreateClip(hands[0]);
                        var right = CreateClip(hands[1]);
                        if (firstHand == 0) left = firstClip; else right = firstClip;
                        var total = firstCost;
                        var cursor = firstClip.CurrentIdx;

                        for (int step = 1; step < DJAUTO_BEAM_DEPTH; step++)
                        {
                            if (!TryFindNextForEitherHand(
                                left, right, cursor, time,
                                out var nextLeft, out var nextRight,
                                out var nextIdx, out var nextCost)) break;
                            left = nextLeft;
                            right = nextRight;
                            cursor = nextIdx;
                            total += nextCost;
                        }

                        if (!best.Valid || total < best.Cost)
                        {
                            best = new DJAutoPlan
                            {
                                left = left,
                                right = right,
                                FirstPlayIdx = firstIdx,
                                FirstHandIdx = firstHand,
                                Cost = total,
                                Valid = true
                            };
                        }
                    }
                }
                return best;
            }

            private static DJAutoHandClip CreateClip(DJAutoHand hand)
            {
                return new DJAutoHandClip
                {
                    Pos = hand.Pos,
                    ServeEnd = hand.ServeEnd,
                    CurrentIdx = hand.CurrentIdx
                };
            }

            private bool TryFindNextForEitherHand(
                DJAutoHandClip left,
                DJAutoHandClip right,
                int cursor,
                float time,
                out DJAutoHandClip nextLeft,
                out DJAutoHandClip nextRight,
                out int nextIdx,
                out float nextCost)
            {
                nextLeft = left;
                nextRight = right;
                nextIdx = -1;
                nextCost = float.MaxValue;
                var begin = math.max(cursor + 1, 0);
                var end = math.min(cursor + DJAUTO_LOOKAHEAD_COUNT + 1, plays.Length);
                for (int idx = begin; idx < end; idx++)
                {
                    if (TryAppendClip(left, idx, time, time, out var l, out var lc) && lc < nextCost)
                    {
                        nextLeft = l;
                        nextRight = right;
                        nextIdx = idx;
                        nextCost = lc;
                    }
                    if (TryAppendClip(right, idx, time, time, out var r, out var rc) && rc < nextCost)
                    {
                        nextLeft = left;
                        nextRight = r;
                        nextIdx = idx;
                        nextCost = rc;
                    }
                }
                return nextIdx >= 0;
            }

            /// <summary>模拟把一个 play 分配给指定手；不修改 plays 的预留状态。</summary>
            private bool TryAppendClip(
                DJAutoHandClip clip,
                int playIdx,
                float time,
                float referenceTime,
                out DJAutoHandClip appended,
                out float cost)
            {
                appended = default;
                cost = float.MaxValue;
                var p = plays[playIdx];
                if (p.Type is DJAutoPlayType.NoneOrFinished || p.IsReserved || time > p.EndTime)
                    return false;
                if (playIdx <= clip.CurrentIdx) return false;

                cost = ComputeClipCost(clip.Pos, clip.ServeEnd, p, referenceTime);
                if (cost >= float.MaxValue) return false;

                var exitIdx = playIdx;
                var exit = p;
                for (int count = 0; exit.BindPlayOffset != 0 && count < DJAUTO_MAX_BIND_CHAIN_LENGTH; count++)
                {
                    var nextIdx = exitIdx + exit.BindPlayOffset;
                    if (nextIdx < 0 || nextIdx >= plays.Length) return false;
                    exitIdx = nextIdx;
                    exit = plays[exitIdx];
                    if (exit.Type is DJAutoPlayType.NoneOrFinished) return false;
                }

                appended = new DJAutoHandClip
                {
                    Pos = exit.GetEndPos(),
                    ServeEnd = exit.EndTime,
                    CurrentIdx = exitIdx
                };
                return true;
            }

            private readonly float ComputeClipCost(float2 fromPos, float serveEnd, DJAutoPlayData p, float time)
            {
                var availableTime = math.max(serveEnd, time);
                var entryDistSq = math.distancesq(fromPos, p.GetEntryPos());
                var reachWindow =
                    (p.StartTime + DJAUTO_COST_REACH_TOL - availableTime) * DJAUTO_HAND_MAX_SPEED;
                if (reachWindow < 0f || entryDistSq > reachWindow * reachWindow)
                    return float.MaxValue;

                var assignPos = p.GetAssignPos();
                var fieldDiameter = MajPos.MAIN_RADIUS * 2f;
                var normalizedDistance =
                    math.distancesq(fromPos, assignPos) / (fieldDiameter * fieldDiameter);
                var normalizedFutureTime =
                    math.max(0f, p.StartTime - availableTime) / DJAUTO_COST_TIME_SCALE_SEC;
                var dx = assignPos.x - fromPos.x;
                var normalizedTurn = math.max(0f, -math.sign(fromPos.x) * dx) / fieldDiameter;

                return __weights[0] * normalizedDistance
                    + __weights[1] * normalizedFutureTime
                    + __weights[2] * normalizedTurn;
            }

            /// <summary>空闲手认领候选：记录 idx/Current 并标 IsReserved 占用。Current 副本保留原 Type 供执行。</summary>
            private void ClaimPlay(int handIdx, int playIdx)
            {
                ref var hand = ref hands.ElementRef(handIdx);
                hand.CurrentIdx = playIdx;
                hand.Current = plays[playIdx];
                var p = plays[playIdx];
                p.IsReserved |= true;
                plays[playIdx] = p;
            }

            /// <summary>渲染 + 触发：On 按当前 data 位置触发 sensor/按键并红手渲染，否则灰手。handSide 用于 wifi L/R 派生。</summary>
            private void RenderAndTrigger(int handIdx)
            {
                var hand = hands[handIdx];
                if (hand.State is DJAutoHandState.On or DJAutoHandState.OnMovingToBoundNext)
                {
                    InputData.HandleWorldPosInput(hand.Pos, hand.Radius);
                }
                else
                {
                    RenderHandOff(hand.Pos);
                }
            }

            /// <summary>Off/Moving 渲染灰色手圆（不触发 sensor）。</summary>
            private static void RenderHandOff(float2 pos)
            {
                if (!InputData.ShowHand) return;
                var idx = Interlocked.Increment(ref *InputData.HitWriteCountPtr) - 1;
                InputData.hitRender[idx] = new HitRenderData
                {
                    pos = pos,
                    radius = DJAUTO_HAND_RADIUS,
                    color = new float4(0.6f, 0.6f, 0.6f, 0.5f)
                };
            }
        }
    }
}
