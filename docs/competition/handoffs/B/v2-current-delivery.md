# B 当前 AI 成果交付清单（2026-10-11 v2）

> 角色／当前任务／状态：`B / 固定并推送已有成果 / WAITING_USER_AUDIT`
> 实际工作目录：`E:\大二\MarkLeaf-AI`
> 个人交付分支：`codex/markleaf-typography`
> 批准基线：`597bfbe4e93e4ba3d72b189eed34ffa8527be594`
> 现有 AI 实现提交：`e4ec61ad55c50c38b32dd9db1722fa88434ec4a2`
> 推送目标：`origin`（`Firewater-Mar/markleaf-agent-demo`）；不向 `original`（原作者仓库）推送
> 统一接口状态：`NOT DEFINED`；旧 B-S0 字段稿仅作审计输入

## 1. 本次交付边界

根据 2026-10-11 v2 补充版，本轮只整理、自检、提交并推送已有 AI 成果，供用户固定版本并审计。没有新增跨模块能力，没有修改产品代码，没有调用真实或付费模型，没有替用户冻结统一接口，也没有要求 A/C 按 B 的字段重写。

本轮新增/更新的只是审计材料：

- `docs/competition/contracts-v1.md`：旧 B-S0 字段草案，已明确降级为历史接口讨论稿；
- `docs/competition/handoffs/B/S0.md`：保留旧阶段事实并补充 v2 顺序及测试更正；
- `docs/competition/handoffs/B/v2-current-delivery.md`：本交付清单。

## 2. 固定版本与代码归属清单

现有 AI 代码来自提交 `e4ec61ad55c50c38b32dd9db1722fa88434ec4a2`，已包含在当前基线中。本轮未修改这些文件。

| 模块 | 文件 | 当前责任与事实 |
|---|---|---|
| AI 契约 | `apps/windows/MarkLeaf.AI.Contracts/ITypographyAiClient.cs` | 定义连接选项、连接测试和返回 JSON 字符串的调用接口。 |
| AI 契约工程 | `apps/windows/MarkLeaf.AI.Contracts/MarkLeaf.AI.Contracts.csproj` | 独立轻量契约程序集。 |
| OpenAI 兼容客户端 | `apps/windows/MarkLeaf.AI.OpenAICompatible/OpenAICompatibleTypographyClient.cs` | 调用 `/chat/completions`，结构化输出失败时做一次兼容回退。 |
| 客户端工程 | `apps/windows/MarkLeaf.AI.OpenAICompatible/MarkLeaf.AI.OpenAICompatible.csproj` | 可选 AI 实现程序集。 |
| 密钥存储 | `apps/windows/MarkLeaf/Services/AI/AiApiKeyStore.cs` | 使用 Windows Credential Manager，按 provider 保存。 |
| 服务商目录 | `apps/windows/MarkLeaf/Services/AI/AiProviderCatalog.cs` | OpenAI、DeepSeek、Kimi、通义千问、智谱、自定义兼容端点及模型预设。 |
| AI 协调器 | `apps/windows/MarkLeaf/Services/AI/TypographyAiCoordinator.cs` | 排版意图、模板生成、约束解析、受限工作流规划及本地白名单校验。 |
| 按需加载 | `apps/windows/MarkLeaf/Services/AI/TypographyAiModule.cs` | 仅启用时反射加载可选 AI DLL。 |
| B 相关测试 | `apps/windows/tests/MarkLeaf.Tests/TypographyAgentPlanTests.cs` | 14 个路由/白名单/拒绝正文写作测试。 |

不属于 B 当前写入范围：A 的文档 AST、排版评估/执行/撤销、导入导出及宿主集成；C 的 WinForms/WebView2 UI、展示 DTO、控件事件和 QA 材料。

## 3. 现有接口现状

### 3.1 `ITypographyAiClient`

```csharp
public sealed record AiConnectionOptions(
    string BaseUrl,
    string Model,
    string ApiKey,
    int TimeoutSeconds = 45);

Task TestConnectionAsync(
    AiConnectionOptions options,
    CancellationToken cancellationToken = default);

Task<string> CompleteJsonAsync(
    AiConnectionOptions options,
    string systemPrompt,
    string userInput,
    CancellationToken cancellationToken = default);
```

边界：结果只有字符串，没有 requestId、usage、finishReason、实际模型、耗时和统一错误码。CancellationToken 已存在于接口和 HTTP 请求，但当前产品没有任务级 run 状态、迟到响应丢弃或预算对象。

### 3.2 `TypographyAiCoordinator`

现有公开能力：

- `CreateTemplateAsync`：把描述/Markdown 样例转换为排版模板；
- `ParseConstraintsAsync`：把自然语言转换为排版约束；
- `PlanWorkflowAsync`：将排版、模板、导入导出、打印等请求转换为白名单计划；
- 本地解析与校验：未知意图失败关闭，正文生成/改写请求被拒绝，不相关请求不能降级为 A4 优化。

当前输入主要是用户要求和会话摘要；不包含统一的文档事实、来源切片、稳定块 ID 或真实工具结果对象。当前输出是排版计划，不是来源驱动章节提案。

### 3.3 当前不存在的统一接口

以下能力尚未落地，名称也未由用户确认：任务单、来源切片、章节/表格候选、真实步骤结果、反馈修订、任务预算、取消后的迟到结果、跨 A/B/C 的错误/状态/事件映射。

`docs/competition/contracts-v1.md` 中的 `TaskSpec`、`SourceSlice`、`ChangeProposal`、`StepResult` 是 B 侧历史讨论稿，不是产品类型，也不是三方冻结规范。

## 4. 模型、提示词与隐私版本

当前模型由用户运行时选择，没有本轮绑定的真实模型。预设目录包括 OpenAI、DeepSeek、Kimi、通义千问、智谱和自定义 OpenAI 兼容端点；具体模型 ID 以 `AiProviderCatalog.cs` 固定版本为准。

| 固定对象 | SHA-256 |
|---|---|
| `TypographyAiCoordinator.cs`（模板/约束/工作流提示词与解析） | `24DA7F2113918ADF689C91BEE7FBE8734B686F68502036E046826D2043584CA0` |
| `AiProviderCatalog.cs`（供应商与模型预设） | `68621EA029E44CB8C30A69FB7B6DC6E7CBC65528D1B4465760D9D5548F09EB42` |
| `ITypographyAiClient.cs` | `D773600267CBD478EB594636FBEF457BD986B691BB8C2CF30A4C72FC98607CEE` |
| `OpenAICompatibleTypographyClient.cs` | `AD1F8742D28038D2DFC7FA7D7BF968D2660712925BE2B18F421F03F28C21B785` |
| `TypographyAgentPlanTests.cs` | `FDED244CCD0856B2DA99C0260782CC26E60F2D9261EC188B8A19E709FE03B0C3` |

客户端固定行为：temperature 为 0；优先请求 `response_format = json_object`；服务端以 400 表明不支持时仅回退一次；超时钳制到 10—120 秒；API Key 通过 Bearer Header 发送，不写入上述交付文档。

本轮没有真实模型调用、没有模拟模型成功记录、没有粘贴或提交密钥。此前用户实机连接或截图不能替代本固定版本的端到端验收。

## 5. 预算、取消与状态语义的真实现状

| 项目 | 当前实现 | 审计结论 |
|---|---|---|
| HTTP 超时 | 有，默认 45 秒，限制 10—120 秒 | 仅单次请求超时，不是任务预算。 |
| 请求取消 | 客户端方法和 HTTP 请求接收 CancellationToken | 缺少贯穿 UI/协调器/任务记录的统一取消生命周期。 |
| 任务调用预算 | 无 | “3 次正常 + 1 次修复”仅是旧讨论稿建议，尚未实现。 |
| 重试 | 仅 `response_format` 不兼容时回退一次 | 没有 429/5xx 退避、结构修复预算或用户反馈重生成预算。 |
| 迟到响应 | 无任务级处理 | 无法保证取消/切换任务后的迟到结果被丢弃。 |
| 幂等写入 | B 无写入实现 | 依赖未来用户规范与 A 的执行事务，不得假称已有。 |
| 状态 | 排版面板有连接/计划/本地工具文案 | 尚未形成跨三方真实 StepResult；模型回复不能代表执行完成。 |

## 6. 自检结果

### 6.1 静态清点

- 当前分支：`codex/markleaf-typography`；开始时 HEAD 与 `origin/main` 均为 `597bfbe4e93e4ba3d72b189eed34ffa8527be594`。
- B 所有权目录的产品代码本轮零修改；只有三份 `docs/competition/` 审计文档变化。
- 代码中没有新增 API Key、机器绝对路径或真实模型响应样例。
- `original` 指向原作者仓库，本轮不向该远端推送；个人交付只推送 `origin` 的独立分支。

### 6.2 构建

使用仓库自带 SDK `10.0.302`，为避免多个 MSBuild 进程竞争同一 `obj` 文件，使用单线程及禁用共享构建服务器：

```powershell
.\.tools\dotnet\dotnet.exe build apps\windows\tests\MarkLeaf.Tests\MarkLeaf.Tests.csproj --no-restore --disable-build-servers --verbosity normal -- -m:1
```

结果：**通过，0 警告、0 错误**。该命令同时构建测试项目、MarkLeaf、AI Contracts 与 OpenAICompatible 项目。

AI 客户端单独构建也通过：

```powershell
.\.tools\dotnet\dotnet.exe build apps\windows\MarkLeaf.AI.OpenAICompatible\MarkLeaf.AI.OpenAICompatible.csproj --no-restore --disable-build-servers --verbosity minimal -- -m:1
```

结果：**通过，0 警告、0 错误**。

### 6.3 定向测试

```powershell
.\.tools\dotnet\dotnet.exe test apps\windows\tests\MarkLeaf.Tests\MarkLeaf.Tests.csproj --filter "FullyQualifiedName~TypographyAgentPlanTests" --no-build --no-restore --disable-build-servers --verbosity normal -- -m:1
```

结果：测试程序集已找到，但 `vstest.console` 在 90 秒内无法连接 `testhost`，运行中止。故本轮不能声明 14 个 B 相关测试通过。构建成功与测试执行成功必须分开陈述。

### 6.4 真实／模拟模型测试

- 真实模型：本轮未调用；结果为 `NOT RUN`。
- 模拟模型：本轮没有执行；结果为 `NOT RUN`。
- 端到端“短资料 → 指定章节 → 来源 → 审阅 → 写回”：当前产品尚不具备统一接口，结果为 `NOT IMPLEMENTED`。

## 7. 已知缺陷和依赖

1. 当前协调器专注排版且主动禁止正文生成，不能直接满足新版“实质章节/表格”目标。
2. 模型不接收统一的来源切片、文档结构与版本事实，无法形成可本地验证的来源驱动提案。
3. AI 客户端结果过于扁平，缺少 usage、requestId、耗时、统一错误和重试信息。
4. 任务级预算、取消、迟到响应、反馈局部修订和持久状态均未实现。
5. 需要用户审计 A 的文档事实/版本/写入/撤销、B 的模型服务、C 的展示 DTO/事件后，才能确定最小适配。
6. `testhost` 在当前环境无法连接；需要 A/用户确认标准测试环境或测试平台配置。
7. UI 中模型连接状态与“本次任务是否调用模型”的文案可能混淆，属 C/宿主映射审计项。

## 8. 给用户审计的检查点

1. 固定 A/B/C 各自分支与完整提交 SHA，审计期间不追随变化的 main。
2. 将 A 的现有文档 ID/修订/来源/写回结果，与 C 的现有展示 DTO 和事件逐项列出，再对照 B 当前接口；不要直接采用本历史字段稿。
3. 对正常、缺资料、资料冲突、无效引用、过期版本、取消六类场景，确认真实产生方、校验方、展示方和执行方。
4. 确定 v1 最小动作是新建文档还是在稳定块后插入；未证明可靠前不开放替换/删除。
5. 决定任务预算、取消、重复确认、迟到响应和状态持久化由哪个层负责。
6. 用户确认统一规范、兼容映射、固定基线、允许文件和具体单阶段后，再放行 B；本轮不自动进入 S1/S3/S4。

## 9. 停止点

本交付推送后状态为 `WAITING_USER_AUDIT`。B 不继续猜测 A/C 接口，不新增工作流框架，不运行付费模型，不自动进入后续阶段。下一步只接受用户提供的固定规范版本与具体单阶段放行。
