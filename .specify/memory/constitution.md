<!--
  ============================================================================
  Sync Impact Report — v1.0.0 → v1.1.0

  Version change: 1.0.0 → 1.1.0
  Bump rationale:  MINOR — 性能要求第1条（文件上传接口）拆分为双 tier，
                    新增 20MB~100MB 异步校验通道，原有 ≤20MB 同步校验约束
                    保持不变。属于对现有原则的实质性扩展，不影响向后兼容。

  Modified principles:
    - 性能要求 §1 文件上传接口：单一同步校验 → 双 tier（≤20MB 同步 / 20~100MB 异步）

  Added sections:  无

  Removed sections: 无

  Templates requiring updates:
    - .specify/templates/plan-template.md     ⚠ pending — Constitution Check
      需反映新的双 tier 上传性能门禁。
    - .specify/templates/spec-template.md     ✅ aligned — 无需更新。
    - .specify/templates/tasks-template.md    ⚠ pending — 测试标记仍为 OPTIONAL
      （与宪法 TDD 原则冲突，与本次变更无关）。
    - .specify/templates/checklist-template.md ✅ aligned — 无需更新。
    - specs/001-excel-analysis-report/spec.md  ✅ aligned — 已预设 ≤100MB
      上限及异步解析假设，与新宪法 tier 结构一致。

  Follow-up TODOs:  None — all placeholders resolved.
  ============================================================================
-->

# AIExport 项目宪法

## Core Principles

### 一、安全第一（不可协商）

所有用户输入 MUST 经过服务器端验证。所有 API 端点 MUST 要求身份验证。

以下凭证和敏感信息**绝对禁止**硬编码在代码、注释、配置文件、日志或提交消息中：

- API 密钥 / Token / Access Key / Secret Key
- 密码 / 连接字符串（含用户名密码）
- 私钥 / 证书 / JWT Secret
- 数据库凭证 / 第三方服务凭证
- 任何形式的认证凭据

敏感数据 MUST 加密存储（传输中和静态）。凭据 MUST 通过环境变量或外部密钥管理系统（Vault / Secret Manager）注入，代码中仅引用占位方式（如 `os.getenv()`）。

任何安全漏洞的修复优先级高于功能开发。发现凭据泄露 MUST 立即轮换并清除历史。

### 二、测试优先（不可协商）

TDD 强制流程：

1. **编写测试用例** — 先于实现代码撰写
2. **团队 review 测试用例** — 确认测试覆盖和正确性
3. **测试失败** — 验证测试确实检测到缺失功能
4. **编写实现代码** — 使测试通过
5. **重构** — 消除重复，改善结构

Red-Green-Refactor 闭环严格执行。单元测试覆盖率 MUST ≥ 80%。

### 三、代码简洁与可读性

代码 MUST 简洁、可读、自文档化：

- 所有公共 API、核心模块 MUST 有文档注释。
- 注释解释"为什么"，而非"是什么"。代码本身应对"是什么"自文档化。
- 变量、函数、类、模块使用描述性、有意义的命名。
- 函数和方法 SHOULD 保持简短、专注，只做好一件事。
- 不写需求之外的功能。不给只用一次的代码建抽象层（YAGNI）。
- 避免过早优化。先写简洁正确的代码，仅在性能分析证明必要时优化。
- README MUST 包含快速启动指南和本地开发环境配置步骤。
- Git 提交消息 MUST 使用中文。

### 四、质量关卡

代码只有在满足以下**全部**关卡时才算完成。任何违规 MUST 在合并或标记完成前解决：

1. **测试先行** — 测试用例在对应实现之前撰写。
2. **全部测试通过** — 测试套件中所有测试通过，无失败、无错误。
3. **代码审查** — 至少一次审查确认遵守所有宪法原则。
4. **无重复** — 已验证 DRY 原则，不存在冗余代码或逻辑。
5. **可读性检查** — 代码简洁、自文档化，遵循命名规范。
6. **边界覆盖** — 测试包含边界条件、错误路径和空/null 处理。

---

## 性能要求

以下性能指标为硬性约束，不可协商：

1. **文件上传接口**：在标准办公网络（10Mbps 上行）下，MUST 在 3s 内返回上传成功的 HTTP 200 响应。基础格式校验（列头完整性）按文件大小分两个 tier，超时 MUST 友好报错：

   | 文件大小 | 上传响应 | 校验方式 | 校验时限 |
   |----------|----------|----------|----------|
   | ≤ 20MB | 3s 内返回 200 | 同步校验 | 5s 内完成 |
   | 20MB ~ 100MB | 3s 内返回 200 | 异步校验 | 不绑定时限，通过进度通知反馈 |

   单个文件上限为 100MB，超出 MUST 拒绝。

2. **任务提交时效**：用户点击"确认"后，系统 MUST 在 1s 内返回任务已接收的回执（Task ID），并立即跳转至"报告生成中"等待页面，严禁前端界面卡死无响应。

3. **常规 Excel 处理**：对于行数 ≤ 10,000、列数 ≤ 50 的 Excel，报告完整生成耗时 MUST 控制在 30s 内。

4. **大型 Excel 处理**：对于行数 > 10,000 的 Excel，允许耗时延长至 3 分钟，但系统 MUST 提供实时进度条（轮询间隔 ≤ 2s），告知用户当前处理阶段（如："正在清洗数据..."、"正在计算交叉表..."、"正在渲染图表..."）。

5. **下载接口**：MUST 支持断点续传（Range Requests），单次下载连接时长不得超过 60s。

---

## Governance

### 权威性声明

本宪法是 AIExport 项目的**不可协商（non-negotiable）**根本准则。当 spec、plan、tasks、CLAUDE.md 或单次对话指令与宪法冲突时，MUST 调整前者，不得曲解、淡化或忽略宪法原则。

### 修订程序

1. 宪法的任何修改 MUST 记录在 Sync Impact Report 中。
2. 版本号遵循语义化版本规范（MAJOR.MINOR.PATCH）。
3. 所有依赖模板（spec、plan、tasks、checklist）MUST 在修订后同步更新。

### 合规审查

- 每次代码审查 MUST 验证遵守宪法原则。
- 每次计划（plan）生成 MUST 执行 Constitution Check 关卡。
- 任何原则违反 MUST 在代码合并前解决。

**Version**: 1.1.0 | **Ratified**: 2026-07-08 | **Last Amended**: 2026-07-08
