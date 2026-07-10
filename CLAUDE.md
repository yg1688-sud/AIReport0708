# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

## Security Rules（安全红线）

> **Hard rule — 违反即事故，不可协商。**

### 禁止在代码中提交敏感信息

- **绝对禁止**在代码、注释、commit message、配置文件（除 `.env.example` 外）、日志中硬编码：
  - API 密钥 / Token / Access Key / Secret Key
  - 密码 / 连接字符串（含用户名密码）
  - 私钥 / 证书 / JWT Secret
  - 数据库凭证 / 第三方服务凭证
  - 任何形式的认证凭据
- **必须使用**环境变量（`.env`）或外部配置系统（如 Secret Manager / Vault），代码中仅引用 `os.getenv()` / `config["xxx"]` 等占位方式。
- **模板文件**（如 `.env.example`、`config.example.yaml`）只能包含占位符，不得填入真实值。
- **发现误提交**：立即轮换泄露的凭据（密钥一旦 push 即视为泄露），然后 force-push 清除历史。
- **自检**：每次提交前确认 diff 中不含任何敏感值 — 不要把 `.env` 或 `*.pem` 加入暂存区。

### .gitignore 强制项

以下文件类型 **必须** 加入 `.gitignore`，不得以任何理由提交：

```
.env                      # 环境变量（真实值）
*.pem                     # 私钥文件
*.key                     # 密钥文件
*.p12 / *.pfx             # 证书文件
credentials*.json         # 云服务凭证
secrets*.yaml             # 密钥配置
```

<!-- SPECKIT START -->
## Spec Kit 计划上下文

**当前特性**: Excel 数据分析报告自动生成
**分支**: `001-excel-analysis-report`
**规范文件**: `specs/001-excel-analysis-report/spec.md`
**实现计划**: `specs/001-excel-analysis-report/plan.md`
**数据模型**: `specs/001-excel-analysis-report/data-model.md`
**API 契约**: `specs/001-excel-analysis-report/contracts/api-spec.md`
**快速启动**: `specs/001-excel-analysis-report/quickstart.md`

**技术栈**:
- 后端: ASP.NET Core 10 Minimal API (C#), EF Core 10 + SQLite, JWT Bearer
- 前端: Ant Design 5.x CDN + 原生 JavaScript, Chart.js
- AI: DeepSeek V4 API (聊天需求澄清)
- 测试: xUnit.net + Moq, Playwright (E2E)

**宪法版本**: v1.1.0
<!-- SPECKIT END -->
