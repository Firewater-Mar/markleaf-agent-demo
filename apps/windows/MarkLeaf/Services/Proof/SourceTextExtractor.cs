using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MarkLeaf.Services.Proof;

internal static partial class SourceTextExtractor
{
    private const int MaxCharacters = 1_500_000;
    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".txt", ".csv", ".tsv", ".json", ".yaml", ".yml", ".xml", ".html", ".htm",
    };

    public static async Task<(string Text, string Status)> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到资料文件。", path);
        var extension = Path.GetExtension(path);
        if (PlainTextExtensions.Contains(extension))
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return (Limit(text), "已建立文本索引");
        }
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return (Limit(ExtractDocx(path)), "已从 Word 建立索引");
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var text = await ExtractPdfAsync(path, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text)
                ? (string.Empty, "PDF 无可读文本层，建议先进行 OCR")
                : (Limit(text), "已从 PDF 文本层建立索引");
        }
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
            return (string.Empty, "图片已登记，当前版本需先用 OCR 转为文本");
        return (string.Empty, "已登记文件，暂不支持提取此格式");
    }

    public static string DetectKind(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "PDF",
            ".docx" => "Word",
            ".md" or ".markdown" => "Markdown",
            ".txt" => "文本",
            ".csv" or ".tsv" => "数据",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => "图片",
            _ => "文档",
        };

    private static string ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("Word 文档缺少 document.xml。文件可能已损坏。");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = document.Descendants(word + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(node => node.Value)))
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(Environment.NewLine, paragraphs);
    }

    private static async Task<string> ExtractPdfAsync(string path, CancellationToken cancellationToken)
    {
        var external = await TryRunPdfToTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(external)) return external;

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var raw = Encoding.Latin1.GetString(bytes);
        var builder = new StringBuilder();
        foreach (Match match in PdfLiteralTextRegex().Matches(raw))
        {
            var value = DecodePdfLiteral(match.Groups[1].Value);
            if (value.Any(char.IsLetterOrDigit)) builder.AppendLine(value);
            if (builder.Length >= MaxCharacters) break;
        }
        return builder.ToString();
    }

    private static async Task<string> TryRunPdfToTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pdftotext",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-layout");
            startInfo.ArgumentList.Add("-enc");
            startInfo.ArgumentList.Add("UTF-8");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("-");
            using var process = Process.Start(startInfo);
            if (process is null) return string.Empty;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return string.Empty;
        }
    }

    private static string DecodePdfLiteral(string value)
        => value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string Limit(string text)
        => text.Length <= MaxCharacters ? text : text[..MaxCharacters] + "\n[内容过长，索引已截断]";

    [GeneratedRegex("""\(((?:\\.|[^\\)]){2,})\)\s*(?:Tj|'|\")""")]
    private static partial Regex PdfLiteralTextRegex();
}
