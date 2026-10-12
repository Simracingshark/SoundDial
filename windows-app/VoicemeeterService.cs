using System.Runtime.InteropServices;

namespace AudioPuck;

internal sealed class VoicemeeterService : IDisposable
{
    private IntPtr _library;
    private LoginDelegate? _login;
    private LogoutDelegate? _logout;
    private GetParameterFloatDelegate? _getFloat;
    private SetParameterFloatDelegate? _setFloat;
    private IsParametersDirtyDelegate? _isDirty;
    private bool _loggedIn;
    private DateTime _lastLoadAttempt;

    public bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            if (_library == IntPtr.Zero || _login is null) return false;
            if (_loggedIn) return true;
            try
            {
                var result = _login();
                _loggedIn = result >= 0;
                return _loggedIn;
            }
            catch (Exception ex)
            {
                Log.Debug($"Voicemeeter login failed: {ex.Message}");
                return false;
            }
        }
    }

    public bool TryGet(string parameter, out float value)
    {
        value = 0;
        if (!IsAvailable || _getFloat is null) return false;
        try
        {
            return _getFloat(parameter, out value) == 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"Voicemeeter read '{parameter}' failed: {ex.Message}");
            _loggedIn = false;
            return false;
        }
    }

    public bool TrySet(string parameter, float value)
    {
        if (!IsAvailable || _setFloat is null) return false;
        try
        {
            return _setFloat(parameter, value) == 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"Voicemeeter write '{parameter}' failed: {ex.Message}");
            _loggedIn = false;
            return false;
        }
    }

    public void Poll()
    {
        if (!IsAvailable || _isDirty is null) return;
        try { _isDirty(); } catch { _loggedIn = false; }
    }

    private void EnsureLoaded()
    {
        if (_library != IntPtr.Zero) return;
        if (DateTime.UtcNow - _lastLoadAttempt < TimeSpan.FromSeconds(10)) return;
        _lastLoadAttempt = DateTime.UtcNow;

        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path)) continue;
                _library = NativeLibrary.Load(path);
                _login = Load<LoginDelegate>("VBVMR_Login");
                _logout = Load<LogoutDelegate>("VBVMR_Logout");
                _getFloat = Load<GetParameterFloatDelegate>("VBVMR_GetParameterFloat");
                _setFloat = Load<SetParameterFloatDelegate>("VBVMR_SetParameterFloat");
                _isDirty = Load<IsParametersDirtyDelegate>("VBVMR_IsParametersDirty");
                Log.Info($"Voicemeeter Remote API loaded from {path}");
                return;
            }
            catch (Exception ex)
            {
                Log.Debug($"Cannot load Voicemeeter API from {path}: {ex.Message}");
                if (_library != IntPtr.Zero)
                {
                    NativeLibrary.Free(_library);
                    _library = IntPtr.Zero;
                }
            }
        }
    }

    private T Load<T>(string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, export));

    private static IEnumerable<string> CandidatePaths()
    {
        var explicitPath = Environment.GetEnvironmentVariable("VOICEMEETER_REMOTE_DLL");
        if (!string.IsNullOrWhiteSpace(explicitPath)) yield return explicitPath;

        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);

        yield return Path.Combine(programFilesX86, "VB", "Voicemeeter",
            Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll");
        yield return Path.Combine(programFiles, "VB", "Voicemeeter",
            Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll");
        yield return Path.Combine(AppContext.BaseDirectory,
            Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll");
    }

    public void Dispose()
    {
        if (_loggedIn)
        {
            try { _logout?.Invoke(); } catch { }
        }
        _loggedIn = false;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LoginDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LogoutDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int GetParameterFloatDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string parameter,
        out float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int SetParameterFloatDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string parameter,
        float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IsParametersDirtyDelegate();
}
