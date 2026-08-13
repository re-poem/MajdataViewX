using MajdataViewX.Managers;
using MajdataViewX.Types.Input;
using System.Collections.Generic;
using Unity.Burst;

namespace MajdataViewX.Base
{
    public static class MajCtx
    {
#pragma warning disable IDE1006 // 命名样式
        public static AllPerfectManager _allPerfectManager { get; set; }
        public static AudioManager _audioManager { get; set; }
        public static BgManager _bgManager { get; set; }
        public static ButtonsManager buttonsManager { get; set; }
        public static DataLoader _dataLoader { get; set; }
        public static EffectManager _effectManager { get; set; }
        public static InputManager _inputManager { get; set; }
        public static ModelManager _modelManager { get; set; }
        public static NoteManager _noteManager { get; set; }
        public static SkinManager _noteSkinManager { get; set; }
        public static ObjectCounter _objectCounter { get; set; }
        public static PlayManager _playManager { get; set; }
        public static ScreenRecorder _screenRecorder { get; set; }
        public static TimeProvider _timeProvider { get; set; }
        public static WsServer _wsServer { get; set; }
#pragma warning restore IDE1006 // 命名样式


        public const float FRAME_LENGTH_SEC = 1f / 60;
        public const float FRAME_LENGTH_MSEC = FRAME_LENGTH_SEC * 1000;

        public const int BUTTON_COUNT = 8;
        public const int SENSOR_COUNT = 33;

        [BurstCompile]
        public static SensorType GetSensor(char areaPos, int startPos)
        {
            return areaPos switch
            {
                'A' => (SensorType)(startPos - 1),
                'B' => (SensorType)(startPos + 7),
                'C' => SensorType.C,
                'D' => (SensorType)(startPos + 16),
                'E' => (SensorType)(startPos + 24),
                _ => SensorType.A1,
            };
        }

        public static readonly IReadOnlyDictionary<SensorType, SensorType[]> TOUCH_GROUPS = new Dictionary<SensorType, SensorType[]>()
        {
            { SensorType.A1, new SensorType[]{ SensorType.D1, SensorType.D2, SensorType.E1, SensorType.E2, SensorType.B1 } },
            { SensorType.A2, new SensorType[]{ SensorType.D2, SensorType.D3, SensorType.E2, SensorType.E3, SensorType.B2 } },
            { SensorType.A3, new SensorType[]{ SensorType.D3, SensorType.D4, SensorType.E3, SensorType.E4, SensorType.B3 } },
            { SensorType.A4, new SensorType[]{ SensorType.D4, SensorType.D5, SensorType.E4, SensorType.E5, SensorType.B4 } },
            { SensorType.A5, new SensorType[]{ SensorType.D5, SensorType.D6, SensorType.E5, SensorType.E6, SensorType.B5 } },
            { SensorType.A6, new SensorType[]{ SensorType.D6, SensorType.D7, SensorType.E6, SensorType.E7, SensorType.B6 } },
            { SensorType.A7, new SensorType[]{ SensorType.D7, SensorType.D8, SensorType.E7, SensorType.E8, SensorType.B7 } },
            { SensorType.A8, new SensorType[]{ SensorType.D8, SensorType.D1, SensorType.E8, SensorType.E1, SensorType.B8 } },

            { SensorType.D1, new SensorType[]{ SensorType.A1, SensorType.A8, SensorType.E1 } },
            { SensorType.D2, new SensorType[]{ SensorType.A2, SensorType.A1, SensorType.E2 } },
            { SensorType.D3, new SensorType[]{ SensorType.A3, SensorType.A2, SensorType.E3 } },
            { SensorType.D4, new SensorType[]{ SensorType.A4, SensorType.A3, SensorType.E4 } },
            { SensorType.D5, new SensorType[]{ SensorType.A5, SensorType.A4, SensorType.E5 } },
            { SensorType.D6, new SensorType[]{ SensorType.A6, SensorType.A5, SensorType.E6 } },
            { SensorType.D7, new SensorType[]{ SensorType.A7, SensorType.A6, SensorType.E7 } },
            { SensorType.D8, new SensorType[]{ SensorType.A8, SensorType.A7, SensorType.E8 } },

            { SensorType.E1, new SensorType[]{ SensorType.D1, SensorType.A1, SensorType.A8, SensorType.B1, SensorType.B8 } },
            { SensorType.E2, new SensorType[]{ SensorType.D2, SensorType.A2, SensorType.A1, SensorType.B2, SensorType.B1 } },
            { SensorType.E3, new SensorType[]{ SensorType.D3, SensorType.A3, SensorType.A2, SensorType.B3, SensorType.B2 } },
            { SensorType.E4, new SensorType[]{ SensorType.D4, SensorType.A4, SensorType.A3, SensorType.B4, SensorType.B3 } },
            { SensorType.E5, new SensorType[]{ SensorType.D5, SensorType.A5, SensorType.A4, SensorType.B5, SensorType.B4 } },
            { SensorType.E6, new SensorType[]{ SensorType.D6, SensorType.A6, SensorType.A5, SensorType.B6, SensorType.B5 } },
            { SensorType.E7, new SensorType[]{ SensorType.D7, SensorType.A7, SensorType.A6, SensorType.B7, SensorType.B6 } },
            { SensorType.E8, new SensorType[]{ SensorType.D8, SensorType.A8, SensorType.A7, SensorType.B8, SensorType.B7 } },

            { SensorType.B1, new SensorType[]{ SensorType.E1, SensorType.E2, SensorType.B8, SensorType.B2, SensorType.A1, SensorType.C } },
            { SensorType.B2, new SensorType[]{ SensorType.E2, SensorType.E3, SensorType.B1, SensorType.B3, SensorType.A2, SensorType.C } },
            { SensorType.B3, new SensorType[]{ SensorType.E3, SensorType.E4, SensorType.B2, SensorType.B4, SensorType.A3, SensorType.C } },
            { SensorType.B4, new SensorType[]{ SensorType.E4, SensorType.E5, SensorType.B3, SensorType.B5, SensorType.A4, SensorType.C } },
            { SensorType.B5, new SensorType[]{ SensorType.E5, SensorType.E6, SensorType.B4, SensorType.B6, SensorType.A5, SensorType.C } },
            { SensorType.B6, new SensorType[]{ SensorType.E6, SensorType.E7, SensorType.B5, SensorType.B7, SensorType.A6, SensorType.C } },
            { SensorType.B7, new SensorType[]{ SensorType.E7, SensorType.E8, SensorType.B6, SensorType.B8, SensorType.A7, SensorType.C } },
            { SensorType.B8, new SensorType[]{ SensorType.E8, SensorType.E1, SensorType.B7, SensorType.B1, SensorType.A8, SensorType.C } },

            { SensorType.C, new SensorType[]{ SensorType.B1, SensorType.B2, SensorType.B3, SensorType.B4, SensorType.B5, SensorType.B6, SensorType.B7, SensorType.B8} },
        };
    }
}