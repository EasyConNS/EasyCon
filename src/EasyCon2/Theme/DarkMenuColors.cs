namespace EasyCon2.Theme;

public class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => ThemeManager.MenuBackground;
    public override Color MenuStripGradientEnd => ThemeManager.MenuBackground;
    public override Color MenuItemSelected => ThemeManager.MenuItemSelected;
    public override Color MenuItemBorder => ThemeManager.MenuItemBorder;
    public override Color MenuItemSelectedGradientBegin => ThemeManager.MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => ThemeManager.MenuItemSelected;
    public override Color MenuItemPressedGradientBegin => ThemeManager.MenuItemPressed;
    public override Color MenuItemPressedGradientEnd => ThemeManager.MenuItemPressed;
    public override Color MenuBorder => ThemeManager.MenuItemBorder;
    public override Color ToolStripDropDownBackground => ThemeManager.MenuDropdownBackground;
    public override Color ImageMarginGradientBegin => ThemeManager.MenuImageMargin;
    public override Color ImageMarginGradientMiddle => ThemeManager.MenuImageMargin;
    public override Color ImageMarginGradientEnd => ThemeManager.MenuImageMargin;
    public override Color SeparatorDark => ThemeManager.MenuSeparatorDark;
    public override Color SeparatorLight => ThemeManager.MenuSeparatorLight;
    public override Color StatusStripGradientBegin => ThemeManager.StatusStripBackground;
    public override Color StatusStripGradientEnd => ThemeManager.StatusStripBackground;
}