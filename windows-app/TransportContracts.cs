namespace AudioPuck;

internal interface IPuckConnection : IDisposable
{
    ConnectionKind Kind { get; }
    string Description { get; }
    bool IsConnected { get; }
    event Action<IPuckConnection, string>? LineReceived;
    event Action<IPuckConnection>? Closed;
    void Start();
    void Send(string line);
}

internal sealed class TransportHub : IDisposable
{
    private readonly object _gate = new();
    private IPuckConnection? _usb;
    private IPuckConnection? _wifi;

    public event Action<PuckEvent>? EventReceived;
    public event Action? ConnectionChanged;

    public ConnectionKind ActiveKind
    {
        get
        {
            lock (_gate)
            {
                if (_usb?.IsConnected == true) return ConnectionKind.Usb;
                if (_wifi?.IsConnected == true) return ConnectionKind.Wifi;
                return ConnectionKind.None;
            }
        }
    }

    public string Description
    {
        get
        {
            lock (_gate)
            {
                return ActiveKind switch
                {
                    ConnectionKind.Usb => _usb?.Description ?? "USB",
                    ConnectionKind.Wifi => _wifi?.Description ?? "WiFi",
                    _ => "Disconnected"
                };
            }
        }
    }

    public void Attach(IPuckConnection connection)
    {
        lock (_gate)
        {
            var previous = connection.Kind == ConnectionKind.Usb ? _usb : _wifi;
            if (previous is not null && !ReferenceEquals(previous, connection))
            {
                previous.Dispose();
            }
            if (connection.Kind == ConnectionKind.Usb) _usb = connection;
            else _wifi = connection;
            connection.LineReceived += OnLine;
            connection.Closed += OnClosed;
            connection.Start();
        }
        Log.Info($"Controller connected through {connection.Description}");
        ConnectionChanged?.Invoke();
    }

    public void Send(string line)
    {
        IPuckConnection? usb;
        IPuckConnection? wifi;
        lock (_gate)
        {
            usb = _usb?.IsConnected == true ? _usb : null;
            wifi = _wifi?.IsConnected == true ? _wifi : null;
        }
        try { usb?.Send(line); }
        catch (Exception ex) { Log.Debug($"USB send failed: {ex.Message}"); }
        try
        {
            if (!ReferenceEquals(usb, wifi)) wifi?.Send(line);
        }
        catch (Exception ex) { Log.Debug($"WiFi send failed: {ex.Message}"); }
    }

    private void OnLine(IPuckConnection connection, string line)
    {
        var parsed = PuckProtocol.ParseEvent(line);
        if (parsed.Kind != PuckEventKind.Unknown)
            EventReceived?.Invoke(parsed);
        else
            Log.Debug($"Unknown controller line: {line}");
    }

    private void OnClosed(IPuckConnection connection)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_usb, connection)) _usb = null;
            if (ReferenceEquals(_wifi, connection)) _wifi = null;
        }
        Log.Info($"Controller disconnected: {connection.Description}");
        ConnectionChanged?.Invoke();
    }

    public void Dispose()
    {
        IPuckConnection? usb;
        IPuckConnection? wifi;
        lock (_gate)
        {
            usb = _usb;
            wifi = _wifi;
            _usb = null;
            _wifi = null;
            if (usb is not null)
            {
                usb.LineReceived -= OnLine;
                usb.Closed -= OnClosed;
            }
            if (wifi is not null)
            {
                wifi.LineReceived -= OnLine;
                wifi.Closed -= OnClosed;
            }
        }
        usb?.Dispose();
        if (!ReferenceEquals(usb, wifi)) wifi?.Dispose();
    }
}
