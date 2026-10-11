using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// Gitee 版本源更新检查。HTTP/JSON 解析/版本比较主体自 MainWindowViewModel.CheckUpdateAsync
/// 原样迁入，结果映射为 <see cref="UpdateCheckResult"/>；失败路径不抛异常（返回
/// <see cref="UpdateStatus.CheckFailed"/>），与原实现的 catch 行为对齐。
/// </summary>
public sealed class GiteeUpdateService : IUpdateService
{
    private readonly HttpClient _http;

    /// <param name="handler">供测试注入自定义 <see cref="HttpMessageHandler"/>；null 时使用默认配置。</param>
    public GiteeUpdateService(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await _http.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags?sort=updated&direction=desc&per_page=3", ct);
            var tags = System.Text.Json.JsonSerializer.Deserialize<VerInfo[]>(data);
            if (tags == null || tags.Length == 0)
            {
                return new UpdateCheckResult(UpdateStatus.CheckFailed, null);
            }
            var cutoff = new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc);
            var latest = tags.FirstOrDefault(t => t.commit != null && t.commit.date >= cutoff);
            if (latest == null)
            {
                return new UpdateCheckResult(UpdateStatus.UpToDate, null);
            }
            // tag 名不保证是合法版本号（v 前缀/任意命名），解析失败按“已是最新”处理，
            // 不能让单条坏 tag 使更新检查报“检查失败”
            var curVer = Assembly.GetEntryAssembly()?.GetName().Version;
            if (ConfigService.TryParseTagVersion(latest.name) is { } latestVer && latestVer > curVer)
                return new UpdateCheckResult(UpdateStatus.UpdateAvailable, latestVer);
            return new UpdateCheckResult(UpdateStatus.UpToDate, null);
        }
        catch
        {
            // 与原实现 catch 对齐：网络故障/解析失败一律归为 CheckFailed，不向调用方抛异常
            return new UpdateCheckResult(UpdateStatus.CheckFailed, null);
        }
    }

    private record CommitInfo
    {
        public string sha { get; set; } = "";
        public DateTime date { get; set; }
    }

    private record VerInfo
    {
        public string name { get; set; } = "";
        public CommitInfo? commit { get; set; }
    }
}
