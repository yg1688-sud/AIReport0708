# API Contract: AIExport 数据分析报告系统

**Version**: 1.0.0 | **Date**: 2026-07-10 | **Base URL**: `/api`

## 通用约定

- **认证**: 除 `/api/auth/login` 外，所有端点要求 `Authorization: Bearer <JWT>` 头
- **Content-Type**: `application/json`（文件上传使用 `multipart/form-data`）
- **SSE**: 进度推送使用 `text/event-stream`，非标准 JSON 响应
- **错误响应格式**: `{ "error": { "code": "ERROR_CODE", "message": "用户友好描述" } }`
- **日期格式**: ISO 8601 (`2026-07-10T14:30:00Z`)

---

## 1. Auth — 认证

### POST /api/auth/login

**描述**: 用户登录，获取 JWT Token。

**Request**:
```json
{
  "username": "string (3-50 chars)",
  "password": "string (6-100 chars)"
}
```

**Response** (200):
```json
{
  "token": "eyJhbG...",
  "expiresAt": "2026-07-10T16:30:00Z",
  "user": { "id": "guid", "username": "string", "role": "user" }
}
```

**Errors**: 401 - 用户名或密码错误; 403 - 账户已被禁用

---

## 2. Files — 文件上传

### POST /api/files/upload

**描述**: 上传一个或多个数据文件。`Content-Type: multipart/form-data`，字段名 `files`。

**Request**: 文件数组（.xlsx/.xls/.csv），单文件 ≤100MB，单次 ≤20 个。

**Response** (200):
```json
{
  "batchId": "guid",
  "files": [
    {
      "id": "guid",
      "originalName": "sales_2024.xlsx",
      "fileSize": 5242880,
      "fileFormat": "xlsx",
      "parseStatus": "parsing"
    }
  ]
}
```

**Events** (SSE `GET /api/files/progress?batchId={batchId}`):
```
event: file-progress
data: {"fileId":"guid","parseStatus":"ready","rowCount":5000,"columnCount":12}

event: batch-ready
data: {"batchId":"guid","readyFiles":3,"totalFiles":3}
```

**Errors**: 400 - 格式不支持/文件过大/文件数量超限; 401 - 未登录

### GET /api/files/progress

**描述**: SSE 端点，订阅文件解析进度。`?batchId={guid}`

### DELETE /api/files/{fileId}

**描述**: 从批次中移除单个文件。**Response**: 204 No Content

### DELETE /api/batches/{batchId}/files

**描述**: 清空批次中所有文件。**Response**: 204 No Content

---

## 3. Chat — 聊天对话

### POST /api/chat/start

**描述**: 启动聊天对话（文件就绪后调用）。

**Request**:
```json
{
  "batchId": "guid",
  "strategy": "merge | separate",
  "templateId": "guid | null"
}
```

**Response** (200):
```json
{
  "sessionId": "guid",
  "mode": "chat | template",
  "firstMessage": "我已读取您的数据，请告诉我您希望进行哪些分析？"
}
```

### POST /api/chat/message

**描述**: 发送聊天消息，获取 AI 回复。

**Request**:
```json
{
  "sessionId": "guid",
  "content": "帮我分析一下按地区的销售趋势"
}
```

**Response** (200):
```json
{
  "messageId": "guid",
  "sender": "system",
  "content": "好的，我将按地区分析销售趋势。您希望使用柱状图还是折线图展示？",
  "timestamp": "2026-07-10T14:31:00Z"
}
```

### GET /api/chat/messages?sessionId={guid}

**描述**: 获取会话的全部聊天记录。

**Response** (200):
```json
{
  "messages": [
    { "id": "guid", "sender": "system", "content": "...", "timestamp": "..." },
    { "id": "guid", "sender": "user", "content": "...", "timestamp": "..." }
  ]
}
```

### POST /api/chat/confirm

**描述**: 确认分析需求，锁定并生成报告。

**Request**:
```json
{
  "sessionId": "guid"
}
```

**Response** (200):
```json
{
  "taskId": "guid",
  "message": "需求已确认，正在生成报告..."
}
```

---

## 4. Reports — 报告生成与下载

### GET /api/reports/progress?taskId={guid}

**描述**: SSE 端点，订阅报告生成进度。

```
event: progress
data: {"stage":"cleaning","percent":10,"message":"正在清洗数据..."}

event: progress
data: {"stage":"cross-table","percent":50,"message":"正在计算交叉表..."}

event: complete
data: {"reportId":"guid","message":"报告生成完成"}
```

### GET /api/reports/{reportId}

**描述**: 获取报告详情（含章节内容 JSON）。

**Response** (200):
```json
{
  "id": "guid",
  "reportStatus": "completed",
  "mode": "chat",
  "reportType": "merge",
  "chapters": {
    "overview": { "rowCount": 5000, "columnCount": 12, "missingRate": 0.02 },
    "statistics": { "columns": [...] },
    "charts": [...],
    "crossAnalysis": [...]
  },
  "createdAt": "2026-07-10T14:32:00Z"
}
```

### GET /api/reports/{reportId}/download

**描述**: 下载 PDF 报告。支持 Range Requests（HTTP 206 Partial Content）。单连接 ≤60s。

**Response**: `application/pdf` 流 + `Content-Disposition: attachment; filename="report.pdf"`

**Headers**: `Accept-Ranges: bytes`, `Content-Length: <bytes>`

### GET /api/reports

**描述**: 获取用户的所有历史报告列表（分页）。

**Query**: `?page=1&pageSize=20&keyword=&dateFrom=&dateTo=`

**Response** (200):
```json
{
  "items": [
    {
      "id": "guid",
      "originalFileName": "sales_2024.xlsx",
      "reportStatus": "completed",
      "mode": "chat",
      "reportType": "merge",
      "fileSize": 2048000,
      "createdAt": "2026-07-10T14:32:00Z",
      "expiresAt": "2026-07-17T14:32:00Z"
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 20
}
```

### DELETE /api/reports/{reportId}

**描述**: 删除历史报告及关联 PDF 文件（需二次确认，前端实现）。**Response**: 204

### POST /api/reports/{reportId}/regenerate

**描述**: 基于保留的原始数据重新生成报告（7 天有效期内）。

**Response** (200):
```json
{
  "taskId": "guid",
  "sessionId": "guid",
  "message": "已基于原始数据创建新的需求确认会话"
}
```

### POST /api/reports/batch-download

**描述**: 批量下载选中的多份报告，打包为 ZIP。

**Request**:
```json
{
  "reportIds": ["guid1", "guid2", "guid3"]
}
```

**Response**: `application/zip` 流

---

## 5. Templates — 分析模版

### GET /api/templates

**描述**: 获取当前用户的全部模版列表。

**Response** (200):
```json
{
  "templates": [
    {
      "id": "guid",
      "name": "月度销售分析",
      "strategy": "merge",
      "columnNames": ["日期","销售额","地区","产品"],
      "createdAt": "2026-07-09T10:00:00Z"
    }
  ]
}
```

### POST /api/templates

**描述**: 保存新模版。

**Request**:
```json
{
  "sessionId": "guid",
  "name": "月度销售分析"
}
```

**Response** (201):
```json
{
  "id": "guid",
  "name": "月度销售分析",
  "message": "模版保存成功"
}
```

**Errors**: 409 - 模版名称已存在（前端提示是否覆盖）; 400 - 名称为空

### PUT /api/templates/{templateId}

**描述**: 覆盖已有模版（模版不可编辑参数，仅限覆盖）。

**Request**:
```json
{
  "sessionId": "guid"
}
```

**Response** (200):
```json
{ "message": "模版已覆盖保存" }
```

### DELETE /api/templates/{templateId}

**描述**: 删除模版。**Response**: 204。**Errors**: 404 - 模版不存在。

### POST /api/templates/{templateId}/validate

**描述**: 校验模版中的列是否在当前批次文件中存在。

**Request**:
```json
{
  "batchId": "guid"
}
```

**Response** (200):
```json
{
  "valid": true,
  "missingColumns": [],
  "strategyConflict": false,
  "templateStrategy": "merge",
  "currentStrategy": "separate"
}
```

---

## 6. SSE 事件类型汇总

| 事件名 | 用途 | 端点 |
|--------|------|------|
| `file-progress` | 单个文件解析进度 | `GET /api/files/progress` |
| `batch-ready` | 批次全部就绪 | `GET /api/files/progress` |
| `progress` | 报告生成阶段进度 | `GET /api/reports/progress` |
| `complete` | 报告生成完成 | `GET /api/reports/progress` |
| `error` | 错误通知 | 所有 SSE 端点 |
