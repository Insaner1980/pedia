using Microsoft.Windows.ApplicationModel.Resources;

namespace Pedia.Services;

public interface IStringService
{
    string Get(string key);
    string Format(string key, params object?[] args);
    string OperationFailed => Get("OperationFailedMessage");
}

public sealed class StringService : IStringService
{
    private readonly ResourceLoader _resourceLoader = new();

    public string Get(string key) => _resourceLoader.GetString(key);

    public string Format(string key, params object?[] args) => string.Format(Get(key), args);

    public string OperationFailed => Get("OperationFailedMessage");
}
