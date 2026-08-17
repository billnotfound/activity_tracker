using System.Net;
using System.Net.Sockets;
using WinActivityTracker.Core.Trackers;

namespace WinActivityTracker.Core.Services;

/// <summary>RFC 4330 SNTP 客户端（UDP 123，48 字节报文）。</summary>
public class SntpTimeReference : ITimeReference
{
    private const ulong NtpToUnixEpoch = 2208988800UL;
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _server;

    public SntpTimeReference(string server) => _server = server;

    public async Task<TimeReferenceResult> QueryAsync(CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ReceiveTimeout = 3000;
            var serverIp = (await Dns.GetHostAddressesAsync(_server, ct)).First(a => a.AddressFamily == AddressFamily.InterNetwork);

            var request = new byte[48];
            request[0] = 0x1B; // LI=0, VN=4, Mode=3 (client)
            var t0 = DateTime.UtcNow;
            WriteTransmitTimestamp(request, t0); // 回填发送时刻（字节 40-47），否则服务器回填的 Originate 无效
            await udp.SendAsync(request.AsMemory(), new IPEndPoint(serverIp, 123), ct);
            var response = await udp.ReceiveAsync(ct);
            var t3 = DateTime.UtcNow;

            if (response.Buffer.Length < 48)
                return Failed($"short response ({response.Buffer.Length} bytes)");

            // Originate（字节 24-31）应等于发出的 Transmit，可用于校验报文对应关系；这里不校验
            var t1 = ParseTimestamp(response.Buffer, 32); // Receive Timestamp（服务器收到请求的时刻）
            var t2 = ParseTimestamp(response.Buffer, 40); // Transmit Timestamp（服务器发出应答的时刻）

            // 标准 RFC 4330 客户端公式：θ = ((t1-t0)+(t2-t3))/2 是时钟修正量（正 θ = 本机慢）。
            // 契约 OffsetSeconds = 本机墙钟 − 参照时间（+ 本机偏快），故对 RFC 修正量 θ 取负。
            var offset = ((t1 - t0).TotalSeconds + (t2 - t3).TotalSeconds) / 2.0;
            var rtt = (t3 - t0).TotalSeconds - (t2 - t1).TotalSeconds;

            return new TimeReferenceResult
            {
                Succeeded = true,
                ReferenceUtc = t2,
                OffsetSeconds = -offset,
                LatencyMs = rtt * 1000,
                SourceName = $"sntp:{_server}"
            };
        }
        catch (Exception ex)
        {
            // 网络失败：吞掉异常，返回 Succeeded=false
            return Failed(ex.Message);
        }
    }

    private TimeReferenceResult Failed(string reason) => new()
    {
        Succeeded = false,
        ReferenceUtc = DateTime.UtcNow,
        LatencyMs = 0,
        SourceName = $"sntp:{_server} ({reason})"
    };

    /// <summary>
    /// 解析应答报文 Transmit Timestamp（字节 40-47，仅整数秒），带 2036 era 回绕检测。
    /// 纯静态方法，供单元测试。
    /// </summary>
    public static DateTime ParsePacket(byte[] packet)
    {
        ulong sec = ((ulong)packet[40] << 24) | ((ulong)packet[41] << 16) | ((ulong)packet[42] << 8) | packet[43];
        if (sec < NtpToUnixEpoch) sec += 1UL << 32; // 2036 era rollover
        return NtpEpoch.AddSeconds(sec); // sec 即 NTP 纪元（1900-01-01）秒数
    }

    /// <summary>解析 NTP 时间戳（8 字节：秒 + 小数），带 era 回绕检测，转换为 UTC。</summary>
    private static DateTime ParseTimestamp(byte[] packet, int offset)
    {
        ulong sec = ((ulong)packet[offset] << 24) | ((ulong)packet[offset + 1] << 16) | ((ulong)packet[offset + 2] << 8) | packet[offset + 3];
        ulong frac = ((ulong)packet[offset + 4] << 24) | ((ulong)packet[offset + 5] << 16) | ((ulong)packet[offset + 6] << 8) | packet[offset + 7];
        if (sec < NtpToUnixEpoch) sec += 1UL << 32; // 2036 era rollover
        return NtpEpoch.AddSeconds(sec + frac / 4294967296.0);
    }

    /// <summary>把 UTC 时刻写进报文 Transmit Timestamp（字节 40-47）：NTP 纪元秒 + 32 位小数。</summary>
    private static void WriteTransmitTimestamp(byte[] request, DateTime utc)
    {
        var ntp = (utc - NtpEpoch).TotalSeconds; // NTP 纪元秒数；2036 后 >2^32，取低 32 位即当前 era 的回绕值
        var sec = (uint)ntp;
        var frac = (uint)((ntp - sec) * 4294967296.0);
        for (var i = 0; i < 4; i++) request[40 + i] = (byte)(sec >> (24 - i * 8));
        for (var i = 0; i < 4; i++) request[44 + i] = (byte)(frac >> (24 - i * 8));
    }
}
