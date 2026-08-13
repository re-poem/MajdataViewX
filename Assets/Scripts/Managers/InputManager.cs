#nullable enable

using MajdataViewX.Base;
using MajdataViewX.Notes;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Rendering;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using static MajdataViewX.Base.MajBurst;
using static MajdataViewX.Base.MajCtx;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace MajdataViewX.Managers
{
    public class InputManager
    {
        public const float AUTOPLAY_START_SEC = -1 * FRAME_LENGTH_SEC;
        public const float DJAUTO_SLIDE_TAP_GUIDE_DELAY_SEC = 3 * FRAME_LENGTH_SEC;

        public const float DJAUTO_SLIDE_RELEASE_DELAY_SEC = 6 * FRAME_LENGTH_SEC;



        public const float BUTTON_HIT_RENDER_RADIUS = 0.4f;

        public Camera CurrentCamera;

        public bool ShowHand
        {
            get => InputData.ShowHand;
            set => InputData.ShowHand = value;
        }
        public InputManager()
        {
            _inputManager = this;
            //get sensor positions
            for (var i = 0; i < SENSOR_COUNT; i++)
            {
                InputData.SensorWorldPositions[i] = MajPos.GetSensorJudgePos((SensorType)i);
            }
        }

        public void BeginHandler()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                CheckButton(keyboard);
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    CheckScreenPos(mouse.position.ReadValue());
                }
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    var phase = touch.phase.ReadValue();
                    if (phase == TouchPhase.None) continue;
                    if (phase is TouchPhase.Began or TouchPhase.Moved or TouchPhase.Stationary)
                        CheckScreenPos(touch.position.ReadValue());
                }
            }
        }

        // wait for slide and other notes finish update
        public void EndHandler()
        {
            InputData.OnLateUpdate();
            // hitSwipeGroup 的 UnlockWrite/Render/Swap 由 NoteManager.LateUpdate 负责。
        }

        private void CheckButton(Keyboard keyboard)
        {
            InputData.HandleButtonInput(SensorType.A1, keyboard[Key.W].isPressed);
            InputData.HandleButtonInput(SensorType.A2, keyboard[Key.E].isPressed);
            InputData.HandleButtonInput(SensorType.A3, keyboard[Key.D].isPressed);
            InputData.HandleButtonInput(SensorType.A4, keyboard[Key.C].isPressed);
            InputData.HandleButtonInput(SensorType.A5, keyboard[Key.X].isPressed);
            InputData.HandleButtonInput(SensorType.A6, keyboard[Key.Z].isPressed);
            InputData.HandleButtonInput(SensorType.A7, keyboard[Key.A].isPressed);
            InputData.HandleButtonInput(SensorType.A8, keyboard[Key.Q].isPressed);
        }
        private void CheckScreenPos(Vector2 screenPos)
        {
            // 场景（Game 容器）整体平移+旋转后，世界坐标不再等于判定坐标。
            // 把屏幕射线变换到 NoteManager 局部空间，与 z=0 平面求交，
            // 得到的 MajPos 局部坐标与 shader 用 _RootMatrix(localToWorld) 渲染互逆。
            var ray = CurrentCamera.ScreenPointToRay(screenPos);
            var tr = _noteManager.transform;
            var origin = tr.InverseTransformPoint(ray.origin);
            var dir = tr.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(dir.z) < 1e-6f) return;
            var t = -origin.z / dir.z;
            var pos = (Vector2)(origin + dir * t);

            InputData.HandleWorldPosInput(pos, NoteManager.DJAUTO_HAND_RADIUS);
        }



        public void ResetState()
        {
        }

        public void OnDestroy()
        {
            // hitSwipeGroup 已迁移至 NoteManager，由其 OnDestroy 释放。
        }
    }

    [BurstCompile]
    public unsafe struct InputDataB
    {
        public bool ShowHand;

        public NativeArray<float2> SensorWorldPositions;

        NativeArray<SensorState> _buttonStates;
        NativeArray<SensorState> _sensorStates;
        NativeArray<int> _nextButtonIndex;
        NativeArray<int> _nextSensorIndex;
        NativeArray<int> _nextButtonIndexNextFrame;
        NativeArray<int> _nextSensorIndexNextFrame;

        [NativeDisableUnsafePtrRestriction]
        public HitRenderData* hitRender;
        [NativeDisableUnsafePtrRestriction]
        public int* HitWriteCountPtr;

        public void Init()
        {
            SensorWorldPositions = new(SENSOR_COUNT, Allocator.Persistent);

            _buttonStates = new(BUTTON_COUNT, Allocator.Persistent);
            _sensorStates = new(SENSOR_COUNT, Allocator.Persistent);
            _nextButtonIndex = new(BUTTON_COUNT, Allocator.Persistent);
            _nextSensorIndex = new(SENSOR_COUNT, Allocator.Persistent);
            _nextButtonIndexNextFrame = new(BUTTON_COUNT, Allocator.Persistent);
            _nextSensorIndexNextFrame = new(SENSOR_COUNT, Allocator.Persistent);

            for (var i = 0; i < BUTTON_COUNT; i++)
                _buttonStates[i] = new();
            for (var i = 0; i < SENSOR_COUNT; i++)
                _sensorStates[i] = new();
        }






        // ==========button/sensor management==========

        public readonly SensorState GetButtonState(SensorType type) => _buttonStates[(int)type];
        public readonly SensorState GetSensorState(SensorType type) => _sensorStates[(int)type];



        // LastActiveDown/ActiveDown 的滚动改在 OnLateUpdate 帧末完成：
        // 本帧由用户输入（Handle*）+ PlayUpdateJob（DJAuto）共同累积 ActiveDown，
        // 供 note 判定 Job 读取边沿；帧末 OnLateUpdate 把 ActiveDown 归零、转入 LastActiveDown。

        /// <summary>
        /// 处理按键输入
        /// </summary>
        public void HandleButtonInput(SensorType type, bool status)
        {
            if (!status) return;
            SetButtonOn(type);
        }
        /// <summary>
        /// 处理世界坐标（手）输入（本帧立即生效）
        /// </summary>
        public void HandleWorldPosInput(in float2 pos, float radius)
        {
            for (int i = 0; i < SensorWorldPositions.Length; i++)
            {
                var combinedR = radius + MajPos.GetSensorRadius((SensorType)i);
                var combinedSq = combinedR * combinedR;
                var sp = SensorWorldPositions[i];
                var dx = pos.x - sp.x;
                var dy = pos.y - sp.y;
                var distSq = dx * dx + dy * dy;

                if (distSq <= combinedSq)
                    SetSensorOn((SensorType)i);
            }
            var r = math.length(pos);
            if (r > MajPos.MAIN_RADIUS)
            {
                // 坐标轴旋转，向上为0度，顺时针为正
                var theta = math.atan2(pos.x, pos.y);
                if (theta < 0) theta += math.PI * 2;
                var key = (int)(theta / (math.PI / 4));
                SetButtonOn((SensorType)key);
            }
            if (ShowHand)
            {
                var idx = Interlocked.Increment(ref *HitWriteCountPtr) - 1;
                hitRender[idx] = new HitRenderData
                {
                    pos = pos,
                    radius = radius,
                    color = new float4(1, 0, 0, 0.75f)
                };
            }
        }

        private void SetButtonOn(SensorType type)
        {
            ref var button = ref _buttonStates.ElementRef((int)type);
            Interlocked.Increment(ref button.ActiveDown);
        }
        private void SetSensorOn(SensorType type)
        {
            ref var sensor = ref _sensorStates.ElementRef((int)type);
            Interlocked.Increment(ref sensor.ActiveDown);
        }

        public void OnLateUpdate()
        {
            if (ShowHand)
            {
                for (int i = 0; i < BUTTON_COUNT; i++)
                {
                    if (_buttonStates[i].Status)
                    {
                        var idx = Interlocked.Increment(ref *HitWriteCountPtr) - 1;
                        hitRender[idx] = new HitRenderData
                        {
                            pos = MajPos.GetBtnPos(i),
                            radius = InputManager.BUTTON_HIT_RENDER_RADIUS,
                            color = new float4(0, 1, 1, 0.5f) // Cyan responsive color
                        };
                    }
                }
                for (int i = 0; i < SENSOR_COUNT; i++)
                {
                    if (_sensorStates[i].Status)
                    {
                        var idx = Interlocked.Increment(ref *HitWriteCountPtr) - 1;
                        hitRender[idx] = new HitRenderData
                        {
                            pos = SensorWorldPositions[i],
                            radius = MajPos.GetSensorRadius((SensorType)i),
                            color = new float4(0, 1, 1, 0.5f) // Cyan responsive color
                        };
                    }
                }
            }


            // 帧末滚动：本帧 ActiveDown（用户输入 + PlayUpdateJob 累积）转为下帧 LastActiveDown，
            // ActiveDown 归零供下帧重新累积。note 判定 Job 在本帧已读完边沿，此处归零安全。
            for (int i = 0; i < BUTTON_COUNT; i++)
            {
                ref var button = ref _buttonStates.ElementRef(i);
                button.LastActiveDown = button.ActiveDown;
                button.ActiveDown = 0;
            }
            for (int i = 0; i < SENSOR_COUNT; i++)
            {
                ref var sensor = ref _sensorStates.ElementRef(i);
                sensor.LastActiveDown = sensor.ActiveDown;
                sensor.ActiveDown = 0;
            }
        }


        // ==========judge management==========
        public readonly void NextTapHold(SensorType pos)
        {
            Interlocked.Increment(ref _nextButtonIndexNextFrame.ElementRef((int)pos));
            Interlocked.Increment(ref _nextSensorIndexNextFrame.ElementRef((int)pos));
        }
        public readonly void NextTouch(SensorType pos)
        {
            Interlocked.Increment(ref _nextSensorIndexNextFrame.ElementRef((int)pos));
        }
        public readonly bool CanJudgeButton(SensorType pos, int order)
        {
            return order == _nextButtonIndex[(int)pos];
        }
        public readonly bool CanJudgeSensor(SensorType pos, int order)
        {
            return order == _nextSensorIndex[(int)pos];
        }


        public readonly void ApplyNextIndices()
        {
            for (int i = 0; i < BUTTON_COUNT; i++)
            {
                _nextButtonIndex.ElementRef(i) = _nextButtonIndexNextFrame[i];
            }
            for (int i = 0; i < SENSOR_COUNT; i++)
            {
                _nextSensorIndex.ElementRef(i) = _nextSensorIndexNextFrame[i];
            }
        }



        public void ResetState()
        {
            for (var i = 0; i < BUTTON_COUNT; i++)
            {
                _buttonStates[i] = default;
                _nextButtonIndex[i] = 0;
                _nextButtonIndexNextFrame[i] = 0;
            }
            for (var i = 0; i < SENSOR_COUNT; i++)
            {
                _sensorStates[i] = default;
                _nextSensorIndex[i] = 0;
                _nextSensorIndexNextFrame[i] = 0;
            }
        }

        public void Dispose()
        {
            if (SensorWorldPositions.IsCreated) SensorWorldPositions.Dispose();
            if (_sensorStates.IsCreated) _sensorStates.Dispose();
            if (_nextSensorIndex.IsCreated) _nextSensorIndex.Dispose();
            if (_nextSensorIndexNextFrame.IsCreated) _nextSensorIndexNextFrame.Dispose();
            if (_buttonStates.IsCreated) _buttonStates.Dispose();
            if (_nextButtonIndex.IsCreated) _nextButtonIndex.Dispose();
            if (_nextButtonIndexNextFrame.IsCreated) _nextButtonIndexNextFrame.Dispose();
        }
    }

    public struct SensorState
    {
        public readonly bool Status => ActiveDown > 0;
        public readonly bool IsPadDown => LastActiveDown <= 0 && ActiveDown > 0;
        public readonly bool IsPadUp => LastActiveDown > 0 && ActiveDown <= 0;

        public int ActiveDown;
        public int LastActiveDown;
    }
}
