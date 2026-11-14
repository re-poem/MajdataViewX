using MajdataViewX.Types.Rendering;
using Unity.Mathematics;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Types.Notes.RenderData
{
    public struct SimpleRenderData : ISortableRenderData
    {
        public float2 pos;
        public float angRad;
        public float2 scale;
        public NoteSp spriteId;
        public float4 color;
        public float brightness;
        public uint sort;
        public uint foldedCount;

        public readonly uint SortKey => sort;
    }
}