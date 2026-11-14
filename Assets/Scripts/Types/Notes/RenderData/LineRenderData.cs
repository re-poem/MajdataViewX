using MajdataViewX.Types.Rendering;
using static MajdataViewX.Managers.SkinManager;

namespace MajdataViewX.Types.Notes.RenderData
{
    public struct LineRenderData : ISortableRenderData
    {
        public float angRad;
        public float scale;
        public NoteSp spriteId;
        public uint sort;
        public uint foldedCount;

        public readonly uint SortKey => sort;
    }
}