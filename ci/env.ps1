# 获取当前脚本目录
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition

# 永久设置到“用户”级别环境变量中
[Environment]::SetEnvironmentVariable("EASYCON_ROOT", $ScriptPath, "User")

Write-Host "已将当前目录 $ScriptPath 设置到环境变量EASYCON_ROOT中" -ForegroundColor Cyan