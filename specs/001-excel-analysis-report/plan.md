# Implementation Plan: Excel 数据分析报告自动生成

**Branch**: `001-excel-analysis-report` | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-excel-analysis-report/spec.md`

## Summary

构建一个 Web 应用，用户上传 Excel/CSV 文件后，通过 AI 聊天对话确认分析需求（或选择已有分析模版），系统自动生成包含数据概览、描述性统计、图表可视化和交叉分析的结构化 PDF 报告。支持全部分别分析、模版保存与复用、历史报告管理、下载和打印。多文件时必须选择已有模版。

**技术路线**：ASP.NET Core 10 Minimal API（后端）+ Ant Design 静态页面（前端）+ SQLite（数据存储）+ DeepSeek V4（AI 聊天需求澄清）。

## Technical Context

**Language/Version**: C# 14 (.NET 10), JavaScript (ES2024), HTML5/CSS3

**Primary Dependencies**:
- Backend: ASP.NET Core 10, Entity Framework Core 10 SQLite, ClosedXML (.xlsx 读写), CsvHelper (CSV 解析 + GBK编码, 依赖 System.Text.Encoding.CodePages), QuestPDF (PDF 报告生成), Microsoft.AspNetCore.Authentication.JwtBearer (JWT 认证), FluentValidation (输入验证)
- Frontend: Ant Design 5.x (CDN 引入), Chart.js (图表渲染), PDF.js (PDF 预览)
- AI: DeepSeek V4 API（HTTP 调用，chat completion 接口）

**Storage**: SQLite（单文件数据库 `app.db`，EF Core Code-First + Migration）

**Testing**: xUnit.net + Moq（后端单元/集成测试）, Playwright（前端 E2E 测试）

**Target Platform**: Windows Server / Linux（.NET 跨平台）, 浏览器 Chrome/Edge/Firefox（桌面 1920×1080 + 平板 768×1024）

**Project Type**: Web 应用（SPA 前端 + RESTful API 后端 + SSE 实时推送）

**Performance Goals**:
- 上传 ≤20MB: 3s 返回 200 + 5s 同步校验；20~100MB: 3s 返回 200 + 异步解析 + SSE 进度
- 报告生成 ≤30s（≤10,000 行）; ≤3min（>10,000 行）+ SSE 进度每 2s
- 任务回执 ≤1s
- 下载支持 Range Requests（ASP.NET Core 原生支持）, 连接 ≤60s

**Constraints**:
- 单文件 ≤100MB，总文件 ≤500MB（建议性），单次 ≤20 个
- 合并后 ≤100 万行 / ≤200 列
- 并发分析任务 ≤100（SemaphoreSlim）
- 报告及原始数据 7 天过期自动清理（BackgroundService）

**Scale/Scope**: 100 并发任务，SQLite 单库支撑（WAL 模式 + 合理索引），单用户不限模版数

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| 宪法原则 | 状态 | 实现策略 |
|----------|------|----------|
| 一、安全第一 | ✅ PASS | JWT Bearer 认证 + `[Authorize]` 全局策略 + FluentValidation 输入验证 + 凭据仅存环境变量 + SQLite 文件系统权限保护 |
| 二、测试优先 | ✅ PASS | TDD 流程：xUnit 测试先行 → 红灯 → 绿灯 → 重构。目标单元测试覆盖率 ≥80% |
| 三、代码简洁 | ✅ PASS | C# 遵循 .NET 命名规范，中文 Git 提交消息。Minimal API 避免过度抽象，Service 层直映业务 |
| 四、质量关卡 | ✅ PASS | 6 项纳入 PR checklist：测试先行/全部通过/Code Review/DRY/可读性/边界覆盖 |
| 性能 §1 上传 | ✅ PASS | ≤20MB 同步校验 5s；20~100MB 异步 + SSE 进度通知 |
| 性能 §2 回执 | ✅ PASS | 确认后 1s 内返回 Task ID（GUID v7） |
| 性能 §3 常规 | ✅ PASS | ≤10,000 行 30s：ClosedXML 流式读 + LINQ 内存计算 + QuestPDF 模板渲染 |
| 性能 §4 大型 | ✅ PASS | >10,000 行 3min：分批处理 + SSE 进度通知 ≤2s |
| 性能 §5 下载 | ✅ PASS | ASP.NET Core 原生 Range Requests，超时 60s 断开 |

## Project Structure

### Documentation (this feature)

```text
specs/001-excel-analysis-report/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 API 契约
│   └── api-spec.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
backend/
├── AIExport.Api/
│   ├── Program.cs                   # 入口 + 中间件管道
│   ├── appsettings.json             # 通用配置（不含敏感值）
│   ├── Endpoints/                   # Minimal API 端点
│   │   ├── AuthEndpoints.cs         # POST /api/auth/login
│   │   ├── FileEndpoints.cs         # POST /api/files/upload, DELETE /api/files/{id}
│   │   ├── ChatEndpoints.cs         # POST /api/chat/message, GET /api/chat/stream
│   │   ├── ReportEndpoints.cs       # POST /api/reports/generate, GET /api/reports/{id}
│   │   ├── TemplateEndpoints.cs     # GET/POST/DELETE /api/templates
│   │   └── HistoryEndpoints.cs      # GET /api/reports, DELETE /api/reports/{id}
│   ├── Services/                    # 业务逻辑
│   │   ├── AuthService.cs           # 登录验证 + JWT 签发
│   │   ├── FileService.cs           # 文件存储 + 格式校验 + 解析
│   │   ├── ChatService.cs           # 对话管理 + DeepSeek LLM 调用
│   │   ├── ReportService.cs         # 数据分析 + PDF 报告生成
│   │   ├── TemplateService.cs       # 模版 CRUD
│   │   └── StorageService.cs        # 文件系统 + 7天过期清理
│   ├── Models/
│   │   ├── Entities/                # EF Core 实体（User/UploadedFile/...等8个）
│   │   └── Dtos/                    # 请求/响应 DTO
│   ├── Data/
│   │   ├── AppDbContext.cs          # DbContext + Fluent API 配置
│   │   └── Migrations/              # EF Core 迁移脚本
│   └── Infrastructure/
│       ├── LlmClient.cs             # DeepSeek API HTTP Client
│       ├── FileParser.cs            # Excel (ClosedXML) + CSV (CsvHelper)
│       ├── PdfGenerator.cs          # QuestPDF 报告模板
│       └── JobQueue.cs              # Channel<T> 后台任务队列 + Progress Hub

frontend/
├── index.html                       # SPA 入口（路由切换页面）
├── pages/
│   ├── login.html                   # 登录页面
│   ├── main.html                    # 主页（上传 + 模版选择 + 聊天 + 报告预览）
│   └── history.html                 # 历史报告管理页
├── js/
│   ├── api.js                       # fetch 封装 + JWT 注入
│   ├── auth.js                      # 登录/登出/Token 管理
│   ├── upload.js                    # 多文件上传 + 进度条 + 文件列表管理
│   ├── chat.js                      # SSE 聊天消息流 + 消息渲染
│   ├── report.js                    # 报告预览 + 下载 + 打印
│   ├── template.js                  # 模版选择 + 管理
│   └── utils.js                     # 通用工具（日期格式化、防抖等）
├── css/
│   └── app.css                      # 全局样式 + 响应式（1920/768 断点）
└── assets/

tests/
├── AIExport.Api.Tests/
│   ├── Unit/                        # Service 层单元测试（Moq）
│   ├── Integration/                 # WebApplicationFactory 集成测试
│   └── Contract/                    # API 契约测试（验证端点输入/输出 Schema）
└── AIExport.E2E.Tests/
    └── specs/                       # Playwright 端到端测试脚本
```

**Structure Decision**: Web 应用结构（Option 2）。前端纯静态文件（Ant Design CDN + 原生 JS 模块），后端 ASP.NET Core 10 Minimal API。前后端通过 RESTful JSON + SSE（Server-Sent Events）通信。SQLite 数据库文件位于 `backend/AIExport.Api/Data/app.db`。

## Complexity Tracking

> 无宪法违规，无需额外合理性说明。

| 检查项 | 状态 |
|--------|------|
| 项目数量 | 符合预期：backend (1) + frontend (1) + tests (2) |
| 外部依赖 | DeepSeek API 仅用于聊天，含降级策略（服务不可用时回退规则引擎） |
| 抽象层级 | Minimal API 直连 Service 层，无 Repository 过度抽象 |
