using MajdataViewX.Base;
using MajdataViewX.Notes.NoteDatas;
using MajdataViewX.Notes.Updaters;
using MajdataViewX.Types.Input;
using MajdataViewX.Types.Notes.RenderData;
using MajdataViewX.Types.Rendering;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using MajSimai;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public partial class NoteManager : MonoBehaviour
    {
        /// <summary>
        /// note数量/最后一个note的时间大于该常数时使用HIGH_DENSITY_CAPACITY_MULTIPLIER
        /// </summary>
        private const float HIGH_DENSITY_THRESHOLD = 1145.14f;
        private const int HIGH_DENSITY_CAPACITY_MULTIPLIER = 3;
        private const int DEFAULT_RENDER_CAPACITY = 65536;
        private const int DEFAULT_SLIDE_RENDER_CAPACITY = 262144;

        public float NoteDensity { get; private set; }

        NativeList<TapData> taps = new(1024, Allocator.Persistent);
        NativeList<EachLineData> eachLines = new(512, Allocator.Persistent);
        NativeList<HoldData> holds = new(1024, Allocator.Persistent);
        NativeList<SlideData> slides = new(1024, Allocator.Persistent);
        NativeList<TouchData> touches = new(1024, Allocator.Persistent);
        NativeList<TouchHoldData> touchHolds = new(1024, Allocator.Persistent);

        NativeList<DJAutoPlayData> plays = new(2048, Allocator.Persistent);

        [SerializeField]
        AnimationCurve DJAutoMoveCurve;

        NativeList<int> touchGroupTotalCounts = new(256, Allocator.Persistent);
        NativeList<int> touchGroupJudgedCounts = new(256, Allocator.Persistent);

        NativeList<int> touchHoldGroupTotalCounts = new(256, Allocator.Persistent);
        NativeList<int> touchHoldGroupPressedCounts = new(256, Allocator.Persistent);

        RenderGroup<LineRenderData> _tapLineGroup;
        RenderGroup<LineRenderData> _eachLineGroup;
        RenderGroup<SimpleRenderData> _slideGroup;
        RenderGroup<NotesRenderData> _notesGroup;
        RenderGroup<MaskRenderData> _thBorderGroup;
        RenderGroup<SimpleRenderData> _touchGroup;
        RenderGroup<HitRenderData> _hitSwipeGroup;
        bool _isHitSwipeGroupLockedThisFrame;

        Material _matHit;
        Mesh _hitMesh;
        Matrix4x4 _matrix;

        GraphicsBuffer _noteUvsBuffer;
        Material _matLine;
        Material _matSimple;
        Material _matNotes;
        Material _matMask;
        Mesh _lineMesh;
        Mesh _quadMesh;
        int _renderCapacityMultiplier;

        JobHandle _prevChain;
        bool _isJobScheduledThisFrame;

        void Awake()
        {
            _noteManager = this;
        }
        void Start()
        {
            _lineMesh = MeshGenerator.CreateRingMesh(32, 0.5f, 0.3f);
            _quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            _matrix = transform.localToWorldMatrix;

            //REMEMBER TO FORCE INCLUDE
            _matLine = new Material(Shader.Find("Custom/NoteLine"));
            _matSimple = new Material(Shader.Find("Custom/NoteSimple"));
            _matNotes = new Material(Shader.Find("Custom/NoteRich"));
            _matMask = new Material(Shader.Find("Custom/NoteMask"));
            _matHit = new Material(Shader.Find("Custom/Hit"));
            _hitMesh = MeshGenerator.CreateCircleMesh(8, 1f, true);

            CreateRenderGroups(1);

            _noteUvsBuffer = new(
                GraphicsBuffer.Target.Structured,
                _noteSkinManager.Uvs.Length,
                sizeof(float) * 4);
            _noteUvsBuffer.SetData(_noteSkinManager.Uvs, 0, 0, _noteSkinManager.Uvs.Length);

            void SetupMaterial(Material mat)
            {
                mat.SetBuffer("_SpriteRects", _noteUvsBuffer);
                mat.SetTexture("_MainTex", _noteSkinManager.Atlas);
                mat.SetVector(
                    "_AtlasSize",
                    new Vector4(
                        _noteSkinManager.Atlas.width,
                        _noteSkinManager.Atlas.height,
                        0,
                        0));
                mat.SetFloat("_PixelsPerUnit", 100);
                mat.SetMatrix("_RootMatrix", _matrix);
            }
            SetupMaterial(_matLine);
            SetupMaterial(_matSimple);
            SetupMaterial(_matNotes);
            SetupMaterial(_matMask);
            _matHit.SetMatrix("_RootMatrix", _matrix);

            _djAutoMoveCurve = new(DJAUTO_CURVE_RESOLUTION, Allocator.Persistent);
            for (var i = 0; i < DJAUTO_CURVE_RESOLUTION; i++)
            {
                var t = i / (float)(DJAUTO_CURVE_RESOLUTION - 1);
                _djAutoMoveCurve[i] = DJAutoMoveCurve.Evaluate(t);
            }

            // 字段初始化器只给全零(DJAutoHand)，首播前显式重置为正确的空闲初始态(CurIdx=-1)，
            // 否则全零的 CurIdx=0 会让两手都误跟踪 hits[0]。
            ResetDJAutoHands();
        }

        private void CreateRenderGroups(int capacityMultiplier)
        {
            _renderCapacityMultiplier = capacityMultiplier;
            var capacity = checked(DEFAULT_RENDER_CAPACITY * capacityMultiplier);
            var slideCapacity = checked(DEFAULT_SLIDE_RENDER_CAPACITY * capacityMultiplier);

            _tapLineGroup = new RenderGroup<LineRenderData>(_matLine, _lineMesh, 0, capacity);
            _eachLineGroup = new RenderGroup<LineRenderData>(_matLine, _lineMesh, 1, capacity);
            _slideGroup = new RenderGroup<SimpleRenderData>(_matSimple, _quadMesh, 2, slideCapacity);
            _notesGroup = new RenderGroup<NotesRenderData>(_matNotes, _quadMesh, 3, capacity);
            _thBorderGroup = new RenderGroup<MaskRenderData>(_matMask, _quadMesh, 4, capacity);
            _touchGroup = new RenderGroup<SimpleRenderData>(_matSimple, _quadMesh, 5, capacity);
            _hitSwipeGroup = new RenderGroup<HitRenderData>(_matHit, _hitMesh, 6, capacity);
        }

        private void DisposeRenderGroups()
        {
            _tapLineGroup?.Dispose();
            _eachLineGroup?.Dispose();
            _slideGroup?.Dispose();
            _notesGroup?.Dispose();
            _thBorderGroup?.Dispose();
            _touchGroup?.Dispose();
            _hitSwipeGroup?.Dispose();
        }

        private void ConfigureRenderCapacity(SimaiChart chart)
        {
            var noteCount = 0;
            foreach (var timing in chart.NoteTimings)
            {
                noteCount += timing.Notes.Length;
            }
            var lastNoteTime = chart.NoteTimings[^1].Timing;

            NoteDensity = lastNoteTime > 0d
                ? noteCount / (float)lastNoteTime
                : noteCount > 0 ? float.PositiveInfinity : 0f;

            var capacityMultiplier = NoteDensity > HIGH_DENSITY_THRESHOLD
                ? HIGH_DENSITY_CAPACITY_MULTIPLIER
                : 1;
            if (capacityMultiplier == _renderCapacityMultiplier) return;

            _prevChain.Complete();
            _isJobScheduledThisFrame = false;
            DisposeRenderGroups();
            CreateRenderGroups(capacityMultiplier);
        }

        void Update()
        {
            _prevChain.Complete();
            // hitSwipeGroup 必须在 BeginHandler 之前锁定并设好 hitRender/HitWriteCountPtr：
            // 用户输入（BeginHandler 内）与 PlayUpdateJob 都直接写这两个指针。
            _isHitSwipeGroupLockedThisFrame = MajBurst.InputData.ShowHand;
            if (_isHitSwipeGroupLockedThisFrame)
            {
                _hitSwipeGroup.AdvanceWrite();
                var hitRenderArr = _hitSwipeGroup.LockForWrite();
                _hitSwipeGroup.ResetCount();
                unsafe
                {
                    MajBurst.InputData.hitRender = (HitRenderData*)hitRenderArr.GetUnsafePtr();
                    MajBurst.InputData.HitWriteCountPtr = _hitSwipeGroup.WriteCountPtr;
                }
            }

            _inputManager.BeginHandler(); // 这里牵扯到用户输入，需要一直调用

            if (taps.Length + eachLines.Length + holds.Length + slides.Length + touches.Length + touchHolds.Length == 0) return;

            _tapLineGroup.AdvanceWrite();
            _eachLineGroup.AdvanceWrite();
            _slideGroup.AdvanceWrite();
            _notesGroup.AdvanceWrite();
            _thBorderGroup.AdvanceWrite();
            _touchGroup.AdvanceWrite();

            var tapLinesRender = _tapLineGroup.LockForWrite();
            var eachLinesRender = _eachLineGroup.LockForWrite();
            var slidesRender = _slideGroup.LockForWrite();
            var notesRender = _notesGroup.LockForWrite();
            var maskRender = _thBorderGroup.LockForWrite();
            var touchesRender = _touchGroup.LockForWrite();

            unsafe
            {
                _tapLineGroup.ResetCount();
                _eachLineGroup.ResetCount();
                _slideGroup.ResetCount();
                _notesGroup.ResetCount();
                _thBorderGroup.ResetCount();
                _touchGroup.ResetCount();

                if (touchHoldGroupTotalCounts.Length > 0)
                {
                    for (int i = 0; i < touchHoldGroupTotalCounts.Length; i++)
                        touchHoldGroupPressedCounts[i] = 0;
                    for (int i = 0; i < touchHolds.Length; i++)
                    {
                        ref var t = ref touchHolds.ElementRef(i);
                        if (t.groupId != -1)
                        {
                            if (t.isEnd)
                            {
                                touchHoldGroupTotalCounts[t.groupId]--;
                                t.groupId = -1;
                            }
                            else if (MajBurst.InputData.GetSensorState(t.sensor).Status)
                            {
                                touchHoldGroupPressedCounts[t.groupId]++;
                            }
                        }
                    }
                }

                JobHandle h = default;

                if (plays.Length > 0)
                {
                    h = new PlayUpdateJob
                    {
                        _djAutoMoveCurve = _djAutoMoveCurve,
                        plays = plays.AsArray(),
                        hands = _djAutoHands,
                    }.Schedule(h);
                }

                // DJAuto持续输入必须先续占下一帧的手，Tap/Touch 只能使用剩余额度，因此hold/slide类note先update
                if (holds.Length > 0)
                    h = new HoldUpdateJob
                    {
                        holds = holds.AsArray(),
                        tapLinesRender = tapLinesRender,
                        notesRender = notesRender,
                        TapLinesWriteCountPtr = _tapLineGroup.WriteCountPtr,
                        NotesWriteCountPtr = _notesGroup.WriteCountPtr,
                        SfxRequests = _audioManager.SfxRequestsPtr,
                        JudgeEffectRequests = _effectManager.JudgeEffectRequestsPtr,
                        ReportResults = _objectCounter.ReportRequestsWriter,
                    }.Schedule(holds.Length, 32, h);

                if (slides.Length > 0)
                    h = new SlideUpdateJob
                    {
                        slides = slides.AsArray(),
                        slidesRender = slidesRender,
                        notesRender = notesRender,
                        SlidesWriteCountPtr = _slideGroup.WriteCountPtr,
                        NotesWriteCountPtr = _notesGroup.WriteCountPtr,
                        SfxRequests = _audioManager.SfxRequestsPtr,
                        ReportResults = _objectCounter.ReportRequestsWriter,
                    }.Schedule(slides.Length, 32, h);

                if (touchHolds.Length > 0)
                    h = new TouchHoldUpdateJob
                    {
                        touchHolds = touchHolds.AsArray(),
                        simpleRender = touchesRender,
                        SimpleWriteCountPtr = _touchGroup.WriteCountPtr,
                        maskRender = maskRender,
                        MaskWriteCountPtr = _thBorderGroup.WriteCountPtr,
                        SfxRequests = _audioManager.SfxRequestsPtr,
                        JudgeEffectRequests = _effectManager.JudgeEffectRequestsPtr,
                        ReportResults = _objectCounter.ReportRequestsWriter,
                        touchGroupTotalCounts = touchGroupTotalCounts.AsArray(),
                        touchGroupJudgedCounts = touchGroupJudgedCounts.AsArray(),
                        touchHoldGroupTotalCounts = touchHoldGroupTotalCounts.AsArray(),
                        touchHoldGroupPressedCounts = touchHoldGroupPressedCounts.AsArray(),
                    }.Schedule(touchHolds.Length, 32, h);

                if (taps.Length > 0)
                    h = new TapUpdateJob
                    {
                        taps = taps.AsArray(),

                        tapLinesRender = tapLinesRender,
                        notesRender = notesRender,

                        tapLinesWriteCountPtr = _tapLineGroup.WriteCountPtr,
                        notesWriteCountPtr = _notesGroup.WriteCountPtr,
                        SfxRequests = _audioManager.SfxRequestsPtr,
                        JudgeEffectRequests = _effectManager.JudgeEffectRequestsPtr,
                        ReportResults = _objectCounter.ReportRequestsWriter,
                    }.Schedule(taps.Length, 32, h);

                if (eachLines.Length > 0)
                    h = new EachLineUpdateJob
                    {
                        eachLines = eachLines.AsArray(),
                        eachLinesRender = eachLinesRender,
                        EachLinesWriteCountPtr = _eachLineGroup.WriteCountPtr,
                    }.Schedule(eachLines.Length, 32, h);

                if (touches.Length > 0)
                    h = new TouchUpdateJob
                    {
                        touches = touches.AsArray(),
                        touchesRender = touchesRender,
                        TouchesWriteCountPtr = _touchGroup.WriteCountPtr,
                        SfxRequests = _audioManager.SfxRequestsPtr,
                        JudgeEffectRequests = _effectManager.JudgeEffectRequestsPtr,
                        ReportResults = _objectCounter.ReportRequestsWriter,
                        touchGroupTotalCounts = touchGroupTotalCounts.AsArray(),
                        touchGroupJudgedCounts = touchGroupJudgedCounts.AsArray(),
                    }.Schedule(touches.Length, 32, h);

                var tapLineSort = _tapLineGroup.ScheduleSort(h);
                var eachLineSort = _eachLineGroup.ScheduleSort(h);
                var slideSort = _slideGroup.ScheduleSort(h);
                var notesSort = _notesGroup.ScheduleSort(h);
                var thBorderSort = _thBorderGroup.ScheduleSort(h);
                var touchSort = _touchGroup.ScheduleSort(h);

                _prevChain = JobHandle.CombineDependencies(
                    JobHandle.CombineDependencies(tapLineSort, eachLineSort),
                    JobHandle.CombineDependencies(slideSort, notesSort),
                    JobHandle.CombineDependencies(thBorderSort, touchSort));
            }
            _isJobScheduledThisFrame = true;
        }

        void LateUpdate()
        {
            _prevChain.Complete();

            if (_isJobScheduledThisFrame)
            {
                _tapLineGroup.UnlockWrite(false);
                _eachLineGroup.UnlockWrite(false);
                _slideGroup.UnlockWrite(false);
                _notesGroup.UnlockWrite(false);
                _thBorderGroup.UnlockWrite(false);
                _touchGroup.UnlockWrite(false);

                _tapLineGroup.Render();
                _eachLineGroup.Render();
                _slideGroup.Render();
                _notesGroup.Render();
                _thBorderGroup.Render();
                _touchGroup.Render();

                _tapLineGroup.Swap();
                _eachLineGroup.Swap();
                _slideGroup.Swap();
                _notesGroup.Swap();
                _thBorderGroup.Swap();
                _touchGroup.Swap();

                _objectCounter.ProcessReportRequests();
                MajBurst.InputData.ApplyNextIndices();

                // 思来想去在th内做加减确实不比在这里遍历一次快
                // 去他妈的可读性
                {
                    int activeTouchHolds = 0;
                    for (int i = 0; i < touchHolds.Length; i++)
                    {
                        if (touchHolds[i].isHolding) activeTouchHolds++;
                    }
                    _audioManager.ActiveTouchHoldCount = activeTouchHolds;
                }

                _isJobScheduledThisFrame = false;
            }

            _inputManager.EndHandler();
            if (_isHitSwipeGroupLockedThisFrame)
            {
                _hitSwipeGroup.UnlockWrite(false);
                _hitSwipeGroup.Render();
                _hitSwipeGroup.Swap();
                _isHitSwipeGroupLockedThisFrame = false;
            }

            _modelManager.LeftHandPos = _noteManager.transform.TransformPoint((Vector2)_djAutoHands[0].Pos);
            _modelManager.RightHandPos = _noteManager.transform.TransformPoint((Vector2)_djAutoHands[1].Pos);
        }

        void OnDestroy()
        {
            _prevChain.Complete();
            DisposeRenderGroups();

            _noteUvsBuffer?.Dispose();

            if (taps.IsCreated) taps.Dispose();
            if (eachLines.IsCreated) eachLines.Dispose();
            if (holds.IsCreated) holds.Dispose();
            if (slides.IsCreated) slides.Dispose();
            if (touches.IsCreated) touches.Dispose();
            if (touchHolds.IsCreated) touchHolds.Dispose();
            if (plays.IsCreated) plays.Dispose();
            if (_djAutoHands.IsCreated) _djAutoHands.Dispose();
            if (_djAutoTouchInfosThisTiming.IsCreated) _djAutoTouchInfosThisTiming.Dispose();
            if (_djAutoMoveCurve.IsCreated) _djAutoMoveCurve.Dispose();

            if (touchGroupTotalCounts.IsCreated) touchGroupTotalCounts.Dispose();
            if (touchGroupJudgedCounts.IsCreated) touchGroupJudgedCounts.Dispose();
            if (touchHoldGroupTotalCounts.IsCreated) touchHoldGroupTotalCounts.Dispose();
            if (touchHoldGroupPressedCounts.IsCreated) touchHoldGroupPressedCounts.Dispose();

            if (_leftHandPlays.IsCreated) _leftHandPlays.Dispose();
            if (_rightHandPlays.IsCreated) _rightHandPlays.Dispose();
        }

        public void ResetState()
        {
            _prevChain.Complete();
            taps.Clear();
            eachLines.Clear();
            holds.Clear();
            slides.Clear();
            touches.Clear();
            touchHolds.Clear();
            plays.Clear();
            _djAutoTouchInfosThisTiming.Clear();
            ResetDJAutoHands();

            touchGroupTotalCounts.Clear();
            touchGroupJudgedCounts.Clear();
            touchHoldGroupTotalCounts.Clear();
            touchHoldGroupPressedCounts.Clear();
            unsafe
            {
                if (slideAreaPool != null)
                    UnsafeUtility.Free(slideAreaPool, Allocator.Persistent);
                if (slidePosePool != null)
                    UnsafeUtility.Free(slidePosePool, Allocator.Persistent);
                slideAreaPool = null;
                slidePosePool = null;
            }
            MajBurst.MultTouchHandler.Clear();

            if (_leftHandPlays.IsCreated) _leftHandPlays.Dispose();
            if (_rightHandPlays.IsCreated) _rightHandPlays.Dispose();
        }
    }
}