using Microsoft.Extensions.Logging;

namespace Pedia.Services;

public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private StreamWriter? _writer;

    public LocalFileLoggerProvider()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pedia",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"pedia-{DateTime.Now:yyyy-MM-dd}.log");
    }

    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        lock (_sync)
        {
            _writer ??= new StreamWriter(new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };

            _writer.Write(DateTimeOffset.UtcNow.ToString("O"));
            _writer.Write(" [");
            _writer.Write(level);
            _writer.Write("] ");
            _writer.Write(category);
            _writer.Write(": ");
            _writer.WriteLine(message);
            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }

    private sealed class LocalFileLogger(LocalFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
