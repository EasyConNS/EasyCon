namespace PTDevice.Arduino
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
        public const byte ScriptScope = 0x86;
    }
}
