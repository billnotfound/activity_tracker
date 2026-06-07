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
    private const int FlushIntervalMs = 50;

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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = 0;
        var lastFlush = Environment.TickCount64;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var op = await _channel.Reader.ReadAsync(stoppingToken);
                op(db);
                count++;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_channel.Reader.TryRead(out var next))
            {
                next(db);
                count++;
            }

            var now = Environment.TickCount64;
            if (count >= BatchSize || (now - lastFlush) >= FlushIntervalMs)
            {
                await db.SaveChangesAsync(stoppingToken);
                count = 0;
                lastFlush = now;
            }
        }

        // Drain any items still in the channel before final flush.
        while (_channel.Reader.TryRead(out var lastOp))
        {
            lastOp(db);
            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(CancellationToken.None);
    }
}
