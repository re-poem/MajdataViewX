using System;
using System.Collections.Generic;
using System.Globalization;

namespace MajdataViewX.Utils
{
    public readonly struct Signature
    {
        public readonly double Time;
        public readonly int Numerator;
        public readonly int Denominator;

        public Signature(double time, int numerator, int denominator)
        {
            Time = time;
            Numerator = numerator;
            Denominator = denominator;
        }
    }

    internal static class FumenHelper
    {
        /// <summary>fumen 里所有拍号生效点，按时间排序；首个元素恒为 t=0 的 4/4。</summary>
        public static List<Signature> ParseSignature(ReadOnlySpan<char> fumen)
        {
            var result = new List<Signature> { new(0, 4, 4) };

            var time = 0d;
            double? bpm = null;
            double? beats = null;
            var beatSeconds = 0d;

            var pos = 0;
            while (pos < fumen.Length)
            {
                var rest = fumen[pos..];

                if (rest.StartsWith("||")) // 注释整行跳过：可能是拍号，里面的逗号不算拍
                {
                    var newline = rest.IndexOf('\n');
                    var comment = (newline < 0 ? rest : rest[..newline]).Trim(); // 顺手去掉 CRLF 的 \r
                    pos += newline < 0 ? rest.Length : newline + 1;

                    // ||s7/8 → 7 / 8；格式不对就整行忽略，跟上一拍同拍号就跳过
                    if (comment.Length > 3 && comment[2] == 's')
                    {
                        var spec = comment[3..];
                        var slash = spec.IndexOf('/');
                        var last = result[^1];
                        if (slash >= 1 &&
                            int.TryParse(spec[..slash], out var numerator) &&
                            int.TryParse(spec[(slash + 1)..], out var denominator) &&
                            (last.Numerator != numerator || last.Denominator != denominator))
                        {
                            // 第一拍就换拍号：默认那条 4/4 从没生效过，直接顶掉，别留两条同时间的
                            if (time <= 0) result[0] = new(0, numerator, denominator);
                            else result.Add(new(time, numerator, denominator));
                        }
                    }

                    continue;
                }

                switch (rest[0])
                {
                    case '(' or '{': // (bpm) {beats}
                    {
                        var end = rest.IndexOf(rest[0] == '(' ? ')' : '}');
                        if (end > 1 && double.TryParse(rest[1..end].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var value))
                        {
                            if (rest[0] == '(') bpm = value;
                            else beats = value;
                            beatSeconds = bpm is { } b && beats is { } n ? 60 / b * 4 / n : 0;
                        }

                        pos += end < 0 ? rest.Length : end + 1; // 没闭合就当到结尾
                        break;
                    }
                    case '<': // <HS*1> / <SV*1>
                    {
                        var end = rest.IndexOf('>');
                        pos += end < 0 ? rest.Length : end + 1;
                        break;
                    }
                    case ',':
                        pos++;
                        time += beatSeconds;
                        break;
                    default:
                        pos++;
                        break;
                }
            }

            return result;
        }
    }
}
