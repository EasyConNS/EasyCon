using EasyCon.Core.Config;

namespace EasyCon.SDLInput.Tests;

/// <summary>校验有效配置解析：缺档 / 旧 VK 档回退默认值，当前版本原样穿透。</summary>
[TestFixture]
public class SdlKeyMappingResolveTests
{
    [Test]
    public void ResolveEffective_Null_ReturnsDefaults()
    {
        KeyMappingConfig resolved = SdlKeyMappingDefaults.ResolveEffective(null);

        Assert.That(resolved, Is.EqualTo(SdlKeyMappingDefaults.Create()));
    }

    [Test]
    public void ResolveEffective_LegacyVkFile_ReturnsDefaults()
    {
        KeyMappingConfig legacy = new()
        {
            SchemaVersion = 0,
            Plus = 107,
            Minus = 109,
            A = 76
        };

        KeyMappingConfig resolved = SdlKeyMappingDefaults.ResolveEffective(legacy);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Plus, Is.EqualTo(87));
            Assert.That(resolved.A, Is.EqualTo(15));
            Assert.That(resolved.SchemaVersion, Is.EqualTo(KeyMappingConfig.CurrentSchemaVersion));
        });
    }

    [Test]
    public void ResolveEffective_CurrentVersion_ReturnsSameInstance()
    {
        KeyMappingConfig current = SdlKeyMappingDefaults.Create();

        KeyMappingConfig resolved = SdlKeyMappingDefaults.ResolveEffective(current);

        Assert.That(resolved, Is.SameAs(current));
    }
}