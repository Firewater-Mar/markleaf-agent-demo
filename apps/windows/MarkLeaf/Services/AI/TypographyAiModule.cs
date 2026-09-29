using System.Reflection;
using System.Runtime.Loader;
using MarkLeaf.AI.Contracts;

namespace MarkLeaf.Services.AI;

public static class TypographyAiModule
{
    private const string AssemblyName = "MarkLeaf.AI.OpenAICompatible";
    private const string ClientTypeName = "MarkLeaf.AI.OpenAICompatible.OpenAICompatibleTypographyClient";

    public static bool IsLoaded => AppDomain.CurrentDomain.GetAssemblies()
        .Any(assembly => string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal));

    public static ITypographyAiClient Load()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(item => string.Equals(item.GetName().Name, AssemblyName, StringComparison.Ordinal));
        if (assembly is null)
        {
            var path = Path.Combine(AppContext.BaseDirectory, AssemblyName + ".dll");
            if (!File.Exists(path)) throw new FileNotFoundException("可选 AI 模块未安装。纯净模式仍可正常使用。", path);
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        var type = assembly.GetType(ClientTypeName, throwOnError: true)
            ?? throw new TypeLoadException("AI 模块类型不可用。");
        return Activator.CreateInstance(type) as ITypographyAiClient
            ?? throw new InvalidCastException("AI 模块版本与当前 MarkLeaf 不兼容。");
    }
}
