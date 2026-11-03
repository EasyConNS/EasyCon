# ECS 模块导入规则

> **状态：现状**。本文是 `ModuleGraphBuilder` 的规则说明与编译序论证，供
> `docs/Pipeline.md`、`docs/ModuleSystem.md` 与 `docs/Script.md` 第十一部分引用。
> 规则编号沿用代码注释：R1–R5、R7（R6 无现行规则）。
> 实现落点：`src/EasyCon.Script/Modules/ModuleGraphBuilder.cs`；
> 语义测试：`test/EasyCon.Tests/LibTests.cs`。

## 1. 术语

- **主脚本 / 入口**：传给 `ProjectCompiler.CompileProject` 的文件，模块名固定 `<main>`。
- **项目根**：主脚本所在目录（`scriptDir`）；所有模块名相对它计算。
- **模块**：一个 `.ecs` 文件（编译单元），模块名见 R5。
- **显式边**：`IMPORT` 产生；**自动边**：目录扫描产生（R1/R3）。

## 2. 编译序

```
std → vision → 用户模块（显式边层级升序，同层按模块名 OrdinalIgnoreCase）→ main
```

- 层级只由**显式边**约束：`LevelOf(module) = max(LevelOf(显式依赖)) + 1`；
  显式环已在建图期报错，层级计算对残留环按 0 层截断（防御分支）。
- **自动边不约束层级**：同目录互见与 `lib/` 自动导入的模块按同层名序排列，
  依赖接口由 `.eci` 预提取先行就绪（见 `docs/ModuleSystem.md §1`）。
- 每个节点的依赖列表按 `std, vision, 自动依赖（名序，且去掉已显式依赖）, 显式依赖（发现序）`
  合成；显式别名优先于自动边。
- main 恒为最后一个编译单元。

## 3. 规则

| 编号 | 规则 | 语义 |
|---|---|---|
| R1 | 同目录互见 | 同一目录（入口目录除外）下的所有 `*.ecs` 互相可见，无需 `IMPORT`。扫描只覆盖该目录的**直接子文件**，不递归 |
| R2 | 导入基准 | `IMPORT "x.ecs"` 相对**导入文件所在目录**解析；基准随模块移动，不是进程 CWD |
| R3 | main 隐式导入 `lib/` | 主脚本自动导入 `<项目根>/lib/*.ecs`（直接子文件），**不传递**：`lib/` 的子目录不会因 R3 自动进入 |
| R4 | 跨目录显式导入 | 不在同一目录、也不在 `lib/` 顶层自动范围的模块，必须写 `IMPORT` |
| R5 | 模块名 | 相对项目根的规范化路径：POSIX `/`、去掉 `.ecs`。如 `lib/net/http.ecs` → `lib/net/http`。内存单文件无项目根时退化为文件名 |
| R7 | 入口目录豁免 | 项目根目录不触发 R1：并列主脚本互不打扰；根目录被显式导入的模块也不与根目录其他脚本互见 |

补充约束：

- 模块名查重**大小写不敏感**（节点表 `StringComparer.OrdinalIgnoreCase`）；同名或仅大小写不同 →
  `ECX0114 模块名冲突`。
- `IMPORT` 文件不存在 → `ECX0113`；显式依赖成环 → `ECX0112`。
- 自动边（R1/R3）不参与环检测：同目录互见天然双向，由接口先行预提取（`.eci`）保证循环可见可编译。
- `lib/` 自动导入只针对 main；lib 内文件之间的互见是 R1 的自然结果，不是 R3 的传递。
- `lib` 是**固定目录名**（`<项目根>/lib`），只扫描顶层 `*.ecs`。

## 4. 建图与收敛

1. 注册内嵌 `std` / `vision` 与 main 节点。
2. 从 main 开始 BFS：
   - `CollectExplicit`：扫描当前节点的 `IMPORT`，记录显式依赖并将新节点入队；
   - main 额外扫描 `<项目根>/lib`（R3）；
   - 非入口目录的节点扫描自身目录（R1/R7）。
3. 新注册节点入队继续扫描，直到队列为空（显式边与自动边相互触发、定点收敛）。
4. 已注册文件按完整路径去重（`byPath`，大小写不敏感）；自动边对已注册文件补齐反向边。
5. 闭包收敛后，对全部节点的显式依赖执行 DFS 环检测（`ECX0112`），再合并自动依赖并生成编译序。

## 5. 轻量导入扫描

建图阶段**不 parse 依赖模块**：

- 已有语法树（main 或缓存未命中后）→ 直接读 `ImportStmt`；
- 否则用 Lexer 令牌流匹配 `IMPORT` + `STRING`，跳过 `NEWLINE` / 空白 / 注释 trivia；
- 禁止裸正则扫描（避免把注释或字符串里的 `IMPORT` 误判为依赖）。

全量 parse 推迟到接口预提取（`.eci`）或缓存未命中时的模块编译。

## 6. 与项目骨架的对齐

`ezcon init` 生成的骨架（见 `docs/Script.md §11.4`）即本文规则的最小示例：

```
my-project/
├── main.ecs            <main>
├── lib/mod1.ecs        lib/mod1        ← R3 自动可见，main 无需 IMPORT
├── lib/net/http.ecs    lib/net/http     ← 不在 lib 顶层，需显式 IMPORT
└── other.ecs           根目录脚本        ← R7：不与 main 自动互见
```

- `main.ecs` 直接调用 `lib/mod1.ecs` 的 `ADD`：R3 + R1；
- `lib/net/http.ecs` 与 `lib/net/json.ecs` 同目录：R1 互见；
- `lib/mod1.ecs` **看不到** `lib/net/http.ecs`：不同目录且无显式 IMPORT（R4）。

## 7. 常见误区

| 现象 | 原因 |
|---|---|
| 根目录另一个 `.ecs` 的函数不可见 | R7 入口目录豁免；主脚本并列互不打扰 |
| `lib/sub/x.ecs` 没有自动可见 | R3 只扫 `lib/*.ecs` 顶层；子目录走 R1/R4 |
| 子目录模块互相调用失败 | R1 只覆盖同一目录；跨目录必须 `IMPORT` |
| `IMPORT` 在子目录模块内写相对主脚本的路径失败 | R2 基准是**导入文件**所在目录 |
| 两个仅大小写不同的模块名报冲突 | 模块名查重大小写不敏感（`ECX0114`） |
