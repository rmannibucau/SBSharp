namespace SBSharp.Tests.Temp;

public class TempFolder : IDisposable
{
    public string Value { get; private set; }

    public TempFolder(string marker)
    {
        Value = Path.Combine(
            Path.GetTempPath(),
            $"{marker}_{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()}"
        );
        Directory.CreateDirectory(Value);
    }

    public void Dispose()
    {
        if (Value is not null)
        {
            Directory.Delete(Value, true);
        }
    }
}
