using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioPuck;

internal sealed class NativeSerialPort : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly object _writeGate = new();
    public string PortName { get; }

    private NativeSerialPort(string portName, SafeFileHandle handle)
    {
        PortName = portName;
        _handle = handle;
    }

    public static NativeSerialPort Open(string portName, int baudRate)
    {
        var handle = CreateFile($"\\\\.\\{portName}",
            GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting,
            FileAttributeNormal, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Cannot open {portName}",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));

        var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
        if (!GetCommState(handle, ref dcb))
        {
            handle.Dispose();
            throw new IOException($"GetCommState failed for {portName}");
        }
        dcb.BaudRate = (uint)baudRate;
        dcb.Flags = 1;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        if (!SetCommState(handle, ref dcb))
        {
            handle.Dispose();
            throw new IOException($"SetCommState failed for {portName}");
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 25,
            ReadTotalTimeoutConstant = 100,
            ReadTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 500,
            WriteTotalTimeoutMultiplier = 0
        };
        SetCommTimeouts(handle, ref timeouts);
        PurgeComm(handle, PurgeRxClear | PurgeTxClear);
        return new NativeSerialPort(portName, handle);
    }

    public int Read(byte[] buffer)
    {
        if (_handle.IsClosed) return 0;
        if (!ReadFile(_handle, buffer, buffer.Length, out var read, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 995) return 0; // cancelled during an intentional shutdown
            throw new IOException($"Serial read failed ({error})");
        }
        return read;
    }

    public void WriteLine(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_writeGate)
        {
            if (_handle.IsClosed) return;
            if (!WriteFile(_handle, bytes, bytes.Length, out var written,
                    IntPtr.Zero) || written != bytes.Length)
                throw new IOException("Serial write failed");
        }
    }

    public static IReadOnlyList<string> EnumeratePortNames()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is not null)
        {
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string port && port.StartsWith("COM",
                        StringComparison.OrdinalIgnoreCase))
                    ports.Add(port);
            }
        }
        return ports.OrderBy(PortNumber).ToList();
    }

    private static int PortNumber(string port) =>
        int.TryParse(port.AsSpan(3), out var number) ? number : int.MaxValue;

    public void Dispose() => _handle.Dispose();

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint PurgeTxClear = 0x0004;
    private const uint PurgeRxClear = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLimit;
        public ushort XoffLimit;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName,
        uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle file,
        ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(SafeFileHandle file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer,
        int bytesToRead, out int bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer,
        int bytesToWrite, out int bytesWritten, IntPtr overlapped);
}
