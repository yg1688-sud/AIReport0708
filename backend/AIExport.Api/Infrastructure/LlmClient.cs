using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIExport.Api.Infrastructure;

public class LlmClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly int _timeoutSeconds;
    private const string ApiEndpoint = "https://api.deepseek.com/v1/chat/completions";
    private const string Model = "deepseek-v4-pro";

    public LlmClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new InvalidOperationException("DEEPSEEK_API_KEY 环境变量未设置");
        _timeoutSeconds = config.GetValue<int>("Chat:LlmTimeoutSeconds", 30);
        _http.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// 调用 DeepSeek V4 理解用户的自然语言分析需求，返回结构化 JSON
    /// </summary>
    public async Task<LlmResponse?> ChatAsync(string userMessage, string[] availableColumns, int attempt = 0)
    {
        var systemPrompt = BuildSystemPrompt(availableColumns);

        var body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.3,
            max_tokens = 2000,
            response_format = new { type = "json_object" }
        };

        try
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(ApiEndpoint, content);

            response.EnsureSuccessStatusCode();
            var resultStream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(resultStream);
            var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            return JsonSerializer.Deserialize<LlmResponse>(reply!);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            if (attempt < 2) // 重试最多 3 次
                return await ChatAsync(userMessage, availableColumns, attempt + 1);

            // 超过重试次数，返回 null 触发降级
            return null;
        }
    }

    private static string BuildSystemPrompt(string[] availableColumns)
    {
        var jsonExample = @"{
          ""dimensions"": [""分析维度列表，如 地区、时间""],
          ""metrics"": [""分析指标列表，如 销售额、利润""],
          ""chartTypes"": [""图表类型：bar/line/pie/scatter""],
          ""followUpQuestion"": ""如果需求不够明确，追问用户的问题；如果明确则留空""
        }";

        return $"你是一个数据分析助手。用户会上传 Excel 数据并要求进行分析。\n" +
               $"可用的数据列：{string.Join(", ", availableColumns)}\n\n" +
               $"请分析用户的意图，返回以下 JSON 格式（不要包含额外文字）：\n" +
               $"{jsonExample}\n\n" +
               "规则：\n" +
               "- 数值型列建议用均值/求和/趋势分析\n" +
               "- 文本型列建议用频次分布/分类汇总\n" +
               "- 日期型列建议用时间趋势/同比环比\n" +
               "- 所有分析必须在可用列范围内";
    }
}

public record LlmResponse(
    List<string> Dimensions,
    List<string> Metrics,
    List<string> ChartTypes,
    string? FollowUpQuestion
);
