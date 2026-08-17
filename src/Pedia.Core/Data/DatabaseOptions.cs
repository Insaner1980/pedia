namespace Pedia.Core.Data;

public sealed record DatabaseOptions(string DatabasePath, int BusyTimeoutMilliseconds = 5_000)
{
    public static DatabaseOptions CreateDefault()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pedia",
            "Data");
        return new DatabaseOptions(Path.Combine(dataDirectory, "pedia.db"));
    }
}
