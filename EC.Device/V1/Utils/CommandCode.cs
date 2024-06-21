namespace EC.Device;

public static class Command
{
    public const byte Ready = 0xA5;
    public const byte Debug = 0x80;
    public const byte Hello = 0x81;
    public const byte Flash = 0x82;
    public const byte ScriptStart = 0x83;
    public const byte ScriptStop = 0x84;
    public const byte Version = 0x85;
    public const byte LED = 0x86;
    public const byte UnPair = 0x87;
    public const byte ChangeControllerMode = 0x88;
    public const byte ChangeControllerColor = 0x89;
    public const byte SaveAmiibo = 0x90;
    public const byte ChangeAmiiboIndex = 0x91;
}

public static class Reply
{
    public const byte Error = 0x0;
    public const byte Busy = 0xFE;
    public const byte Ack = 0xFF;
    public const byte Hello = 0x80;
    public const byte FlashStart = 0x81;
    public const byte FlashEnd = 0x82;
    public const byte ScriptAck = 0x83;
}
