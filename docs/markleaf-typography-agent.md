# MarkLeaf A4 排版智能体

## 1. 项目定位

MarkLeaf A4 排版智能体是基于 MarkLeaf Windows（C# WinForms + WebView2）的离线优先排版扩展。它只优化 Markdown 的打印与纸面阅读质量，不生成、续写或改写正文。

核心创新是把本地 Markdown AST 与 WebView2 的真实 DOM 测量结合，形成可解释的“感知 → 规划 → 执行 → 反馈”闭环。没有网络、没有模型、没有 API Key 时，质量评分、自动排版、差异审阅、模板和导入导出仍完整可用。

## 2. 分层架构

| 层 | 责任 | 关键实现 |
| --- | --- | --- |
| 视图层 | 主窗口 AI 模式、评分、缺陷列表、模板选择、Diff 勾选、导入入口 | `TypographyAiPanel`、`TypographyWorkbenchDialog`、`TypographyTemplateLibraryDialog` |
| 文档处理层 | 轻量块级 AST、源位置、受限 HTML/TXT 转 Markdown | `MarkdownAst`、`SimpleDocumentImportService` |
| 智能引擎层 | 评估、状态规划、最多 1–5 轮反馈循环 | `TypographyEvaluator`、`TypographyPlanner`、`TypographyFeedbackEngine` |
| AI 交互层 | 本地白名单路由任务；生成模型按需理解复杂模板描述和约束 | `TypographyAiModule`、`TypographyAiCoordinator` |
| 工具能力层 | 标题、空行、图片、表格、分页、目录和 Diff | `TypographyExecutor`、`LineDiff` |

AI 交互层默认关闭。用户从“查看 → AI 模式”（F10）进入主窗口右侧 AI 工作区，编辑区不会被替换；连接设置默认折叠，工作区宽度可拖动并记忆。生成模型客户端位于独立的 `MarkLeaf.AI.OpenAICompatible.dll`，纯净模式启动时不会加载，只有用户主动测试连接或执行需要自然语言理解的 AI 任务时才按需加载。检查、明确的单工具任务、导入导出和打印优先由本地白名单直接路由；其他自然表达由生成模型映射为编译期白名单中的受控意图，不要求固定命令。未知意图以及写作、续写、改写、润色、翻译正文等请求会被本地安全层拒绝，不会回退成 A4 优化。内置生成模型服务商预设包括 OpenAI、DeepSeek、Kimi / Moonshot、通义千问 / 百炼、智谱 GLM，并支持自定义兼容接口。API Key 保存在 Windows 凭据管理器，普通设置文件只记录服务地址和模型名。AI 只返回白名单字段，例如 `target_score`、`max_iterations`、`max_line_chars`、`image_max_width`、`generate_toc`，未知字段会被忽略、数值会被本地限制。本地规划器是唯一任务调度者，本地工具是唯一文档写入者。

## 3. P0 闭环

### 感知

`MarkdownAst.Parse` 产生带行号和字符偏移的块级 AST。`EditorHostController.MeasureTypographyAsync` 从 WebView2 读取块的实际宽高、版心宽度、页面内位置和 CSS 行高。`TypographyEvaluator` 合并两类证据并输出 0–100 分及结构化缺陷：

- 标题跳级、多余空行：AST 证据；
- 图片/表格溢出：DOM 宽度与版心比值；
- 孤行/寡行：块位置、A4 可用页高与行高；
- 行长、连续短段落：只提示，不自动重写正文；
- 缺少目录：标题数量与模板规则。

### 规划

`TypographyPlanner` 是确定性本地状态规划器。它把缺陷映射为工具任务，按 `严重度 × 1000 - 行号` 排序。用户拒绝的工具类型进入 `ForbiddenTaskTypes`，下一轮不会再次安排。

### 执行与 Diff

`TypographyExecutor` 只执行排版白名单操作：规范标题、压缩空行、限制图片宽度、给表格长词插入无语义 `<wbr>`、插入分页控制、生成目录。每项工具执行后由 `LineDiff` 生成 `DocumentChange`。工作台默认勾选所有改动，用户可逐项拒绝。

### 反馈

`TypographyFeedbackEngine` 每轮重新评分并重新规划。用户显式生成方案时，只要仍有可修复缺陷，即使初始分数已达阈值也会先执行一轮。后续停止条件：达到模板目标分、没有可执行任务、分数不再提升、达到模板最大轮次（强制限制 1–5）。

## 4. P1 跨格式

- TXT：保留纯文本并规范换行；
- 简单 HTML：只读取 `p/h1-h6/img/table`，脚本、样式和其他标签不会进入 Markdown；
- Markdown：使用 MarkLeaf 原生保存/另存为；
- HTML、PDF：复用 MarkLeaf 已有打印样式、预览与 WebView2 PDF 管线，不引入浏览器或重量级转换库。

## 5. P2 模板库

模板存储在用户数据目录的 `TypographyTemplates` 文件夹。内置模板只读，编辑内置模板时自动创建用户副本。模板库默认提供可视化规则表单，并能在本地从参数明确的自然语言描述或 Markdown 样例生成规则草稿；更自由的 AI 理解集中在主窗口 AI 模式中，避免模板库继续弹出连接设置窗口。创建 AI 模板时只发送工作区输入框中的描述或样例，不发送当前打开文档。JSON 只作为高级编辑和导入/导出格式，普通用户不需要手写。

```json
{
  "schema_version": 1,
  "id": "my-course-report",
  "name": "课程报告",
  "description": "A4 双面打印课程报告",
  "built_in": false,
  "rules": {
    "body_font_family": "宋体",
    "heading_font_family": "微软雅黑",
    "body_font_size_pt": 10.5,
    "heading_1_font_size_pt": 18,
    "heading_2_font_size_pt": 15,
    "heading_3_font_size_pt": 12,
    "line_spacing": 1.6,
    "paragraph_spacing_pt": 6,
    "max_line_chars": 42,
    "allow_orphan": false,
    "allow_widow": false,
    "image_max_width": 88,
    "table_max_width": 100,
    "consecutive_blank_line_limit": 1,
    "short_paragraph_chars": 18,
    "minimum_toc_headings": 4,
    "target_score": 92,
    "max_iterations": 4,
    "generate_toc": true,
    "keep_headings_with_next": true
  }
}
```

内置预设：学术报告、技术笔记、简历。模板库会明确显示正文与各级标题字号、字体、行距和段后距，并按参数实时绘制 A4 标准样张。切换模板会同时改变视觉规格、评分阈值、规划策略和执行参数。

## 6. 开发与答辩顺序

1. 离线领域模型和单元测试；
2. AST 评估与 DOM 测量；
3. 规划/执行/Diff/反馈状态机；
4. 工作台和模板库；
5. TXT/HTML 导入及原生 PDF/HTML 导出回归；
6. 性能、DPI、分页和异常文件测试；
7. 用同一篇缺陷文档演示“低分 → 修复 → 拒绝 → 重规划 → PDF”。

## 7. 技术坑点

- WebView2 返回的是 CSS 像素；A4 高度和页边距必须换算成同一单位。
- DOM 子节点与 Markdown 空行不是一一对应；桥接时跳过 AST 空白块并回填 `BlockIndex`。
- 暗色编辑主题不能进入打印输出；PDF/HTML 继续走 MarkLeaf 的独立导出样式。
- Markdown 图片标题和带空格 URL 不能用简单空格切割；尖括号 URL 需单独处理。
- 表格宽度不能靠 Markdown 标准属性表达；当前只插入 `<wbr>`，不截断或改写单元格内容。
- 多轮 Diff 的偏移会变化；应用改动时通过行号附近的原文匹配，而不是复用过期绝对偏移。
- 模板 JSON 必须限制范围和版本，损坏的用户模板不能阻止软件启动。
- 自动修复不得处理“行长/短段落”的语义内容，这是项目区别于通用写作 Agent 的边界。

## 8. 比赛评分映射

- 创新性：AST + 真实 DOM 的双证据闭环、拒绝反馈约束；
- 技术表现：确定性状态机、可解释评分、可测试工具、无模型也能运行；
- 功能完整性：P0/P1/P2 均有 UI 闭环；
- 方案完整性：分层、纯净模式、风险边界、模板契约和回归测试；
- 应用价值：课程报告、论文初稿、技术文档和简历可直接导出 A4 PDF。
