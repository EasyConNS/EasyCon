#nullable enable

namespace EasyCon.Core.Services;

/// <summary>桌面通知能力。调用方不应等待网络发送完成。</summary>
public interface IAlertService
{
    void Dispatch(string content, string title = "伊机控消息", byte[]? image = null);
}