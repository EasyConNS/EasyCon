using PTController;

namespace EasyCon2.Global
{
    public record VControllerConfig
    {
        public KeyMapping KeyMapping { get; set; }
        public bool ShowControllerHelp { get; set; } = true;
        public string CaptureType { get; set; } = "ANY";
        public string AlertToken { get; set; } = string.Empty;
        public string ChannelToken { get; set; } = string.Empty;

        public bool ChannelControl { get; set; } = true;

        public void SetDefault()
        {
            KeyMapping = new KeyMapping();
        }

        public bool CheckPPToken() => AlertToken.Length == 32;
    }
}
