using System.Runtime.InteropServices;
using System.Text;

namespace AudioPuck;

internal sealed class NativeSerialPort : IDisposable
{
    private const int ORdwr = 2;
    private const int ONoctty = 0x100;
    private const int ONonblock = 0x800;
    private readonly object _writeGate = new();
    private int _fd;
    public string PortName { get; }

    private NativeSerialPort(string portName, int fd)
    { PortName = portName; _fd = fd; }

    public static NativeSerialPort Open(string portName, int baudRate)
    {
        var fd = open(portName, ORdwr | ONoctty | ONonblock);
        if (fd < 0) throw new IOException($"Cannot open {portName}: errno {Marshal.GetLastPInvokeError()}");
        var configure = ProcessRunner.Run("stty", new[] { "-F", portName,
            baudRate.ToString(), "raw", "-echo" });
        if (!configure.Success)
        {
            close(fd);
            throw new IOException($"Cannot configure {portName}: {configure.Error.Trim()}");
        }
        return new(portName, fd);
    }

    public int Read(byte[] buffer)
    {
        if (_fd < 0) return 0;
        var count = read(_fd, buffer, (nuint)buffer.Length);
        if (count >= 0) return (int)count;
        var error = Marshal.GetLastPInvokeError();
        if (error is 11 or 35) { Thread.Sleep(15); return 0; }
        throw new IOException($"Serial read failed: errno {error}");
    }

    public void WriteLine(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_writeGate)
        {
            if (_fd < 0) return;
            var written = write(_fd, bytes, (nuint)bytes.Length);
            if (written != bytes.Length) throw new IOException("Serial write failed");
        }
    }

    public static IReadOnlyList<string> EnumeratePortNames()
    {
        var result = new List<string>();
        Add("/dev/serial/by-id", "*", result);
        Add("/dev", "ttyACM*", result);
        Add("/dev", "ttyUSB*", result);
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Add(string directory, string pattern, List<string> result)
    {
        try { if (Directory.Exists(directory)) result.AddRange(
            Directory.EnumerateFiles(directory, pattern).OrderBy(x => x)); } catch { }
    }

    public void Dispose()
    {
        var fd = Interlocked.Exchange(ref _fd, -1);
        if (fd >= 0) close(fd);
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
}

internal sealed class SerialTransport : IDisposable
{
    private readonly TransportHub _hub;
    private readonly string _configuredPort;
    private readonly int _baudRate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _scanner;
    private SerialPuckConnection? _active;

    public SerialTransport(TransportHub hub, TransportConfig config)
    {
        _hub = hub; _configuredPort = config.SerialPort;
        _baudRate = config.SerialBaud; _scanner = Task.Run(ScanLoop);
    }

    private async Task ScanLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_active?.IsConnected != true)
                {
                    foreach (var port in CandidatePorts())
                    {
                        NativeSerialPort? serial = null;
                        try
                        {
                            serial = NativeSerialPort.Open(port, _baudRate);
                            serial.WriteLine("PING");
                            if (!WaitForPuck(serial, TimeSpan.FromMilliseconds(1200)))
                            { serial.Dispose(); continue; }
                            var connection = new SerialPuckConnection(serial);
                            serial = null; _active = connection;
                            connection.Closed += _ => _active = null;
                            _hub.Attach(connection); break;
                        }
                        catch (Exception ex)
                        { Log.Debug($"Serial probe {port}: {ex.Message}"); serial?.Dispose(); }
                    }
                }
                await Task.Delay(1800, _stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("Serial scanner failed", ex); }
        }
    }

    private IEnumerable<string> CandidatePorts() =>
        _configuredPort.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? NativeSerialPort.EnumeratePortNames() : new[] { _configuredPort };

    private static bool WaitForPuck(NativeSerialPort serial, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout; var bytes = new byte[128];
        var text = new StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            var count = serial.Read(bytes);
            if (count <= 0) continue;
            text.Append(Encoding.UTF8.GetString(bytes, 0, count));
            if (text.ToString().Contains("HELLO\tSOUNDDIAL",
                    StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void Dispose()
    { _stop.Cancel(); _active?.Dispose(); try { _scanner.Wait(1500); } catch { } _stop.Dispose(); }
}

internal sealed class SerialPuckConnection : IPuckConnection
{
    private readonly NativeSerialPort _serial;
    private readonly CancellationTokenSource _stop = new();
    private Task? _reader; private int _closed;
    public SerialPuckConnection(NativeSerialPort serial) => _serial = serial;
    public ConnectionKind Kind => ConnectionKind.Usb;
    public string Description => $"USB {_serial.PortName}";
    public bool IsConnected => Volatile.Read(ref _closed) == 0;
    public event Action<IPuckConnection, string>? LineReceived;
    public event Action<IPuckConnection>? Closed;
    public void Start() { _reader ??= Task.Run(ReadLoop); Send("LINK\tUSB"); }
    public void Send(string line) { if (IsConnected) _serial.WriteLine(line); }
    private void ReadLoop()
    {
        var bytes = new byte[256]; var line = new List<byte>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var count = _serial.Read(bytes);
                for (var i = 0; i < count; i++)
                    if (bytes[i] == (byte)'\n')
                    { var text = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'); line.Clear();
                      if (text.Length > 0) LineReceived?.Invoke(this, text); }
                    else if (line.Count < 1024) line.Add(bytes[i]); else line.Clear();
            }
        }
        catch (Exception ex) { Log.Debug($"USB connection ended: {ex.Message}"); }
        finally { SignalClosed(); }
    }
    private void SignalClosed()
    { if (Interlocked.Exchange(ref _closed, 1) != 0) return; _serial.Dispose(); Closed?.Invoke(this); }
    public void Dispose() { _stop.Cancel(); SignalClosed(); _stop.Dispose(); }
}
