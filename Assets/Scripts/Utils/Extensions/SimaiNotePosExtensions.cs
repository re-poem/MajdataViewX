using Cimai;
using System;
using System.Collections.Generic;
using System.Text;
using MajdataViewX.Types.Input;

namespace MajdataViewX.Utils.Extensions
{
    public static class SimaiNotePosExtensions
    {
        public static int GetPosNum(this SimaiNotePos pos)
        {
            return pos switch
            {
                SimaiNotePos.NONE => 0,
                >= SimaiNotePos.BTN1 and <= SimaiNotePos.BTN8 => (int)pos,
                >= SimaiNotePos.A1 and <= SimaiNotePos.A8 => (int)pos - (int)SimaiNotePos.A1 + 1,
                >= SimaiNotePos.B1 and <= SimaiNotePos.B8 => (int)pos - (int)SimaiNotePos.B1 + 1,
                SimaiNotePos.C1 or SimaiNotePos.C2 => 1,
                >= SimaiNotePos.D1 and <= SimaiNotePos.D8 => (int)pos - (int)SimaiNotePos.D1 + 1,
                >= SimaiNotePos.E1 and <= SimaiNotePos.E8 => (int)pos - (int)SimaiNotePos.E1 + 1,
                _ => -1,
            };
        }
        public static SensorType GetSensor(this SimaiNotePos pos)
        {
            return pos switch
            {
                SimaiNotePos.NONE => SensorType.Invalid,
                >= SimaiNotePos.BTN1 and <= SimaiNotePos.BTN8 => (SensorType)((int)pos - (int)SimaiNotePos.BTN1),
                >= SimaiNotePos.A1 and <= SimaiNotePos.B8 => (SensorType)((int)pos - (int)SimaiNotePos.A1),
                SimaiNotePos.C1 or SimaiNotePos.C2 => SensorType.C,
                >= SimaiNotePos.D1 and <= SimaiNotePos.E8 => (SensorType)((int)pos - (int)SimaiNotePos.D1 + (int)SensorType.D1),
                _ => SensorType.Invalid
            };
        }
    }
}