# Research Report: Excel 数据分析报告自动生成

**Date**: 2026-07-10 | **Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## 1. 技术栈选型

### 1.1 后端框架：ASP.NET Core 10 Minimal API

- **Decision**: ASP.NET Core 10 Minimal API（非 Controller 模式）
- **Rationale**: .NET 10 是用户指定技术栈。Minimal API 减少样板代码，适合本项目中等规模 API 场景（~20 个端点）。相比传统 Controller 模式减少约 40% 代码量，与宪法"代码简洁"原则一致。
- **Alternatives considered**:
  - ASP.NET Core Controller：全功能但代码量更大，适合大型项目
  - FastAPI (Python)：不符合用户 .NET 要求

### 1.2 ORM：Entity Framework Core 10 + SQLite

- **Decision**: EF Core 10 + Microsoft.EntityFrameworkCore.Sqlite
- **Rationale**: SQLite 是用户指定数据库。EF Core 是 .NET 生态标准 ORM，Code-First Migration 支持良好。SQLite WAL 模式支持并发读，满足 100 并发分析任务需求。
- **Alternatives considered**:
  - Dapper：更轻量但缺少 Migration 和变更追踪，SQLite 场景优势不明显
  - Microsoft.Data.Sqlite 裸 ADO.NET：代码量大，不符合简洁原则

### 1.3 Excel 解析：ClosedXML

- **Decision**: ClosedXML（开源 MIT 协议）
- **Rationale**: 支持 .xlsx/.xls 读写，API 简洁（`workbook.Worksheet(1).Rows()`），支持合并单元格展开、编码检测。流式读取避免大文件 OOM。
- **Alternatives considered**:
  - EPPlus：功能更强但商业版需授权（LGPL 限制）
  - NPOI：Java POI 移植，API 风格不够 .NET 化

### 1.4 CSV 解析：CsvHelper

- **Decision**: CsvHelper（.NET 生态 CSV 标准库）
- **Rationale**: 高性能流式解析，自动编码检测（UTF-8/GBK），灵活映射配置。社区活跃（NuGet 下载量 1 亿+）。
- **Alternatives considered**: 手动 `TextFieldParser` 或 `StreamReader.Split(',')`（无法处理引号转义和编码检测）

### 1.5 PDF 生成：QuestPDF

- **Decision**: QuestPDF（开源 MIT 协议）
- **Rationale**: 纯 C# Fluent API 生成 PDF，支持表格/图片/A4 排版，商业友好 MIT 协议。无需外部依赖（如 wkhtmltopdf）。
- **Alternatives considered**:
  - iTextSharp/iText7：AGPL 协议限制商用
  - Puppeteer/Playwright + HTML：额外 Node.js 依赖，部署复杂

### 1.6 认证：JWT Bearer

- **Decision**: ASP.NET Core JWT Bearer Authentication + BCrypt 密码哈希
- **Rationale**: 无状态认证，适合 SPA 前后端分离架构。Token 存储在浏览器 localStorage，每次请求通过 `Authorization: Bearer` 头传递。BCrypt 是密码哈希工业标准。
- **Alternatives considered**:
  - ASP.NET Core Identity：全功能但设计为 Cookie 认证 + Razor Pages，对 SPA 适配成本高
  - Session-based：需要服务端状态存储，与 SQLite 轻量设计不符

### 1.7 AI 聊天：DeepSeek V4 API

- **Decision**: 通过 `HttpClient` 调用 DeepSeek V4 `/chat/completions` 接口
- **Rationale**: 用户指定模型。1M token 上下文窗口足以处理中等规模数据分析需求的自然语言理解。使用 System Prompt 约束输出格式为结构化 JSON（分析维度/指标/图表类型），便于后续自动化报告生成。
- **Fallback**: DeepSeek API 不可用时自动降级为规则引擎模式（基于列类型预设分析选项菜单）
- **Alternatives considered**: Claude API / GPT API（用户未指定）

### 1.8 实时推送：Server-Sent Events (SSE)

- **Decision**: ASP.NET Core SSE（`text/event-stream` 响应）
- **Rationale**: 单向服务端→客户端推送报告生成进度，比 WebSocket 更轻量（HTTP 协议原生支持），比轮询更高效。前端使用 `EventSource` API 原生接收。
- **Alternatives considered**:
  - WebSocket (SignalR)：双向通信但本项目仅需服务端推送，过度设计
  - 短轮询：简单但浪费带宽，宪法要求 ≤2s 更新间隔

### 1.9 前端：Ant Design + 原生 JavaScript

- **Decision**: Ant Design 5.x CDN + 原生 JavaScript（ES Modules）
- **Rationale**: 用户指定 UI 框架。CDN 引入避免 Node.js 构建工具链（npm/webpack），与"简洁"原则一致。原生 JS 模块化管理代码，无需 React/Vue 框架学习成本。Chart.js 用于报告图表渲染，PDF.js 用于 PDF 预览。
- **Alternatives considered**:
  - React + Ant Design：需要完整 Node.js 构建链，增加项目复杂度
  - Bootstrap：未指定，Ant Design 组件更丰富（Table/Upload/Progress/Chat 组件开箱即用）

### 1.10 测试框架

- **Decision**: xUnit.net + Moq（后端）, Playwright（E2E）
- **Rationale**: xUnit 是 .NET 标准测试框架。Moq 提供强类型 Mock。WebApplicationFactory 支持集成测试（内存 SQLite + 真实 HTTP 管道）。Playwright 用于前端 E2E 验证关键用户流程。
- **Alternatives considered**: NUnit/MSTest（社区习惯差异，xUnit 在 .NET 社区更主流）

## 2. 关键技术决策

### 2.1 后台任务处理

- **Decision**: `System.Threading.Channels` + `BackgroundService`
- **Rationale**: 报告生成是 CPU 密集型长任务，需要异步处理。Channel<T> 提供有界队列（BoundedChannel），限制并发 ≤100。BackgroundService 作为长期运行的消费者。
- **Alternatives considered**: Hangfire/Quartz（需要额外持久化存储，SQLite 场景过重）

### 2.2 文件存储

- **Decision**: 本地文件系统（`App_Data/uploads/` + `App_Data/reports/`）
- **Rationale**: SQLite + 文件系统是最简部署方案，无需配置外部存储。按用户 ID 分子目录，文件名使用 GUID 防冲突。定期清理任务（BackgroundService）扫描并删除过期文件。
- **Alternatives considered**: Azure Blob / S3（云依赖，不符 SQLite 嵌入式设计理念）

### 2.3 图表生成

- **Decision**: 前端 Chart.js + 后端提供图表数据（JSON）
- **Rationale**: 后端计算统计数据，序列化为 JSON 返回前端，前端使用 Chart.js 渲染 Canvas 图表。QuestPDF 生成 PDF 时嵌入图表为静态图片（服务端使用 SkiaSharp 渲染）。
- **Alternatives considered**: 纯后端生成图片（增加服务端渲染负担）；纯前端计算（大数据量时浏览器性能不足）

## 2.4 规则引擎设计（LLM 降级策略）

当 DeepSeek API 不可用或用户选择不依赖 AI 时，系统回退至规则引擎模式。规则引擎基于 Excel 列的数据类型自动推断分析建议：

### 列类型推断规则

| Excel 数据类型 | 识别方式 | 自动建议的分析方式 |
|---------------|----------|-------------------|
| 数值型（整数/小数） | ClosedXML 单元格类型 `Number`，或全部值为数字 | 均值、求和、中位数、最值、标准差、趋势图（折线图）、分布图（直方图） |
| 文本型（字符串） | 全部值为非数字字符串 | 频次分布（柱状图）、分类汇总（饼图）、Top-N 排名 |
| 日期型 | `DateTime` 类型或可解析为日期的字符串 | 时间趋势（折线图）、同比/环比、按年/月/季度汇总 |
| 混合型 | 同一列包含数字和文本 | 提示用户该列数据类型不统一，建议手动指定分析方式 |

### 分析建议生成流程

1. 读取所有文件的列头 → 对每列采样前 20 行数据
2. 应用类型推断规则 → 为每列打标签（数值/文本/日期/混合）
3. 生成默认分析建议：数值列默认计算描述性统计，文本列默认生成频次分布
4. 以结构化菜单形式呈现给用户（Ant Design Checkbox.Group + Select），让用户勾选/调整
5. 用户确认后生成 AnalysisRequirement 结构化参数

### 与 LLM 模式的切换

- LLM 模式：用户自由文本输入 → DeepSeek V4 理解意图 → 返回结构化 JSON → 追问缺失维度
- 规则引擎模式：系统展示预设分析选项菜单 → 用户勾选 → 直接生成结构化参数
- 降级触发条件：DeepSeek API 连续 3 次请求失败或响应超时 >10s

## 3. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| DeepSeek API 不可用 | 聊天功能降级 | 自动回退规则引擎模式（预设分析选项菜单） |
| SQLite 并发写入瓶颈 | 多用户同时提交任务 | WAL 模式 + 写入队列序列化 |
| 大文件（>100MB）解析 OOM | 服务端内存溢出 | 流式解析 + 文件大小硬限制 100MB |
| 100 万行数据分析超时 | 报告生成 >3min | 分批处理 + 采样聚合（大数据的统计估算） |
