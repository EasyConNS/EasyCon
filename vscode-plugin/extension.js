const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
    console.log('EasyCon Script extension is now active!');

    // 创建状态栏项显示版本号
    const statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'easycon.showVersion';
    context.subscriptions.push(statusBarItem);

    // 获取并显示版本号
    updateVersionStatusBar(statusBarItem);

    // 注册显示版本命令
    let showVersion = vscode.commands.registerCommand('easycon.showVersion', function () {
        updateVersionStatusBar(statusBarItem, true);
    });
    context.subscriptions.push(showVersion);

    // 注册运行脚本命令
    let runScript = vscode.commands.registerCommand('easycon.runScript', function () {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('没有打开的文件');
            return;
        }

        const document = editor.document;
        const filePath = document.uri.fsPath;

        // 先保存文件
        if (document.isDirty) {
            document.save().then(() => {
                executeScript(filePath);
            });
        } else {
            executeScript(filePath);
        }
    });

    context.subscriptions.push(runScript);
}

function getEzconPath() {
    const ezconRoot = process.env.EASYCON_ROOT;
    if (ezconRoot) {
        // Windows 下使用 .cmd 后缀
        if (process.platform === 'win32') {
            return `${ezconRoot}/ezcon.exe`;
        }
        return `${ezconRoot}/ezcon`;
    }
    return 'ezcon';
}

function parseToml(content) {
    // 简单的 TOML 解析器，支持 [section] 和 key = "value" 格式
    const result = {};
    let currentSection = null;

    const lines = content.split('\n');
    for (let line of lines) {
        line = line.trim();
        if (!line || line.startsWith('#')) continue;

        // 匹配 [section]
        const sectionMatch = line.match(/^\[([^\]]+)\]$/);
        if (sectionMatch) {
            currentSection = sectionMatch[1];
            result[currentSection] = {};
            continue;
        }

        // 匹配 key = "value" 或 key = value
        const kvMatch = line.match(/^(\w+)\s*=\s*(.+)$/);
        if (kvMatch && currentSection) {
            let value = kvMatch[2].trim();
            // 去掉引号
            if ((value.startsWith('"') && value.endsWith('"')) ||
                (value.startsWith("'") && value.endsWith("'"))) {
                value = value.slice(1, -1);
            }
            // 尝试转换为数字
            if (!isNaN(value) && value !== '') {
                value = Number(value);
            }
            result[currentSection][kvMatch[1]] = value;
        }
    }
    return result;
}

function loadConfig(scriptPath) {
    const scriptDir = path.dirname(scriptPath);
    const configPath = path.join(scriptDir, 'config.toml');

    if (!fs.existsSync(configPath)) {
        return null;
    }

    try {
        const content = fs.readFileSync(configPath, 'utf-8');
        return parseToml(content);
    } catch (e) {
        console.error('读取配置文件失败:', e);
        return null;
    }
}

function buildArgs(config) {
    const args = [];

    if (!config || !config.core) {
        return args;
    }

    const core = config.core;

    // -p, --port 控制设备端口
    if (core.port) {
        args.push('-p', core.port);
    }

    // -d, --device 视频采集设备
    if (core.camera !== undefined) {
        args.push('-d', core.camera);
    }

    // -vt, --videotype 采集类型
    if (core.videotype) {
        args.push('-vt', core.videotype);
    }

    return args;
}

function executeScript(filePath) {
    const terminal = vscode.window.activeTerminal || vscode.window.createTerminal('EasyCon');
    const ezcon = getEzconPath();

    // 加载配置文件
    const config = loadConfig(filePath);
    const args = buildArgs(config);
    const argsStr = args.length > 0 ? ' ' + args.join(' ') : '';

    // 确保终端可见
    terminal.show();

    // 执行脚本
    terminal.sendText(`${ezcon} run "${filePath}"${argsStr}`);
}

function updateVersionStatusBar(statusBarItem, showMessage = false) {
    const ezcon = getEzconPath();
    exec(`${ezcon} --version`, (error, stdout, stderr) => {
        if (error) {
            statusBarItem.text = '$(alert) EasyCon';
            statusBarItem.tooltip = '无法获取版本号: ' + error.message;
            if (showMessage) {
                vscode.window.showErrorMessage('无法获取 EasyCon 版本号: ' + error.message);
            }
        } else {
            // 只取最后一行作为版本号
            const lines = stdout.trim().split('\n');
            let version = lines[lines.length - 1].trim();
            let shortver = version;
            // 去掉 + 后面的 git commit id
            const plusIndex = version.indexOf('+');
            if (plusIndex !== -1) {
                shortver = version.substring(0, plusIndex);
            }
            statusBarItem.text = `$(package) EasyCon ${shortver}`;
            statusBarItem.tooltip = `EasyCon 版本: ${shortver}`;
            if (showMessage) {
                vscode.window.showInformationMessage(`EasyCon 版本: ${version}`);
            }
        }
        statusBarItem.show();
    });
}

function deactivate() {}

module.exports = {
    activate,
    deactivate
}
