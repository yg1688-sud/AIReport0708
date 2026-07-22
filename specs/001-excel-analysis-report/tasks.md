# Tasks: Excel 数据分析报告自动生成

**Input**: Design documents from `/specs/001-excel-analysis-report/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/api-spec.md](./contracts/api-spec.md)

**Tests**: 包含测试任务 — 宪法规定 TDD 强制（测试先行，覆盖率 ≥80%）。每个 User Story 先写测试，确保红灯，再实现。

**Organization**: 任务按 8 个 User Story 分组，每个 Story 可独立实现与测试。

## Format: `[ID] [P?] [Story] Description`

- **[P]**: 可并行执行（不同文件，无依赖）
- **[Story]**: 所属 User Story（US1~US8）
- 每个任务包含精确文件路径

## Path Conventions

基于 [plan.md](./plan.md) 的 Web 应用结构：

```text
backend/AIExport.Api/           # ASP.NET Core 项目
frontend/                        # 静态文件前端
tests/AIExport.Api.Tests/       # 后端测试
tests/AIExport.E2E.Tests/       # E2E 测试
```

---

## Phase 1: Setup（项目初始化）

**Purpose**: 创建项目骨架、安装依赖、配置工具链

- [ ] T001 创建 .NET solution 和项目结构：`backend/AIExport.Api/AIExport.Api.csproj`（ASP.NET Core 10 Minimal API），配置 `appsettings.json`
- [ ] T002 [P] 初始化前端目录 `frontend/index.html`、`frontend/css/app.css`、`frontend/js/`，引入 Ant Design 5.x CDN 和 Chart.js CDN
- [ ] T003 [P] 创建测试项目 `tests/AIExport.Api.Tests/AIExport.Api.Tests.csproj`（xUnit.net + Moq + WebApplicationFactory）
- [ ] T004 [P] 创建 E2E 测试项目 `tests/AIExport.E2E.Tests/`（Playwright + `playwright.config.ts`）
- [ ] T005 安装 NuGet 包：`Microsoft.EntityFrameworkCore.Sqlite`、`ClosedXML`、`CsvHelper`、`QuestPDF`、`Microsoft.AspNetCore.Authentication.JwtBearer`、`FluentValidation`、`BCrypt.Net-Next`
- [ ] T006 [P] 配置 `.gitignore`（排除 `app.db`、`appsettings.Development.json`、`*.pem`、`*.key`、`App_Data/`、`node_modules/`）
- [ ] T007 [P] 创建 `backend/AIExport.Api/appsettings.json`（SQLite 连接字符串、上传限制配置、报告保留天数）

---

## Phase 2: Foundational（基础设施 — 阻塞所有 User Story）

**Purpose**: 共享基础设施，MUST 在 User Story 实现前完成

**⚠️ CRITICAL**: 所有 User Story 依赖本阶段完成

### 数据库 & 实体

- [ ] T008 创建 `backend/AIExport.Api/Data/AppDbContext.cs` — DbContext + OnModelCreating（Fluent API 配置所有 8 个实体关系）
- [ ] T009 [P] 创建 `backend/AIExport.Api/Models/Entities/User.cs`（Id, Username, PasswordHash, Role, IsActive, CreatedAt, LastLoginAt）
- [ ] T010 [P] 创建 `backend/AIExport.Api/Models/Entities/UploadedFile.cs`（Id, BatchId, OriginalName, StoredPath, FileSize, FileFormat, RowCount, ColumnCount, ColumnHeaders, ParseStatus, ErrorMessage, UploadedAt）
- [ ] T011 [P] 创建 `backend/AIExport.Api/Models/Entities/UploadBatch.cs`（Id, UserId, TotalFiles, ReadyFiles, TotalSize, BatchStatus, Strategy, CreatedAt）
- [ ] T012 [P] 创建 `backend/AIExport.Api/Models/Entities/AnalysisSession.cs`（Id, BatchId, TemplateId, SessionStatus, Mode, CreatedAt, ConfirmedAt, ExpiresAt）
- [ ] T013 [P] 创建 `backend/AIExport.Api/Models/Entities/ChatMessage.cs`（Id, SessionId, Sender, Content, Timestamp）
- [ ] T014 [P] 创建 `backend/AIExport.Api/Models/Entities/AnalysisRequirement.cs`（Id, SessionId, Dimensions, Metrics, ChartTypes, Filters, ConfirmedAt）
- [ ] T015 [P] 创建 `backend/AIExport.Api/Models/Entities/AnalysisReport.cs`（Id, SessionId, OriginalFileName, ReportStatus, Mode, ReportType, Chapters, PdfPath, FileSize, ErrorMessage, CreatedAt, CompletedAt, ExpiresAt）
- [ ] T016 [P] 创建 `backend/AIExport.Api/Models/Entities/AnalysisTemplate.cs`（Id, UserId, Name, Dimensions, Metrics, ChartTypes, Filters, ColumnNames, Strategy, CreatedAt）
- [ ] T017 创建初始 Migration：`dotnet ef migrations add InitialCreate`，验证数据库生成成功

### 认证 & 中间件

- [ ] T018 创建 `backend/AIExport.Api/Infrastructure/JwtConfig.cs` — JWT Token 签发/验证配置（Secret 从环境变量读取，Issuer/Audience/Expiry 配置）
- [ ] T019 在 `backend/AIExport.Api/Program.cs` 中注册 JWT 认证中间件（`AddAuthentication().AddJwtBearer()`），配置全局 `[Authorize]` 策略
- [ ] T020 [P] 创建 `backend/AIExport.Api/Models/Dtos/AuthDtos.cs` — LoginRequest, LoginResponse, UserInfo
- [ ] T021 [P] 配置 FluentValidation 全局输入验证管道（`AddValidatorsFromAssemblyContaining<Program>()`）
- [ ] T022 创建 `backend/AIExport.Api/Program.cs` 基础骨架：CORS 配置（允许前端域名）、SQLite WAL 模式启用、异常处理中间件

### 基础设施服务

- [ ] T023 [P] 创建 `backend/AIExport.Api/Infrastructure/FileParser.cs` — Excel 解析（ClosedXML 流式读取，自动展开合并单元格）和 CSV 解析（CsvHelper + 编码自动检测 UTF-8/GBK），返回行列数/列头/前100行预览；涵盖 Excel 文件编码检测和列标题特殊字符清理（移除不可打印字符、统一全角/半角）
- [ ] T024 [P] 创建 `backend/AIExport.Api/Infrastructure/LlmClient.cs` — DeepSeek V4 API HttpClient（`/chat/completions`），System Prompt 约束输出结构化 JSON（维度/指标/图表），含超时和重试逻辑
- [ ] T025 [P] 创建 `backend/AIExport.Api/Infrastructure/JobQueue.cs` — `Channel<T>` 有界队列（BoundedCapacity=100）+ `BackgroundService` 消费者骨架
- [ ] T026 [P] 创建 `backend/AIExport.Api/Services/StorageService.cs` — 文件存储/读取/删除，按 UserId 分目录，GUID 命名防冲突

**Checkpoint**: 基础设施就绪 — User Story 实现可以开始

---

## Phase 3: US1 — 用户登录与身份验证 (Priority: P1) 🎯 MVP

**Goal**: 用户通过用户名密码登录，获得 JWT Token，未登录被拦截

**Independent Test**: 访问任意页面 → 重定向登录页；有效凭据登录 → 进入主页；错误凭据 → 提示错误

### Tests for US1 ⚠️

> **TDD: 先写测试 → 确认红灯 → 再实现**

- [ ] T027 [P] [US1] 登录接口单元测试：`tests/AIExport.Api.Tests/Unit/AuthServiceTests.cs` — 有效凭据/无效凭据/禁用账户/空输入
- [ ] T028 [P] [US1] 登录接口契约测试：`tests/AIExport.Api.Tests/Contract/AuthContractTests.cs` — 请求 Schema/响应 Schema/401/403 状态码

### Implementation for US1

- [ ] T029 [US1] 创建 `backend/AIExport.Api/Services/AuthService.cs` — `LoginAsync(username, password)` 验证 BCrypt 哈希，签发 JWT Token（含 UserId/Username/Role claims），返回过期时间
- [ ] T030 [US1] 创建 `backend/AIExport.Api/Endpoints/AuthEndpoints.cs` — `POST /api/auth/login`，调用 AuthService，返回 `{ token, expiresAt, user }`
- [ ] T031 [US1] 创建 `frontend/pages/login.html` — Ant Design Form 组件（用户名输入框 + 密码输入框 + 登录按钮），居中卡片布局
- [ ] T032 [US1] 创建 `frontend/js/auth.js` — `login()` 调用 `/api/auth/login`，存储 Token 到 localStorage，`logout()` 清除 Token 并跳转登录页，`checkAuth()` 检查 Token 有效性
- [ ] T033 [US1] 创建 `frontend/js/api.js` — `fetch` 封装：自动注入 `Authorization: Bearer` 头，401 响应自动跳转登录页，统一错误处理

**Checkpoint**: 登录流程端到端可用 — 用户可登录并持有有效 Token

---

## Phase 4: US2 — 多文件上传与校验 (Priority: P1) 🎯 MVP

**Goal**: 支持一次上传多个 .xlsx/.xls/.csv 文件（≤20 个，单文件 ≤100MB），列表展示进度，独立删除/清空，>5MB 显示解析动画，全部就绪提示

**Independent Test**: 上传合法文件 → 列表展示名称/大小/进度；上传非法文件 → 友好报错；全部解析完成 → "所有文件就绪"

### Tests for US2 ⚠️

- [ ] T034 [P] [US2] 文件解析单元测试：`tests/AIExport.Api.Tests/Unit/FileParserTests.cs` — Excel(.xlsx/.xls)列头/行数/空文件/合并单元格/编码；CSV UTF-8/GBK/空文件
- [ ] T035 [P] [US2] 文件服务单元测试：`tests/AIExport.Api.Tests/Unit/FileServiceTests.cs` — 上传校验（格式/大小/数量限制/空文件检测）
- [ ] T036 [P] [US2] 上传接口契约测试：`tests/AIExport.Api.Tests/Contract/FileContractTests.cs` — multipart 上传/200/400 状态码/SSE 进度事件

### Implementation for US2

- [ ] T037 [US2] 创建 `backend/AIExport.Api/Services/FileService.cs` — `ValidateAsync(files)` 校验格式/大小/列头/空文件/加密检测，返回校验结果；`ParseAsync(fileId)` 异步解析并更新行列数/列头/状态；`GetPreviewAsync(fileId)` 返回前100行数据
- [ ] T038 [US2] 创建 `backend/AIExport.Api/Endpoints/FileEndpoints.cs` — `POST /api/files/upload`（multipart 接收 + 触发解析）、`GET /api/files/progress?batchId=`（SSE 推送 file-progress/batch-ready 事件）、`DELETE /api/files/{fileId}`（移除单个）、`DELETE /api/batches/{batchId}/files`（清空所有）
- [ ] T039 [US2] 实现 SSE 进度管道：FileService 解析完成后写入 Channel → `GET /api/files/progress` 读取并推送事件
- [ ] T040 [US2] 创建 `frontend/js/upload.js` — `uploadFiles(files)` 使用 FormData 批量上传、`renderFileList(batch)` 渲染 Ant Design List 组件（文件名/大小/进度条/删除按钮）、`subscribeProgress(batchId)` 监听 SSE 事件更新进度、`handleClearAll()` 二次确认弹窗
- [ ] T041 [US2] 在 `frontend/pages/main.html` 集成文件上传区域：Ant Design Upload 组件（drag-and-drop）+ 文件列表 + "清空所有"按钮 + 整体进度提示"X/Y 个文件已就绪"

**Checkpoint**: 文件上传端到端可用 — 多文件上传 → 列表进度展示 → 全部就绪提示

---

## Phase 5: US3 — 多文件处理策略 (Priority: P2) *(简化)*

**Goal**: 统一使用"分别分析"策略，用户无需手动选择。多文件时必须选择已有模版。

**Independent Test**: 单文件 → 对话模式或模版模式均可；多文件 → 必须选模版，对话模式不可用。

### Implementation for US3 *(已完成)*

- [x] T045 [US3] `frontend/pages/main.html` — 移除策略选择 UI，统一设为 separate；多文件时 `window._forceTemplate=true`
- [x] T046 [US3] `frontend/js/template.js` — `renderTemplateSelector` 加 `forceTemplate` 参数，多文件时隐藏"不使用模版"选项
- [x] `backend/AIExport.Api/Services/FileService.cs` — 创建批次时默认 `Strategy.Separate`
- [x] `backend/AIExport.Api/Services/TemplateService.cs` — 保存模版默认 `Strategy.Separate`

**Checkpoint**: 策略已简化 — 全部分别分析，多文件强制模版模式

---

## Phase 6: US4 — 分析模版选择 (Priority: P2)

**Goal**: 已有模版的用户可选择模版直接生成（不可聊天）。单文件时可选"不使用模版"进入对话模式；多文件时必须选模版。模版列校验不通过时阻止生成。

**Independent Test**: 单文件 → 可选模版或对话；多文件 → 必须选模版，不显示"不使用模版"选项

### Tests for US4 ⚠️

- [ ] T047 [P] [US4] 模版服务单元测试：`tests/AIExport.Api.Tests/Unit/TemplateServiceTests.cs` — 保存/列表/删除/列校验/策略冲突检测/名称重复检测
- [ ] T048 [P] [US4] 模版接口契约测试：`tests/AIExport.Api.Tests/Contract/TemplateContractTests.cs` — CRUD 端点/201/409/404

### Implementation for US4

- [ ] T049 [US4] 创建 `backend/AIExport.Api/Services/TemplateService.cs` — `GetUserTemplates(userId)` 获取模版列表、`SaveTemplate(sessionId, name)` 从会话提取需求参数保存为模版、`DeleteTemplate(templateId)`、`ValidateTemplateColumns(templateId, batchId)` 校验列存在性 + 策略冲突检测
- [ ] T050 [US4] 创建 `backend/AIExport.Api/Endpoints/TemplateEndpoints.cs` — `GET /api/templates`（列表）、`POST /api/templates`（保存）、`PUT /api/templates/{id}`（覆盖）、`DELETE /api/templates/{id}`、`POST /api/templates/{id}/validate`（列校验）
- [ ] T051 [US4] 创建 `frontend/js/template.js` — `loadTemplates()` 加载模版列表、`renderTemplateSelector(templates)` 渲染 Ant Design Select 组件（含"不使用模版"选项）、`validateTemplate(templateId)` 调用列校验、`handleTemplateSelect(templateId)` 切换模版模式（隐藏聊天+显示立即生成按钮）
- [ ] T052 [US4] 在 `frontend/pages/main.html` 中集成模版选择区域：文件就绪后展示（在策略选择之后），无模版时隐藏
- [ ] T052a [P] [US4] 创建 `frontend/pages/templates.html` — 独立模版管理页面：Ant Design Table（模版名称/策略/列名/保存时间）+ 行操作（查看详情/删除确认）+ 空状态提示"暂无保存的模版"
- [ ] T052b [P] [US4] 在 `frontend/js/template.js` 中添加模版管理方法：`loadTemplateList()` 分页加载、`deleteTemplate(id)` 删除确认、`viewTemplateDetail(id)` 查看模版参数详情

**Checkpoint**: 模版选择与管理可用 — 选择已有模版 → 列校验通过 → 进入模版模式；不选模版 → 进入对话模式；模版管理页可查看/删除模版

---

## Phase 7: US5 — 聊天式需求确认 (Priority: P2)

**Goal**: 对话模式下，系统通过混合方式确认需求（规则引擎引导 + LLM 自由文本理解），支持滚动查看历史，输入"确认"或点击按钮触发报告生成

**Independent Test**: 进入聊天 → 系统发送引导消息 → 用户输入模糊需求 → 系统追问 → 输入"确认" → 1s 内返回 Task ID

### Tests for US5 ⚠️

- [ ] T053 [P] [US5] 聊天服务单元测试：`tests/AIExport.Api.Tests/Unit/ChatServiceTests.cs` — 引导消息生成/模糊需求追问/不存在列名检测/"确认"关键词识别/LLM 降级规则引擎
- [ ] T054 [P] [US5] 聊天接口契约测试：`tests/AIExport.Api.Tests/Contract/ChatContractTests.cs` — start/message/confirm 端点 Schema

### Implementation for US5

- [ ] T055 [US5] 创建 `backend/AIExport.Api/Services/ChatService.cs` — `StartSession(batchId, strategy, templateId)` 创建会话+返回引导消息、`SendMessage(sessionId, content)` 调用 LLM（或降级规则引擎）生成回复并追问缺失维度/指标/图表、`ConfirmRequirements(sessionId)` 锁定需求并提取结构化参数（AnalysisRequirement）、LLM 不可用时自动降级为规则引擎模式（基于列类型的预设选项菜单）
- [ ] T056 [US5] 创建 `backend/AIExport.Api/Endpoints/ChatEndpoints.cs` — `POST /api/chat/start`（启动会话）、`POST /api/chat/message`（发送消息）、`GET /api/chat/messages?sessionId=`（历史记录）、`POST /api/chat/confirm`（确认需求，返回 Task ID）
- [ ] T057 [US5] 创建 `frontend/js/chat.js` — `startChat(batchId, strategy)` 启动会话、`sendMessage(content)` 发送用户消息、`renderMessage(msg)` 渲染消息气泡（Ant Design Comment 组件）、`scrollToBottom()` 自动滚动、`handleConfirm()` 确认需求并跳转报告等待页
- [ ] T058 [US5] 在 `frontend/pages/main.html` 中集成聊天区域：消息列表 + 输入框 + "确认需求"按钮（模版模式下隐藏），对话模式下可见
- [ ] T058a [US5] 实现会话超时处理：后端 `AnalysisSession.ExpiresAt` 到期后拒绝新消息并返回 410 Gone；前端 `frontend/js/chat.js` 监听 410 状态码，显示 Ant Design notification 提示"会话已超时，请重新上传文件开始"，并提供重新上传入口

**Checkpoint**: 聊天需求确认可用 — 引导追问 → 确认需求 → 1s 内 Task ID → 跳转等待页；30分钟超时自动终止

---

## Phase 8: US6 — 自动生成结构化数据分析报告 (Priority: P2)

**Goal**: 需求确认后自动分析数据并生成 PDF 报告（含数据概览/描述性统计/图表/交叉分析）；常规 ≤30s，大型 ≤3min + SSE 进度；报告完成后提示保存模版

**Independent Test**: 确认需求 → 进度条展示阶段 → 30s/3min 内 → 完整报告展示 → 弹出模版保存提示

### Tests for US6 ⚠️

- [ ] T059 [P] [US6] 报告服务单元测试：`tests/AIExport.Api.Tests/Unit/ReportServiceTests.cs` — 描述性统计计算（均值/中位数/最值/标准差）/交叉分析/频次分布/PDF 生成章节验证/常规数据30s时限/大型数据进度事件
- [ ] T060 [P] [US6] 报告接口契约测试：`tests/AIExport.Api.Tests/Contract/ReportContractTests.cs` — progress SSE 事件/200/404

### Implementation for US6

- [ ] T061 [US6] 创建 `backend/AIExport.Api/Infrastructure/PdfGenerator.cs` — QuestPDF 报告模板：A4 页面 → 封面（标题+日期）→ 数据概览章节（表格：行数/列数/缺失率）→ 描述性统计章节 → 图表章节（嵌入 SkiaSharp 渲染的图表图片）→ 交叉分析章节
- [ ] T062 [US6] 创建 `backend/AIExport.Api/Services/ReportService.cs` — `GenerateAsync(taskId)` 从 JobQueue 消费任务：①清洗数据（去空行/类型推断）②计算描述性统计 ③生成图表数据（JSON 格式，供前端 Chart.js 渲染） ④计算交叉表 ⑤调用 PdfGenerator 生成 PDF ⑥每个阶段推送 SSE 进度事件；`GetReportAsync(reportId)` 返回章节内容 JSON
- [ ] T063 [US6] 创建 `backend/AIExport.Api/Endpoints/ReportEndpoints.cs` — `GET /api/reports/progress?taskId=`（SSE 推送 progress/complete 事件）、`GET /api/reports/{id}`（报告详情+章节 JSON）
- [ ] T064 [US6] 在 `backend/AIExport.Api/Infrastructure/JobQueue.cs` 中实现完整的 `BackgroundService` 消费者：从 Channel 读取任务 → 调用 ReportService.GenerateAsync → 更新报告状态 → 完成后推送 SSE complete 事件。使用 `SemaphoreSlim(100)` 控制并发
- [ ] T065 [US6] 创建 `frontend/js/report.js` — `subscribeReportProgress(taskId)` 监听 SSE 更新进度条和阶段文字、`renderReport(report)` 渲染报告页面（Chart.js 渲染图表 + 数据表格）、`promptSaveTemplate()` 报告完成后弹出模版保存弹窗（Ant Design Modal + Input）
- [ ] T066 [US6] 在 `frontend/pages/main.html` 中集成报告展示区域：进度条 + 阶段文字 + 报告内容区（Ant Design Tabs 切换章节） + 模版保存弹窗

**Checkpoint**: 报告生成端到端可用 — 确认需求 → 进度展示 → 完整报告 → 模版保存提示

---

## Phase 9: US7 — 报告下载与打印 (Priority: P3)

**Goal**: 报告可下载为 PDF（支持断点续传 Range Requests），可浏览器打印（A4 排版）

**Independent Test**: 点击下载 → PDF 文件成功下载；网络中断恢复 → 断点续传；点击打印 → 浏览器打印对话框

### Tests for US7 ⚠️

- [ ] T067 [P] [US7] 下载接口契约测试：`tests/AIExport.Api.Tests/Contract/DownloadContractTests.cs` — Range Requests/206 Partial Content/Content-Length/Accept-Ranges/60s 超时

### Implementation for US7

- [ ] T068 [US7] 在 `backend/AIExport.Api/Endpoints/ReportEndpoints.cs` 中添加下载端点：`GET /api/reports/{id}/download` → 返回 `PhysicalFile` + `EnableRangeProcessing = true` + `Content-Disposition: attachment`
- [ ] T069 [US7] 在 `frontend/js/report.js` 中添加 `downloadReport(reportId)` — 触发文件下载（支持断点续传，浏览器原生处理 Range Requests）
- [ ] T070 [US7] 在 `frontend/js/report.js` 中添加 `printReport(reportId)` — 打开 PDF 文件新窗口，调用 `window.print()`，A4 排版 CSS `@media print`
- [ ] T071 [US7] 在 `frontend/pages/main.html` 中添加下载和打印按钮（Ant Design Button.Group），报告生成完成后显示

**Checkpoint**: 下载和打印可用 — PDF 下载 + 断点续传 + 浏览器打印

---

## Phase 10: US8 — 历史报告管理 (Priority: P3)

**Goal**: 历史报告页面：列表展示（按时间倒序）+ 关键词搜索 + 日期筛选 + 查看详情 + 删除 + 批量下载（ZIP）

**Independent Test**: 进入历史页 → 列表展示 → 搜索/筛选正确 → 查看详情 → 批量下载 ZIP → 删除

### Tests for US8 ⚠️

- [ ] T072 [P] [US8] 历史服务单元测试：`tests/AIExport.Api.Tests/Unit/HistoryServiceTests.cs` — 列表分页/搜索过滤/日期范围/批量下载 ZIP/7天过期清理
- [ ] T073 [P] [US8] 历史接口契约测试：`tests/AIExport.Api.Tests/Contract/HistoryContractTests.cs` — GET 列表/删除/批量下载

### Implementation for US8

- [ ] T074 [US8] 在 `backend/AIExport.Api/Services/ReportService.cs` 中添加历史管理方法：`GetHistoryAsync(userId, page, pageSize, keyword, dateFrom, dateTo)` 分页查询+搜索+筛选、`DeleteReportAsync(reportId)` 删除报告+PDF+原始文件、`BatchDownloadAsync(reportIds)` 打包 ZIP 流
- [ ] T075 [US8] 创建 `backend/AIExport.Api/Endpoints/HistoryEndpoints.cs` — `GET /api/reports?page=&pageSize=&keyword=&dateFrom=&dateTo=`（分页查询）、`DELETE /api/reports/{id}`（删除）、`POST /api/reports/batch-download`（批量下载 ZIP）
- [ ] T076 [US8] 在 `backend/AIExport.Api/Program.cs` 中注册定时清理 BackgroundService：每 1 小时扫描 `ExpiresAt < DateTime.UtcNow` 的报告，删除 PDF 和原始文件
- [ ] T077 [US8] 创建 `frontend/pages/history.html` — Ant Design Table 组件（报告列表 + 分页 + 关键词搜索框 + 日期范围选择器 DatePicker.RangePicker）+ 行操作（查看/删除）+ 批量勾选 + "批量下载"按钮
- [ ] T078 [US8] 创建 `frontend/js/report.js` 中的历史管理方法：`loadHistory(params)` 分页加载、`searchHistory(keyword)` 搜索过滤、`deleteReport(id)` 删除确认、`batchDownload(ids)` 批量下载

**Checkpoint**: 历史管理可用 — 列表/搜索/筛选/删除/批量下载/7天自动清理

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: 跨 Story 的优化与完善

### 前端

- [ ] T079 [P] 全局错误边界：在 `frontend/js/utils.js` 中实现 `window.onerror` 全局错误捕获，显示 Ant Design Result 组件（"页面遇到了意外错误" + 重新上传入口）
- [ ] T080 [P] 响应式布局验证：`frontend/css/app.css` 添加 `@media (max-width: 768px)` 和 `@media (min-width: 1920px)` 断点，确保平板和桌面端无布局错乱/横向滚动条。平板端（768×1024）特别注意：导航简化（水平菜单改为折叠汉堡菜单）、文件列表项增大触控区域（≥44px）、聊天输入框固定在底部全宽、报告章节 Tab 改为纵向手风琴折叠面板
- [ ] T081 [P] 创建 `frontend/index.html` SPA 路由入口：使用 `window.location.hash` 切换 login/main/history 页面
- [ ] T082 [P] 所有错误信息位置统一：文件项旁错误用 Ant Design Alert inline，全局错误用 Result + "重试"/"重新上传"按钮

### 后端

- [ ] T083 [P] 创建 `backend/AIExport.Api/Program.cs` 管理员初始化脚本：应用启动时检查 `Users` 表是否有 admin 用户，无则从环境变量 `ADMIN_PASSWORD` 创建（BCrypt 哈希）
- [ ] T083a [P] 创建 `backend/AIExport.Api/Endpoints/AdminEndpoints.cs` — `GET /api/admin/users`（用户列表）、`POST /api/admin/users`（创建用户）、`PUT /api/admin/users/{id}/disable`（禁用/启用）、`DELETE /api/admin/users/{id}`（删除用户），全部要求 `[Authorize(Roles="Admin")]`
- [ ] T083b [P] 创建 `frontend/pages/admin.html` — 管理员用户管理页面：Ant Design Table（用户列表 + 角色/状态列）+ "创建用户"按钮（Modal 表单：用户名/密码/角色）+ 行操作（禁用/启用/删除确认）
- [ ] T083c [P] 创建 `frontend/js/admin.js` — `loadUsers()` 加载用户列表、`createUser(data)` 创建用户、`toggleUserStatus(id)` 禁用/启用、`deleteUser(id)` 删除用户
- [ ] T084 [P] 添加 API 限流中间件：`backend/AIExport.Api/Infrastructure/RateLimitMiddleware.cs` — 文件上传接口每用户每分钟最多 5 次
- [ ] T085 全局异常处理中间件：捕获未处理异常，返回统一错误格式 `{ error: { code, message } }`

### 测试 & 文档

- [ ] T086 [P] E2E 测试场景1（对话模式）：`tests/AIExport.E2E.Tests/specs/chat-mode.spec.ts` — 登录 → 上传 → 聊天确认 → 生成报告 → 保存模版
- [ ] T087 [P] E2E 测试场景2（模版模式）：`tests/AIExport.E2E.Tests/specs/template-mode.spec.ts` — 登录 → 上传 → 选模版 → 立即生成 → 下载
- [ ] T088 [P] E2E 测试场景3（错误处理）：`tests/AIExport.E2E.Tests/specs/error-handling.spec.ts` — 空文件/加密文件/超100MB/列不匹配
- [ ] T089 实现 `README.md` 项目级文档（基于 quickstart.md），含项目简介/快速启动/技术栈/贡献指南
- [ ] T090 运行全部测试套件验证：`dotnet test`（后端）+ `npx playwright test`（E2E），确保覆盖率 ≥80%

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup)
    │
    ▼
Phase 2 (Foundational) ── BLOCKS ALL USER STORIES ──┐
    │                                                 │
    ├── Phase 3 (US1: Auth P1)                        │
    ├── Phase 4 (US2: Upload P1)                      │
    ├── Phase 5 (US3: Strategy P2)                    │
    ├── Phase 6 (US4: Template P2)                    │
    ├── Phase 7 (US5: Chat P2)                        │
    ├── Phase 8 (US6: Report Gen P2)                  │
    ├── Phase 9 (US7: Download P3)                    │
    └── Phase 10 (US8: History P3)                    │
                                                      │
    Phase 11 (Polish) ────────────────────────────────┘
```

### User Story Dependencies

| Story | 依赖 | 可独立于 |
|-------|------|----------|
| US1 (Auth) | Foundational | US2~US8 |
| US2 (Upload) | Foundational, US1 (需登录) | US3~US8 |
| US3 (Strategy) | US2 (文件就绪) | US4~US8（通过 mock 数据） |
| US4 (Template) | US2, US3 (策略已选) | US5~US8（通过 mock 模版数据） |
| US5 (Chat) | US2, US3, US4 (模版选择结果) | US6~US8 |
| US6 (Report) | US2~US5 (需求已确认) | US7, US8 |
| US7 (Download) | US6 (报告已生成) | US8 |
| US8 (History) | US6 (有历史报告数据) | US7 |

### Within Each User Story

```
Tests (TDD: 先写 → 红灯) → Models/Entities → Services → Endpoints → Frontend UI → Integration
```

---

## Parallel Opportunities

### Phase 2: Foundational（并行性最高）

```bash
# 所有实体可并行创建：
Task: "T009 创建 User.cs"
Task: "T010 创建 UploadedFile.cs"
Task: "T011 创建 UploadBatch.cs"
Task: "T012 创建 AnalysisSession.cs"
Task: "T013 创建 ChatMessage.cs"
Task: "T014 创建 AnalysisRequirement.cs"
Task: "T015 创建 AnalysisReport.cs"
Task: "T016 创建 AnalysisTemplate.cs"

# 基础设施服务可并行：
Task: "T023 创建 FileParser.cs"
Task: "T024 创建 LlmClient.cs"
Task: "T025 创建 JobQueue.cs"
Task: "T026 创建 StorageService.cs"
```

### Phase 3-10: 每个 Story 内

```bash
# Story 内测试可并行（不同测试文件）：
Task: "T027 [US1] AuthServiceTests.cs"
Task: "T028 [US1] AuthContractTests.cs"

# 跨 Story 独立并行（前提：Foundational 完成）：
# US1（Auth）和 US2（Upload）后端可并行开发
# US7（Download）和 US8（History）可并行开发（仅依赖 US6 报告数据）
```

### Phase 11: Polish

```bash
# 所有 Polish 任务可并行：
Task: "T079 全局错误边界"
Task: "T080 响应式布局"
Task: "T081 SPA 路由"
Task: "T083 管理员初始化"
Task: "T086-T088 E2E 测试"
```

---

## Implementation Strategy

### MVP First（最小可行产品：US1+US2+US5+US6）

1. Phase 1: Setup
2. Phase 2: Foundational（CRITICAL）
3. Phase 3: US1 登录 → **可交付：用户可登录**
4. Phase 4: US2 上传 → **可交付：用户可上传 Excel 查看数据预览**
5. Phase 7: US5 聊天 → **可交付：用户可与系统对话确认需求**
6. Phase 8: US6 报告 → **可交付：MVP 完成！端到端生成报告**
7. **STOP AND VALIDATE**: 测试完整对话模式流程

### Incremental Delivery（增量交付）

| 里程碑 | 新增 Phase | 交付价值 |
|--------|-----------|----------|
| M1: 基础框架 | Setup + Foundational | 项目骨架可编译运行 |
| M2: 认证可用 | US1 | 用户可登录 |
| M3: 数据入口 | US2 | 用户可上传文件 |
| M4: 多文件支持 | US3 | 合并/分别分析策略 |
| M5: 模版复用 | US4 | 模版保存/选择/直接生成 |
| M6: 智能对话 | US5 | AI 聊天确认需求 |
| M7: **MVP** 🎯 | US6 | 端到端报告生成 |
| M8: 报告消费 | US7 | 下载+打印 |
| M9: 生命周期 | US8 | 历史管理+自动清理 |
| M10: 生产就绪 | Polish | 错误处理+响应式+E2E |

---

## Notes

- [P] 标记的任务可并行执行（不同文件，无依赖）
- [USx] 标签将任务映射到具体 User Story，便于追踪
- TDD 严格执行：先写测试 → 红灯 → 实现 → 绿灯 → 重构
- 每个 Checkpoint 是独立可验证的交付节点
- Git 提交消息 MUST 使用中文
