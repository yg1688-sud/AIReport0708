# Quickstart Guide: Excel 数据分析报告自动生成

**Date**: 2026-07-10 | **Feature**: [spec.md](./spec.md)

## 前提条件

- .NET 10 SDK ([下载](https://dotnet.microsoft.com/download/dotnet/10.0))
- 现代浏览器（Chrome 120+ / Edge 120+ / Firefox 128+）
- Git（可选）
- DeepSeek API Key（[申请](https://platform.deepseek.com/)）
- [可选] Node.js 20+（仅用于 Playwright E2E 测试）

## 快速启动（本地开发）

### 1. 克隆项目

```bash
git clone <repo-url>
cd AIExport0708
```

### 2. 配置环境变量

```bash
# Windows PowerShell
$env:DEEPSEEK_API_KEY = "sk-your-api-key"
$env:JWT_SECRET = "your-jwt-secret-dev-only"
$env:ADMIN_PASSWORD = "admin123"

# Linux/macOS
export DEEPSEEK_API_KEY="sk-your-api-key"
export JWT_SECRET="your-jwt-secret-dev-only"
export ADMIN_PASSWORD="admin123"
```

> ⚠️ 生产环境凭据应通过 Vault/Secret Manager 注入，禁止硬编码或提交到版本控制。

### 3. 初始化数据库

```bash
cd backend/AIExport.Api
dotnet restore
dotnet ef database update
```

这将自动创建 SQLite 数据库文件 `Data/app.db` 及所有表结构，并预置管理员账户（用户名 `admin`）。

### 4. 启动后端

```bash
dotnet run --urls "http://localhost:5000"
```

验证: 浏览器打开 `http://localhost:5000/api/health`，应返回 `{ "status": "ok" }`。

### 5. 启动前端

```bash
# 前端为纯静态文件，使用任意 HTTP 服务器
cd frontend
python -m http.server 3000
# 或使用 npx serve . -p 3000
```

打开 `http://localhost:3000`，使用 `admin` / 管理员密码登录。

## 验证场景

### 场景 1: 完整流程验证（对话模式）

1. 登录系统（默认管理员账户）
2. 上传测试 Excel 文件（`tests/data/sample-sales.xlsx`，包含"日期/销售额/地区/产品"列）
3. 确认文件解析成功 → 显示"所有文件就绪"
4. 进入聊天对话 → 输入"帮我分析按地区的月度销售趋势"
5. 系统追问"使用柱状图还是折线图？" → 回复"柱状图"
6. 输入"确认需求" → 1s 内显示任务 ID
7. 等待报告生成（进度条显示阶段） → 最终展示完整报告
8. 提示"是否保存为模版？" → 输入"月度销售分析" → 保存成功
9. 点击"下载" → PDF 文件下载成功
10. 点击"打印" → 浏览器打印对话框弹出

### 场景 2: 模版模式验证

1. 重新上传相同结构的 Excel 文件
2. 文件就绪后 → 模版列表显示"月度销售分析"
3. 选择该模版 → "确认需求"按钮隐藏，"立即生成报告"按钮出现
4. 点击"立即生成报告" → 直接生成报告（跳过聊天）
5. 报告内容与上次一致

### 场景 3: 多文件分别分析

1. 上传 3 个结构不同的 Excel 文件
2. 系统询问"合并分析还是分别分析？"
3. 选择"分别分析" → 系统确认
4. 完成聊天需求确认 → 生成 3 份独立报告
5. 在报告列表中切换查看各文件报告

### 场景 4: 历史报告管理

1. 进入"历史报告"页面 → 看到之前生成的报告列表
2. 搜索文件名 → 过滤正确
3. 选择日期范围筛选 → 结果正确
4. 勾选 2 份报告 → 批量下载 → ZIP 文件成功下载
5. 删除一份报告 → 确认后移除

### 场景 5: 错误处理

1. 上传空 Excel → 提示"无数据"
2. 上传 >100MB 文件 → 提示"文件大小超过 100MB"
3. 上传加密 Excel → 提示"文件解析失败，请确认未加密"
4. 选择模版但新文件缺少模版中的列 → 提示具体缺失列名

## 测试执行

```bash
# 后端单元测试
cd tests/AIExport.Api.Tests
dotnet test

# 后端集成测试
dotnet test --filter "Category=Integration"

# E2E 测试（需先启动后端+前端）
cd tests/AIExport.E2E.Tests
npx playwright test
```

## 项目配置参考

| 配置项 | 文件 | 说明 |
|--------|------|------|
| 数据库连接 | `appsettings.json` → `ConnectionStrings:Default` | `Data Source=Data/app.db` |
| JWT Secret | 环境变量 `JWT_SECRET` | 至少 32 字符 |
| DeepSeek API | 环境变量 `DEEPSEEK_API_KEY` | sk- 开头的 API Key |
| 文件大小限制 | `appsettings.json` → `Upload:MaxFileSize` | 默认 104857600 (100MB) |
| 文件数量限制 | `appsettings.json` → `Upload:MaxFileCount` | 默认 20 |
| 报告保留天数 | `appsettings.json` → `Report:RetentionDays` | 默认 7 |
| 并发任务上限 | `appsettings.json` → `Report:MaxConcurrentJobs` | 默认 100 |
