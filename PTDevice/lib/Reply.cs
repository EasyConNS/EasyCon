namespace PTDevice.Arduino
{
    public static class Reply
    {
        public const byte Error = 0x0;
        public const byte Ack = 0xFF;
        public const byte Hello = 0x80;
        public const byte FlashStart = 0x81;
        public const byte FlashEnd = 0x82;
        public const byte ScriptAck = 0x83;
    }
}
