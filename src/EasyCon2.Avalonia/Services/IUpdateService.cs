namespace EasyCon2.Avalonia.Services;

public enum UpdateStatus { UpToDate, UpdateAvailable, CheckFailed }

public sealed record UpdateCheckResult(UpdateStatus Status, Version? LatestVersion);

/// <summary>版本更新检查（外部版本源 HTTP 查询）。与 UI 完全解耦。</summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}
