
namespace ECDevice
{
    public static class Command
    {
        public const byte Ready = 0xA5;
        public const byte Debug = 0x80;
        public const byte Hello = 0x81;
        public const byte Flash = 0x82;
        public const byte ScriptStart = 0x83;
        public const byte ScriptStop = 0x84;
        public const byte Version = 0x85;
    }

    public static class Reply
    {
        public const byte Error = 0x0;
        public const byte Ack = 0xFF;
        public const byte Hello = 0x80;
        public const byte FlashStart = 0x81;
        public const byte FlashEnd = 0x82;
        public const byte ScriptAck = 0x83;
    }

    public enum Status
    {
        Connecting,
        Connected,
        ConnectedUnsafe,
        Error,
    }
}
