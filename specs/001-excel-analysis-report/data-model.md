# Data Model: Excel 数据分析报告自动生成

**Date**: 2026-07-10 | **Feature**: [spec.md](./spec.md)

## Entity Relationship Diagram

```
User ──< UploadBatch ──< UploadedFile
 │              │
 │              └──< AnalysisSession ──< ChatMessage
 │              │         │
 │              │         └──< AnalysisRequirement
 │              │                    │
 │              └────< AnalysisReport ──< (generates) PDF file
 │
 └──< AnalysisTemplate
```

## Entities

### User（用户）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK, 自动生成 | 用户唯一标识 |
| Username | string(50) | Required, Unique | 登录用户名 |
| PasswordHash | string(200) | Required | BCrypt 哈希后的密码 |
| Role | enum | Required | 0=普通用户, 1=管理员 |
| IsActive | bool | Required, Default=true | 账户启用状态 |
| CreatedAt | DateTime | Required | 创建时间 |
| LastLoginAt | DateTime | Nullable | 最近登录时间 |

**验证规则**: Username 长度 3-50，仅允许字母/数字/下划线。PasswordHash 不可为空。

### UploadedFile（上传文件）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 文件唯一标识 |
| BatchId | Guid | FK → UploadBatch | 所属上传批次 |
| OriginalName | string(255) | Required | 原始文件名 |
| StoredPath | string(500) | Required | 服务端存储路径 |
| FileSize | long | Required | 文件大小（字节） |
| FileFormat | enum | Required | 0=xlsx, 1=xls, 2=csv |
| RowCount | int | Nullable | 数据行数（解析后） |
| ColumnCount | int | Nullable | 数据列数（解析后） |
| ColumnHeaders | string(JSON) | Nullable | 列头列表（JSON 数组） |
| ParseStatus | enum | Required | 0=上传中, 1=解析中, 2=已就绪, 3=解析失败 |
| ErrorMessage | string(500) | Nullable | 解析失败时的错误描述 |
| UploadedAt | DateTime | Required | 上传时间 |

**状态转换**: 上传中 → 解析中 → 已就绪 / 解析失败

### UploadBatch（上传批次）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 批次唯一标识 |
| UserId | Guid | FK → User | 所属用户 |
| TotalFiles | int | Required | 文件总数 |
| ReadyFiles | int | Required, Default=0 | 已就绪文件数 |
| TotalSize | long | Required | 总大小（字节） |
| BatchStatus | enum | Required | 0=上传中, 1=部分就绪, 2=全部就绪 |
| Strategy | enum | Nullable | 0=合并分析, 1=分别分析, null=未选择 |
| CreatedAt | DateTime | Required | 创建时间 |

**状态转换**: 上传中 → 部分就绪 → 全部就绪

### AnalysisSession（分析会话）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 会话唯一标识 |
| BatchId | Guid | FK → UploadBatch | 关联上传批次 |
| TemplateId | Guid | FK → AnalysisTemplate (Nullable) | 使用的模版（模版模式时有值） |
| SessionStatus | enum | Required | 0=对话中, 1=已确认, 2=已过期, 3=已超时 |
| Mode | enum | Required | 0=对话模式, 1=模版模式 |
| CreatedAt | DateTime | Required | 创建时间 |
| ConfirmedAt | DateTime | Nullable | 需求确认时间 |
| ExpiresAt | DateTime | Required | 会话过期时间（创建+30分钟） |

### ChatMessage（对话消息）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 消息唯一标识 |
| SessionId | Guid | FK → AnalysisSession | 关联会话 |
| Sender | enum | Required | 0=用户, 1=系统 |
| Content | string(4000) | Required | 消息内容 |
| Timestamp | DateTime | Required | 消息时间戳 |

### AnalysisRequirement（分析需求）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 需求唯一标识 |
| SessionId | Guid | FK → AnalysisSession, Unique | 关联会话（一对一） |
| Dimensions | string(JSON) | Required | 分析维度列表（JSON 数组） |
| Metrics | string(JSON) | Required | 分析指标列表 |
| ChartTypes | string(JSON) | Required | 图表类型列表 |
| Filters | string(JSON) | Nullable | 筛选条件（JSON） |
| ConfirmedAt | DateTime | Required | 确认时间 |

### AnalysisReport（分析报告）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 报告唯一标识 |
| SessionId | Guid | FK → AnalysisSession, Unique | 关联会话（一对一） |
| OriginalFileName | string(500) | Required | 原始上传文件名（用于历史列表展示） |
| ReportStatus | enum | Required | 0=生成中, 1=已完成, 2=失败 |
| Mode | enum | Required | 0=对话模式, 1=模版模式 |
| ReportType | enum | Required | 0=合并报告, 1=单文件报告 |
| Chapters | string(JSON) | Nullable | 报告章节内容（JSON） |
| PdfPath | string(500) | Nullable | PDF 文件存储路径 |
| FileSize | long | Nullable | PDF 文件大小 |
| ErrorMessage | string(500) | Nullable | 生成失败时的错误描述 |
| CreatedAt | DateTime | Required | 创建时间 |
| CompletedAt | DateTime | Nullable | 完成时间 |
| ExpiresAt | DateTime | Required | 过期时间（创建+7天） |

**状态转换**: 生成中 → 已完成 / 失败

### AnalysisTemplate（分析模版）

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | Guid | PK | 模版唯一标识 |
| UserId | Guid | FK → User | 所属用户 |
| Name | string(100) | Required | 模版名称 |
| Dimensions | string(JSON) | Required | 分析维度列表 |
| Metrics | string(JSON) | Required | 分析指标列表 |
| ChartTypes | string(JSON) | Required | 图表类型列表 |
| Filters | string(JSON) | Nullable | 筛选条件 |
| ColumnNames | string(JSON) | Required | 关联列名列表（用于校验） |
| Strategy | enum | Required | 0=合并分析, 1=分别分析 |
| CreatedAt | DateTime | Required | 保存时间 |

**验证规则**: Name 同一用户下不可重复（覆盖时提示确认）。ColumnNames 用于模版使用时校验列存在性。

## Database Configuration

**设计说明**: `UploadBatch.Strategy`（合并/分别分析）与 `AnalysisSession.Mode`（对话/模版模式）是两个正交的独立维度，任意组合均合法。Strategy 在文件就绪后确定（US3），Mode 在模版选择后确定（US4），两者通过 FR-057（策略冲突检测）在模版选择时进行协调。

## Database Configuration

- **Provider**: SQLite (Microsoft.EntityFrameworkCore.Sqlite)
- **WAL Mode**: `PRAGMA journal_mode=WAL;` — 支持并发读写
- **Foreign Keys**: `PRAGMA foreign_keys=ON;`
- **Indexes**:
  - `User.Username` (Unique)
  - `UploadBatch.UserId` + `BatchStatus`
  - `AnalysisSession.BatchId` (Unique 有效会话)
  - `AnalysisReport.SessionId` (Unique)
  - `AnalysisReport.CreatedAt` (历史列表排序)
  - `AnalysisTemplate.UserId` + `Name` (Unique 组合)
  - `ChatMessage.SessionId` + `Timestamp`
  - `UploadedFile.BatchId`
