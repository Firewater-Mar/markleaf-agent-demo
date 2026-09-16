# MarkLeaf Proof 使用说明

MarkLeaf Proof 是 MarkLeaf 内置的可信文档工作台。它面向需要按照明确要求、依据真实资料完成正式文档的用户，包括学生、科研人员、技术团队和项目申报人员。

## 打开方式

1. 在 MarkLeaf 中打开一个 Markdown 文档或工作区。
2. 打开“编辑”菜单。
3. 选择“MarkLeaf Proof 可信文档工作台”。

工作台的数据保存在当前项目的 `.markleaf/proof-project.json` 中。正文仍然是标准 Markdown 文件。

## 推荐流程

1. 在“任务要求”中导入评分标准、作业要求、期刊规范或公司模板。
2. 在“资料中心”添加 Markdown、文本、Word、PDF、数据文件或图片资料。
3. 运行“文档体检”，查看要求覆盖、证据状态、结构和本地资源问题。
4. 在“文档 Agent”中选择一个小任务，核对来源后再确认插入。
5. 在“交付中心”生成体检报告、AI 使用说明、答辩提纲和调研工具包。

## 可信写作规则

- Agent 只接收与当前任务命中的资料片段，不默认上传整个工作区。
- 每次写入正文前都需要人工确认。
- 临时来源编号会转换为标准 Markdown 脚注，包含来源文件和行号。
- AI 输出、人工确认和导出操作会写入项目审计记录。
- 软件检查的是“资料是否支持文档中的表达”，不声称判断绝对真伪。
- 调研工具包只生成问题与记录结构，不生成或代填调研结果。

## 资料格式

- Markdown、TXT、CSV、TSV、JSON、YAML 和 HTML：直接建立文本索引。
- Word `.docx`：无需安装 Office，可直接提取正文。
- PDF：优先调用系统中的 `pdftotext`，否则尝试读取基础文本层。扫描版 PDF 需要先做 OCR。
- PNG、JPG、WEBP 和 BMP：登记来源文件。当前版本需要先使用 OCR 转为文本后才能检索。

## AI 与隐私

默认配置连接本机 Ollama：

```text
API 地址：http://localhost:11434/v1
模型：qwen3:4b
```

也可以使用兼容 OpenAI Chat Completions 的服务。选择非本地地址时，MarkLeaf Proof 会在发送命中的资料片段前再次确认。API Key 只保留在当前应用进程中，不写入项目文件。

## 导出文件

交付文件保存在：

```text
.markleaf/exports/
```

其中包含：

- `文档体检报告.md`
- `AI使用说明.md`
- `答辩提纲.md`
- `调研工具包.md`

所有文件均为 Markdown，可以继续修改、纳入 Git 版本管理或用 MarkLeaf 导出为 PDF、HTML。
