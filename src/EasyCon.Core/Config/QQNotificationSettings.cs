#nullable enable

using System.Text.Json.Serialization;

namespace EasyCon.Core.Config;

/// <summary>
/// QQ 开放平台机器人通知配置。AppSecret 只在内存中保留，持久化时由
/// <see cref="QQNotificationSecretProtector"/> 写入 protected_secret。
/// </summary>
public sealed class QQNotificationSettings
{
    public string app_id { get; set; } = "";

    [JsonIgnore]
    public string secret { get; set; } = "";

    public string protected_secret { get; set; } = "";
    public bool remember_secret { get; set; }
    public string user_openid { get; set; } = "";
    public string group_openid { get; set; } = "";
    public bool user_enabled { get; set; } = true;
    public bool group_enabled { get; set; }
    [JsonIgnore]
    public bool enabled { get; set; }

    [JsonIgnore]
    public string load_error { get; set; } = "";

    /// <summary>默认附带当前视频画面；没有画面时由宿主提供 Logo 作为回退。</summary>
    public bool attach_image { get; set; } = true;

    [JsonIgnore]
    public bool verified { get; set; }

    public QQNotificationSettings Clone()
    {
        return new QQNotificationSettings
        {
            app_id = app_id,
            secret = secret,
            protected_secret = protected_secret,
            remember_secret = remember_secret,
            user_openid = user_openid,
            group_openid = group_openid,
            user_enabled = user_enabled,
            group_enabled = group_enabled,
            enabled = enabled,
            attach_image = attach_image,
            verified = verified,
            load_error = load_error,
        };
    }

    internal void PrepareSecretForSave()
    {
        // 无法解密且未重新填写时，保留原密文，避免保存其它推送项时丢失它。
        if (secret.Length > 0)
        {
            if (!OperatingSystem.IsWindows())
                remember_secret = false;
            protected_secret = remember_secret ? QQNotificationSecretProtector.Protect(secret) : "";
        }
        else if (!remember_secret)
        {
            protected_secret = "";
        }
    }

    public IReadOnlyList<QQNotificationTarget> Targets()
    {
        List<QQNotificationTarget> result = [];
        if (user_enabled)
        {
            if (string.IsNullOrWhiteSpace(user_openid))
                throw new InvalidOperationException("请先绑定勾选的 QQ 私聊接收方。");
            result.Add(new QQNotificationTarget(QQNotificationTargetKind.User, user_openid.Trim()));
        }

        if (group_enabled)
        {
            if (string.IsNullOrWhiteSpace(group_openid))
                throw new InvalidOperationException("请先绑定勾选的 QQ 群聊接收方。");
            result.Add(new QQNotificationTarget(QQNotificationTargetKind.Group, group_openid.Trim()));
        }

        if (result.Count == 0)
            throw new InvalidOperationException("请至少勾选一个 QQ 接收方。");
        return result;
    }

    public bool IsReady()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(app_id)
                && !string.IsNullOrWhiteSpace(secret)
                && Targets().Count > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public enum QQNotificationTargetKind
{
    User,
    Group,
}

public sealed record QQNotificationTarget(QQNotificationTargetKind Kind, string OpenId);

/// <summary>Windows DPAPI 包装。非 Windows 环境不保存 AppSecret。</summary>
public static class QQNotificationSecretProtector
{
    public static string Protect(string secret)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("当前平台不支持 Windows DPAPI。");

        return Convert.ToBase64String(ProtectedData.Protect(secret));
    }

    public static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("当前平台不支持 Windows DPAPI。");

        return ProtectedData.Unprotect(Convert.FromBase64String(value));
    }

    private static class ProtectedData
    {
        private const uint CryptProtectUiForbidden = 0x1;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public nint pbData;
        }

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            nint entropy,
            string? reserved,
            nint prompt,
            uint flags,
            out DataBlob dataOut);

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            nint description,
            nint entropy,
            string? reserved,
            nint prompt,
            uint flags,
            out DataBlob dataOut);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern nint LocalAlloc(uint flags, nuint bytes);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern nint LocalFree(nint memory);

        private const uint LmemFixed = 0x0000;

        public static byte[] Protect(string value)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(value);
            nint inputPtr = LocalAlloc(LmemFixed, (nuint)input.Length);
            if (inputPtr == 0)
                throw new InvalidOperationException("无法分配 DPAPI 输入缓冲区。");

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(input, 0, inputPtr, input.Length);
                DataBlob inputBlob = new() { cbData = input.Length, pbData = inputPtr };
                if (!CryptProtectData(ref inputBlob, "EasyCon QQ notification", 0, null, 0, CryptProtectUiForbidden, out DataBlob outputBlob))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());

                try
                {
                    byte[] output = new byte[outputBlob.cbData];
                    System.Runtime.InteropServices.Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                    return output;
                }
                finally
                {
                    LocalFree(outputBlob.pbData);
                }
            }
            finally
            {
                LocalFree(inputPtr);
            }
        }

        public static string Unprotect(byte[] encrypted)
        {
            nint inputPtr = LocalAlloc(LmemFixed, (nuint)encrypted.Length);
            if (inputPtr == 0)
                throw new InvalidOperationException("无法分配 DPAPI 输入缓冲区。");

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(encrypted, 0, inputPtr, encrypted.Length);
                DataBlob inputBlob = new() { cbData = encrypted.Length, pbData = inputPtr };
                if (!CryptUnprotectData(ref inputBlob, 0, 0, null, 0, CryptProtectUiForbidden, out DataBlob outputBlob))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());

                try
                {
                    byte[] output = new byte[outputBlob.cbData];
                    System.Runtime.InteropServices.Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                    return System.Text.Encoding.UTF8.GetString(output);
                }
                finally
                {
                    LocalFree(outputBlob.pbData);
                }
            }
            finally
            {
                LocalFree(inputPtr);
            }
        }
    }
}