using System.Threading.Channels;

namespace AIExport.Api.Infrastructure;

/// <summary>
/// 后台任务队列：使用 Channel<T> 实现有界队列，控制并发分析任务数
/// </summary>
public class JobQueue
{
    private readonly Channel<ReportJob> _channel;
    private readonly int _maxConcurrency;

    public JobQueue(int maxConcurrency = 100)
    {
        _maxConcurrency = maxConcurrency;
        var options = new BoundedChannelOptions(maxConcurrency)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<ReportJob>(options);
    }

    public async ValueTask EnqueueAsync(ReportJob job, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(job, ct);
    }

    public IAsyncEnumerable<ReportJob> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    public int PendingCount => _channel.Reader.Count;
}

public record ReportJob(Guid TaskId, Guid SessionId, Guid ReportId);
