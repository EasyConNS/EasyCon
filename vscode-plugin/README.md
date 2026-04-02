# EasyCon Script

VS Code extension providing syntax highlighting for EasyCon Script language.

## Features

- Syntax highlighting for `.ecs` files
- Support for:
  - Comments (`#`) with TODO/FIXME/HACK/UNDONE markers
  - Strings (single and double quoted)
  - Variables (`$var`), capture variables (`@var`), constants (`_const`)
  - Numeric literals (integers, decimals, scientific notation)
  - Control flow keywords (IF, FOR, WHILE, FUNC, etc.)
  - Built-in functions (RAND, TIME, PRINT, ALERT, WAIT, AMIIBO, BEEP)
  - Gamepad button constants (A, B, X, Y, L, R, etc.)
- Comment toggling (`Ctrl+/`)
- Bracket matching and auto-closing

## Installation

### From source

1. Copy the `vscode-plugin` folder to your VS Code extensions directory:
   - Windows: `%USERPROFILE%\.vscode\extensions\`
   - macOS: `~/.vscode/extensions/`
   - Linux: `~/.vscode/extensions/`

2. Restart VS Code

### Using vsce (recommended)

```bash
cd vscode-plugin
npm install -g @vscode/vsce
vsce package
code --install-extension easycon-script-0.1.0.vsix
```

## Color Scheme

| Element | Color |
|---------|-------|
| Comments | Green |
| Strings | Orange |
| Numbers | Purple |
| Keywords | Blue (bold) |
| Built-in Functions | Light Blue (bold) |
| Gamepad Buttons | Red (bold) |
| Variables | Orange |
| Capture Variables | Purple-Orange |
| Constants | Brown |
