using Cysharp.Text;
using MajdataViewX.Notes;
using MajdataViewX.Types;
using MajdataViewX.Types.Enums;
using Cimai;
using System;
using MajdataViewX.Utils;
using TMPro;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public partial class ObjectCounter : MonoBehaviour
    {
        private static readonly Utf16PreparedFormat<double> AchievementFormat =
            ZString.PrepareUtf16<double>("{0:0.0000}%");
        private static readonly Utf16PreparedFormat<long> IntegerFormat =
            ZString.PrepareUtf16<long>("{0}");

        private Utf16ValueStringBuilder outputBuilder = ZString.CreateStringBuilder();

        [SerializeField]
        Color AchievementDudColor;
        [SerializeField]
        Color AchievementBronzeColor;
        [SerializeField]
        Color AchievementSilverColor;
        [SerializeField]
        Color AchievementGoldColor;

        public BgInfoDisplay TextMode { get; private set; }
        public UIType? CurrentUIType { get; private set; } = null;

        //Legacy UI
        [SerializeField]
        private GameObject legacyUIRoot;
        [SerializeField]
        private TextMeshProUGUI timeDisplay;
        [SerializeField]
        private TextMeshProUGUI objectCount;
        [SerializeField]
        private TextMeshProUGUI objectRate;
        [SerializeField]
        private TextMeshProUGUI judgeResultCount;

        //Trg UI
        [SerializeField]
        private GameObject trgUIRoot;
        [SerializeField]
        private TextMeshProUGUI objTime;
        [SerializeField]
        private TextMeshProUGUI objRate;
        [SerializeField]
        private TextMeshProUGUI objCombo;
        [SerializeField]
        private TextMeshProUGUI objNoteCount;
        [SerializeField]
        private TextMeshProUGUI objMeter;
        [SerializeField]
        private TextMeshProUGUI objBpm;
        [SerializeField]
        private TextMeshProUGUI objBpmRange;
        [SerializeField]
        private TextMeshProUGUI objJudgeResult;
        [SerializeField]
        private TextMeshProUGUI objAutoMode;

        //Main Output
        [SerializeField]
        private TextMeshProUGUI statusAchievement;
        [SerializeField]
        private TextMeshProUGUI statusCombo;
        [SerializeField]
        private TextMeshProUGUI statusDXScore;
        [SerializeField]
        private TextMeshProUGUI headerAchievement;
        [SerializeField]
        private TextMeshProUGUI headerCombo;
        [SerializeField]
        private TextMeshProUGUI headerDXScore;

        [SerializeField]
        private TMP_FontAsset LegacyUIComboFont;
        [SerializeField]
        private TMP_FontAsset TrgUIComboFont;
        [SerializeField]
        private TMP_FontAsset LegacyUIComboHeaderFont;
        [SerializeField]
        private TMP_FontAsset TrgUIComboHeaderFont;

        public void Setting(BgInfoDisplay mode, UIType type)
        {
            TextMode = mode;
            switch (mode)
            {
                case BgInfoDisplay.None:
                    statusCombo.gameObject.SetActive(false);
                    statusAchievement.gameObject.SetActive(false);
                    statusDXScore.gameObject.SetActive(false);
                    break;
                case BgInfoDisplay.Combo:
                    statusCombo.gameObject.SetActive(true);
                    statusAchievement.gameObject.SetActive(false);
                    statusDXScore.gameObject.SetActive(false);
                    break;
                case BgInfoDisplay.Achievement_101:
                case BgInfoDisplay.Achievement_100:
                case BgInfoDisplay.Achievement:
                case BgInfoDisplay.AchievementClassical:
                case BgInfoDisplay.AchievementClassical_100:
                case BgInfoDisplay.S_Border:
                case BgInfoDisplay.SS_Border:
                case BgInfoDisplay.SSS_Border:
                    statusCombo.gameObject.SetActive(false);
                    statusAchievement.gameObject.SetActive(true);
                    statusDXScore.gameObject.SetActive(false);
                    break;
                case BgInfoDisplay.DXScore:
                case BgInfoDisplay.DXScore_Dec:
                    statusCombo.gameObject.SetActive(false);
                    statusAchievement.gameObject.SetActive(false);
                    statusDXScore.gameObject.SetActive(true);
                    break;
            }
            if (type is UIType.TrgUI)
            {
                switch (NoteHelper.Settings.AutoPlayMode)
                {
                    case AutoPlayMode.Enable:
                        objAutoMode.text = "ENABLED\nNONE";
                        break;
                    case AutoPlayMode.DJAutoButton:
                        objAutoMode.text = "ENABLED\nDJAuto (Btn)";
                        break;
                    case AutoPlayMode.DJAutoSensor:
                        objAutoMode.text = "ENABLED\nDJAuto";
                        break;
                    case AutoPlayMode.Random:
                        objAutoMode.text = "ENABLED\nRANDOM";
                        break;
                    case AutoPlayMode.Disable:
                        objAutoMode.text = "DISABLED\nNONE";
                        break;
                }
            }
            if (CurrentUIType == type) return;
            switch (type)
            {
                case UIType.Legacy:
                    {
                        CurrentUIType = type;
                        legacyUIRoot.SetActive(true);
                        trgUIRoot.SetActive(false);

                        statusAchievement.font = LegacyUIComboFont;
                        headerAchievement.font = LegacyUIComboHeaderFont;
                        statusCombo.font = LegacyUIComboFont;
                        headerCombo.font = LegacyUIComboHeaderFont;
                        statusDXScore.font = LegacyUIComboFont;
                        headerDXScore.font = LegacyUIComboHeaderFont;
                        break;
                    }
                case UIType.TrgUI:
                    {
                        CurrentUIType = type;
                        legacyUIRoot.SetActive(false);
                        trgUIRoot.SetActive(true);

                        statusAchievement.font = TrgUIComboFont;
                        headerAchievement.font = TrgUIComboHeaderFont;
                        statusCombo.font = TrgUIComboFont;
                        headerCombo.font = TrgUIComboHeaderFont;
                        statusDXScore.font = TrgUIComboFont;
                        headerDXScore.font = TrgUIComboHeaderFont;
                        break;
                    }
            }
        }

        public void ReportMeterBpm(ReadOnlySpan<char> fumen, ReadOnlySpan<SimaiTiming> timings)
        {
            meterList.Clear();
            bpmList.Clear();

            foreach (var timing in timings)
            {
                meterList = FumenHelper.ParseSignature(fumen);

                var lastBpm = bpmList.Count > 0 ? bpmList[^1].Bpm : 0;
                if (timing.Bpm != lastBpm)
                    bpmList.Add((timing.Time, timing.Bpm));
            }

            var min = bpmList.Count > 0 ? bpmList[0].Bpm : 0;
            var max = min;
            foreach (var (_, bpm) in bpmList)
            {
                if (bpm < min) min = bpm;
                if (bpm > max) max = bpm;
            }

            outputBuilder.Clear();
            outputBuilder.Append(min);
            outputBuilder.Append(" ～ ");
            outputBuilder.Append(max);
            SetOutputText(objBpmRange);
        }

        private void UpdateOutput()
        {
            OutputMain();
            OutputSide();
            OutputTime();
        }

        private void OutputMain()
        {
            switch (TextMode)
            {
                case BgInfoDisplay.Combo:
                    {
                        outputBuilder.Clear();
                        if (combo > 0)
                            IntegerFormat.FormatTo(ref outputBuilder, combo);
                        SetOutputText(statusCombo);
                    }
                    break;
                case BgInfoDisplay.Achievement_101:
                    {
                        UpdateAchievement(accRate[2]);
                    }
                    break;
                case BgInfoDisplay.Achievement_100:
                    {
                        UpdateAchievement(accRate[3]);
                    }
                    break;
                case BgInfoDisplay.Achievement:
                    {
                        UpdateAchievement(accRate[4]);
                    }
                    break;
                case BgInfoDisplay.AchievementClassical:
                    {
                        UpdateAchievement(accRate[0]);
                    }
                    break;
                case BgInfoDisplay.AchievementClassical_100:
                    {
                        UpdateAchievement(accRate[1]);
                    }
                    break;
                case BgInfoDisplay.DXScore:
                    {
                        outputBuilder.Clear();
                        IntegerFormat.FormatTo(ref outputBuilder, curDXScore);
                        SetOutputText(statusDXScore);
                    }
                    break;
                case BgInfoDisplay.DXScore_Dec:
                    {
                        outputBuilder.Clear();
                        IntegerFormat.FormatTo(ref outputBuilder, totalDXScore + lostDXScore);
                        SetOutputText(statusDXScore);
                    }
                    break;
                case BgInfoDisplay.S_Border:
                    {
                        var rate = accRate[2] - 97;
                        UpdateBorder(rate, statusAchievement);
                    }
                    break;
                case BgInfoDisplay.SS_Border:
                    {
                        var rate = accRate[2] - 99;
                        UpdateBorder(rate, statusAchievement);
                    }
                    break;
                case BgInfoDisplay.SSS_Border:
                    {
                        var rate = accRate[2] - 100;
                        UpdateBorder(rate, statusAchievement);
                    }
                    break;
            }

            void UpdateAchievement(double rate)
            {
                outputBuilder.Clear();
                AchievementFormat.FormatTo(ref outputBuilder, rate);
                SetOutputText(statusAchievement);
                UpdateAchievementColor(rate);
            }

            void UpdateAchievementColor(double rate)
            {
                var newColor = rate switch
                {
                    >= 100 => AchievementGoldColor,
                    >= 97f => AchievementSilverColor,
                    >= 80f => AchievementBronzeColor,
                    _ => AchievementDudColor
                };

                if (statusAchievement.color != newColor)
                    statusAchievement.color = newColor;
                if (headerAchievement.color != newColor)
                    headerAchievement.color = newColor;
            }

            void UpdateBorder(double rate, TextMeshProUGUI textElement)
            {
                if (rate <= 0)
                {
                    textElement.gameObject.SetActive(false);
                    return;
                }

                textElement.gameObject.SetActive(true);
                outputBuilder.Clear();
                AchievementFormat.FormatTo(ref outputBuilder, rate);
                SetOutputText(textElement);
            }
        }

        private void OutputSide()
        {
            if (CurrentUIType is UIType.Legacy)
            {
                outputBuilder.Clear();
                outputBuilder.Append("TAP: ");
                outputBuilder.Append(TapFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(TapSum);
                outputBuilder.Append("\nHOD: ");
                outputBuilder.Append(HoldFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(HoldSum);
                outputBuilder.Append("\nSLD: ");
                outputBuilder.Append(SlideFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(SlideSum);
                outputBuilder.Append("\nTOH: ");
                outputBuilder.Append(TouchFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(TouchSum);
                outputBuilder.Append("\nBRK: ");
                outputBuilder.Append(BreakFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(BreakSum);
                outputBuilder.Append("\nALL: ");
                outputBuilder.Append(NoteFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(NoteSum);
                outputBuilder.Append("\nMOD: ");
                outputBuilder.Append(NoteHelper.Settings.AutoPlayMode switch
                {
                    AutoPlayMode.Enable => "Enable",
                    AutoPlayMode.DJAutoButton => "DJAuto (Btn)",
                    AutoPlayMode.DJAutoSensor => "DJAuto",
                    AutoPlayMode.Random => "Random",
                    AutoPlayMode.Disable => "Disable",
                    _ => "INVALID"
                });
                SetOutputText(objectCount);

                outputBuilder.Clear();
                outputBuilder.Append("FiNALE  Rate:\n");
                outputBuilder.Append(ClassicRateFromCount, "000.00");
                outputBuilder.Append("   %\nDELUXE Rate:\n");
                outputBuilder.Append(DeluxeRateFromCount, "000.0000");
                outputBuilder.Append(" % ");
                SetOutputText(objectRate);

                outputBuilder.Clear();
                outputBuilder.Append(cPerfectCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(perfectCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(greatCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(goodCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(missCount);
                outputBuilder.Append("\n\n");
                outputBuilder.Append(fastCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(lateCount);
                SetOutputText(judgeResultCount);
            }
            else
            {
                outputBuilder.Clear();
                outputBuilder.Append(TapFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(TapSum);
                outputBuilder.Append('\n');
                outputBuilder.Append(HoldFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(HoldSum);
                outputBuilder.Append('\n');
                outputBuilder.Append(SlideFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(SlideSum);
                outputBuilder.Append('\n');
                outputBuilder.Append(TouchFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(TouchSum);
                outputBuilder.Append('\n');
                outputBuilder.Append(BreakFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(BreakSum);
                outputBuilder.Append('\n');
                outputBuilder.Append(NoteFinishedCount);
                outputBuilder.Append(" / ");
                outputBuilder.Append(NoteSum);
                SetOutputText(objNoteCount);

                var rate = DeluxeRateFromCount;
                var intPart = (int)rate;
                var fracPart = (rate - intPart) * 10000;
                outputBuilder.Clear();
                outputBuilder.Append("<size=7.5>");
                outputBuilder.Append(intPart);
                outputBuilder.Append("</size><size=5.7>.");
                outputBuilder.Append(fracPart, "0000");
                outputBuilder.Append("</size> <size=3.7>%</size>");
                SetOutputText(objRate);

                outputBuilder.Clear();
                outputBuilder.Append(cPerfectCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(perfectCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(greatCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(goodCount);
                outputBuilder.Append('\n');
                outputBuilder.Append(missCount);
                SetOutputText(objJudgeResult);

                outputBuilder.Clear();
                IntegerFormat.FormatTo(ref outputBuilder, combo);
                SetOutputText(objCombo);

                var time = _timeProvider.NoteTime;
                for (var i = meterList.Count - 1; i >= 0; i--)
                {
                    var meter = meterList[i];
                    if (meter.Time > time) continue;

                    outputBuilder.Clear();
                    outputBuilder.Append(meter.Numerator);
                    outputBuilder.Append('\n');
                    outputBuilder.Append(meter.Denominator);
                    SetOutputText(objMeter);
                    break;
                }
                for (var i = bpmList.Count - 1; i >= 0; i--)
                {
                    var bpm = bpmList[i];
                    if (bpm.Timing > time) continue;

                    outputBuilder.Clear();
                    outputBuilder.Append(bpm.Bpm);
                    SetOutputText(objBpm);
                    break;
                }
            }
        }
        private void OutputTime()
        {
            var ctime = _timeProvider.AudioTime;
            var timeNowInt = (int)ctime;
            var minute = timeNowInt / 60;
            var second = timeNowInt - 60 * minute;
            double milli = (ctime - timeNowInt) * 10000;

            outputBuilder.Clear();
            if (ctime < 0)
            {
                minute = Math.Abs(minute);
                second = Math.Abs(second);
                milli = Math.Abs(milli);
                outputBuilder.Append('-');
                outputBuilder.Append(minute);
                outputBuilder.Append(':');
                outputBuilder.Append(second, "00");
                outputBuilder.Append('.');
                outputBuilder.Append(milli / 10, "000");
            }
            else
            {
                outputBuilder.Append(minute);
                outputBuilder.Append(':');
                outputBuilder.Append(second, "00");
                outputBuilder.Append('.');
                outputBuilder.Append(milli, "0000");
            }

            if (CurrentUIType == UIType.Legacy)
                SetOutputText(timeDisplay);
            else
                SetOutputText(objTime);
        }

        private void SetOutputText(TMP_Text text)
        {
            var chars = outputBuilder.AsArraySegment();
            text.SetCharArray(chars.Array, chars.Offset, chars.Count);
        }
    }
}