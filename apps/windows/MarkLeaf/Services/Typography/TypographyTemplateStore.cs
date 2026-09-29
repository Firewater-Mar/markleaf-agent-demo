using System.Text.Json;

namespace MarkLeaf.Services.Typography;

public sealed class TypographyTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _directory;
    public TypographyTemplateStore(string dataDirectory) => _directory = Path.Combine(dataDirectory, "TypographyTemplates");

    public IReadOnlyList<TypographyTemplate> LoadAll()
    {
        Directory.CreateDirectory(_directory);
        var items = BuiltIns().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { var item = Deserialize(File.ReadAllText(file)); item.BuiltIn = false; items[item.Id] = item; }
            catch (InvalidDataException) { }
        }
        return items.Values.OrderByDescending(item => item.BuiltIn).ThenBy(item => item.Name).ToArray();
    }

    public TypographyTemplate Save(TypographyTemplate item)
    {
        Validate(item);
        if (item.BuiltIn) item = item.CloneAsUserTemplate();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, $"{SafeName(item.Id)}.json"), Serialize(item));
        return item;
    }

    public TypographyTemplate Import(string file)
    {
        var item = Deserialize(File.ReadAllText(file));
        item.BuiltIn = false;
        if (BuiltIns().Any(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))) item.Id = Guid.NewGuid().ToString("N");
        return Save(item);
    }
    public void Export(TypographyTemplate item, string file) => File.WriteAllText(file, Serialize(item));
    public static string Serialize(TypographyTemplate item) { Validate(item); return JsonSerializer.Serialize(item, JsonOptions); }
    public static TypographyTemplate Deserialize(string json)
    {
        try { var item = JsonSerializer.Deserialize<TypographyTemplate>(json, JsonOptions) ?? throw new InvalidDataException("模板为空。"); Validate(item); return item; }
        catch (JsonException ex) { throw new InvalidDataException("模板不是有效 JSON。", ex); }
    }

    public static IReadOnlyList<TypographyTemplate> BuiltIns() =>
    [
        Preset("academic-report", "学术报告", "严格的 A4 学术报告规则。", 42, 88, 92, 4, true,
            bodyFont: "宋体", headingFont: "微软雅黑", bodySize: 10.5M, h1: 18M, h2: 15M, h3: 12M,
            lineSpacing: 1.6M, paragraphSpacing: 6M),
        Preset("technical-notes", "技术笔记", "适合代码、表格与快速扫描。", 64, 100, 88, 3, true, true,
            "微软雅黑", "微软雅黑", 10.5M, 20M, 15M, 12M, 1.5M, 8M),
        Preset("resume", "简历", "紧凑的一至两页简历。", 48, 35, 90, 3, false,
            bodyFont: "微软雅黑", headingFont: "微软雅黑", bodySize: 9.5M, h1: 22M, h2: 13M, h3: 10.5M,
            lineSpacing: 1.35M, paragraphSpacing: 4M),
    ];
    private static TypographyTemplate Preset(string id, string name, string description, int line, int image,
        int score, int iterations, bool toc, bool allowOrphan = false,
        string bodyFont = "宋体", string headingFont = "微软雅黑", decimal bodySize = 10.5M,
        decimal h1 = 18M, decimal h2 = 15M, decimal h3 = 12M,
        decimal lineSpacing = 1.6M, decimal paragraphSpacing = 6M) => new()
    {
        Id = id, Name = name, Description = description, BuiltIn = true,
        Rules = new()
        {
            BodyFontFamily = bodyFont, HeadingFontFamily = headingFont,
            BodyFontSizePt = bodySize, Heading1FontSizePt = h1, Heading2FontSizePt = h2,
            Heading3FontSizePt = h3, LineSpacing = lineSpacing, ParagraphSpacingPt = paragraphSpacing,
            MaxLineChars = line, ImageMaxWidth = image, TargetScore = score,
            MaxIterations = iterations, GenerateTableOfContents = toc, AllowOrphan = allowOrphan,
        },
    };
    private static void Validate(TypographyTemplate item)
    {
        if (item.SchemaVersion != 1) throw new InvalidDataException("不支持的模板版本。");
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name)) throw new InvalidDataException("模板 id 和名称不能为空。");
        item.Rules ??= new(); item.Rules.Normalize();
    }
    private static string SafeName(string value) => new(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}
