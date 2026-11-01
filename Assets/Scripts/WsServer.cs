#nullable enable

using Cysharp.Threading.Tasks;
using MajdataViewX.Managers;
using MajdataViewX.Types.MajSetting;
using MajdataViewX.Types.Protocol;
using MajProtocol.Generated;
using MajProtocol.Standard;
using MajProtocol.Standard.Packets;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using static MajdataViewX.Base.MajCtx;
using Debug = UnityEngine.Debug;

namespace MajdataViewX
{
    /// <summary>
    /// MajProtocol 2.0 服务端。
    ///
    /// 线上是二进制帧：每个包第一个字节是 <see cref="OpCode"/>，其后按 packet 结构体字段顺序排列。
    /// 分派走 <c>MajProtocol.Standard</c> 生成器产出的 <see cref="IMajProtocolServer.Handle"/>，
    /// 与协议仓库的示例实现 <c>MajProtocol.Server.WebSocketSharp.MajWebSocketServer</c> 同构。
    ///
    /// 与示例的唯一差别：示例的静态单例只把请求转成静态事件、不回应答，而协议里每个请求都有
    /// 对应的 <c>*Response</c>（只带 <c>accepted</c>），客户端靠它结算在途请求。
    /// 因此这里保留示例的「WebSocketServer + WebSocketBehavior + Handle 路由」结构，
    /// 但由 behavior 自己把 <c>*Response</c> 发回发起请求的会话。
    /// </summary>
    public class WsServer : MonoBehaviour
    {
        public static class Endpoint
        {
            public const int PORT = 8083;
            public const string SERVICE_PATH = "/majdata";
        }

        private WebSocketServer? _webSocket;
        private CancellationToken _lifetimeCancellationToken;

        /// <summary>待处理请求。<c>Session</c> 用来把响应发回发起方。</summary>
        internal readonly ConcurrentQueue<(MajdataWsService Session, byte[] Data)> MessageQueue = new();

        private void Awake()
        {
            _wsServer = this;
        }

        // 这里是游戏及游戏外部的初始化
        void Start()
        {
            QualitySettings.vSyncCount = 1;

            _webSocket = new WebSocketServer(IPAddress.Any, Endpoint.PORT);
            _webSocket.AddWebSocketService<MajdataWsService>(Endpoint.SERVICE_PATH);
            _webSocket.Start();
            _lifetimeCancellationToken = this.GetCancellationTokenOnDestroy();
            ProcessQueue(_lifetimeCancellationToken).Forget();
        }

        private async UniTaskVoid ProcessQueue(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (MessageQueue.TryDequeue(out var item))
                    {
                        while (_playManager is null ||
                               PlayManager.State == ViewStatus.Busy)
                            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

                        await item.Session.DispatchAsync(item.Data);
                    }
                    else
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        /// <summary>
        /// 记录一条播放器侧故障。
        ///
        /// MajProtocol 2.0 的响应只有 <c>accepted</c>，没有错误文本通道，
        /// 因此这里只落本地日志；协议层用 <c>accepted = false</c> 表达失败。
        /// </summary>
        public void Error<T>(T exception) where T : Exception
        {
            Debug.LogException(exception);
        }
        /// <summary>记录一条播放器侧故障（同上，只落本地日志）。</summary>
        public void Error(string errMsg)
        {
            Debug.LogError($"[WsServer] {errMsg}");
        }

        private void OnDestroy()
        {
            if (_webSocket is null) return;
            _webSocket.RemoveWebSocketService(Endpoint.SERVICE_PATH);
            _webSocket.Stop();
        }
    }

    /// <summary>
    /// 每个 WebSocket 会话一个实例：实现 <see cref="IMajProtocolServer"/> 复用协议生成的
    /// 按 OpCode 路由，同时能通过 <c>Send</c> 把应答发回本会话。
    /// </summary>
    public class MajdataWsService : WebSocketBehavior, IMajProtocolServer
    {
        // 生成的路由接口是同步 void，天生没法在里面 await：各 On*RequestReceived 只把异步处理
        // 挂到这里，DispatchAsync 再交给主循环 await，
        // 这样 Unity API 仍然只在主线程上被访问，请求之间也保持串行。
        private UniTask _pending = UniTask.CompletedTask;

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.RawData is not { Length: > 0 } rawData)
                return;

            _wsServer.MessageQueue.Enqueue((this, rawData));
        }

        /// <summary>处理一个已序列化的请求包，并在主线程上等到它完成。</summary>
        internal async UniTask DispatchAsync(byte[] data)
        {
            var opCode = (OpCode)data[0];
            _pending = UniTask.CompletedTask;

            try
            {
                // 生成的路由：读 data[0] 的 OpCode，转成对应 packet 后调用下面的 On*RequestReceived。
                ((IMajProtocolServer)this).Handle(data);

                await _pending;
                Debug.Log($"[WsServer] request finished: {opCode}");
            }
            catch (Exception ex)
            {
                _wsServer.Error(ex);
                Debug.LogError($"[WsServer] request failed: {opCode}");
            }
        }

        private void Reply(byte[] packet)
        {
            try { Send(packet); }
            catch (Exception ex) { _wsServer.Error(ex); }
        }

        // ---- 协议路由（IMajProtocolServer）----

        void IMajProtocolServer.OnHandshakeRequestReceived(HandshakeRequest req)
        {
            // 与示例实现一致：版本一致时不回包（客户端连上即视为成功），
            // 不匹配才回 accepted = false 并断开。
            if (req.protocolVersion == Properties.ProtocolVersion) return;

            _wsServer.Error($"protocol version mismatch: client {req.protocolVersion}, server {Properties.ProtocolVersion}");
            Reply(new HandshakeResponse { accepted = false }.ToBytes());
            try { Context.WebSocket.Close(); }
            catch (Exception ex) { _wsServer.Error(ex); }
        }

        void IMajProtocolServer.OnFilesRequestReceived(FilesRequest req)
        {
            _pending = HandleFilesAsync(req);
        }

        void IMajProtocolServer.OnSimaiRequestReceived(SimaiRequest req)
        {
            _pending = HandleSimaiAsync(req);
        }

        void IMajProtocolServer.OnPlayRequestReceived(PlayRequest req)
        {
            _pending = HandlePlayAsync(req);
        }

        void IMajProtocolServer.OnPauseRequestReceived(PauseRequest req)
        {
            _pending = HandlePauseAsync();
        }

        void IMajProtocolServer.OnOptionsRequestReceived(OptionsRequest req)
        {
            // 纯本地状态更新，无需等待主线程帧，直接同步完成并回应答。
            _playManager.Setting(ToViewSetting(req), ToVolumeSetting(req));
            Reply(new OptionsResponse { accepted = true }.ToBytes());
        }

        void IMajProtocolServer.OnStateRequestReceived(StateRequest req)
        {
            Reply(new StateResponse
            {
                position = PlayManager.Position,
                isPlaying = PlayManager.State is ViewStatus.Playing,
            }.ToBytes());
        }

        void IMajProtocolServer.OnUnloadRequestReceived(UnloadRequest req)
        {
            _pending = HandleUnloadAsync();
        }

        // ---- 具体处理 ----
        //
        // 生成的路由接口把 On*RequestReceived 声明为同步 void，天生没法 await，
        // 所以上面每个路由只把异步体挂到 _pending 上，由 DispatchAsync 在主线程串行等待。

        private async UniTask HandleFilesAsync(FilesRequest req)
        {
            var accepted = await _playManager.LoadAsync(req.trackPath, req.jacketPath, req.moviePath);
            Reply(new FilesResponse { accepted = accepted }.ToBytes());
        }

        private async UniTask HandleSimaiAsync(SimaiRequest req)
        {
            var accepted = await _playManager.ParseAsync(
                req.content,
                (float)req.offset,
                req.title,
                req.artist,
                req.difficultyValue,
                req.designer,
                req.difficultyIndex);
            Reply(new SimaiResponse { accepted = accepted }.ToBytes());
        }

        private async UniTask HandlePlayAsync(PlayRequest req)
        {
            // 协议用两个布尔量表达播放模式，取代了旧版的 PlaybackMode 枚举。
            var mode = req.record
                ? PlaybackMode.Record
                : req.showIntroAnimation
                    ? PlaybackMode.IncludeOp
                    : PlaybackMode.Normal;

            var accepted = await _playManager.PlayAsync(mode, req.position, req.speed);
            Reply(new PlayResponse { accepted = accepted }.ToBytes());
        }

        private async UniTask HandlePauseAsync()
        {
            var paused = await _playManager.PauseAsync();
            // 协议没有 Stop：编辑器用 Pause 表达「停下」。播放器已经不在播放时视为已达成，
            // 只有真正失败（进 Error）才回 accepted = false。
            var accepted = paused || PlayManager.State is not ViewStatus.Error;
            Reply(new PauseResponse { accepted = accepted }.ToBytes());
        }

        private async UniTask HandleUnloadAsync()
        {
            var accepted = await _playManager.UnloadAsync();
            Reply(new UnloadResponse { accepted = accepted }.ToBytes());
        }

        /// <summary>
        /// <see cref="OptionsRequest"/> 只给常用项留了独立字段，其余 ViewSetting 项在
        /// <c>extra</c> 里以 JSON 传递。键名与 <see cref="MajViewSetting"/> 的
        /// <c>[JsonProperty]</c> 一致，因此直接反序列化回填即可。
        /// </summary>
        private static MajViewSetting ToViewSetting(OptionsRequest req)
        {
            var setting = new MajViewSetting
            {
                TapSpeed = req.tapSpeed,
                TouchSpeed = req.touchSpeed,
            };

            if (!string.IsNullOrWhiteSpace(req.extra))
            {
                try
                {
                    JsonConvert.PopulateObject(req.extra, setting);
                }
                catch (Exception ex)
                {
                    _wsServer.Error(ex);
                }
            }

            return setting;
        }

        private static MajVolumeSetting ToVolumeSetting(OptionsRequest req) => new()
        {
            Track = req.trackVolume,
            Answer = req.answerVolume,
            Tap = req.tapVolume,
            Slide = req.slideVolume,
            Break = req.breakVolume,
            BreakSlide = req.breakSlideVolume,
            Ex = req.exVolume,
            Touch = req.touchVolume,
            Hanabi = req.hanabiVolume,
        };
    }
}
