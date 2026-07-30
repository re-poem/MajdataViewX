using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using MajdataViewX.Base;
using Unity.Mathematics;

namespace Notes.SlideUtils
{
    public static class TouchSlideParser
    {
        public static ParametricSlidePath Parse(int startPos, ReadOnlySpan<char> shape, int endPos, out bool canJudge)
        {
            static float2 ParsePos(int pos) => pos < 8
                ? MajPos.GetBtnPos(pos)
                : MajPos.GetAreaPos((SensorType)(pos - 8));
            var _start = ParsePos(startPos);
            var _end = ParsePos(endPos);
            var start = new Complex(_start.x, _start.y);
            var end = new Complex(_end.x, _end.y);

            var path = SlidePathConstructor.BeginAt(start);

            if (shape[0] == '-')
            {
                if (startPos == endPos)
                {
                    throw new ArgumentException("Not supported Touch-Slide");
                }

                if (startPos < 8 && endPos == startPos + 8 || endPos < 8 && startPos == endPos + 8)
                {
                    // 1-A1 这种东西
                    throw new ArgumentException("Not supported Touch-Slide");
                }
                
                // 只有连接 ABC 区的直线可以写判定区
                canJudge = startPos < (int)SensorType.D1 + 8 && endPos < (int)SensorType.D1 + 8;
                
                return path
                    .LineToPoint(end)
                    .GeneratePath();
            }

            if (shape[0] is '<' or '>')
            {
                // 只有 AB 区里的圆弧可以写判定区，或者 1>A1 其实也行
                // 也就是说，起终点的整数 id 要么都在 0~15，要么都在 16~23
                // 所以可以先检查 < 24，然后擦掉最后 4 bits 再比较
                canJudge = startPos < (int)SensorType.C + 8
                           && endPos < (int)SensorType.C + 8
                           && (startPos & ~0xF) == (endPos & ~0xF);

                return path
                    .LogarithmicSpiralTo(end, MajGeometry.PointCenter(), shape[0] == '<')
                    .GeneratePath();
            }

            throw new ArgumentException("Not supported Touch-Slide");
        }
    }
}
