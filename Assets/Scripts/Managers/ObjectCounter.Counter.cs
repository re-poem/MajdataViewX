using MajdataViewX.Notes;
using MajdataViewX.Types.Notes;
using Cimai;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MajdataViewX.Managers
{
    public partial class ObjectCounter : MonoBehaviour
    {
        public bool AllFinished =>
            TapFinishedCount == TapSum &&
            HoldFinishedCount == HoldSum &&
            SlideFinishedCount == SlideSum &&
            TouchFinishedCount == TouchSum &&
            BreakFinishedCount == BreakSum;
        public int TapFinishedCount { get; private set; }
        public int HoldFinishedCount { get; private set; }
        public int SlideFinishedCount { get; private set; }
        public int TouchFinishedCount { get; private set; }
        public int BreakFinishedCount { get; private set; }
        public int NoteFinishedCount =>
            TapFinishedCount +
            HoldFinishedCount +
            SlideFinishedCount +
            TouchFinishedCount +
            BreakFinishedCount;

        public int TapSum { get; private set; }
        public int HoldSum { get; private set; }
        public int SlideSum { get; private set; }
        public int TouchSum { get; private set; }
        public int BreakSum { get; private set; }
        public int NoteSum { get; private set; }

        public long TotalNoteScore => TotalNoteBaseScore + TotalNoteExtraScore;
        public long TotalNoteBaseScore { get; private set; }
        public long TotalNoteExtraScore { get; private set; }

        public long CurrentNoteScore => CurrentNoteBaseScore + CurrentNoteExtraScore;
        public long CurrentNoteScoreClassic => CurrentNoteBaseScore + CurrentNoteExtraScoreClassic;
        public long CurrentNoteBaseScore { get; private set; }
        public long CurrentNoteExtraScore { get; private set; }
        public long CurrentNoteExtraScoreClassic { get; private set; }

        public long TotalLostNoteScore => LostNoteBaseScore + LostNoteExtraScore;
        public long TotalLostNoteScoreClassic => LostNoteBaseScore + LostNoteExtraScoreClassic;
        public long LostNoteBaseScore { get; private set; }
        public long LostNoteExtraScore { get; private set; }
        public long LostNoteExtraScoreClassic { get; private set; }

        double[] accRate = new double[5]
        {
            0.00,
            100.00,
            101.0000,
            100.0000,
            0.0000,
        };

        private double ClassicRateFromCount
        {
            get
            {
                var currentScore = TapFinishedCount +
                                      HoldFinishedCount * 2.0 +
                                      SlideFinishedCount * 3.0 +
                                      TouchFinishedCount * 1.0 +
                                      BreakFinishedCount * 5.2;

                var totalScore = TapSum +
                                    HoldSum * 2.0 +
                                    SlideSum * 3.0 +
                                    TouchSum * 1.0 +
                                    BreakSum * 5.0;

                if (totalScore <= 0) return 0.0;
                var rate = (currentScore / totalScore) * 100.0;
                return Math.Round(rate, 4);
            }
        }

        private double DeluxeRateFromCount
        {
            get
            {
                var currentDeluxe = TapFinishedCount +
                                       HoldFinishedCount * 2.0 +
                                       SlideFinishedCount * 3.0 +
                                       TouchFinishedCount * 1.0 +
                                       BreakFinishedCount * 5.0;

                var totalDeluxe = TapSum +
                                     HoldSum * 2.0 +
                                     SlideSum * 3.0 +
                                     TouchSum * 1.0 +
                                     BreakSum * 5.0;

                var baseRate = totalDeluxe > 0 ? (currentDeluxe / totalDeluxe) * 100.0 : 0.0;
                var breakBonus = BreakSum > 0 ? (double)BreakFinishedCount / BreakSum : 0.0;
                var finalRate = baseRate + breakBonus;

                return Math.Round(finalRate, 4);
            }
        }
        long cPerfectCount = 0;
        long perfectCount = 0;
        long greatCount = 0;
        long goodCount = 0;
        long missCount = 0;

        long fastCount = 0;
        long lateCount = 0;

        long totalDXScore = 0;
        long curDXScore = 0;
        long lostDXScore = 0;

        long combo = 0;

        readonly Dictionary<JudgeGrade, int> judgedTapCount = new();
        readonly Dictionary<JudgeGrade, int> judgedHoldCount = new();
        readonly Dictionary<JudgeGrade, int> judgedTouchCount = new();
        readonly Dictionary<JudgeGrade, int> judgedTouchHoldCount = new();
        readonly Dictionary<JudgeGrade, int> judgedSlideCount = new();
        readonly Dictionary<JudgeGrade, int> judgedBreakCount = new();
        readonly Dictionary<JudgeGrade, int> totalJudgedCount = new();

        readonly List<(double Timing, int Numerator, int Denominator)> meterList = new();
        readonly List<(double Timing, float Bpm)> bpmList = new();

        private NativeList<ReportResultEntry> reportRequests = new(65536, Allocator.Persistent);
        public NativeList<ReportResultEntry>.ParallelWriter ReportRequestsWriter => reportRequests.AsParallelWriter();

        public void CountNoteSum(SimaiChart chart)
        {
            foreach (var timing in chart.Timings)
            {
                foreach (var note in chart.Notes.Slice((int)timing.NotesFirst, (int)timing.NotesCount))
                {
                    if (!note.IsBreak)
                    {
                        switch (note.Type)
                        {
                            case SimaiNoteType.TAP:
                                TapSum++;
                                break;
                            case SimaiNoteType.HOLD:
                            case SimaiNoteType.TOUCHHOLD:
                                HoldSum++;
                                break;
                            case SimaiNoteType.SLIDE:
                                SlideSum++;
                                break;
                            case SimaiNoteType.TOUCH:
                                TouchSum++;
                                break;
                        }
                    }
                    else
                    {
                        BreakSum++;
                    }
                }
            }
            NoteSum = TapSum + HoldSum + TouchSum + BreakSum + SlideSum;
            TotalNoteBaseScore = (TapSum + TouchSum) * 500 + HoldSum * 1000 + SlideSum * 1500 + BreakSum * 2500;
            TotalNoteExtraScore = BreakSum * 100;
            totalDXScore = NoteSum * 3;
        }

        public void CountIgnoreNoteCountAsync(SimaiChart chart, double ignore)
        {
            foreach (var timing in chart.Timings)
            {
                if (timing.Time >= ignore) continue;
                foreach (var note in chart.Notes.Slice((int)timing.NotesFirst, (int)timing.NotesCount))
                {
                    if (note.Type is SimaiNoteType.SLIDE && 
                        timing.Time + note.SlideShootDelay >= ignore)
                        continue;
                    if (!note.IsBreak)
                    {
                        switch (note.Type)
                        {
                            case SimaiNoteType.TAP:
                                TapFinishedCount++;
                                break;
                            case SimaiNoteType.HOLD:
                            case SimaiNoteType.TOUCHHOLD:
                                HoldFinishedCount++;
                                break;
                            case SimaiNoteType.SLIDE:
                                SlideFinishedCount++;
                                break;
                            case SimaiNoteType.TOUCH:
                                TouchFinishedCount++;
                                break;
                        }
                    }
                    else
                    {
                        BreakFinishedCount++;
                    }

                    if (NoteHelper.IsSimulated) continue;
                    // ReportResult
                    var type = note.Type;
                    var judge = JudgeGrade.LateCritical;
                    var isBreak = note.IsBreak;
                    if (NoteHelper.Settings.AutoPlayMode is Types.Enums.AutoPlayMode.Random)
                    {
                        judge = (JudgeGrade)Base.MajBurst.GlobalRandom.NextInt((int)JudgeGrade.FastGood, (int)JudgeGrade.Miss);
                        judge = note.IsMine
                            ? judge < JudgeGrade.FastPerfect3rd
                                ? JudgeGrade.Miss
                                : JudgeGrade.LateCritical
                            : judge;
                    }
                    UpdateNoteScoreCount(type, judge, isBreak);
                    UpdateJudgeCountAndDXScore(judge);
                    UpdateFastLateCount(judge);
                }
            }
        }

        public void ProcessReportRequests()
        {
            if (reportRequests.Length == 0) return;

            var length = reportRequests.Length;
            for (int i = 0; i < length; i++)
            {
                var entry = reportRequests[i];
                ReportResult(entry.NoteType, entry.Grade, entry.IsBreak, updateAcc: false);
            }
            UpdateAccRate();
            reportRequests.Clear();
        }

        public void ReportResult(
            SimaiNoteType type,
            JudgeGrade judgeGrade,
            bool isBreak = false,
            bool updateAcc = true)
        {
            UpdateNoteScoreCount(type, judgeGrade, isBreak);
            UpdateNoteFinishedCount(type, judgeGrade, isBreak);
            UpdateJudgeCountAndDXScore(judgeGrade);
            UpdateFastLateCount(judgeGrade);
            if (updateAcc) UpdateAccRate();
        }

        private void UpdateNoteScoreCount(SimaiNoteType type, JudgeGrade judge, bool isBreak)
        {
            var baseScore = 500;

            switch (type)
            {
                case SimaiNoteType.TAP:
                case SimaiNoteType.TOUCH:
                    baseScore = 500;
                    break;
                case SimaiNoteType.HOLD:
                case SimaiNoteType.TOUCHHOLD:
                    baseScore = 1000;
                    break;
                case SimaiNoteType.SLIDE:
                    baseScore = 1500;
                    break;
            }

            if (!isBreak)
            {
                switch (judge)
                {
                    case JudgeGrade.Miss:
                    case JudgeGrade.TooFast:
                        LostNoteBaseScore += baseScore;
                        break;
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        CurrentNoteBaseScore += (long)(baseScore * 0.5);
                        LostNoteBaseScore += (long)(baseScore * 0.5);
                        break;
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.LateGreat2nd:
                    case JudgeGrade.LateGreat1st:
                    case JudgeGrade.FastGreat1st:
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.FastGreat3rd:
                        CurrentNoteBaseScore += (long)(baseScore * 0.8);
                        LostNoteBaseScore += (long)(baseScore * 0.2);
                        break;
                    default:
                        CurrentNoteBaseScore += baseScore;
                        break;
                }
            }
            else
            {

                switch (judge)
                {
                    case JudgeGrade.Miss:
                    case JudgeGrade.TooFast:
                        LostNoteBaseScore += 2500;
                        LostNoteExtraScore += 100;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        CurrentNoteBaseScore += 1000;
                        CurrentNoteExtraScore += 30;
                        LostNoteBaseScore += 1500;
                        LostNoteExtraScore += 70;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.FastGreat3rd:
                        CurrentNoteBaseScore += 1250;
                        CurrentNoteExtraScore += 40;
                        LostNoteBaseScore += 1250;
                        LostNoteExtraScore += 60;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.LateGreat2nd:
                        CurrentNoteBaseScore += 1500;
                        CurrentNoteExtraScore += 40;
                        LostNoteBaseScore += 1000;
                        LostNoteExtraScore += 60;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.LateGreat1st:
                    case JudgeGrade.FastGreat1st:
                        CurrentNoteBaseScore += 2000;
                        CurrentNoteExtraScore += 40;
                        LostNoteBaseScore += 500;
                        LostNoteExtraScore += 60;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.LatePerfect3rd:
                    case JudgeGrade.FastPerfect3rd:
                        CurrentNoteBaseScore += 2500;
                        CurrentNoteExtraScore += 50;
                        LostNoteExtraScore += 50;
                        LostNoteExtraScoreClassic += 100;
                        break;
                    case JudgeGrade.LatePerfect2nd:
                    case JudgeGrade.FastPerfect2nd:
                        CurrentNoteBaseScore += 2500;
                        CurrentNoteExtraScore += 75;
                        CurrentNoteExtraScoreClassic += 50;
                        LostNoteExtraScore += 25;
                        LostNoteExtraScoreClassic += 50;
                        break;
                    case JudgeGrade.LateCritical:
                    case JudgeGrade.FastCritical:
                        CurrentNoteBaseScore += 2500;
                        CurrentNoteExtraScore += 100;
                        CurrentNoteExtraScoreClassic += 100;
                        LostNoteExtraScore += 0;
                        LostNoteExtraScoreClassic += 0;
                        break;
                }
            }
        }
        private void UpdateAccRate()
        {
            if (TotalNoteBaseScore == 0) return;

            Span<decimal> newAccRate = stackalloc decimal[5];

            newAccRate[0] = CurrentNoteScoreClassic / (decimal)TotalNoteBaseScore;
            newAccRate[1] = (TotalNoteBaseScore - LostNoteBaseScore + CurrentNoteExtraScoreClassic) / (decimal)TotalNoteBaseScore;
            newAccRate[2] = ((TotalNoteBaseScore - LostNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((TotalNoteExtraScore - LostNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));
            newAccRate[3] = ((TotalNoteBaseScore - LostNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((CurrentNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));
            newAccRate[4] = ((CurrentNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((CurrentNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));

            accRate[0] = decimal.ToDouble(newAccRate[0] * 100);
            accRate[1] = decimal.ToDouble(newAccRate[1] * 100);
            accRate[2] = decimal.ToDouble(newAccRate[2] * 100);
            accRate[3] = decimal.ToDouble(newAccRate[3] * 100);
            accRate[4] = decimal.ToDouble(newAccRate[4] * 100);
        }
        private void UpdateNoteFinishedCount(SimaiNoteType type, JudgeGrade judge, bool isBreak)
        {
            if (isBreak)
            {
                judgedBreakCount[judge]++;
                BreakFinishedCount++;
            }
            else
            {
                switch (type)
                {
                    case SimaiNoteType.TAP:
                        {
                            judgedTapCount[judge]++;
                            TapFinishedCount++;
                        }
                        break;
                    case SimaiNoteType.SLIDE:
                        {
                            judgedSlideCount[judge]++;
                            SlideFinishedCount++;
                        }
                        break;
                    case SimaiNoteType.HOLD:
                        {
                            judgedHoldCount[judge]++;
                            HoldFinishedCount++;
                        }
                        break;
                    case SimaiNoteType.TOUCH:
                        {
                            judgedTouchCount[judge]++;
                            TouchFinishedCount++;
                        }
                        break;
                    case SimaiNoteType.TOUCHHOLD:
                        {
                            judgedTouchHoldCount[judge]++;
                            HoldFinishedCount++;
                        }
                        break;
                }
            }
            totalJudgedCount[judge]++;
        }
        private void UpdateJudgeCountAndDXScore(JudgeGrade judge)
        {
            if (judge is not (JudgeGrade.Miss or JudgeGrade.TooFast))
                combo++;

            switch (judge)
            {
                case JudgeGrade.Miss:
                case JudgeGrade.TooFast:
                    missCount++;
                    combo = 0;
                    lostDXScore -= 3;
                    break;
                case JudgeGrade.LateCritical:
                case JudgeGrade.FastCritical:
                    cPerfectCount++;
                    curDXScore += 3;
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                    perfectCount++;
                    curDXScore += 2;
                    lostDXScore -= 1;
                    break;
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat1st:
                case JudgeGrade.FastGreat1st:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat3rd:
                    curDXScore += 1;
                    lostDXScore -= 2;
                    greatCount++;
                    break;
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    lostDXScore -= 3;
                    goodCount++;
                    break;
            }
        }
        private void UpdateFastLateCount(JudgeGrade judge)
        {
            if (judge is JudgeGrade.Miss or JudgeGrade.TooFast)
                return;
            if (judge < JudgeGrade.FastCritical)
                fastCount++;
            else if (judge > JudgeGrade.LateCritical)
                lateCount++;
        }

        public void ResetCur()
        {
            for (var i = 0; i <= 15; i++)
            {
                judgedTapCount[(JudgeGrade)i] = 0;
                judgedHoldCount[(JudgeGrade)i] = 0;
                judgedTouchCount[(JudgeGrade)i] = 0;
                judgedTouchHoldCount[(JudgeGrade)i] = 0;
                judgedSlideCount[(JudgeGrade)i] = 0;
                judgedBreakCount[(JudgeGrade)i] = 0;
                totalJudgedCount[(JudgeGrade)i] = 0;
            }

            accRate[0] = 0.00;
            accRate[1] = 100.00;
            accRate[2] = 101.0000;
            accRate[3] = 100.0000;
            accRate[4] = 0.0000;

            TapFinishedCount = 0;
            HoldFinishedCount = 0;
            SlideFinishedCount = 0;
            TouchFinishedCount = 0;
            BreakFinishedCount = 0;

            CurrentNoteBaseScore = 0;
            CurrentNoteExtraScore = 0;
            CurrentNoteExtraScoreClassic = 0;
            LostNoteBaseScore = 0;
            LostNoteExtraScore = 0;
            LostNoteExtraScoreClassic = 0;

            cPerfectCount = 0;
            perfectCount = 0;
            greatCount = 0;
            goodCount = 0;
            missCount = 0;

            fastCount = 0;
            lateCount = 0;

            curDXScore = 0;
            lostDXScore = 0;

            combo = 0;
        }

        public void ResetLoaded()
        {
            TapSum = 0;
            HoldSum = 0;
            SlideSum = 0;
            TouchSum = 0;
            BreakSum = 0;

            TotalNoteBaseScore = 0;
            TotalNoteExtraScore = 0;

            totalDXScore = 0;

            meterList.Clear();
            bpmList.Clear();
        }
    }

    public struct ReportResultEntry
    {
        public JudgeGrade Grade;
        public bool IsBreak;
        public SimaiNoteType NoteType;
    }
}