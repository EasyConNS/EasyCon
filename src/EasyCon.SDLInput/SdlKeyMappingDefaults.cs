using EasyCon.Core.Config;
using SDL;

namespace EasyCon.SDLInput;

public static class SdlKeyMappingDefaults
{
    public static KeyMappingConfig Create()
    {
        return new KeyMappingConfig
        {
            SchemaVersion = KeyMappingConfig.CurrentSchemaVersion,
            A = (int)SDL_Scancode.SDL_SCANCODE_L,
            B = (int)SDL_Scancode.SDL_SCANCODE_K,
            X = (int)SDL_Scancode.SDL_SCANCODE_I,
            Y = (int)SDL_Scancode.SDL_SCANCODE_J,
            L = (int)SDL_Scancode.SDL_SCANCODE_G,
            R = (int)SDL_Scancode.SDL_SCANCODE_T,
            ZL = (int)SDL_Scancode.SDL_SCANCODE_F,
            ZR = (int)SDL_Scancode.SDL_SCANCODE_R,
            Plus = (int)SDL_Scancode.SDL_SCANCODE_KP_PLUS,
            Minus = (int)SDL_Scancode.SDL_SCANCODE_KP_MINUS,
            Capture = (int)SDL_Scancode.SDL_SCANCODE_Z,
            Home = (int)SDL_Scancode.SDL_SCANCODE_C,
            LClick = (int)SDL_Scancode.SDL_SCANCODE_Q,
            RClick = (int)SDL_Scancode.SDL_SCANCODE_E,
            Up = 0,
            Down = 0,
            Left = 0,
            Right = 0,
            UpRight = 0,
            DownRight = 0,
            UpLeft = 0,
            DownLeft = 0,
            LSUp = (int)SDL_Scancode.SDL_SCANCODE_W,
            LSDown = (int)SDL_Scancode.SDL_SCANCODE_S,
            LSLeft = (int)SDL_Scancode.SDL_SCANCODE_A,
            LSRight = (int)SDL_Scancode.SDL_SCANCODE_D,
            RSUp = (int)SDL_Scancode.SDL_SCANCODE_UP,
            RSDown = (int)SDL_Scancode.SDL_SCANCODE_DOWN,
            RSLeft = (int)SDL_Scancode.SDL_SCANCODE_LEFT,
            RSRight = (int)SDL_Scancode.SDL_SCANCODE_RIGHT,
        };
    }

    /// <summary>把盘上读到的配置解析为有效配置：缺档 / 旧 VK 档（SchemaVersion 落后）→ 默认值。</summary>
    public static KeyMappingConfig ResolveEffective(KeyMappingConfig? fromDisk)
    {
        if (fromDisk != null && fromDisk.SchemaVersion >= KeyMappingConfig.CurrentSchemaVersion)
            return fromDisk;

        return Create();
    }

    /// <summary>真实磁盘读取 + 解析（仅由 KeyMappingStore 的默认 loader 调用）。</summary>
    public static KeyMappingConfig LoadFromDiskOrDefaults()
        => ResolveEffective(ConfigManager.LoadKeyMapping());
}