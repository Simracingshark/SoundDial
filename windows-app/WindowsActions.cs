using System.Runtime.InteropServices;

namespace AudioPuck;

internal static class WindowsActions
{
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode,
        uint flags, UIntPtr extraInfo);

    public static void MediaPlayPause() => Press(VkMediaPlayPause);
    public static void MediaNext() => Press(VkMediaNextTrack);
    public static void MediaPrevious() => Press(VkMediaPrevTrack);

    private static void Press(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyeventfKeyup, UIntPtr.Zero);
    }
}

internal sealed class DefaultAudioDeviceSwitcher
{
    public bool TrySetDefault(string endpointId)
    {
        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            CoreAudioService.ThrowIfFailed(
                policy.SetDefaultEndpoint(endpointId, Role.Console));
            CoreAudioService.ThrowIfFailed(
                policy.SetDefaultEndpoint(endpointId, Role.Multimedia));
            CoreAudioService.ThrowIfFailed(
                policy.SetDefaultEndpoint(endpointId, Role.Communications));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Cannot switch default audio endpoint", ex);
            return false;
        }
        finally
        {
            if (policy is not null && Marshal.IsComObject(policy))
            {
                try { Marshal.FinalReleaseComObject(policy); } catch { }
            }
        }
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            out IntPtr format);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, out IntPtr format);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod,
            out long defaultPeriodValue, out long minimumPeriodValue);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            ref long period);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr mode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool store, ref PropertyKey key,
            out PropVariant value);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool store, ref PropertyKey key,
            ref PropVariant value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            Role role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
