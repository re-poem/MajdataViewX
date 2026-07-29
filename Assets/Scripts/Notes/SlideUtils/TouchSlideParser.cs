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
        public static ParametricSlidePath Parse(int startPos, ReadOnlySpan<char> shape, int endPos)
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
                return path
                    .LineToPoint(end)
                    .GeneratePath();
            }

            if (shape[0] is '<' or '>')
            {
                return path
                    .LogarithmicSpiralTo(end, MajGeometry.PointCenter(), shape[0] == '<')
                    .GeneratePath();
            }

            throw new ArgumentException("Not supported Touch-Slide");
        }
    }
}
