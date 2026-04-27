const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

const KEYWORDS = [
    'IMPORT',
    'FOR', 'TO', 'STEP', 'NEXT', 'BREAK', 'CONTINUE',
    'WHILE', 'END', 'IF', 'ELIF', 'ELSE', 'ENDIF', 'FUNC', 'RETURN', 'ENDFUNC',
    'CALL', 'PRINT', 'ALERT', 'RAND', 'TIME', 'WAIT', 'AMIIBO', 'BEEP',
    'and', 'or', 'not'
];

const projName = 'EasyCon';
const IMG_LABEL = 'ImgLabel';
const projcfg = 'config.toml';

const SEARCH_METHOD_MAP = {
    0: '平方差匹配',
    1: '标准差匹配',
    2: '相关匹配',
    3: '标准相关匹配',
    4: '相关系数匹配',
    5: '标准相关系数匹配',
    6: '严格匹配',
    7: '随机严格匹配',
    8: '透明度匹配',
    9: '相似匹配',
    11: 'XY平均值边缘检测',
    12: '拉普拉斯边缘检测',
    13: 'Canny边缘检测',
    107: 'OCR单行文本识别'
};

const imgLabelCache = new Map();
const imgLabelWatchers = new Map();
let imgLabelStatusBarItem = null;
let outputChannel = vscode.window.createOutputChannel(projName);
let lspClient = null;

// --------------------------------------------------------------------------- //
//  LSP client
// --------------------------------------------------------------------------- //

function getBundledServerPath(context) {
    const platform = process.platform;
    const binaryName = platform === 'win32' ? 'ecs-lsp.exe' : 'ecs-lsp';
    const bundled = path.join(context.extensionPath, 'bin', binaryName);
    if (fs.existsSync(bundled)) {
        return bundled;
    }
    return undefined;
}

function startLspClient(context) {
    const config = vscode.workspace.getConfiguration('easycon');
    const customPath = config.get('languageServer.path', '');

    let serverOptions;

    if (customPath) {
        serverOptions = {
            command: customPath,
            transport: TransportKind.stdio,
        };
    } else {
        const bundled = getBundledServerPath(context);
        if (bundled) {
            serverOptions = {
                command: bundled,
                transport: TransportKind.stdio,
            };
        } else {
            serverOptions = {
                command: 'python',
                args: ['-m', 'easycon_script_lsp'],
                options: {
                    cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath,
                },
            };
        }
    }

    const clientOptions = {
        documentSelector: [{ scheme: 'file', language: 'easycon-script' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.ecs'),
        },
        outputChannel: vscode.window.createOutputChannel('EasyCon Script LSP'),
    };

    lspClient = new LanguageClient(
        'ecs-lsp',
        'EasyCon Script Language Server',
        serverOptions,
        clientOptions
    );

    context.subscriptions.push(lspClient);
    lspClient.start();
}

// --------------------------------------------------------------------------- //
//  Activation
// --------------------------------------------------------------------------- //

function activate(context) {
    context.subscriptions.push(outputChannel);
    outputChannel.appendLine('EasyCon Script extension is now active!');

    // Start LSP client
    startLspClient(context);

    // ImgLabel initialization
    initImgLabelCompletions();

    vscode.workspace.onDidChangeWorkspaceFolders(() => {
        initImgLabelCompletions();
    });

    // Format on save via LSP
    vscode.workspace.onDidSaveTextDocument((document) => {
        if (document.languageId === 'easycon-script') {
            const formatOnSave = vscode.workspace.getConfiguration('editor', document.uri).get('formatOnSave');
            if (!formatOnSave) {
                vscode.commands.executeCommand('editor.action.formatDocument');
            }
        }
    });

    // Status bar: version
    const statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'easycon.showVersion';
    context.subscriptions.push(statusBarItem);
    updateVersionStatusBar(statusBarItem);

    // Status bar: ImgLabel count
    imgLabelStatusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
    imgLabelStatusBarItem.command = 'easycon.refreshImgLabel';
    context.subscriptions.push(imgLabelStatusBarItem);
    updateImgLabelStatusBar(imgLabelStatusBarItem);

    // Command: refresh ImgLabel
    let refreshImgLabel = vscode.commands.registerCommand('easycon.refreshImgLabel', () => {
        imgLabelCache.clear();
        initImgLabelCompletions();
    });
    context.subscriptions.push(refreshImgLabel);

    // Command: show version
    let showVersion = vscode.commands.registerCommand('easycon.showVersion', () => {
        updateVersionStatusBar(statusBarItem, true);
    });
    context.subscriptions.push(showVersion);

    // Command: format document
    let formatDocumentCmd = vscode.commands.registerCommand('easycon.formatDocument', () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        const document = editor.document;
        if (document.languageId !== 'easycon-script') return;

        if (document.isDirty) {
            document.save().then(() => {
                vscode.commands.executeCommand('editor.action.formatDocument');
            });
        } else {
            vscode.commands.executeCommand('editor.action.formatDocument');
        }
    });
    context.subscriptions.push(formatDocumentCmd);

    // Command: run script
    let runScript = vscode.commands.registerCommand('easycon.runScript', () => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showErrorMessage('没有打开的文件');
            return;
        }
        const document = editor.document;
        const filePath = document.uri.fsPath;
        if (document.isDirty) {
            document.save().then(() => executeScript(filePath));
        } else {
            executeScript(filePath);
        }
    });
    context.subscriptions.push(runScript);

    // ImgLabel completion provider (@ trigger only)
    context.subscriptions.push(vscode.languages.registerCompletionItemProvider('easycon-script', {
        provideCompletionItems(document, position) {
            const line = document.lineAt(position.line).text;
            const textBefore = line.substring(0, position.character);
            const completionItems = [];

            if (textBefore.endsWith('@')) {
                const imgLabels = getImgLabelCompletions(document);
                imgLabels.forEach(c => {
                    const item = new vscode.CompletionItem(c, vscode.CompletionItemKind.Reference);
                    item.insertText = c;
                    completionItems.push(item);
                });
            }

            return completionItems;
        }
    }, '@'));

    // ImgLabel hover provider
    context.subscriptions.push(vscode.languages.registerHoverProvider('easycon-script', {
        provideHover(document, position) {
            const line = document.lineAt(position.line).text;
            const range = new vscode.Range(position.line, 0, position.line, line.length);
            const word = document.getText(range).match(/@(\S+)/);
            if (!word) return null;

            const labelName = word[1];
            const scriptDir = path.dirname(document.uri.fsPath);
            const imgLabelPath = path.join(scriptDir, IMG_LABEL, labelName + '.IL');

            if (!fs.existsSync(imgLabelPath)) {
                return new vscode.Hover(`未找到标签: ${labelName}`, range);
            }

            try {
                const content = fs.readFileSync(imgLabelPath, 'utf-8');
                const labelData = JSON.parse(content);
                const md = new vscode.MarkdownString();
                md.appendMarkdown(`**文件:** ${imgLabelPath}\n\n`);
                md.appendMarkdown(`**名称:** ${labelName}\n\n`);
                md.appendMarkdown(`**搜图方法:** ${SEARCH_METHOD_MAP[labelData.searchMethod] || '未知'} \n\n`);
                md.appendMarkdown(`![image](data:image/png;base64,${labelData.ImgBase64})`);
                return new vscode.Hover(md, range);
            } catch (e) {
                return new vscode.Hover(`解析标签失败: ${e.message}`, range);
            }
        }
    }));
}

// --------------------------------------------------------------------------- //
//  ImgLabel functions
// --------------------------------------------------------------------------- //

function getImgLabelCompletions(document) {
    const scriptDir = path.dirname(document.uri.fsPath);
    if (imgLabelCache.has(scriptDir)) {
        return imgLabelCache.get(scriptDir);
    }
    return loadImgLabelDir(scriptDir);
}

function loadImgLabelDir(scriptDir, createWatcher = true) {
    const imgLabelDir = path.join(scriptDir, IMG_LABEL);
    const completions = [];
    console.log('loadImgLabelDir:', imgLabelDir, 'exists:', fs.existsSync(imgLabelDir));

    if (fs.existsSync(imgLabelDir)) {
        try {
            const files = fs.readdirSync(imgLabelDir);
            console.log('Files in ImgLabel dir:', files);
            files.forEach(file => {
                if (file.toUpperCase().endsWith('.IL')) {
                    const name = file.slice(0, -3);
                    completions.push(name);
                }
            });
            console.log('Loaded completions:', completions);
            imgLabelCache.set(scriptDir, completions);

            if (!imgLabelWatchers.has(scriptDir) && createWatcher) {
                const watcher = fs.watch(imgLabelDir, { recursive: false }, (eventType, filename) => {
                    if (filename && filename.toUpperCase().endsWith('.IL')) {
                        const newCompletions = loadImgLabelDir(scriptDir, false);
                        imgLabelCache.set(scriptDir, newCompletions);
                        updateImgLabelStatusBarAll();
                    }
                });
                imgLabelWatchers.set(scriptDir, watcher);
            }
        } catch (e) {
            console.error('读取' + IMG_LABEL + '目录失败:', e);
        }
    }
    return completions;
}

function initImgLabelCompletions() {
    const scriptDirs = new Set();
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders) {
        workspaceFolders.forEach(folder => {
            scriptDirs.add(folder.uri.fsPath);
        });
    }
    vscode.workspace.textDocuments.forEach(doc => {
        if (doc.uri.fsPath.endsWith('.ecs')) {
            scriptDirs.add(path.dirname(doc.uri.fsPath));
        }
    });
    console.log('initImgLabelCompletions, scriptDirs:', Array.from(scriptDirs));
    scriptDirs.forEach(scriptDir => {
        console.log('Loading ImgLabel from:', scriptDir);
        loadImgLabelDir(scriptDir);
    });
    updateImgLabelStatusBarAll();
}

function updateImgLabelStatusBarAll() {
    outputChannel.appendLine('开始更新搜图标签');
    if (!imgLabelStatusBarItem) return;
    let totalCount = 0;
    imgLabelCache.forEach(completions => {
        totalCount += completions.length;
    });
    imgLabelStatusBarItem.text = `已加载标签: ${totalCount}`;
    imgLabelStatusBarItem.tooltip = `已加载标签: ${totalCount}`;
    imgLabelStatusBarItem.show();
}

// --------------------------------------------------------------------------- //
//  Script execution / config
// --------------------------------------------------------------------------- //

function getEzconPath() {
    const ezconRoot = process.env.EASYCON_ROOT;
    if (ezconRoot) {
        return process.platform === 'win32'
            ? `${ezconRoot}/ezcon.exe`
            : `${ezconRoot}/ezcon`;
    }
    return 'ezcon';
}

function parseToml(content) {
    const result = {};
    let currentSection = null;
    const lines = content.split('\n');
    for (let line of lines) {
        line = line.trim();
        if (!line || line.startsWith('#')) continue;
        const sectionMatch = line.match(/^\[([^\]]+)\]$/);
        if (sectionMatch) {
            currentSection = sectionMatch[1];
            result[currentSection] = {};
            continue;
        }
        const kvMatch = line.match(/^([\w一-鿿]+)\s*=\s*(.+)$/);
        if (kvMatch && currentSection) {
            let value = kvMatch[2].trim();
            if ((value.startsWith('"') && value.endsWith('"')) ||
                (value.startsWith("'") && value.endsWith("'"))) {
                value = value.slice(1, -1);
            }
            if (!isNaN(value) && value !== '') {
                value = Number(value);
            }
            result[currentSection][kvMatch[1]] = value;
        }
    }
    return result;
}

function loadConfig(scriptPath) {
    const configPath = path.join(path.dirname(scriptPath), projcfg);
    if (!fs.existsSync(configPath)) return null;
    try {
        return parseToml(fs.readFileSync(configPath, 'utf-8'));
    } catch (e) {
        console.error('读取配置文件失败:', e);
        return null;
    }
}

function buildArgs(config) {
    const args = [];
    if (!config || !config.core) return args;
    const core = config.core;
    if (core.port) args.push('-p', core.port);
    if (core.camera !== undefined) args.push('-d', core.camera);
    if (core.videotype) args.push('-vt', core.videotype);
    return args;
}

function executeScript(filePath) {
    if (!outputChannel) {
        outputChannel = vscode.window.createOutputChannel(projName);
    }
    outputChannel.show();
    outputChannel.appendLine(`[${new Date().toLocaleTimeString()}] 执行脚本: ${path.basename(filePath)}`);

    const terminal = vscode.window.activeTerminal || vscode.window.createTerminal(projName);
    const ezcon = getEzconPath();
    const config = loadConfig(filePath);
    const args = buildArgs(config);
    const argsStr = args.length > 0 ? ' ' + args.join(' ') : '';
    terminal.show();
    const cmd = `${ezcon} run "${filePath}"${argsStr}`;
    outputChannel.appendLine(`执行命令: ${cmd}`);
    terminal.sendText(cmd);
}

// --------------------------------------------------------------------------- //
//  Status bar
// --------------------------------------------------------------------------- //

function updateVersionStatusBar(statusBarItem, showMessage = false) {
    exec(`${getEzconPath()} --version`, (error, stdout) => {
        if (error) {
            statusBarItem.text = '$(alert) EasyCon';
            statusBarItem.tooltip = '无法获取版本号: ' + error.message;
            if (showMessage) vscode.window.showErrorMessage('无法获取 EasyCon 版本号: ' + error.message);
        } else {
            const lines = stdout.trim().split('\n');
            let version = lines[lines.length - 1].trim();
            const shortver = version.includes('+') ? version.substring(0, version.indexOf('+')) : version;
            statusBarItem.text = `$(package) EasyCon ${shortver}`;
            statusBarItem.tooltip = `EasyCon 版本: ${shortver}`;
            if (showMessage) vscode.window.showInformationMessage(`EasyCon 版本: ${version}`);
        }
        statusBarItem.show();
    });
}

function updateImgLabelStatusBar(statusBarItem) {
    let totalCount = 0;
    imgLabelCache.forEach(completions => {
        totalCount += completions.length;
    });
    statusBarItem.text = `已加载标签: ${totalCount}`;
    statusBarItem.tooltip = `已加载标签: ${totalCount}`;
    statusBarItem.show();
}

// --------------------------------------------------------------------------- //
//  Deactivation
// --------------------------------------------------------------------------- //

function deactivate() {
    imgLabelWatchers.forEach(watcher => {
        watcher.close();
    });
    imgLabelWatchers.clear();
    imgLabelCache.clear();

    if (lspClient) {
        return lspClient.stop();
    }
}

module.exports = { activate, deactivate };
