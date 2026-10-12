namespace AudioPuck;

internal sealed class DeviceSettingsForm : Form
{
    private readonly ComboBox _connection = Choice("AUTO", "USB", "WIFI", "OFFLINE");
    private readonly CheckBox _automaticProfiles = Toggle("Automatic game profiles");
    private readonly ComboBox _volumeStep = Choice("1%", "2%", "4%", "5%", "10%");
    private readonly CheckBox _targetConfirm = Toggle("Confirm target selection with a click");
    private readonly ComboBox _trackPopup = Choice("OFF", "3 SEC", "5 SEC", "10 SEC");
    private readonly ComboBox _mediaTimeout = Choice("NEVER", "10 SEC", "30 SEC", "60 SEC");
    private readonly ComboBox _screenTimeout = Choice("NEVER", "10 SEC", "20 SEC", "30 SEC", "60 SEC", "120 SEC", "300 SEC");
    private readonly NumericUpDown _ledBrightness = new() { Minimum = 0, Maximum = 160, Increment = 8, Width = 120 };
    private readonly ComboBox _lightMode = Choice("VOLUME", "SOLID", "RAINBOW", "OFF");
    private readonly Button _solidColor = new() { Text = "#00AAFF", Width = 170, FlatStyle = FlatStyle.Flat };
    private readonly ComboBox _rainbowSpeed = Choice("SLOW", "NORMAL", "FAST");
    private readonly ComboBox _rainbowDirection = Choice("CLOCKWISE", "COUNTER-CLOCKWISE");
    private readonly ComboBox _rainbowSpread = Choice("TIGHT", "NORMAL", "WIDE", "MAX");
    private readonly Button _rainbowSettings = new() { Text = "Configure Rainbow…", AutoSize = true };
    private readonly FlowLayoutPanel _rainbowSection = new() { AutoSize = true, WrapContents = false };
    private readonly CheckBox _oledInvert = Toggle("Invert OLED colors");
    private readonly CheckBox _encoderReverse = Toggle("Reverse encoder direction");
    private readonly CheckBox _acceleration = Toggle("Accelerate fast turns");
    private readonly ComboBox _seekStep = Choice("5 SEC", "10 SEC", "15 SEC", "30 SEC");
    private readonly ComboBox _seekTimeout = Choice("3 SEC", "5 SEC", "10 SEC", "NEVER");
    private readonly ComboBox _seekHold = Choice("0.8 SEC", "1.2 SEC", "1.5 SEC", "2.0 SEC");
    private readonly CheckBox _batteryMonitor = Toggle("Enable dial battery measurement");
    private readonly NumericUpDown _batteryMin = Voltage();
    private readonly NumericUpDown _batteryMax = Voltage();
    private readonly ComboBox _batteryDisplay = Choice("OFF", "ICON", "PERCENT");
    private readonly ComboBox _laptopBattery = Choice("OFF", "ICON", "PERCENT");
    private readonly CheckBox _idleDashboard = Toggle("Show idle dashboard");
    private readonly CheckBox _showClock = Toggle("Show clock on idle dashboard");
    private readonly NumericUpDown _idleAfter = WholeNumber(5, 3600, 5);
    private readonly CheckBox _lowBatteryWarning = Toggle("Laptop low-battery warning");
    private readonly NumericUpDown _lowBattery = WholeNumber(2, 75, 1);
    private readonly NumericUpDown _criticalBattery = WholeNumber(1, 50, 1);
    private readonly NumericUpDown _repeatWarning = WholeNumber(0, 1440, 5);
    private readonly int _testBatteryPercent;
    private readonly bool _testPluggedIn;
    private readonly Label _deviceIp = new() { Text = "—", AutoSize = true };
    private readonly Button _save = new() { Text = "Apply settings", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { Text = "Reading device settings…", AutoSize = true, ForeColor = Color.DimGray };
    private readonly Action<PuckDeviceSettings> _saveAction;
    private readonly Action<BatteryConfig, HostStatusConfig> _companionSaveAction;
    private readonly Action _refreshAction;

    public DeviceSettingsForm(Action<PuckDeviceSettings> saveAction,
        Action<BatteryConfig, HostStatusConfig> companionSaveAction,
        Action refreshAction, Icon icon, BatteryConfig battery,
        HostStatusConfig host)
    {
        _saveAction = saveAction;
        _companionSaveAction = companionSaveAction;
        _refreshAction = refreshAction;
        _testBatteryPercent = host.TestBatteryPercent;
        _testPluggedIn = host.TestPluggedIn;
        Text = "SoundDial — Device Settings";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 780);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(20),
            ColumnCount = 2,
            RowCount = 31,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        AddRow(layout, 0, "Connection mode", _connection);
        AddWide(layout, 1, _automaticProfiles);
        AddRow(layout, 2, "Default volume step", _volumeStep);
        AddWide(layout, 3, _targetConfirm);
        AddRow(layout, 4, "New track popup", _trackPopup);
        AddRow(layout, 5, "Leave Media after", _mediaTimeout);
        AddRow(layout, 6, "Turn display off after", _screenTimeout);
        AddRow(layout, 7, "LED brightness", _ledBrightness);
        AddRow(layout, 8, "LED mode", _lightMode);
        AddRow(layout, 9, "Solid color", _solidColor);
        _rainbowSection.Controls.Add(new Label
        {
            Text = "Rainbow settings",
            AutoSize = true,
            Margin = new Padding(3, 8, 28, 3)
        });
        _rainbowSection.Controls.Add(_rainbowSettings);
        AddWide(layout, 10, _rainbowSection);
        AddWide(layout, 11, _oledInvert);
        AddWide(layout, 12, _encoderReverse);
        AddWide(layout, 13, _acceleration);
        AddRow(layout, 14, "Hold to enter Seek", _seekHold);
        AddRow(layout, 15, "Seek step", _seekStep);
        AddRow(layout, 16, "Leave Seek after", _seekTimeout);
        AddWide(layout, 17, _batteryMonitor);
        AddRow(layout, 18, "Dial empty voltage", _batteryMin);
        AddRow(layout, 19, "Dial full voltage", _batteryMax);
        AddRow(layout, 20, "Dial battery display", _batteryDisplay);
        AddRow(layout, 21, "Laptop battery display", _laptopBattery);
        AddWide(layout, 22, _idleDashboard);
        AddWide(layout, 23, _showClock);
        AddRow(layout, 24, "Idle dashboard after (sec)", _idleAfter);
        AddWide(layout, 25, _lowBatteryWarning);
        AddRow(layout, 26, "Laptop low warning (%)", _lowBattery);
        AddRow(layout, 27, "Laptop critical warning (%)", _criticalBattery);
        AddRow(layout, 28, "Repeat warning after (min)", _repeatWarning);
        AddRow(layout, 29, "Device IP", _deviceIp);

        var hint = new Label
        {
            Text = "Settings are stored inside the ESP32 and remain after power is disconnected.",
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(3, 10, 3, 8)
        };
        AddWide(layout, 30, hint);

        _batteryMin.Value = (decimal)battery.MinVoltage;
        _batteryMax.Value = (decimal)battery.MaxVoltage;
        _batteryDisplay.SelectedIndex = battery.Display switch
        {
            "off" => 0, "icon" => 1, _ => 2
        };
        _laptopBattery.SelectedIndex = host.BatteryDisplay switch
        {
            "icon" => 1, "percent" => 2, _ => 0
        };
        _idleDashboard.Checked = host.IdleDashboard;
        _showClock.Checked = host.ShowClock;
        _idleAfter.Value = Math.Clamp(host.IdleAfterSeconds, 5, 3600);
        _lowBatteryWarning.Checked = host.LowBatteryWarning;
        _lowBattery.Value = Math.Clamp(host.LowBatteryPercent, 2, 75);
        _criticalBattery.Value = Math.Clamp(host.CriticalBatteryPercent, 1, 50);
        _repeatWarning.Value = Math.Clamp(host.RepeatWarningMinutes, 0, 1440);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        var close = new Button { Text = "Close", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        close.Click += (_, _) => Close();
        refresh.Click += (_, _) => RequestRefresh();
        _save.Click += (_, _) => SaveSettings();
        _solidColor.Click += (_, _) => ChooseSolidColor();
        _rainbowSettings.Click += (_, _) => ConfigureRainbow();
        _lightMode.SelectedIndexChanged += (_, _) =>
            UpdateLightControls();
        UpdateLightControls();
        buttons.Controls.Add(close);
        buttons.Controls.Add(_save);
        buttons.Controls.Add(refresh);

        _status.MaximumSize = new Size(475, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(layout);
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(20, 8, 20, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(buttons, 0, 1);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(scroll, 0, 0);
        root.Controls.Add(footer, 0, 1);
        Controls.Add(root);
        Shown += (_, _) => RequestRefresh();
    }

    private void RequestRefresh()
    {
        _save.Enabled = false;
        _status.Text = "Reading device settings…";
        _refreshAction();
    }

    public void ApplySettings(PuckDeviceSettings value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplySettings(value));
            return;
        }
        _connection.SelectedIndex = Math.Clamp(value.LinkMode, 0, 3);
        _automaticProfiles.Checked = value.AutomaticProfiles;
        _volumeStep.SelectedIndex = Array.IndexOf(new[] { 1, 2, 4, 5, 10 }, value.VolumeStep) is var step && step >= 0 ? step : 2;
        _targetConfirm.Checked = value.TargetConfirm;
        _trackPopup.SelectedIndex = Array.IndexOf(new[] { 0, 3, 5, 10 }, value.TrackPopupSeconds) is var popup && popup >= 0 ? popup : 2;
        _mediaTimeout.SelectedIndex = Array.IndexOf(new[] { 0, 10, 30, 60 }, value.MediaTimeoutSeconds) is var media && media >= 0 ? media : 2;
        _screenTimeout.SelectedIndex = Array.IndexOf(new[] { 0, 10, 20, 30, 60, 120, 300 }, value.ScreenTimeoutSeconds) is var screen && screen >= 0 ? screen : 3;
        _ledBrightness.Value = Math.Clamp(value.LedBrightness, 0, 160);
        _lightMode.SelectedIndex = value.LightMode switch
        {
            0 => 0,
            1 => 1,
            3 => 2,
            _ => 3
        };
        SetSolidColor(Color.FromArgb(value.SolidRed, value.SolidGreen,
            value.SolidBlue));
        _rainbowSpeed.SelectedIndex = Math.Clamp(value.RainbowSpeed, 0, 2);
        _rainbowDirection.SelectedIndex = value.RainbowReverse ? 1 : 0;
        _rainbowSpread.SelectedIndex = Array.IndexOf(
            new[] { 24, 48, 64, 96 }, value.RainbowSpread) is var spread &&
            spread >= 0 ? spread : 2;
        UpdateLightControls();
        _oledInvert.Checked = value.OledInverted;
        _encoderReverse.Checked = value.EncoderReversed;
        _acceleration.Checked = value.Acceleration;
        _seekStep.SelectedIndex=Array.IndexOf(new[]{5,10,15,30},value.SeekStepSeconds) is var seekStep&&seekStep>=0?seekStep:0;
        _seekTimeout.SelectedIndex=Array.IndexOf(new[]{3,5,10,0},value.SeekTimeoutSeconds) is var seekExit&&seekExit>=0?seekExit:1;
        _seekHold.SelectedIndex=Array.IndexOf(new[]{800,1200,1500,2000},value.SeekHoldMs) is var seekHold&&seekHold>=0?seekHold:1;
        _batteryMonitor.Checked = value.BatteryMonitor;
        _deviceIp.Text = string.IsNullOrWhiteSpace(value.DeviceIp)
            ? "No Wi-Fi address"
            : value.DeviceIp;
        _save.Enabled = true;
        _status.Text = string.IsNullOrWhiteSpace(value.DeviceIp)
            ? "Device connected. Settings loaded."
            : $"Device connected at {value.DeviceIp}. Settings loaded.";
        _status.ForeColor = Color.DarkGreen;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }

    private void SaveSettings()
    {
        if (_batteryMax.Value - _batteryMin.Value < 0.30M)
        {
            _status.Text = "Dial full voltage must be at least 0.30 V above empty voltage.";
            _status.ForeColor = Color.Firebrick;
            return;
        }
        var steps = new[] { 1, 2, 4, 5, 10 };
        var popups = new[] { 0, 3, 5, 10 };
        var mediaTimeouts = new[] { 0, 10, 30, 60 };
        var screenTimeouts = new[] { 0, 10, 20, 30, 60, 120, 300 };
        var lightModes = new[] { 0, 1, 3, 2 };
        var rainbowSpreads = new[] { 24, 48, 64, 96 };
        var seekSteps = new[] { 5, 10, 15, 30 };
        var seekTimeouts = new[] { 3, 5, 10, 0 };
        var seekHolds = new[] { 800, 1200, 1500, 2000 };
        _saveAction(new PuckDeviceSettings(
            Math.Max(0, _connection.SelectedIndex),
            steps[Math.Max(0, _volumeStep.SelectedIndex)],
            _targetConfirm.Checked,
            popups[Math.Max(0, _trackPopup.SelectedIndex)],
            mediaTimeouts[Math.Max(0, _mediaTimeout.SelectedIndex)],
            screenTimeouts[Math.Max(0, _screenTimeout.SelectedIndex)],
            (int)_ledBrightness.Value,
            lightModes[Math.Max(0, _lightMode.SelectedIndex)],
            _oledInvert.Checked,
            _encoderReverse.Checked,
            _acceleration.Checked,
            _solidColor.BackColor.R,
            _solidColor.BackColor.G,
            _solidColor.BackColor.B,
            _automaticProfiles.Checked,
            Math.Max(0, _rainbowSpeed.SelectedIndex),
            _rainbowDirection.SelectedIndex == 1,
            rainbowSpreads[Math.Max(0, _rainbowSpread.SelectedIndex)],
            string.Empty,
            _batteryMonitor.Checked,
            seekSteps[Math.Max(0, _seekStep.SelectedIndex)],
            seekTimeouts[Math.Max(0, _seekTimeout.SelectedIndex)],
            seekHolds[Math.Max(0, _seekHold.SelectedIndex)]));
        var low = (int)_lowBattery.Value;
        var critical = Math.Min((int)_criticalBattery.Value, low - 1);
        _companionSaveAction(new BatteryConfig
        {
            MinVoltage = (double)_batteryMin.Value,
            MaxVoltage = (double)_batteryMax.Value,
            Display = new[] { "off", "icon", "percent" }[
                Math.Max(0, _batteryDisplay.SelectedIndex)]
        }, new HostStatusConfig
        {
            BatteryDisplay = new[] { "off", "icon", "percent" }[
                Math.Max(0, _laptopBattery.SelectedIndex)],
            IdleDashboard = _idleDashboard.Checked,
            ShowClock = _showClock.Checked,
            IdleAfterSeconds = (int)_idleAfter.Value,
            LowBatteryWarning = _lowBatteryWarning.Checked,
            LowBatteryPercent = low,
            CriticalBatteryPercent = critical,
            RepeatWarningMinutes = (int)_repeatWarning.Value,
            TestBatteryPercent = _testBatteryPercent,
            TestPluggedIn = _testPluggedIn
        });
        _status.Text = "Sending and saving settings…";
        _status.ForeColor = Color.DarkOrange;
        _save.Enabled = false;
    }

    private void ChooseSolidColor()
    {
        using var dialog = new ColorDialog
        {
            Color = _solidColor.BackColor,
            FullOpen = true,
            AnyColor = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetSolidColor(dialog.Color);
    }

    private void SetSolidColor(Color color)
    {
        _solidColor.BackColor = color;
        _solidColor.ForeColor = color.GetBrightness() > 0.55f
            ? Color.Black
            : Color.White;
        _solidColor.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        UpdateLightControls();
    }

    private void UpdateLightControls()
    {
        var solid = _lightMode.SelectedIndex == 1;
        var rainbow = _lightMode.SelectedIndex == 2;
        _solidColor.Enabled = solid;
        _rainbowSection.Visible = rainbow;
        _rainbowSettings.Text = rainbow
            ? $"Configure Rainbow…  ({_rainbowSpeed.Text}, {_rainbowDirection.Text}, {_rainbowSpread.Text})"
            : "Configure Rainbow…";
    }

    private void ConfigureRainbow()
    {
        using var dialog = new Form
        {
            Text = "SoundDial — Rainbow",
            Icon = Icon is null ? null : (Icon)Icon.Clone(),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(430, 235),
            Font = Font,
            AutoScaleMode = AutoScaleMode.Dpi
        };
        var speed = Choice("SLOW", "NORMAL", "FAST");
        var direction = Choice("CLOCKWISE", "COUNTER-CLOCKWISE");
        var spread = Choice("TIGHT", "NORMAL", "WIDE", "MAX");
        speed.SelectedIndex = Math.Max(0, _rainbowSpeed.SelectedIndex);
        direction.SelectedIndex = Math.Max(0, _rainbowDirection.SelectedIndex);
        spread.SelectedIndex = Math.Max(0, _rainbowSpread.SelectedIndex);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        AddRow(layout, 0, "Speed", speed);
        AddRow(layout, 1, "Direction", direction);
        AddRow(layout, 2, "Color spread", spread);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        var ok = new Button { Text = "Apply", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AddWide(layout, 3, buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Controls.Add(layout);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _rainbowSpeed.SelectedIndex = speed.SelectedIndex;
        _rainbowDirection.SelectedIndex = direction.SelectedIndex;
        _rainbowSpread.SelectedIndex = spread.SelectedIndex;
        UpdateLightControls();
    }

    public void ShowNoResponse(bool transportConnected)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowNoResponse(transportConnected));
            return;
        }
        _status.Text = transportConnected
            ? "SoundDial is connected, but its firmware does not support PC settings. Flash the latest firmware, then click Refresh."
            : "SoundDial is offline. Check USB or Wi-Fi, then click Refresh.";
        _status.MaximumSize = new Size(450, 0);
        _status.ForeColor = Color.Firebrick;
        _save.Enabled = false;
    }

    private static ComboBox Choice(params string[] values)
    {
        var result = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        result.Items.AddRange(values);
        result.SelectedIndex = 0;
        return result;
    }

    private static NumericUpDown Voltage() => new()
    {
        Minimum = 2.50M, Maximum = 4.50M, DecimalPlaces = 2,
        Increment = 0.01M, Width = 170
    };

    private static NumericUpDown WholeNumber(int minimum, int maximum,
        int increment) => new()
    {
        Minimum = minimum, Maximum = maximum, Increment = increment,
        Width = 170
    };

    private static CheckBox Toggle(string text) => new() { Text = text, AutoSize = true };

    private static void AddRow(TableLayoutPanel layout, int row, string label,
        Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, row);
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(control, 1, row);
    }

    private static void AddWide(TableLayoutPanel layout, int row, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 7, 3, 7);
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }
}
