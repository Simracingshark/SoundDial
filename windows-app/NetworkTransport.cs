using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AudioPuck;

internal sealed class NetworkTransport : IDisposable
{
    private readonly TransportHub _hub;
    private readonly TransportConfig _config;
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener;
    private readonly UdpClient _discovery;
    private readonly Task _acceptTask;
    private readonly Task _discoveryTask;

    public NetworkTransport(TransportHub hub, TransportConfig config)
    {
        _hub = hub;
        _config = config;
        _listener = new TcpListener(IPAddress.Any, config.TcpPort);
        _listener.Start();
        _discovery = new UdpClient(config.DiscoveryPort);
        _acceptTask = Task.Run(AcceptLoop);
        _discoveryTask = Task.Run(DiscoveryLoop);
        Log.Info($"WiFi transport listening on UDP {config.DiscoveryPort}, TCP {config.TcpPort}");
    }

    private async Task DiscoveryLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var packet = await _discovery.ReceiveAsync(_stop.Token);
                var text = Encoding.UTF8.GetString(packet.Buffer).Trim();
                var parts = text.Split('\t');
                if (parts.Length < 3 || parts[0] != "SOUNDDIAL_DISCOVER") continue;
                if (!ConstantTimeEquals(parts[^1], _config.PairingToken))
                {
                    Log.Warn($"Rejected SoundDial discovery from {packet.RemoteEndPoint}: wrong token");
                    continue;
                }
                var reply = Encoding.UTF8.GetBytes(
                    $"SOUNDDIAL_HOST\t{PuckProtocol.Version}\t{_config.TcpPort}");
                await _discovery.SendAsync(reply, packet.RemoteEndPoint, _stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested)
                    Log.Error("WiFi discovery failed", ex);
            }
        }
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => AuthenticateAndAttach(client));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested)
                    Log.Error("WiFi accept failed", ex);
            }
        }
    }

    private async Task AuthenticateAndAttach(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token, _stop.Token);
            var stream = client.GetStream();
            var hello = await ReadLine(stream, linked.Token);
            var parts = hello.Split('\t');
            if (parts.Length < 4 || parts[0] != "HELLO" ||
                parts[1] != "SOUNDDIAL" ||
                !ConstantTimeEquals(parts[3], _config.PairingToken))
            {
                client.Dispose();
                Log.Warn("Rejected unauthenticated WiFi controller connection");
                return;
            }
            _hub.Attach(new TcpPuckConnection(client));
        }
        catch (Exception ex)
        {
            client.Dispose();
            Log.Debug($"WiFi handshake failed: {ex.Message}");
        }
    }

    private static async Task<string> ReadLine(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 1024)
        {
            var read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            if (one[0] == (byte)'\n')
                return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(one[0]);
        }
        throw new InvalidDataException("Protocol line is too long");
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        if (a.Length == 0 || b.Length == 0)
            return a.Length == b.Length;
        var difference = a.Length ^ b.Length;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            difference |= a[i % a.Length] ^ b[i % b.Length];
        return difference == 0;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _discovery.Dispose();
        try { Task.WaitAll(new[] { _acceptTask, _discoveryTask }, 1500); } catch { }
        _stop.Dispose();
    }
}

internal sealed class TcpPuckConnection : IPuckConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly string _description;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _reader;
    private int _closed;

    public TcpPuckConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _description = $"WiFi {client.Client.RemoteEndPoint}";
    }

    public ConnectionKind Kind => ConnectionKind.Wifi;
    public string Description => _description;
    public bool IsConnected => Volatile.Read(ref _closed) == 0 && _client.Connected;
    public event Action<IPuckConnection, string>? LineReceived;
    public event Action<IPuckConnection>? Closed;

    public void Start()
    {
        _reader ??= Task.Run(ReadLoop);
        Send("LINK\tWIFI");
    }

    public void Send(string line)
    {
        if (!IsConnected) return;
        _ = SendAsync(line);
    }

    private async Task SendAsync(string line)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _writeGate.WaitAsync(_stop.Token);
            try { await _stream.WriteAsync(bytes, _stop.Token); }
            finally { _writeGate.Release(); }
        }
        catch { SignalClosed(); }
    }

    private async Task ReadLoop()
    {
        var buffer = new byte[512];
        var line = new List<byte>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var count = await _stream.ReadAsync(buffer, _stop.Token);
                if (count == 0) break;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        var text = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
                        line.Clear();
                        if (text.Length > 0) LineReceived?.Invoke(this, text);
                    }
                    else if (line.Count < 1024) line.Add(buffer[i]);
                    else line.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug($"WiFi connection ended: {ex.Message}"); }
        finally { SignalClosed(); }
    }

    private void SignalClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _client.Dispose(); } catch { }
        Closed?.Invoke(this);
    }

    public void Dispose()
    {
        _stop.Cancel();
        SignalClosed();
        _writeGate.Dispose();
        _stop.Dispose();
    }
}
