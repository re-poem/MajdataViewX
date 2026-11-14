using MajdataViewX.Types.Rendering;
using Unity.Mathematics;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Types.Notes.RenderData
{
    public struct NotesRenderData : ISortableRenderData
    {
        public float2 pos;
        public float angRad;
        public float scale;
        public float stretchY;
        public NoteSp spriteId;
        public float4 color;
        public float brightness;
        public NoteSp exSprite;
        public float4 exColor;
        public float2 sliceBorder;   // (topFrac, botFrac), (0,0) = normal
        public uint sort;
        public uint foldedCount;

        public readonly uint SortKey => sort;
    }
}