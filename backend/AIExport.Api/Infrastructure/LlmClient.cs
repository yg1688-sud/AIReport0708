using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIExport.Api.Infrastructure;

public class LlmClient
{
    private readonly HttpClient _http;
    private readonly string _deepseekKey;
    private readonly string _kimiKey;
    private readonly int _timeoutSeconds;

    private const string DeepSeekEndpoint = "https://api.deepseek.com/v1/chat/completions";
    private const string KimiEndpoint = "https://api.moonshot.cn/v1/chat/completions";

    public LlmClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _deepseekKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? config.GetValue<string> ("DEEPSEEK_API_KEY");
        _kimiKey = Environment.GetEnvironmentVariable("KIMI_API_KEY") ?? config.GetValue<string> ("KIMI_API_KEY");
        _timeoutSeconds = config.GetValue<int>("Chat:LlmTimeoutSeconds", 30);
    }

    private (string endpoint, string key, string model) ResolveModel(string? model)
    {
        return model switch
        {
            "kimi" => (KimiEndpoint, _kimiKey, "moonshot-v1-128k"),
            _ => (DeepSeekEndpoint, _deepseekKey, "deepseek-v4-pro") // 默认 deepseek
        };
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(string userMessage, string[] availableColumns,
        string? model = null, List<(string role, string content)>? history = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (endpoint, key, modelName) = ResolveModel(model);
        if (string.IsNullOrEmpty(key))
        {
            var envName = model == "kimi" ? "KIMI_API_KEY" : "DEEPSEEK_API_KEY";
            yield return $"⚠️ 服务端未配置 {envName} 环境变量，无法调用 AI 模型。请配置后重启服务。";
            yield break;
        }

        var systemPrompt = BuildSystemPrompt(availableColumns);
        var json = BuildRequestBody(modelName, systemPrompt, userMessage, history);

        var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") yield break;

            using var doc = JsonDocument.Parse(data);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");

            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            {
                var rtext = rc.GetString();
                if (!string.IsNullOrEmpty(rtext)) { yield return $"__REASONING__:{rtext}"; continue; }
            }

            if (delta.TryGetProperty("content", out var ctJson) && ctJson.ValueKind == JsonValueKind.String)
            {
                var text = ctJson.GetString();
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }

    public async Task<LlmResponse?> ChatAsync(string userMessage, string[] availableColumns,
        string? model = null, List<(string role, string content)>? history = null, int attempt = 0)
    {
        var (endpoint, key, modelName) = ResolveModel(model);
        if (string.IsNullOrEmpty(key)) return null;

        var systemPrompt = BuildSystemPrompt(availableColumns);
        // 构建消息列表（含历史）
        var msgList = new List<object> { new { role = "system", content = systemPrompt } };
        if (history is not null)
        { foreach (var (role, content) in history) msgList.Add(new { role, content }); }
        msgList.Add(new { role = "user", content = userMessage });

        var body = new { model = modelName, messages = msgList.ToArray(), temperature = 0.3, max_tokens = 4000, thinking = new { type = "enabled" } };

        try
        {
            var json = JsonSerializer.Serialize(body);
            var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var response = await _http.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var resultStream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(resultStream);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            string? reasoning = null;
            if (message.TryGetProperty("reasoning_content", out var reasoningEl))
                reasoning = reasoningEl.GetString();

            var reply = message.GetProperty("content").GetString();
            var jsonStart = reply!.IndexOf('{');
            var jsonEnd = reply.LastIndexOf('}');
            var jsonStr = (jsonStart >= 0 && jsonEnd > jsonStart) ? reply[jsonStart..(jsonEnd + 1)] : reply;

            var result = JsonSerializer.Deserialize<LlmResponse>(jsonStr);
            if (result is not null && reasoning is not null)
                result = result with { Reasoning = reasoning };
            return result;
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            if (attempt < 2) return await ChatAsync(userMessage, availableColumns, model, history, attempt + 1);
            return null;
        }
    }

    private static string BuildRequestBody(string modelName, string systemPrompt, string userMessage,
        List<(string role, string content)>? history = null)
    {
        var msgList = new List<object> { new { role = "system", content = systemPrompt } };

        // 注入对话历史
        if (history is not null)
        {
            foreach (var (role, content) in history)
                msgList.Add(new { role, content });
        }

        // 当前用户消息
        msgList.Add(new { role = "user", content = userMessage });

        var messages = msgList.ToArray();
        if (modelName.StartsWith("moonshot"))
            return JsonSerializer.Serialize(new { model = modelName, messages, temperature = 0.3, max_tokens = 4000, stream = true });
        else
            return JsonSerializer.Serialize(new { model = modelName, messages, temperature = 0.3, max_tokens = 4000, thinking = new { type = "enabled" }, stream = true });
    }

    private static string BuildSystemPrompt(string[] availableColumns)
    {
        return "你是一个专业的数据分析助手，帮助用户分析 Excel 数据。\n\n" +
            $"可用数据列：{string.Join(", ", availableColumns)}\n\n" +
            "请用自然语言与用户对话。重要规则：\n\n" +
            "1. 当用户给出具体、明确的分析需求时（如包含具体列名、计算方法、公式等），应直接确认并表示理解，不要反复追问。\n" +
            "2. 用户可以提出自定义计算需求（如去重计数、条件过滤、比率计算等），你应当记录下来并在确认时传递给报告系统。\n" +
            "3. 只有当用户需求确实模糊时才追问（如只说了\"帮我分析\"而没有指定任何维度或指标）。\n" +
            "4. 对于用户明确指定的计算公式，视为需求已明确，直接确认并总结。告知用户可输入\"确认生成报告\"来生成报告。\n" +
            "5. 所有分析必须在可用列范围内，如果用户指定的列名不存在则友好提示。\n" +
            "6. 回复中严禁使用 Markdown 表格语法（即用 | 分隔的表格），也不要展示示例数值。总结指标时用无序列表，每行一个指标，格式为\"- **指标名**：计算逻辑\"。\n\n" +
            "注意：请直接用自然语言回复，不要输出 JSON 格式代码。当用户需求明确时，回复末尾加上【需求已明确，请输入确认生成报告】。";
    }
}

public record LlmResponse(
    List<string> Dimensions,
    List<string> Metrics,
    List<string> ChartTypes,
    string? FollowUpQuestion,
    string? Reasoning = null
);
