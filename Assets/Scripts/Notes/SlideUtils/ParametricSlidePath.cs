using MajdataViewX.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Notes.SlideUtils
{
    /// <summary>
    /// <p>用来控制箭头对齐的标志</p>
    /// </summary>
    public enum SlideParseMarker
    {
        None = 0,

        /// <summary>
        /// 调整箭头间距，以保证本段结束时箭头位置恰好对齐本段终点
        /// </summary>
        SmoothAlign,

        /// <summary>
        /// 不调整箭头间距，但本段结束时把箭头位置强制设为本段终点
        /// </summary>
        ForceAlign,
    }

    /// <summary>
    /// <p>slide 结尾的形状</p>
    /// </summary>
    public enum SlideEndShape
    {
        Straight,
        CircleCCW,
        CircleCW
    }

    /// <summary>
    /// <p>一个 slide 路径片段的 abstract 类</p>
    /// </summary>
    public abstract class PathSegment
    {
        /// <summary>
        /// <p>用来控制箭头对齐的标志</p>
        /// </summary>
        public SlideParseMarker ParseMarker { get; private set; } = SlideParseMarker.None;

        /// <summary>
        /// <p>这个参数表示这段路径上每隔多少距离放一个箭头</p>
        /// <p>默认值是判定圆周长的 1/64</p>
        /// </summary>
        public double ArrowDistance { get; private set; } = MajGeometry.DefaultDistance;

        public abstract bool IsCurve { get; }

        /// <summary>
        /// <p>计算路径上某点的坐标，保证均匀插值</p>
        /// </summary>
        /// <param name="t">0 ~ 1 (both inclusive)</param>
        public abstract Complex GetPointAt(double t);

        /// <summary>
        /// <p>计算路径上某点处的有向切线，方向为路径前进方向</p>
        /// </summary>
        /// <param name="t">0 ~ 1 (both inclusive)</param>
        /// <returns>complex (magnitude = 1)</returns>
        public abstract Complex GetTangentAt(double t);

        /// <summary>
        /// <p>计算本段路径总长</p>
        /// </summary>
        public abstract double GetSegmentLength();

        /// <summary>
        /// <p>设置控制箭头对齐的标志，给 parser 用</p>
        /// </summary>
        public void SetParseMarker(SlideParseMarker marker) => ParseMarker = marker;

        /// <summary>
        /// <p>设置本段路径的箭头排列间距</p>
        /// </summary>
        public void SetArrowDistance(double distance) => ArrowDistance = distance;
    }

    /// <summary>
    /// <p>slide 直线片段</p>
    /// </summary>
    public class LineSegment : PathSegment
    {
        public readonly Complex StartPoint;
        public readonly Complex EndPoint;

        public LineSegment(Complex start, Complex end)
        {
            StartPoint = start;
            EndPoint = end;
        }

        public override bool IsCurve { get; } = false;

        public override Complex GetPointAt(double t)
        {
            return StartPoint + (EndPoint - StartPoint) * t;
        }

        public override Complex GetTangentAt(double t)
        {
            var v = EndPoint - StartPoint;
            return v / v.Magnitude;
        }

        public override double GetSegmentLength()
        {
            return (EndPoint - StartPoint).Magnitude;
        }
    }

    /// <summary>
    /// <p>slide 圆弧片段，对角度进行线性插值，不会自动扣除一整圈</p>
    /// </summary>
    public class ArcSegment : PathSegment
    {
        public readonly CircleStruct Circle;
        public readonly double StartRadian;
        public readonly double EndRadian;

        public ArcSegment(CircleStruct circle, double startRadian, double endRadian)
        {
            Circle = circle;
            StartRadian = startRadian;
            EndRadian = endRadian;
        }

        public override bool IsCurve { get; } = true;

        public override Complex GetPointAt(double t)
        {
            var angle = StartRadian + t * (EndRadian - StartRadian);
            return Circle.Center + Complex.FromPolarCoordinates(Circle.Radius, angle);
        }

        public override Complex GetTangentAt(double t)
        {
            var angle = StartRadian + t * (EndRadian - StartRadian);
            if (StartRadian < EndRadian)
            {
                return Complex.FromPolarCoordinates(1, angle) * Complex.ImaginaryOne;
            }
            else
            {
                return Complex.FromPolarCoordinates(-1, angle) * Complex.ImaginaryOne;
            }
        }

        public override double GetSegmentLength()
        {
            return Math.Abs(EndRadian - StartRadian) * Circle.Radius;
        }
    }

    /// <summary>
    /// <p>slide 圆周片段，总之就是转一整圈</p>
    /// </summary>
    public class CircleSegment : PathSegment
    {
        public readonly CircleStruct Circle;
        public readonly double StartRadian;
        public readonly bool IsCcw;

        public CircleSegment(CircleStruct circle, double startRadian, bool isCcw)
        {
            Circle = circle;
            StartRadian = startRadian;
            IsCcw = isCcw;
        }

        public override bool IsCurve { get; } = true;

        public override Complex GetPointAt(double t)
        {
            double angle;
            if (IsCcw)
            {
                angle = StartRadian + t * Math.PI * 2.0;
            }
            else
            {
                angle = StartRadian - t * Math.PI * 2.0;
            }

            return Circle.Center + Complex.FromPolarCoordinates(Circle.Radius, angle);
        }

        public override Complex GetTangentAt(double t)
        {
            double angle;
            if (IsCcw)
            {
                angle = StartRadian + t * Math.PI * 2.0;
                return Complex.FromPolarCoordinates(1, angle) * Complex.ImaginaryOne;
            }
            else
            {
                angle = StartRadian - t * Math.PI * 2.0;
                return Complex.FromPolarCoordinates(-1, angle) * Complex.ImaginaryOne;
            }
        }

        public override double GetSegmentLength()
        {
            return Math.PI * Circle.Radius * 2.0;
        }
    }

    /// <summary>
    /// <p>以指定点为极点，连接起点和终点的对数螺线片段</p>
    /// <p>按照指定方向连接；起终点位于同一辐角时绕行一整圈</p>
    /// <p>起终点半径相等时退化为圆弧</p>
    /// </summary>
    public class LogarithmicSpiralSegment : PathSegment
    {
        public readonly Complex StartPoint;
        public readonly Complex EndPoint;
        public readonly Complex Pole;
        public readonly bool IsCcw;

        private readonly double _startRadius;
        private readonly double _endRadius;
        private readonly double _startRadian;
        private readonly double _radianDelta;
        private readonly double _logRadiusRatio;
        private readonly Complex _unitTangentFactor;
        private readonly double _segmentLength;

        public LogarithmicSpiralSegment(
            Complex startPoint,
            Complex endPoint,
            Complex pole,
            bool isCcw)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            Pole = pole;
            IsCcw = isCcw;

            var startVector = startPoint - pole;
            var endVector = endPoint - pole;
            _startRadius = startVector.Magnitude;
            _endRadius = endVector.Magnitude;
            if (_startRadius == 0.0 || _endRadius == 0.0)
            {
                throw new ArgumentException("A logarithmic spiral cannot start or end at its pole.");
            }

            _startRadian = startVector.Phase;
            _radianDelta = endVector.Phase - _startRadian;
            if (isCcw)
            {
                while (_radianDelta <= 0.0) _radianDelta += Math.PI * 2.0;
            }
            else
            {
                while (_radianDelta >= 0.0) _radianDelta -= Math.PI * 2.0;
            }

            _logRadiusRatio = Math.Log(_endRadius / _startRadius);

            // 令 v 从 0 变化到 1，则
            // z(v) = r0 * exp((log(r1/r0) + i * deltaAngle) * v)。
            // z'(v) 中与方向有关的常量因子就是下面这个复数。
            var tangentFactor = new Complex(_logRadiusRatio, _radianDelta);
            _unitTangentFactor = tangentFactor / tangentFactor.Magnitude;

            // ∫|z'(v)|dv = hypot(log(r1/r0), deltaAngle) * L(r0, r1)，
            // 其中 L 是两个半径的对数平均。
            var logarithmicMeanRadius = Math.Abs(_logRadiusRatio) < 1e-8
                ? (_startRadius + _endRadius) / 2.0
                : (_endRadius - _startRadius) / _logRadiusRatio;
            _segmentLength = tangentFactor.Magnitude * logarithmicMeanRadius;
        }

        public override bool IsCurve => true;

        private double GetSpiralParameterAt(double t)
        {
            if (_logRadiusRatio == 0.0)
            {
                return t;
            }

            // 对数螺线从起点到任意半径的弧长与半径差成正比，
            // 所以先按 t 线性插值半径，再反解螺线参数，便可保证等弧长插值。
            var radius = _startRadius + (_endRadius - _startRadius) * t;
            return Math.Log(radius / _startRadius) / _logRadiusRatio;
        }

        public override Complex GetPointAt(double t)
        {
            if (t == 0.0) return StartPoint;
            if (t == 1.0) return EndPoint;

            var v = GetSpiralParameterAt(t);
            var radius = _startRadius + (_endRadius - _startRadius) * t;
            var radian = _startRadian + _radianDelta * v;
            return Pole + Complex.FromPolarCoordinates(radius, radian);
        }

        public override Complex GetTangentAt(double t)
        {
            var v = GetSpiralParameterAt(t);
            var radian = _startRadian + _radianDelta * v;
            return Complex.FromPolarCoordinates(1.0, radian) * _unitTangentFactor;
        }

        public override double GetSegmentLength()
        {
            return _segmentLength;
        }
    }

    /// <summary>
    /// <p>参数化的 slide 路径曲线</p>
    /// </summary>
    public class ParametricSlidePath
    {
        /// <summary>
        /// <p>组成这条路径的所有片段</p>
        /// </summary>
        public readonly PathSegment[] Segments;

        /// <summary>
        /// <p>片段之间的切割点，<c>Fractions[i]</c>是第 i 个片段的终点的 t 值</p>
        /// </summary>
        public readonly double[] Fractions;

        /// <summary>
        /// <p>片段累积长度，<c>AccumulatedLengths[i]</c>是到第 i 个片段的终点为止经过的总长度</p>
        /// </summary>
        public readonly double[] AccumulatedLengths;

        public ParametricSlidePath(IEnumerable<PathSegment> pathSegments)
        {
            Segments = pathSegments.ToArray();
            if (Segments.Length == 0)
            {
                throw new ArgumentException("At least one path segment is required.");
            }

            var lengths = Segments.Select(s => s.GetSegmentLength());
            var sum = 0.0;
            AccumulatedLengths = lengths.Select(x => (sum += x)).ToArray();
            Fractions = AccumulatedLengths.Select(x => x / sum).ToArray();
        }

        /// <summary>
        /// <p>获取 t 位置所在的片段</p>
        /// </summary>
        /// <param name="t">0 ~ 1 (both inclusive)</param>
        /// <param name="segmentT">这个点在返回片段上的 t 值，0 ~ 1 (both inclusive)</param>
        /// <returns>片段对象</returns>
        public PathSegment GetSegmentAt(double t, out double segmentT)
        {
            if (t <= 0.0)
            {
                segmentT = 0.0;
                return Segments[0];
            }

            if (t >= 1.0)
            {
                segmentT = 1.0;
                return Segments[^1];
            }

            // The index of the specified value in the specified array, if value is found;
            // otherwise, a negative number.
            //
            // If value is not found and value is less than one or more elements in array,
            // the negative number returned is the bitwise complement of the index of the first element
            // that is larger than value.
            //
            // If value is not found and value is greater than all elements in array,
            // the negative number returned is the bitwise complement of (the index of the last element plus 1).
            var idx = Array.BinarySearch(Fractions, t);

            if (idx < 0)
            {
                // t 不在 Fractions 里，此时 idx 的按位取反是 第一个比 t 大的元素 的索引
                idx = ~idx;
            }
            // 如果 idx >= 0 说明 t 在 Fractions 里，此时 idx 就是 t 的索引
            // 所以无论怎样都有 Fractions[idx-1] < t && Fractions[idx] >= t
            // Note: Fractions[i] 是 Segments[i] 的终点

            if (idx >= Segments.Length)
            {
                // t 超出了最后一段的终点，按理来说这不可能，因为越界情况已经处理掉了
                // 但可能会有舍入误差什么的……
                segmentT = 1.0;
                return Segments[^1];
            }

            if (idx == 0)
            {
                segmentT = t / Fractions[0];
                return Segments[0];
            }

            segmentT = (t - Fractions[idx - 1]) / (Fractions[idx] - Fractions[idx - 1]);
            return Segments[idx];
        }

        /// <summary>
        /// <p>计算路径总长</p>
        /// </summary>
        public double GetPathLength() => AccumulatedLengths[^1];

        /// <summary>
        /// 是否关于“A1 - C - A5”反射路径
        /// </summary>
        public bool Reflected { get; private set; } = false;
        /// <summary>
        /// 顺时针旋转路径，多少个 45 度
        /// </summary>
        public int Rotated45CW { get; private set; } = 0;

        private Complex _rotor = Complex.One;

        /// <summary>
        /// 反射并旋转整条路径，先反射再旋转
        /// </summary>
        /// <param name="reflect">是否关于“A1 - C - A5”反射</param>
        /// <param name="rotate45CW">顺时针旋转多少个 45 度，0~7</param>
        public void SetRotoreflection(bool reflect, int rotate45CW)
        {
            Rotated45CW = rotate45CW;
            Reflected = reflect;
            var angle = reflect ? (135 - rotate45CW * 45) : (-rotate45CW * 45);
            _rotor = Complex.FromPolarCoordinates(1, angle / 180.0 * Math.PI);
        }

        private Complex CalcRotoreflection(Complex z)
        {
            var x = Reflected ? Complex.Conjugate(z) : z;
            return x * _rotor;
        }

        /// <summary>
        /// <p>计算路径上某点的坐标，保证均匀插值</p>
        /// </summary>
        /// <param name="t">0 ~ 1 (both inclusive)</param>
        public virtual Complex GetPointAt(double t)
        {
            var segment = GetSegmentAt(t, out var segT);
            return CalcRotoreflection(segment.GetPointAt(segT));
        }

        /// <summary>
        /// <p>计算路径上某点处的有向切线，方向为路径前进方向</p>
        /// </summary>
        /// <param name="t">0 ~ 1 (both inclusive)</param>
        /// <returns>complex (magnitude = 1)</returns>
        public virtual Complex GetTangentAt(double t)
        {
            var segment = GetSegmentAt(t, out var segT);
            return CalcRotoreflection(segment.GetTangentAt(segT));
        }

        public virtual SlideEndShape GetEndShape()
        {
            var lastSegment = Segments[^1];
            if (lastSegment is CircleSegment circle)
            {
                return circle.IsCcw != Reflected ? SlideEndShape.CircleCCW : SlideEndShape.CircleCW;
            }

            if (lastSegment is ArcSegment arc)
            {
                return (arc.EndRadian > arc.StartRadian) != Reflected ? SlideEndShape.CircleCCW : SlideEndShape.CircleCW;
            }

            if (lastSegment is LogarithmicSpiralSegment spiral && spiral.IsCurve)
            {
                return spiral.IsCcw != Reflected
                    ? SlideEndShape.CircleCCW
                    : SlideEndShape.CircleCW;
            }

            return SlideEndShape.Straight;
        }
    }
}
