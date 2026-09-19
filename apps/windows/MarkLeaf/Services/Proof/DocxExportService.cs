using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MarkLeaf.Services.Export;

internal static partial class DocxExportService
{
    public static async Task ExportAsync(string markdown, string outputPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteTextEntry(archive, "[Content_Types].xml", ContentTypes);
        WriteTextEntry(archive, "_rels/.rels", PackageRelationships);
        WriteTextEntry(archive, "word/_rels/document.xml.rels", DocumentRelationships);
        WriteTextEntry(archive, "word/styles.xml", Styles);
        WriteDocument(archive, markdown ?? string.Empty);
        await stream.FlushAsync(cancellationToken);
    }

    private static void WriteDocument(ZipArchive archive, string markdown)
    {
        var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        writer.WriteStartDocument();
        writer.WriteStartElement("w", "document", WordNamespace);
        writer.WriteStartElement("w", "body", WordNamespace);
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            WriteParagraph(writer, string.Join(" ", paragraph), null);
            paragraph.Clear();
        }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) { FlushParagraph(); continue; }
            if (line.StartsWith("```", StringComparison.Ordinal)) { FlushParagraph(); continue; }
            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                WriteParagraph(writer, heading.Groups[2].Value.Trim(), $"Heading{Math.Clamp(heading.Groups[1].Value.Length, 1, 3)}");
                continue;
            }
            var list = ListRegex().Match(line);
            if (list.Success)
            {
                FlushParagraph();
                WriteParagraph(writer, "• " + list.Groups[1].Value.Trim(), "ListParagraph");
                continue;
            }
            if (HorizontalRuleRegex().IsMatch(line)) { FlushParagraph(); continue; }
            paragraph.Add(line.TrimStart('>', ' '));
        }
        FlushParagraph();
        writer.WriteStartElement("w", "sectPr", WordNamespace);
        writer.WriteStartElement("w", "pgSz", WordNamespace);
        writer.WriteAttributeString("w", "w", WordNamespace, "11906");
        writer.WriteAttributeString("w", "h", WordNamespace, "16838");
        writer.WriteEndElement();
        writer.WriteStartElement("w", "pgMar", WordNamespace);
        writer.WriteAttributeString("w", "top", WordNamespace, "1440");
        writer.WriteAttributeString("w", "right", WordNamespace, "1440");
        writer.WriteAttributeString("w", "bottom", WordNamespace, "1440");
        writer.WriteAttributeString("w", "left", WordNamespace, "1440");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteParagraph(XmlWriter writer, string text, string? style)
    {
        writer.WriteStartElement("w", "p", WordNamespace);
        if (!string.IsNullOrWhiteSpace(style))
        {
            writer.WriteStartElement("w", "pPr", WordNamespace);
            writer.WriteStartElement("w", "pStyle", WordNamespace);
            writer.WriteAttributeString("w", "val", WordNamespace, style);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        var cursor = 0;
        foreach (Match match in BoldRegex().Matches(text))
        {
            if (match.Index > cursor) WriteRun(writer, text[cursor..match.Index], false);
            WriteRun(writer, match.Groups[1].Value, true);
            cursor = match.Index + match.Length;
        }
        if (cursor < text.Length) WriteRun(writer, text[cursor..], false);
        if (text.Length == 0) WriteRun(writer, string.Empty, false);
        writer.WriteEndElement();
    }

    private static void WriteRun(XmlWriter writer, string text, bool bold)
    {
        writer.WriteStartElement("w", "r", WordNamespace);
        if (bold)
        {
            writer.WriteStartElement("w", "rPr", WordNamespace);
            writer.WriteElementString("w", "b", WordNamespace, string.Empty);
            writer.WriteEndElement();
        }
        writer.WriteStartElement("w", "t", WordNamespace);
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(text);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string ContentTypes = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>""";
    private const string PackageRelationships = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""";
    private const string DocumentRelationships = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>""";
    private const string Styles = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:rFonts w:eastAsia="微软雅黑"/><w:sz w:val="22"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="36"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="30"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="26"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="420"/></w:pPr></w:style></w:styles>""";

    [GeneratedRegex("^(#{1,6})\\s+(.+)$")]
    private static partial Regex HeadingRegex();
    [GeneratedRegex("^\\s*(?:[-+*]|\\d+[.)])\\s+(.+)$")]
    private static partial Regex ListRegex();
    [GeneratedRegex("^\\s*(?:---+|\\*\\*\\*+)\\s*$")]
    private static partial Regex HorizontalRuleRegex();
    [GeneratedRegex("\\*\\*([^*]+)\\*\\*")]
    private static partial Regex BoldRegex();
}
