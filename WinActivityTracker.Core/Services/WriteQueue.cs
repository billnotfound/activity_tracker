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
    private readonly int _capacity = 2000;
    private DateTime _lastSuccessfulFlush = DateTime.UtcNow;

    public int ChannelFillPercent => _channel.Reader.Count * 100 / _capacity;
    public DateTime LastSuccessfulFlush => _lastSuccessfulFlush;

    public WriteQueue(IServiceScopeFactory scopeFactory, ILogger<WriteQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<Action<AppDbContext>>(
            new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.DropOldest });
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
                try { op(db); count++; }
                catch (Exception ex) { _logger.LogError(ex, "WriteQueue op failed, skipping"); }

                while (_channel.Reader.TryRead(out var next))
                {
                    try { next(db); count++; }
                    catch (Exception ex) { _logger.LogError(ex, "WriteQueue op failed, skipping"); }
                    if (count >= BatchSize) break;
                }

                if (count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    _lastSuccessfulFlush = DateTime.UtcNow;
                }
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
                    try { op(finalDb); count++; }
                    catch (Exception ex) { _logger.LogError(ex, "WriteQueue final drain op failed, skipping"); }
                }
                if (count > 0)
                {
                    await finalDb.SaveChangesAsync(CancellationToken.None);
                    _lastSuccessfulFlush = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteQueue final drain error");
            }
        }
    }
}
