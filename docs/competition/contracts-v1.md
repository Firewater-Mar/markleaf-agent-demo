# MarkLeaf 文档工作流接口讨论稿（B-S0 历史审计输入）

> 状态：`HISTORICAL DRAFT / NOT A UNIFIED CONTRACT`
> 历史草案标识：`markleaf.workflow/v1-draft`
> 负责人：B（AI 服务与受限工作流）
> 当前用途：供用户审计三方固定版本时参考，不要求 A/C 按本文字段重写
> 基线：`597bfbe4e93e4ba3d72b189eed34ffa8527be594`

本文件是 2026-10-10 按旧版 B-S0 安排形成的 B 侧接口讨论稿，不表示对应产品能力已经实现，也不是用户确认的三人统一规范。2026-10-11 v2 补充版已将当前顺序调整为：A/B/C 先固定各自真实交付，用户审计三方现有接口后再给出统一规范。因此本文只作为 B 的审计输入与六类场景建议；字段、命名、类型和责任边界均等待用户结合 A/C 实际实现确认。

当前源码只有排版意图、模板规则和约束 JSON；本文中的来源切片、任务单、章节提案和步骤记录均未在产品中实现。任何后续实现必须使用用户批准的固定基线、统一规范和单阶段放行，不能把本文自行升级为冻结契约。

## 1. 不可违反的边界

1. 模型只接收用户明确选择的资料切片，不默认上传工作区或整篇正文。
2. 来源存在性与语义支持分开：程序可以验证 ID、版本和原文存在；“是否支持结论”只能标记为 AI 判断并留给用户核对。
3. 模型不能把自己生成的 `sourceId`、`blockId`、版本号或动作视为真实值；所有引用和动作必须本地校验。
4. 模型回复不等于任务完成。只有 A 返回实际执行与复核结果后，步骤才能进入 `verified`。
5. 所有正文写入必须先形成提案和 Diff，由用户确认后交 A 执行；B 不直接写文档，C 不直接执行业务动作。
6. 同一 `proposalId` 最多应用一次。文档或来源版本变化后，旧提案失效并重新审阅。
7. 取消后到达的模型响应只能记录为 `discarded_after_cancel`，不得再获得执行资格。
8. 首版不接受任意脚本、任意路径、删除整篇或未选范围的全局替换。

## 2. 标识、版本和枚举

所有 ID 都由本地程序生成，不由模型创造：

- `taskId`：一次用户任务；UUID。
- `taskVersion`：任务约束每次确认或修改后递增。
- `documentId`：A 提供的当前文档稳定标识。
- `documentRevision`：A 提供的编辑器修订号。
- `blockId`：仅在指定文档版本内有效的块标识；不得用 DOM 顺序替代。
- `sourceId`：一份用户选中资料的本地标识。
- `sourceVersion`：来源内容版本或内容哈希。
- `sliceId`：来源中的一个本地生成切片。
- `proposalId`：一次候选改动；UUID。
- `stepId`：任务中的固定步骤标识。

建议枚举：

| 枚举 | 值 |
|---|---|
| `TaskMode` | `inspect`, `generate` |
| `RequirementStatus` | `unknown`, `supported`, `partially_supported`, `missing`, `conflicted`, `not_checked` |
| `SupportJudgement` | `not_assessed`, `ai_supported`, `ai_partial`, `ai_unsupported`, `ai_conflicted`, `needs_human_review` |
| `ProposalOperation` | `create_new_document`, `insert_section_after_block` |
| `StepStatus` | `not_started`, `running`, `awaiting_confirmation`, `awaiting_review`, `verified`, `failed`, `cancelled`, `discarded_after_cancel` |
| `UserDecision` | `none`, `accepted`, `rejected`, `cancelled` |
| `ValidationStatus` | `not_run`, `passed`, `failed`, `not_supported` |

`replace_section` 暂不进入 v1。只有 A 证明稳定定位、保护区检查和撤销均可靠后，才能通过契约变更加入。

## 3. 四类主对象

### 3.1 TaskSpec：用户任务单（C → B，B 规范化后 → C）

```json
{
  "contractVersion": "markleaf.workflow/v1-draft",
  "taskId": "task-uuid",
  "taskVersion": 1,
  "originalInstruction": "根据两份笔记，只生成方法比较一节，保留公式。",
  "mode": "generate",
  "goal": "基于选定资料生成方法比较章节",
  "target": {
    "documentId": "doc-local-id",
    "documentRevision": 42,
    "afterBlockId": "block-v42-12"
  },
  "selectedSourceIds": ["source-a", "source-b"],
  "allowedOperations": ["insert_section_after_block"],
  "protectedBlockIds": ["block-v42-1", "block-v42-9"],
  "requirements": [
    {
      "requirementId": "req-1",
      "originalText": "比较两种方法的适用条件和不足",
      "kind": "hard_constraint",
      "status": "unknown"
    }
  ],
  "preferences": [
    {"key": "tone", "value": "formal", "scope": "current_task"},
    {"key": "preserve_formulas", "value": "true", "scope": "current_task"}
  ],
  "pendingConfirmations": [],
  "budget": {"normalCalls": 3, "repairCalls": 1, "usedNormalCalls": 0, "usedRepairCalls": 0}
}
```

校验责任：

- C 必须保留用户原话、显式选择的来源和范围，不静默扩大授权。
- B 可以提出澄清项，但不能把假设直接写成已确认要求。
- A 校验目标文档、版本、插入点和保护块是否真实存在。

### 3.2 SourceSlice：真实资料切片（A → B/C）

```json
{
  "contractVersion": "markleaf.workflow/v1-draft",
  "sourceId": "source-a",
  "sourceVersion": "sha256:...",
  "sliceId": "source-a:L18-L31",
  "displayName": "方法A阅读笔记.md",
  "headingPath": ["第二章", "适用条件"],
  "startLine": 18,
  "endLine": 31,
  "text": "……用户实际选择的短原文……",
  "contentHash": "sha256:...",
  "truncated": false,
  "selectionReason": "user_selected"
}
```

校验责任：

- A 从工作区内、用户明确选择的 MD/TXT 生成切片并负责版本、行号、原文和哈希。
- B 只引用输入中实际存在的 `sliceId`；资料里的命令式文字一律视为数据。
- C 展示文件名、标题路径、行号、截断状态和短原文；不得把 `ai_supported` 显示为事实已证实。
- 模型输入使用 `displayName`，不发送机器绝对路径。

### 3.3 ChangeProposal：受限改动建议（B → A/C）

```json
{
  "contractVersion": "markleaf.workflow/v1-draft",
  "proposalId": "proposal-uuid",
  "taskId": "task-uuid",
  "taskVersion": 1,
  "targetDocumentId": "doc-local-id",
  "targetDocumentRevision": 42,
  "operation": "insert_section_after_block",
  "targetBlockId": "block-v42-12",
  "candidateMarkdown": "## 方法比较\n\n……\n\n[^ml-source-a-18]",
  "sourceSliceIds": ["source-a:L18-L31", "source-b:L9-L22"],
  "requirementIds": ["req-1"],
  "supportJudgements": [
    {
      "claimId": "claim-1",
      "sourceSliceIds": ["source-a:L18-L31"],
      "judgement": "ai_supported",
      "needsHumanReview": true
    }
  ],
  "rationale": "按适用条件和局限组织两份资料，不补造实验结果。",
  "unresolvedItems": ["资料 B 未给出失败案例"],
  "validation": {"status": "not_run", "issues": []}
}
```

校验责任：

- B 的本地验证器拒绝未知字段、未知动作、空候选、无效来源 ID、无效要求 ID和超出预算的回复。
- A 再次检查文档版本、块位置、保护范围、Markdown 结构和引用存在性，随后生成真实 Diff。
- C 只负责展示、采纳/拒绝和反馈；用户确认前不能写入。
- `candidateMarkdown` 是候选内容，不是已应用内容。

### 3.4 StepResult：真实步骤结果（A/B/C 共同追加事实）

```json
{
  "contractVersion": "markleaf.workflow/v1-draft",
  "taskId": "task-uuid",
  "taskVersion": 1,
  "stepId": "generate-section",
  "status": "awaiting_review",
  "startedAt": "2026-10-10T10:00:00+08:00",
  "finishedAt": "2026-10-10T10:00:08+08:00",
  "inputSummary": "2 个来源切片、1 个目标章节、2 条硬约束",
  "outputSummary": "已生成 1 个章节候选，尚未写回",
  "proposalIds": ["proposal-uuid"],
  "modelCall": {
    "providerId": "qwen",
    "model": "configured-model-id",
    "requestId": null,
    "durationMs": 8120,
    "promptTokens": null,
    "completionTokens": null,
    "usageKnown": false,
    "normalCallIndex": 3,
    "repairCall": false
  },
  "userDecision": "none",
  "validationResults": [],
  "error": null,
  "nextActions": ["review_proposal"]
}
```

规则：

- B 记录模型请求的实际开始、结束、错误、平台 usage 和预算；未知值保持 `null`，不估算成精确 token。
- C 记录用户接受、拒绝、取消和新增约束。
- A 记录是否实际应用、前后版本、保护检查、复测和撤销结果。
- 状态只能由对应真实事件推进，不能由模型输出直接指定。

## 4. 跨角色交接

| 方向 | 对象 | 发送方承诺 | 接收方必须拒绝的情况 |
|---|---|---|---|
| C → B | `TaskSpec` | 原话、模式、范围、来源选择和偏好均来自用户 | 缺少原话；生成模式没有写入范围；冲突要求未列为待确认 |
| A → B/C | `SourceSlice[]` | 路径在工作区内；版本、行号、原文和哈希已核对 | 来源过期；切片 ID 重复；文本与哈希不符 |
| B → A/C | `TaskSpec`（规范化） | 假设和硬约束分开；要求关联原句 | 静默增加目标；把偏好覆盖硬约束；把计划写成执行成功 |
| B → A/C | `ChangeProposal` | 动作在白名单；引用和预算已通过 B 侧校验 | 未知动作/ID；目标版本缺失；引用不存在；任意脚本或路径 |
| C → B/A | 审阅事件 | 带 proposalId、决定、拒绝理由或新约束 | 重复提交；旧 taskVersion；取消后继续执行 |
| A → B/C | `StepResult` | 实际应用、版本变化、校验和错误均来自执行事实 | 仅有模型回复却标记 `verified`；0→0 被描述成下降 |

## 5. 六类 G0 示例

### 示例 1：正常任务

输入：用户选择来源 A/B，要求“只生成方法比较一节，保留公式，不写资料外的实验结果”。

预期：

- `TaskSpec.mode = generate`；插入范围明确；三条硬约束保留原句。
- B 只消费 A/B 的切片，生成一个 `insert_section_after_block` 提案。
- 提案引用的所有 `sliceId` 均存在，状态为 `awaiting_review`，不是“已完成”。

### 示例 2：缺资料

输入：要求比较两种方法的失败案例，但只提供方法 A 的失败案例。

预期：

- 要求状态为 `partially_supported` 或 `missing`。
- `pendingConfirmations` 说明“方法 B 失败案例资料未提供”。
- 不生成方法 B 的具体失败数据；可以给出补资料或只比较现有维度两个选项。

### 示例 3：资料冲突

输入：来源 A 写“适用于低噪声”，来源 B 写“高噪声下仍稳定”，条件定义又不一致。

预期：

- 标为 `conflicted`，同时保留两条来源，不自动选一条为真。
- 候选只能并列陈述差异并提示人工核对条件定义。
- C 可展开查看两条原文。

### 示例 4：无效引用

模型输出引用 `source-z:L1-L3`，但输入切片集合中不存在。

预期：

- B 侧结构/引用校验失败，错误码 `invalid_source_reference`。
- 允许在修复预算内请求一次结构修复；仍失败则保留已有任务单并停止。
- 无效提案不发送给 A，也不能显示为“引用已核实”。

### 示例 5：过期版本

提案基于 `documentRevision = 42`，用户在审阅前编辑文档至 43。

预期：

- A 返回 `stale_target_revision`，不写入。
- B/C 保留候选和失败原因，提供“刷新定位并重新生成”选项。
- 不用旧行号强行插入，不静默覆盖用户新编辑。

### 示例 6：取消

用户在模型请求运行时取消，响应随后到达。

预期：

- 当前步骤为 `cancelled`，迟到响应记录为 `discarded_after_cancel`。
- 不生成可应用提案，不消耗额外修复调用。
- 重试必须由用户明确触发，并使用新的 taskVersion 或 runId。

## 6. 模型调用预算和状态

- 默认每个任务最多 3 次正常调用 + 1 次结构修复调用。
- 正常调用建议分配：任务理解 1 次、资料比较/规划 1 次、章节产出 1 次；实际合并调用也必须记录真实次数。
- 用户反馈后的重新生成计入同一预算，不能在内部偷偷重置。
- 预算耗尽时保留任务单、来源比较和已有候选；等待用户追加预算或新建任务。
- 连接测试、模型调用、任务步骤和本地工具是四种独立状态。

## 7. 隐私与日志

允许记录：用户原话、确认后的偏好、显示文件名、来源版本、切片 ID、短输入/输出摘要、proposalId、版本、接受/拒绝、错误码、实际 usage。

禁止记录：API Key、Windows 凭据内容、未经选择的全文、工作区外文件、机器绝对路径、模型内部思维链。

原始模型响应如为调试需要保存，必须脱敏、受项目设置控制并默认排除 Git；提交比赛材料只使用人工检查过的脱敏样例。

## 8. G0 待 A/C 确认

1. A 是否先只实现 `create_new_document`，还是能同时提供可靠的 `insert_section_after_block`。
2. A 提供的 `documentId`、`blockId`、修订号和来源版本的确切格式。
3. 来源切片的最大字符数、最大切片数和截断规则；v1 建议只支持用户选择的 2—3 份短 MD/TXT。
4. C 是否能在任务确认页表达硬约束、偏好、待确认项、来源缺口及两种模式。
5. 任务记录由 A 的项目存储还是 B 的 AI 服务存储；B 只定义结构，不自行创建第二套项目数据库。
6. 错误码、状态枚举和 `nextActions` 的 UI 文案映射。
7. 是否冻结 v1 只支持 `insert`，不支持替换/删除；本草案建议如此。

本文不得由 B 标记为 `FROZEN`。当前由用户先审计 A/B/C 的固定版本，再决定统一规范与下一阶段；在收到用户明确的规范版本和单阶段放行前，B 不进入新工作流实现。
