# AIExport — 数据分析报告自动生成系统

自动生成结构化的 Excel 数据分析报告。上传数据文件 → AI 聊天对话确认需求（或选择模版）→ 自动生成分析报告 → 导出 Excel/下载 PDF/打印。支持多文件分别分析、模版复用、历史管理。

## 技术栈

| 层 | 技术 |
|----|------|
| 后端 | ASP.NET Core 10 Minimal API (C# 14) |
| ORM | Entity Framework Core 10 + SQLite (WAL 模式) |
| 前端 | Ant Design 5.x CDN + 原生 JavaScript + Chart.js |
| AI | DeepSeek V4 API（聊天需求澄清 + 规则引擎降级） |
| PDF | QuestPDF (A4 报告模板) |
| 认证 | JWT Bearer (HS256) + BCrypt |
| 测试 | xUnit.net + Moq + WebApplicationFactory + Playwright |

## 快速启动

### 前提条件

- .NET 10 SDK
- 现代浏览器 (Chrome/Edge/Firefox)
- DeepSeek API Key（[申请](https://platform.deepseek.com/)）

### 配置环境变量

```bash
# Windows PowerShell
$env:DEEPSEEK_API_KEY = "sk-your-api-key"
$env:JWT_SECRET = "your-jwt-secret-at-least-32-chars"
$env:ADMIN_PASSWORD = "your-pwd"

# Linux/macOS
export DEEPSEEK_API_KEY="sk-your-api-key"
export JWT_SECRET="your-jwt-secret-at-least-32-chars"
export ADMIN_PASSWORD="your-pwd"
```

> ⚠️ 生产环境凭据应通过 Vault/Secret Manager 注入。

### 启动后端

```bash
cd backend/AIExport.Api
dotnet run --urls "http://localhost:5000"
```

### 启动前端

```bash
cd frontend
python -m http.server 3000
# 或: npx serve . -p 3000
```

浏览器打开 `http://localhost:3000`，使用 `admin` / 管理员密码登录。

### 运行测试

```bash
cd tests/AIExport.Api.Tests
dotnet test
```

## 项目结构

```text
backend/AIExport.Api/    # ASP.NET Core Web API
frontend/                # 静态文件 (Ant Design CDN)
tests/
├── AIExport.Api.Tests/  # 单元测试 + 集成测试
└── AIExport.E2E.Tests/  # Playwright E2E
specs/001-excel-analysis-report/  # 设计文档
```

## 功能

- 多文件上传（.xlsx/.csv，单文件 ≤100MB，最多 20 个）
- AI 聊天确认分析需求（DeepSeek V4 + 规则引擎混合模式）
- 分析模版保存与复用（双模式：模版模式/对话模式）
- 结构化分析报告（数据概览/描述性统计/汇总指标/图表/计算结果表格）
- 图表支持：柱状图 / 折线图 / 饼图 / 漏斗图 / 条形图（Chart.js 渲染，漏斗图为纯 SVG）
- 导出 Excel（表格数据 + 汇总指标）、下载 PDF、浏览器打印
- 时间统一使用本地 24 小时制
- 历史报告管理（搜索/筛选/删除/批量下载/7天自动清理）
- 管理员用户管理
- 响应式设计（桌面 1920×1080 + 平板 768×1024）
