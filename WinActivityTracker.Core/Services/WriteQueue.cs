using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Data;

namespace WinActivityTracker.Core.Services;

public sealed class WriteQueue : BackgroundService
{
    private readonly Channel<Action<AppDbContext>> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WriteQueue> _logger;
    private readonly SemaphoreSlim _consumeGate = new(1, 1);

    private const int BatchSize = 50;
    private readonly int _capacity = 2000;
    private DateTime _lastSuccessfulFlush = DateTime.UtcNow;
    private long _rejectedWrites;

    public int ChannelFillPercent => _channel.Reader.Count * 100 / _capacity;
    public DateTime LastSuccessfulFlush => _lastSuccessfulFlush;
    /// <summary>当前排队中的写操作数（真实计数，非百分比）。</summary>
    public int PendingCount => _channel.Reader.Count;
    public long RejectedWrites => Interlocked.Read(ref _rejectedWrites);

    public WriteQueue(IServiceScopeFactory scopeFactory, ILogger<WriteQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<Action<AppDbContext>>(
            new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TryWrite(Action<AppDbContext> writeOp)
    {
        if (_channel.Writer.TryWrite(writeOp)) return true;

        var rejected = Interlocked.Increment(ref _rejectedWrites);
        if (rejected == 1 || rejected % 100 == 0)
            _logger.LogError("WriteQueue is full; rejected {RejectedWrites} writes", rejected);
        return false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _consumeGate.WaitAsync(cancellationToken);
        try
        {
            _channel.Writer.TryComplete();
            var remaining = new List<Action<AppDbContext>>();
            while (_channel.Reader.TryRead(out var op)) remaining.Add(op);
            await FlushOrSalvageOnShutdownAsync(remaining);
        }
        finally
        {
            _consumeGate.Release();
        }
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _channel.Reader.WaitToReadAsync())
        {
            await _consumeGate.WaitAsync();
            try
            {
                var batch = new List<Action<AppDbContext>>(BatchSize);
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var next))
                    batch.Add(next);

                if (batch.Count > 0
                    && !await FlushBatchWithRetryAsync(batch, CancellationToken.None))
                    await SalvageIndividuallyAsync(batch, CancellationToken.None);
            }
            finally
            {
                _consumeGate.Release();
            }
        }
    }

    private async Task<bool> FlushBatchWithRetryAsync(
        IReadOnlyList<Action<AppDbContext>> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var op in batch)
                {
                    try { op(db); }
                    catch (Exception ex) { _logger.LogError(ex, "WriteQueue op failed, skipping"); }
                }

                await db.SaveChangesAsync(cancellationToken);
                _lastSuccessfulFlush = DateTime.UtcNow;
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WriteQueue batch flush attempt {Attempt}/3 failed", attempt);
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
        return false;
    }

    private async Task SalvageIndividuallyAsync(
        IReadOnlyList<Action<AppDbContext>> batch, CancellationToken cancellationToken)
    {
        foreach (var op in batch)
        {
            if (!await FlushBatchWithRetryAsync(new[] { op }, cancellationToken))
                _logger.LogError("WriteQueue operation dropped after batch and individual retries");
        }
    }

    private async Task FlushOrSalvageOnShutdownAsync(IReadOnlyList<Action<AppDbContext>> batch)
    {
        if (batch.Count == 0) return;
        if (!await FlushBatchWithRetryAsync(batch, CancellationToken.None))
            await SalvageIndividuallyAsync(batch, CancellationToken.None);
    }
}
