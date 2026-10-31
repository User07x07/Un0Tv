#if ANDROID
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Java.IO;

namespace Un0Tv;

// A local VPN that sees only DNS lookups, never the content of any connection.
//
// Only the TV's DNS address (10.0.0.1) is sent through this VPN, so every app's
// lookup arrives here first. Each name is checked with the same rules as the
// browser filter (RequestFilter, HaGeZi list). A blocked name gets an
// "does not exist" answer. An allowed name is asked for over DNS-over-HTTPS
// (encrypted), and the answer is handed back to the app that asked.
//
// Nothing else is routed here, so the VPN's own connection to the DNS provider
// goes straight out and cannot loop back in. That connection is made to the
// provider's IP address, so the VPN does not need a DNS lookup to reach it.
[Service(Permission = "android.permission.BIND_VPN_SERVICE", Exported = false)]
[IntentFilter(new[] { "android.net.VpnService" })]
public class DnsFilterService : VpnService
{
    private const string TunAddress = "10.0.0.2";
    private const string DnsAddress = "10.0.0.1";

    // The upstream encrypted DNS provider (AdGuard DNS), and its IP address.
    private const string Upstream = "https://dns.adguard-dns.com/dns-query";
    private const string UpstreamIp = "94.140.14.14";

    private static readonly object WriteLock = new();

    private static readonly HttpClient Doh = new(new SocketsHttpHandler
    {
        // Connect to the provider's IP directly. TLS still checks the name in
        // the address (dns.adguard-dns.com), so the connection stays secure.
        ConnectCallback = async (ctx, ct) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(UpstreamIp), 443), ct);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            return new NetworkStream(socket, true);
        }
    })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private ParcelFileDescriptor? _tun;
    private FileOutputStream? _out;
    private CancellationTokenSource? _cts;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (DnsSettings.VpnRunning)
            return StartCommandResult.Sticky;

        ParcelFileDescriptor? tun;
        try
        {
            tun = new VpnService.Builder(this)
                .SetSession("Un0Tv DNS filter")
                .AddAddress(TunAddress, 32)
                .AddRoute(DnsAddress, 32)
                .AddDnsServer(DnsAddress)
                .Establish();
        }
        catch
        {
            tun = null;
        }

        if (tun is null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        _tun = tun;
        _cts = new CancellationTokenSource();
        DnsSettings.VpnRunning = true;

        var ct = _cts.Token;
        _ = Task.Run(() => ReadLoop(tun, ct));

        return StartCommandResult.Sticky;
    }

    // Reads every packet the VPN is sent and hands each one to a handler.
    private void ReadLoop(ParcelFileDescriptor tun, CancellationToken ct)
    {
        try
        {
            using var input = new FileInputStream(tun.FileDescriptor);
            _out = new FileOutputStream(tun.FileDescriptor);

            var buffer = new byte[32767];
            while (!ct.IsCancellationRequested)
            {
                var n = input.Read(buffer, 0, buffer.Length);
                if (n < 0)
                    break;
                if (n == 0)
                    continue;

                var packet = new byte[n];
                Buffer.BlockCopy(buffer, 0, packet, 0, n);
                _ = HandlePacketAsync(packet, ct);
            }
        }
        catch
        {
            // The VPN was closed; the loop simply ends.
        }
    }

    private async Task HandlePacketAsync(byte[] packet, CancellationToken ct)
    {
        try
        {
            if (!TryParse(packet, out var srcPort, out var query))
                return;

            var name = ReadName(query, out var questionEnd);
            if (name is null)
                return;

            var verdict = RequestFilter.Decide("https://" + name + "/");

            byte[] answer;
            if (verdict.Blocked)
            {
                answer = Answer(query, questionEnd, 3); // 3 = "no such name"
            }
            else
            {
                var forwarded = await ForwardAsync(query, ct);
                answer = forwarded ?? Answer(query, questionEnd, 2); // 2 = "try again later"
            }

            Send(BuildReply(srcPort, answer));
        }
        catch
        {
            // A bad or unexpected packet is dropped, never allowed to stop the VPN.
        }
    }

    // Checks the packet is an IPv4 UDP packet to our DNS address on port 53,
    // and gives back its DNS message and the port the app asked from.
    private static bool TryParse(byte[] p, out int srcPort, out byte[] dns)
    {
        srcPort = 0;
        dns = Array.Empty<byte>();

        if (p.Length < 28 || (p[0] >> 4) != 4)
            return false;

        var ihl = (p[0] & 0x0F) * 4;
        if (p[9] != 17 || p.Length < ihl + 8)
            return false;

        // Destination must be the DNS address, 10.0.0.1.
        if (p[16] != 10 || p[17] != 0 || p[18] != 0 || p[19] != 1)
            return false;

        srcPort = (p[ihl] << 8) | p[ihl + 1];
        var dstPort = (p[ihl + 2] << 8) | p[ihl + 3];
        if (dstPort != 53)
            return false;

        var udpLength = (p[ihl + 4] << 8) | p[ihl + 5];
        var start = ihl + 8;
        var length = Math.Min(udpLength - 8, p.Length - start);
        if (length <= 12)
            return false;

        dns = new byte[length];
        Buffer.BlockCopy(p, start, dns, 0, length);
        return true;
    }

    // Reads the name being looked up from the DNS question. Gives back the
    // offset just after the question, which is where an answer can be cut.
    private static string? ReadName(byte[] d, out int questionEnd)
    {
        questionEnd = -1;
        var sb = new StringBuilder();
        var i = 12;

        while (true)
        {
            if (i >= d.Length)
                return null;

            var len = d[i];
            if (len == 0)
            {
                i++;
                break;
            }

            // Compression pointers never appear in a question; refuse them.
            if ((len & 0xC0) != 0 || i + 1 + len > d.Length)
                return null;

            if (sb.Length > 0)
                sb.Append('.');
            for (var k = 0; k < len; k++)
                sb.Append((char)d[i + 1 + k]);

            i += len + 1;
        }

        if (i + 4 > d.Length || sb.Length == 0)
            return null;

        questionEnd = i + 4;
        return sb.ToString().ToLowerInvariant();
    }

    // A short answer with no records: the same question, marked as a reply.
    private static byte[] Answer(byte[] query, int questionEnd, int rcode)
    {
        var r = new byte[questionEnd];
        Buffer.BlockCopy(query, 0, r, 0, questionEnd);

        r[2] = (byte)(r[2] | 0x80);            // this is a reply
        r[3] = (byte)(0x80 | (rcode & 0x0F));   // recursion available + result code
        for (var i = 6; i < 12; i++)            // no answers, no authority, no extras
            r[i] = 0;

        return r;
    }

    // Sends the query to the encrypted provider and returns its answer.
    private static async Task<byte[]?> ForwardAsync(byte[] query, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Upstream)
            {
                Content = new ByteArrayContent(query)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

            using var resp = await Doh.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsByteArrayAsync(ct);
            return body.Length >= 12 ? body : null;
        }
        catch
        {
            return null;
        }
    }

    // Wraps a DNS answer in an IPv4 + UDP packet addressed back to the app.
    private static byte[] BuildReply(int srcPort, byte[] dns)
    {
        var total = 28 + dns.Length;
        var r = new byte[total];

        // IPv4 header
        r[0] = 0x45;
        r[2] = (byte)(total >> 8);
        r[3] = (byte)total;
        r[6] = 0x40;   // do not fragment
        r[8] = 64;     // time to live
        r[9] = 17;     // UDP
        r[12] = 10; r[13] = 0; r[14] = 0; r[15] = 1;   // from the DNS address
        r[16] = 10; r[17] = 0; r[18] = 0; r[19] = 2;   // back to the VPN address
        Checksum(r, 0, 20, 10);

        // UDP header: from port 53, back to the app's port
        r[20] = 0;
        r[21] = 53;
        r[22] = (byte)(srcPort >> 8);
        r[23] = (byte)srcPort;
        var udp = 8 + dns.Length;
        r[24] = (byte)(udp >> 8);
        r[25] = (byte)udp;

        Buffer.BlockCopy(dns, 0, r, 28, dns.Length);
        return r;
    }

    // The standard internet checksum, written into the header at "at".
    private static void Checksum(byte[] b, int off, int len, int at)
    {
        b[at] = 0;
        b[at + 1] = 0;

        long sum = 0;
        for (var i = 0; i < len; i += 2)
        {
            var word = (b[off + i] << 8) | (i + 1 < len ? b[off + i + 1] : 0);
            sum += word;
        }

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        var c = (ushort)(~sum & 0xFFFF);
        b[at] = (byte)(c >> 8);
        b[at + 1] = (byte)c;
    }

    private void Send(byte[] packet)
    {
        var o = _out;
        if (o is null)
            return;

        lock (WriteLock)
        {
            try { o.Write(packet, 0, packet.Length); }
            catch { }
        }
    }

    public override void OnRevoke()
    {
        Shutdown();
        base.OnRevoke();
    }

    public override void OnDestroy()
    {
        Shutdown();
        base.OnDestroy();
    }

    private void Shutdown()
    {
        DnsSettings.VpnRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _tun?.Close(); } catch { }
        _tun = null;
        _out = null;
    }
}
#endif
