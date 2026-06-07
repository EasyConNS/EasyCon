<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**IMPORTANT: This project has a knowledge graph. ALWAYS use the
code-review-graph MCP tools BEFORE using Grep/Glob/Read to explore
the codebase.** The graph is faster, cheaper (fewer tokens), and gives
you structural context (callers, dependents, test coverage) that file
scanning cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes` or `query_graph` instead of Grep
- **Understanding impact**: `get_impact_radius` instead of manually tracing imports
- **Code review**: `detect_changes` + `get_review_context` instead of reading entire files
- **Finding relationships**: `query_graph` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview` + `list_communities`

Fall back to Grep/Glob/Read **only** when the graph doesn't cover what you need.

### Key Tools

| Tool | Use when |
|------|----------|
| `detect_changes` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context` | Need source snippets for review — token-efficient |
| `get_impact_radius` | Understanding blast radius of a change |
| `get_affected_flows` | Finding which execution paths are impacted |
| `query_graph` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes` | Finding functions/classes by name or keyword |
| `get_architecture_overview` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes` for code review.
3. Use `get_affected_flows` to understand impact.
4. Use `query_graph` pattern="tests_for" to check coverage.

## 架构规范：MVVM

本项目 Avalonia UI（EasyCon2.Avalonia / EasyCon2.Avalonia.Core）采用 MVVM 架构。

- **ViewModel 中不引用任何 Avalonia 控件类型**（Window、Control、TextBox 等）。
- **View → ViewModel 通信**：优先使用绑定（`{Binding ...}`、`{x:Bind ...}`），避免在 code-behind 中订阅 ViewModel 事件。
- **ViewModel → View 通信**：优先使用可观察属性（`[ObservableProperty]`、`AvaloniaProperty`）通过绑定驱动 UI；需要 View 主动推送数据给 ViewModel 时，用 `AvaloniaProperty`（DirectProperty / StyledProperty）配合 TwoWay 绑定，而非事件回调。
- **Code-behind 仅用于**：平台级 API（文件对话框、拖放）、UI 布局自适应（SizeChanged）、视觉树初始化（FoldingManager、LSP）。业务逻辑不进 code-behind。
- **自定义控件**：需要暴露可绑定属性时，使用 `AvaloniaProperty.RegisterDirect` / `Register`，不要用普通 CLR 属性 + 事件。
