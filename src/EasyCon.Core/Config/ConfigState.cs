namespace EasyCon.Core.Config;

public record ConfigState
{
    public const string DefaultWelcomeText = "欢迎使用 easycon";

    public string CaptureType { get; set; } = "ANY";
    public bool ShowControllerHelp { get; set; } = true;
    public bool EnableAutoCompletion { get; set; } = false;
    public bool AutoRunAfterFlash { get; set; } = false;
    public bool AutoSaveLog { get; set; } = false;
    public bool DarkMode { get; set; } = false;
    public string ColorSchemeName { get; set; } = "";
    public bool AutoSwitchColorSchemeEnabled { get; set; } = false;
    public string ThemeStyleName { get; set; } = "";
    public bool ShowFolding { get; set; } = true;
    public bool ShowDebugInfo { get; set; } = false;
    public bool AutoSwitchLayoutEnabled { get; set; } = false;
    public bool IsIdleThreeColumnLayoutSelected { get; set; } = true;
    public bool IsIdleTwoColumnLayoutSelected { get; set; } = false;
    public bool IsRunningThreeColumnLayoutSelected { get; set; } = false;
    public bool IsRunningTwoColumnLayoutSelected { get; set; } = true;
    public bool IsRunningOneColumnLayoutSelected { get; set; } = false;
    public double EditorFontSize { get; set; } = 14;
    public bool HighResolutionTiming { get; set; } = false;
    public string WelcomeText { get; set; } = DefaultWelcomeText;

    /// <summary>界面语言（文化代码，如 zh_CN / en_US）。空值 = 默认 zh_CN。</summary>
    public string LanguageCode { get; set; } = "";
}