using System.Security.Cryptography;
using System.Text;

namespace Pedia.Core.Importing;

public sealed class ImportPreviewService
{
    private readonly long _maximumFileBytes;

    public ImportPreviewService(long maximumFileBytes = 16 * 1024 * 1024)
    {
        if (maximumFileBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        _maximumFileBytes = maximumFileBytes;
    }

    public async Task<ImportPreview> PreviewAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The import source file was not found.", fullPath);
        }

        var format = GetFormat(fileInfo.Extension);
        if (fileInfo.Length > _maximumFileBytes)
        {
            throw new InvalidDataException($"The import source exceeds the {_maximumFileBytes} byte limit.");
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (bytes.LongLength > _maximumFileBytes)
        {
            throw new InvalidDataException($"The import source exceeds the {_maximumFileBytes} byte limit.");
        }

        var content = DecodeText(bytes);
        var fallbackTitle = Path.GetFileNameWithoutExtension(fileInfo.Name);
        var document = format switch
        {
            ImportFileFormat.PlainText => TextDocumentParser.Parse(content, fallbackTitle),
            ImportFileFormat.Markdown => MarkdownDocumentParser.Parse(content, fallbackTitle),
            _ => throw new InvalidDataException("Unsupported import format.")
        };
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var source = new LocalFileSourceMetadata(
            fullPath,
            fileInfo.Name,
            format,
            bytes.LongLength,
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
            hash);
        return new ImportPreview(document, source);
    }

    private static ImportFileFormat GetFormat(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => ImportFileFormat.PlainText,
        ".md" or ".markdown" => ImportFileFormat.Markdown,
        _ => throw new NotSupportedException($"Unsupported import file extension '{extension}'.")
    };

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true)
                .GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            return Encoding.Unicode.GetString(bytes, Encoding.Unicode.Preamble.Length, bytes.Length - Encoding.Unicode.Preamble.Length);
        }

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, Encoding.BigEndianUnicode.Preamble.Length, bytes.Length - Encoding.BigEndianUnicode.Preamble.Length);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }
}
