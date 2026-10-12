namespace AudioPuck;

internal static class SelfTests
{
    public static int Run(string configPath)
    {
        try
        {
            var config = ConfigLoader.Load(configPath);
            if (config.Profiles.Count == 0) throw new Exception("No profiles loaded");
            if (PuckProtocol.ParseEvent("MEDIA_SEEK\t-15") is not
                { Kind: PuckEventKind.MediaSeek, Value: -15 })
                throw new Exception("MEDIA_SEEK parser failed");
            var encoded = PuckProtocol.Encode("Тест / Linux");
            if (PuckProtocol.Decode(encoded) != "Тест / Linux")
                throw new Exception("Protocol text roundtrip failed");
            var timeline = PuckProtocol.MediaTimeline(new(true,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), "FIREFOX"), true);
            if (!timeline.StartsWith("MEDIA_TIMELINE\t1\t10\t100\t"))
                throw new Exception("Timeline packet failed");
            Console.WriteLine("SoundDial Linux self-tests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SoundDial Linux self-tests: FAIL: {ex.Message}");
            return 1;
        }
    }
}
