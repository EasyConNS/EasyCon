namespace EasyCon2;

public record VerInfo
{
    public string name { get; set; }
    public string message { get; set; }

    public Version Ver => new(name);

}

public class VersionParser(VerInfo[] version, Version cur)
{
    private readonly VerInfo _ver = version.Last();
    private readonly Version _cur = cur;

    public Version NewVer =>_ver.Ver;

    public bool IsNewVersion => _ver.Ver > _cur;
}
