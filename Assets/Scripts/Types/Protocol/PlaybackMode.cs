namespace MajdataViewX.Types.Protocol
{
    /// <summary>
    /// 播放模式。
    ///
    /// MajProtocol 2.0 的 <c>PlayRequest</c> 用 <c>showIntroAnimation</c> 与 <c>record</c>
    /// 两个布尔量表达播放模式（取代了旧协议里的 PlaybackMode 枚举），
    /// 这里保留枚举供 <see cref="MajdataViewX.Managers.TimeProvider"/> 等内部代码使用。
    /// </summary>
    public enum PlaybackMode
    {
        /// <summary>普通播放。</summary>
        Normal,

        /// <summary>带 song detail 开场动画的播放。</summary>
        IncludeOp,

        /// <summary>录制模式：带开场动画并按固定帧率逐帧推进。</summary>
        Record,
    }
}
