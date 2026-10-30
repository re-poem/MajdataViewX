namespace MajdataViewX.Types.Protocol
{
    /// <summary>
    /// 播放器的本地视图状态。
    ///
    /// MajProtocol 2.0 的线上状态只有一个布尔量（<c>StateResponse.isPlaying</c>），
    /// 其余状态（是否装载过、是否出错）由播放器自己维护，不再通过协议传输。
    /// </summary>
    public enum ViewStatus
    {
        /// <summary>什么都没装载。</summary>
        Idle,

        /// <summary>音轨 / 背景已装载，可以播放。</summary>
        Loaded,

        /// <summary>正在播放。</summary>
        Playing,

        /// <summary>已暂停，谱面与播放位置保留。</summary>
        Paused,

        /// <summary>正在处理一条请求，暂不接受新请求。</summary>
        Busy,

        /// <summary>上一条请求失败。</summary>
        Error,
    }
}
