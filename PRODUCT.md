# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

需要持续产出结构化 Markdown 文档的学生、研究者、产品与技术团队，以及需要按明确要求提交材料的参赛者。用户通常一边整理本地资料，一边写作、核查和修改文档。

## Product Purpose

MarkLeaf Agent 是一款桌面 Markdown 编辑器与文档 Agent 融合的软件。成功意味着用户无需离开编辑器，就能让 Agent 理解当前文档、工作区资料和任务要求，提出有来源、可审阅、可撤销的修改。

## Positioning

它不是通用聊天框，也不是代码编辑器换皮；它把 Markdown 文档本身作为 Agent 的工作对象，将要求覆盖、来源证据、修改审阅和配套材料导出放在同一条工作流中。

## Operating Context

用户在 Windows 桌面端打开文件夹或 Markdown 文档。左侧管理文件和大纲，中间编辑与预览 Markdown，右侧 Agent 始终可见。Agent 可以读取用户主动导入的本地资料；发送到远程模型前按设置确认；写回文档前必须由用户确认。

## Capabilities and Constraints

- 保留 MarkLeaf 已有的 Markdown 编辑、预览、文件管理、搜索、大纲、快捷键和导出能力。
- Agent 支持规划与执行两种工作方式，并提供资料导入、任务要求解析、证据引用、文档体检和配套材料导出。
- Agent 输出不得无提示覆盖正文；应用修改必须可撤销并留下审计记录。
- 本地优先，支持 OpenAI-compatible API 与本地 Ollama；API Key 不写入项目文件。
- Windows 桌面应用，C# WinForms 外壳与 WebView2 编辑/Agent 表面。

## Brand Commitments

产品名保留 MarkLeaf Agent。界面应克制、清晰、专业，延续 MarkLeaf 的绿色识别色；不使用紫色 AI 渐变、玻璃拟态、装饰性卡片堆叠或表情符号图标。参考现代 Agent 工作台的任务线程、活动状态和人工审阅模式，但保持 Markdown 编辑器的主地位。

## Evidence on Hand

现有 MarkLeaf Windows 代码库、编辑器实现、Agent 原型、用户提供的两张高 DPI 截图，以及现有的要求解析、资料索引、引用转换、文档体检和导出服务。没有真实客户、商业数据或性能基准，不得编造。

## Product Principles

- 打开即是编辑器与 Agent 并排的完整工作台。
- Agent 的每一步都可理解、可停止、可确认、可撤销。
- Markdown 是核心成果格式，不把产品退化成通用聊天工具。
- 资料和要求进入同一上下文，重要结论必须可追溯到来源。
- 高频写作动作保持快速、键盘友好和高 DPI 稳定。

## Accessibility & Inclusion

支持键盘导航、清晰焦点、文本缩放、高 DPI 和减少动态效果；状态不能只依赖颜色表达。
