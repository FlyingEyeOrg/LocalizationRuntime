using Microsoft.CodeAnalysis;

namespace LocalizationRuntime.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "Localization";

        public static readonly DiagnosticDescriptor NoCultureFiles = new DiagnosticDescriptor(
            "LR0001",
            "未找到资源文件",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidJson = new DiagnosticDescriptor(
            "LR1001",
            "资源文件 JSON 无效",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateKey = new DiagnosticDescriptor(
            "LR1002",
            "键重复",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KeyCollision = new DiagnosticDescriptor(
            "LR1003",
            "键名归一化后冲突",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingDefaultCulture = new DiagnosticDescriptor(
            "LR1004",
            "缺少基语言资源文件",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ExtraKey = new DiagnosticDescriptor(
            "LR1005",
            "存在基语言没有的键",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingKey = new DiagnosticDescriptor(
            "LR1006",
            "缺少键",
            "{0}",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PlaceholderMismatch = new DiagnosticDescriptor(
            "LR1007",
            "占位符与基语言不一致",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor EmptyValue = new DiagnosticDescriptor(
            "LR1008",
            "值为空字符串",
            "{0}",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor EmptyContainer = new DiagnosticDescriptor(
            "LR1010",
            "空对象或空数组",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullValue = new DiagnosticDescriptor(
            "LR1011",
            "值必须是非 null 字符串",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KeyContainsDot = new DiagnosticDescriptor(
            "LR1012",
            "键名不能包含点号",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HeterogeneousArray = new DiagnosticDescriptor(
            "LR1013",
            "数组元素类型必须一致",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidIdentifier = new DiagnosticDescriptor(
            "LR1014",
            "键名不是合法的 C# 标识符",
            "{0}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor[] All =
        {
            NoCultureFiles,
            InvalidJson,
            DuplicateKey,
            KeyCollision,
            MissingDefaultCulture,
            ExtraKey,
            MissingKey,
            PlaceholderMismatch,
            EmptyValue,
            EmptyContainer,
            NullValue,
            KeyContainsDot,
            HeterogeneousArray,
            InvalidIdentifier
        };

        public static DiagnosticDescriptor? Find(string id)
        {
            for (var i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, System.StringComparison.Ordinal))
                {
                    return All[i];
                }
            }

            return null;
        }
    }
}
