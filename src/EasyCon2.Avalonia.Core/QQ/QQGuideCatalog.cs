using Avalonia;

namespace EasyCon2.Avalonia.Core.QQ;

public sealed record QQGuideStep(
    int Number,
    string Title,
    string Phase,
    string ImageName,
    int PixelWidth,
    int PixelHeight,
    Rect Crop,
    IReadOnlyList<string> Actions,
    string Detail,
    string Result,
    IReadOnlyList<Rect> Focus,
    bool Caution = false)
{
    public string DisplayTitle => $"{Number:00}  {Title}";
    public Uri ImageUri => new($"avares://EasyCon2.Avalonia.Core/QQ/Resources/Guide/{ImageName}");
}

public static class QQGuideCatalog
{
    public static Uri PlatformUri { get; } = new("https://q.qq.com/#/apps");
    public const string AlertExample = "ALERT \"任务完成\"";

    public static IReadOnlyList<QQGuideStep> Steps { get; } = Array.AsReadOnly<QQGuideStep>(
    [
        new(1, "注册 QQ 开放平台账号", "注册与准备", "step-00.jpg", 2560, 1271,
            new Rect(880, 285, 800, 700),
            ["登录 QQ 开放平台。首次登录出现「你尚未注册」时，阅读服务协议，勾选同意后点击「同意」。"],
            "已有开发者账号时，可以直接进入控制台并跳过本步。",
            "完成注册，进入开放平台首页。",
            [new Rect(1278, 832, 190, 55)]),
        new(2, "完善主体信息", "注册与准备", "step-01.png", 1996, 1154,
            new Rect(20, 15, 1250, 260),
            ["点击首页顶部的「完善主体信息」，按平台提示填写并提交资料，完成后返回首页。"],
            "如果账号已经完成主体信息，可以跳过本步。具体资料以你账号页面的要求为准。",
            "账号主体信息已按页面要求完善。",
            [new Rect(464, 86, 97, 43)]),
        new(3, "选择「机器人」", "创建机器人", "step-02.png", 1996, 1154,
            new Rect(20, 65, 1350, 265),
            ["在开放平台首页点击第一项「机器人」。"],
            "这里是 QQ 机器人的入口。",
            "进入「我的机器人」介绍页面。",
            [new Rect(32, 133, 220, 75)]),
        new(4, "进入机器人管理", "创建机器人", "step-03.png", 1996, 1154,
            new Rect(35, 85, 1910, 460),
            ["点击页面中间的「去创建或管理我的 QQ 机器人」。"],
            "这个按钮会进入机器人的创建与管理页面。",
            "看到机器人列表或首次创建页面。",
            [new Rect(801, 426, 392, 82)]),
        new(5, "新建一个机器人", "创建机器人", "step-04.png", 1240, 1202,
            new Rect(20, 20, 1200, 235),
            ["点击「我的机器人」页面右上角的「＋ 创建机器人」。"],
            "如果已有要用于通知的机器人，可直接进入它的管理页面，从第 9 步继续。",
            "进入机器人资料填写页面。",
            [new Rect(1075, 59, 128, 52)]),
        new(6, "填写资料并创建", "创建机器人", "step-05.png", 2300, 1271,
            new Rect(860, 325, 595, 520),
            ["设置机器人的头像、名称和简介。可以使用自己的头像和名称。", "确认资料后，点击下方的「创建机器人」。"],
            "截图中的「通知机器人」仅为示例，按自己的用途填写即可。",
            "机器人创建成功，进入「设置 AI 服务」页面。",
            [new Rect(1045, 698, 215, 59)]),
        new(7, "选择「稍后连接」", "创建机器人", "step-06.png", 2300, 1271,
            new Rect(600, 20, 1120, 330),
            ["在「设置 AI 服务」页面，点击右上方的「稍后连接」。"],
            "EasyCon 会直接连接机器人，此处可以跳过平台推荐的 AI 服务。",
            "返回「我的机器人」列表，看到刚创建的机器人。",
            [new Rect(1588, 102, 98, 46)]),
        new(8, "打开机器人的管理页", "获取接入凭据", "step-07.png", 2300, 1271,
            new Rect(530, 20, 1230, 330),
            ["在机器人卡片上点击右侧箭头，进入它的管理页面。"],
            "刚创建时可能显示「离线（服务不可用）」。先继续准备凭据，之后在 EasyCon 中验证收发。",
            "进入该机器人的账号信息页面。",
            [new Rect(875, 169, 87, 72)]),
        new(9, "打开「开发设置」", "获取接入凭据", "step-08.png", 2300, 1271,
            new Rect(550, 20, 1200, 615),
            ["点击左侧菜单最下方的「开发设置」。"],
            "接下来需要的 AppID 和 AppSecret 位于这个页面。",
            "看到「APPID 接入凭证」和「事件与回调配置」。",
            [new Rect(571, 315, 105, 36)]),
        new(10, "复制 AppID，获取 AppSecret", "获取接入凭据", "step-09.png", 1240, 1013,
            new Rect(300, 95, 905, 345),
            ["点击 AppID 右侧的复制按钮，将它填入 qq bot 配置的 AppID 输入框。", "点击 AppSecret 右侧的眼睛图标；如果弹出重置提示，继续下一步。"],
            "确认事件接收方式为「WebSocket」，EasyCon 用它绑定接收方。已有可用 AppSecret 时，可以使用现有密钥并跳过重置。",
            "已取得 AppID，并进入获取 AppSecret 的流程。",
            [new Rect(1139, 154, 49, 36), new Rect(1140, 222, 44, 40)]),
        new(11, "按需重置 AppSecret", "获取接入凭据", "step-10.png", 1239, 969,
            new Rect(235, 460, 505, 295),
            ["需要生成新密钥时，阅读弹窗说明后点击「确认重置」。已有可用密钥时，点击「取消」并使用原密钥。"],
            "重置后旧 AppSecret 将失效，已经使用旧密钥的程序也需要更新。无需为了每次测试反复重置。",
            "确认重置后，页面显示新 AppSecret 的复制按钮。",
            [new Rect(489, 651, 192, 50)], Caution: true),
        new(12, "复制密钥，返回 qq bot 配置", "获取接入凭据", "step-11.png", 1240, 1013,
            new Rect(300, 95, 905, 210),
            ["点击 AppSecret 右侧的复制按钮，将密钥填入 qq bot 配置的 AppSecret 输入框。"],
            "AppSecret 是机器人鉴权密钥，请自行保存。接收通知的人无需拿到这份密钥。未勾选「记住 AppSecret」时，软件只在当前会话中使用它。",
            "AppID 和 AppSecret 已准备好，可以继续接入 EasyCon。",
            [new Rect(1138, 219, 47, 43)])
    ]);
}