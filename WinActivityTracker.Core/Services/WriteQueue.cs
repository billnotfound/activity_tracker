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

    private const int BatchSize = 50;

    public WriteQueue(IServiceScopeFactory scopeFactory, ILogger<WriteQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<Action<AppDbContext>>(
            new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public bool TryWrite(Action<AppDbContext> writeOp)
        => _channel.Writer.TryWrite(writeOp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var count = 0;

                var op = await _channel.Reader.ReadAsync(stoppingToken);
                op(db);
                count++;

                while (_channel.Reader.TryRead(out var next))
                {
                    next(db);
                    count++;
                    if (count >= BatchSize) break;
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteQueue flush error");
            }
        }

        // Final drain on shutdown
        if (_channel.Reader.Count > 0)
        {
            try
            {
                using var finalScope = _scopeFactory.CreateScope();
                var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var count = 0;
                while (_channel.Reader.TryRead(out var op))
                {
                    op(finalDb);
                    count++;
                }
                if (count > 0)
                    await finalDb.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteQueue final drain error");
            }
        }
    }
}
