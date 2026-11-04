using EasyCon.Core.LLM.Agent.Tools;

namespace EasyCon2.Avalonia.Services;

// ── 组合视图 ───────────────────────────────
// 设计约定（runtime knows capabilities, not integrations）：
// 每个工具只依赖它需要的那一个窄端口；窄端口本身已是 Core 词汇
// （见 EasyCon.Core.LLM.Agent.Tools.Ports），工具随词汇一起住在 Core。
// 本接口只是 GUI 内部的组合视图：依赖 GUI 宿主状态的实现（ToolCallService）
// 把全部窄端口聚合为单一注入点，方便宿主一次装配；Core 不可见此类型。

/// <summary>GUI agent 工具服务的组合视图（全部窄端口）。工具代码请依赖窄端口，勿依赖本接口。</summary>
public interface IToolCallService : IScriptEditorPort, IScriptRunPort, IWorkspaceInfoPort, IObservabilityPort, IDeviceRuntimePort
{
}