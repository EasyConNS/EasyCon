using EasyCon.Capture;
using EasyCon.Core.Capabilities;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 服务的设备桥：GUI 宿主把监控页已持有的采集源/单片机实例经委托接入，
/// 使画布 HTTP 后端、agent 的 flow_* 工具与 GUI 面板操作<strong>同一实例</strong>
/// （一块采集卡/串口只开一次，连接状态两边实时一致）。
/// 独立宿主（CLI serve）不传桥（null），<see cref="FlowServiceState"/> 退回自持实例的原行为。
/// </summary>
public class FlowDeviceBridge
{
    /// <summary>视频源当前是否已连接（读宿主侧实时状态）。</summary>
    public required Func<bool> IsVideoConnected { get; init; }

    /// <summary>按枚举索引连接视频源；返回错误消息（null = 成功）。API 后端由宿主配置决定（桥忽略 api 参数）。</summary>
    public required Func<int, string?> ConnectVideo { get; init; }

    /// <summary>断开视频源（同步等待采集循环退出，可阻塞数秒；勿在 UI 线程调用）。</summary>
    public required Action DisconnectVideo { get; init; }

    /// <summary>当前帧存储（未连接为 null）；运行上下文据此包装 FrameStoreCaptureSource，保留帧号语义（等帧/慢感知依赖）。</summary>
    public required Func<FrameStore?> GetFrameStore { get; init; }

    /// <summary>单片机当前是否已连接（读宿主侧实时状态；mock 连接不走桥）。</summary>
    public required Func<bool> IsMcuConnected { get; init; }

    /// <summary>连接真实串口；返回错误消息（null = 成功）。</summary>
    public required Func<string, string?> ConnectMcu { get; init; }

    /// <summary>断开单片机。</summary>
    public required Action DisconnectMcu { get; init; }

    /// <summary>构造手柄输入适配器（桥连接成功后调用；应现建不缓存——设备重连后旧适配器失效）。</summary>
    public required Func<IPadInput?> GetPad { get; init; }
}