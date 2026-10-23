using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace LocalizationRuntime.Generator
{
    /// <summary>
    /// 读取 Localization/{culture}.json，把每种语言的文本编译成代码里的常量数组：
    ///   · 键 id 常量（const int）
    ///   · 每语言一个 string?[]，缺键为 null
    ///   · 强类型 id 常量（LocalizationIds）
    /// 运行时因此不加载任何资源文件、不做 JSON 解析。
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class LocalizationGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var files = context.AdditionalTextsProvider
                .Select(static (f, ct) => RawFile.TryCreate(f, ct))
                .Where(static f => f.HasValue)
                .Select(static (f, _) => f.GetValueOrDefault())
                .Collect();

            var settings = context.AnalyzerConfigOptionsProvider
                .Select(static (p, _) => new Settings(
                    GetOption(p, "build_property.RootNamespace") ?? "Localization",
                    GetOption(p, "build_property.LocalizationClassName") ?? "L",
                    GetOption(p, "build_property.LocalizationNamespaceSuffix") ?? ".Localization",
                    GetOption(p, "build_property.LocalizationDefaultCulture") ?? "zh-CN"));

            // 诊断的抑制与升级走 MSBuild 属性，理由见 ReadSeverityOverrides。
            var severities = context.AnalyzerConfigOptionsProvider
                .Select(static (p, _) => ReadSeverityOverrides(p));

            var model = files.Combine(settings)
                .Select(static (pair, ct) => Build(pair.Left.ToArray(), pair.Right, ct));

            context.RegisterSourceOutput(
                model.Combine(severities),
                static (spc, pair) => Emit(spc, pair.Left, pair.Right));
        }

        /// <summary>
        /// 读取诊断的抑制与升级配置。
        ///
        /// 为什么不用 .editorconfig 的 dotnet_diagnostic.&lt;id&gt;.severity：
        /// 那类配置由编译器的诊断过滤层处理，不会进入 AnalyzerConfigOptionsProvider，
        /// 源生成器读不到（已用探针验证过）。因此改读两个 MSBuild 属性：
        ///
        ///   &lt;LocalizationSuppressedDiagnostics&gt;LR1006;LR1008&lt;/LocalizationSuppressedDiagnostics&gt;
        ///   &lt;LocalizationDiagnosticsAsErrors&gt;LR1010&lt;/LocalizationDiagnosticsAsErrors&gt;
        ///
        /// 多个 ID 用逗号或分号分隔。
        /// </summary>
        private static Dictionary<string, DiagnosticSeverity> ReadSeverityOverrides(
            AnalyzerConfigOptionsProvider provider)
        {
            var result = new Dictionary<string, DiagnosticSeverity>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in SplitIds(GetOption(provider, "build_property.LocalizationSuppressedDiagnostics")))
            {
                result[id] = DiagnosticSeverity.Hidden;
            }

            foreach (var id in SplitIds(GetOption(provider, "build_property.LocalizationDiagnosticsAsErrors")))
            {
                result[id] = DiagnosticSeverity.Error;
            }

            return result;
        }

        private static IEnumerable<string> SplitIds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var part in value!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }

        private static string? GetOption(AnalyzerConfigOptionsProvider provider, string name)
        {
            string? value;
            return provider.GlobalOptions.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static ResourceModel Build(RawFile[] files, Settings settings, CancellationToken ct)
        {
            var diagnostics = new List<DiagnosticItem>();

            if (files.Length == 0)
            {
                diagnostics.Add(new DiagnosticItem(
                    Diagnostics.NoCultureFiles.Id,
                    "未找到资源文件。约定为 Localization/<culture>.json，例如 Localization/zh-CN.json。",
                    1, 1, string.Empty));

                return new ResourceModel(
                    settings.Namespace, settings.ClassName, settings.DefaultCulture,
                    new KeyDef[0], new CultureTable[0], diagnostics.ToArray());
            }

            // 1) 解析
            var parsed = new List<ParsedFile>();
            foreach (var file in files.OrderBy(static f => f.Culture, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                JsonNode root;
                try
                {
                    root = JsonReader.Parse(file.Content ?? string.Empty);
                }
                catch (JsonParseException ex)
                {
                    diagnostics.Add(new DiagnosticItem(
                        Diagnostics.InvalidJson.Id,
                        "JSON 解析失败：" + ex.Message,
                        ex.LineNumber, ex.ColumnNumber, file.Path));
                    continue;
                }

                if (root.Kind != JsonKind.Object)
                {
                    diagnostics.Add(new DiagnosticItem(
                        Diagnostics.InvalidJson.Id,
                        "资源文件根节点必须是对象。",
                        root.Line, root.Column, file.Path));
                    continue;
                }

                // 结构与命名约束：空容器、null、点号键、混合数组、非大驼峰
                ShapeValidator.Validate(root, string.Empty, file.Path, diagnostics);

                parsed.Add(new ParsedFile(file.Culture, file.Path, ResourceFlattener.Flatten(root)));
            }

            if (parsed.Count == 0)
            {
                return new ResourceModel(
                    settings.Namespace, settings.ClassName, settings.DefaultCulture,
                    new KeyDef[0], new CultureTable[0], diagnostics.ToArray());
            }

            // 2) 基语言定义键集合
            ParsedFile? defaultFile = null;
            foreach (var file in parsed)
            {
                if (string.Equals(file.Culture, settings.DefaultCulture, StringComparison.OrdinalIgnoreCase))
                {
                    defaultFile = file;
                    break;
                }
            }

            if (defaultFile == null)
            {
                diagnostics.Add(new DiagnosticItem(
                    Diagnostics.MissingDefaultCulture.Id,
                    "缺少基语言资源文件 " + settings.DefaultCulture + ".json。" +
                    "基语言既定义键集合，也是其它语言缺键时的回退目标，必须存在。可用 " +
                    "LocalizationDefaultCulture 属性改为其它语言。",
                    1, 1, string.Empty));

                return new ResourceModel(
                    settings.Namespace, settings.ClassName, settings.DefaultCulture,
                    new KeyDef[0], new CultureTable[0], diagnostics.ToArray());
            }

            var baseValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var entry in defaultFile.Entries)
            {
                if (baseValues.ContainsKey(entry.Path))
                {
                    diagnostics.Add(new DiagnosticItem(
                        Diagnostics.DuplicateKey.Id,
                        "键 '" + entry.Path + "' 在基语言文件内重复出现。",
                        entry.Line, entry.Column, defaultFile.Path));
                    continue;
                }

                baseValues[entry.Path] = entry.Value;
            }

            // 3) 稳定排序后编号。数组元素 .0 .1 .2 因自然序而连续，便于折叠成连续 id。
            var sortedKeys = baseValues.Keys.ToList();
            sortedKeys.Sort(NaturalKeyComparer.Instance);

            var keys = new KeyDef[sortedKeys.Count];
            var index = new Dictionary<string, int>(sortedKeys.Count, StringComparer.Ordinal);
            for (var i = 0; i < sortedKeys.Count; i++)
            {
                index[sortedKeys[i]] = i;
                keys[i] = new KeyDef(i, sortedKeys[i], 1, 1);
            }

            // 4) 大小写仅差异的键在生成成员时会撞名
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in sortedKeys)
            {
                var flat = key.Replace(".", string.Empty).ToUpperInvariant();
                string? first;
                if (normalized.TryGetValue(flat, out first))
                {
                    diagnostics.Add(new DiagnosticItem(
                        Diagnostics.KeyCollision.Id,
                        "键 '" + key + "' 与 '" + first + "' 归一化后同名，生成的成员会冲突。",
                        1, 1, defaultFile.Path));
                }
                else
                {
                    normalized[flat] = key;
                }
            }

            // 5) 每语言一张表 + 与基语言比对
            var cultures = new List<CultureTable>();

            foreach (var file in parsed)
            {
                ct.ThrowIfCancellationRequested();

                var values = new string?[keys.Length];
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var entry in file.Entries)
                {
                    if (!seen.Add(entry.Path))
                    {
                        if (!ReferenceEquals(file, defaultFile))
                        {
                            diagnostics.Add(new DiagnosticItem(
                                Diagnostics.DuplicateKey.Id,
                                "键 '" + entry.Path + "' 在本文件内重复出现。",
                                entry.Line, entry.Column, file.Path));
                        }

                        continue;
                    }

                    int id;
                    if (!index.TryGetValue(entry.Path, out id))
                    {
                        // 基语言没有这个键：拼写错误或残留死数据
                        diagnostics.Add(new DiagnosticItem(
                            Diagnostics.ExtraKey.Id,
                            "键 '" + entry.Path + "' 在基语言 " + defaultFile.Culture + " 中不存在。",
                            entry.Line, entry.Column, file.Path));
                        continue;
                    }

                    values[id] = entry.Value;

                    if (entry.Value != null && entry.Value.Length == 0)
                    {
                        diagnostics.Add(new DiagnosticItem(
                            Diagnostics.EmptyValue.Id,
                            "键 '" + entry.Path + "' 的值为空字符串，界面上会显示为空白。",
                            entry.Line, entry.Column, file.Path));
                    }

                    string? template;
                    if (!ReferenceEquals(file, defaultFile) && baseValues.TryGetValue(entry.Path, out template))
                    {
                        var expected = PlaceholderScanner.Scan(template);
                        var actual = PlaceholderScanner.Scan(entry.Value);
                        if (!expected.SetEquals(actual))
                        {
                            diagnostics.Add(new DiagnosticItem(
                                Diagnostics.PlaceholderMismatch.Id,
                                "键 '" + entry.Path + "' 的占位符与基语言不一致：基语言为 " +
                                PlaceholderScanner.Describe(expected) + "，本语言为 " +
                                PlaceholderScanner.Describe(actual) + "。",
                                entry.Line, entry.Column, file.Path));
                        }
                    }
                }

                if (!ReferenceEquals(file, defaultFile))
                {
                    foreach (var entry in defaultFile.Entries)
                    {
                        if (!seen.Contains(entry.Path))
                        {
                            diagnostics.Add(new DiagnosticItem(
                                Diagnostics.MissingKey.Id,
                                "语言 " + file.Culture + " 缺少键 '" + entry.Path +
                                "'，运行时会回退到 " + defaultFile.Culture + "。",
                                entry.Line, entry.Column, file.Path));
                        }
                    }
                }

                cultures.Add(new CultureTable(file.Culture, values));
            }

            return new ResourceModel(
                settings.Namespace,
                settings.ClassName,
                defaultFile.Culture,
                keys,
                cultures.ToArray(),
                diagnostics.ToArray());
        }

        private static void Emit(
            SourceProductionContext context,
            ResourceModel model,
            System.Collections.Generic.Dictionary<string, DiagnosticSeverity> severityOverrides)
        {
            foreach (var d in model.Diagnostics)
            {
                var descriptor = Diagnostics.Find(d.Id);
                if (descriptor == null)
                {
                    continue;
                }

                // 应用 .editorconfig 覆盖。
                // 源生成器通过 ReportDiagnostic 上报的诊断不会自动套用
                // dotnet_diagnostic.<id>.severity，所以这里自己读配置。
                DiagnosticSeverity? overrideSeverity = null;
                DiagnosticSeverity configured;
                if (severityOverrides.TryGetValue(d.Id, out configured))
                {
                    if (configured == DiagnosticSeverity.Hidden)
                    {
                        continue;   // 抑制
                    }

                    overrideSeverity = configured;
                }

                var location = Location.None;
                if (!string.IsNullOrEmpty(d.File))
                {
                    var position = new LinePosition(Math.Max(0, d.Line - 1), Math.Max(0, d.Column - 1));
                    location = Location.Create(
                        d.File,
                        new TextSpan(0, 0),
                        new LinePositionSpan(position, position));
                }

                var diagnostic = overrideSeverity.HasValue
                    ? Diagnostic.Create(
                        descriptor,
                        location,
                        overrideSeverity.Value,
                        null,
                        System.Collections.Immutable.ImmutableDictionary<string, string?>.Empty,
                        d.Message)
                    : Diagnostic.Create(descriptor, location, d.Message);

                context.ReportDiagnostic(diagnostic);
            }

            if (!model.HasKeys)
            {
                return;
            }

            context.AddSource("Localization.Catalog.g.cs", SourceText.From(EmitCatalog(model), Encoding.UTF8));
            context.AddSource("Localization.Keys.g.cs", SourceText.From(EmitKeys(model), Encoding.UTF8));
        }

        private static string EmitKeys(ResourceModel model)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();
            sb.Append("namespace ");
            sb.Append(model.Namespace);
            sb.AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("/// <summary>编译期键 id。由生成器产出，不要手写。</summary>");
            sb.AppendLine("public static class LocalizationIds");
            sb.AppendLine("{");

            var root = BuildKeyTree(model);

            EmitKeyNodes(sb, root, "    ", string.Empty);

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 由键列表构建命名树并分配容器 id。EmitKeys 与 EmitCatalog 共用，
        /// 保证「生成的常量」与「运行时的父子表」出自同一棵树。
        /// </summary>
        private static KeyNode BuildKeyTree(ResourceModel model)
        {
            var root = new KeyNode(null, null);

            // 先登记所有叶子，同时记录每片叶子的真实路径（用于数组元素命名）
            foreach (var key in model.Keys)
            {
                root.Add(key.Path, key.Id);
            }

            // 容器 id 从叶子总数开始，与叶子区间不重叠。
            root.AssignContainerIds(model.Keys.Length);
            return root;
        }

        /// <summary>
        /// 键路径树。
        ///
        ///   · 叶子（字符串）       → const int，值是真实数组下标
        ///   · 普通对象             → static class，名字即键名
        ///   · 数组                 → {名}Object 类 + 同名容器常量；类里形如 {名}{N} 的元素常量
        ///   · 数组元素（对象/数组） → {累计前缀}Item{N}Object 类
        ///
        /// 数组节点的类名带 Object 后缀，是为了能和同名容器常量共存（否则 CS0102）。
        /// </summary>
        private sealed class KeyNode
        {
            internal KeyNode(string? name, string? prefix)
            {
                Name = name;
                Prefix = prefix;
            }

            internal string? Name { get; }

            /// <summary>累计前缀，用于给数组元素命名，保证任意深度都不重名。</summary>
            internal string? Prefix { get; }

            /// <summary>
            /// 生成类时使用的名字。普通对象与 Name 相同；
            /// 数组元素用累计前缀（Data + Item0 → DataItem0），与成员常量名区分开。
            /// </summary>
            internal string ClassName
            {
                get { return _className ?? Name ?? string.Empty; }
                set { _className = value; }
            }

            private string? _className;

            /// <summary>叶子：真实数组下标。容器：结构 id（≥ 叶子总数）。普通对象：null。</summary>
            internal int? Id { get; set; }

            /// <summary>本节点是否对应一个数组。</summary>
            internal bool IsArray { get; set; }

            /// <summary>本节点是否是数组里的一个元素。只有这类节点生成带 Object 后缀的类。</summary>
            internal bool IsArrayElement { get; set; }

            internal List<KeyNode> Children { get; } = new List<KeyNode>();

            internal KeyNode GetOrAddChild(string name, string prefix)
            {
                for (var i = 0; i < Children.Count; i++)
                {
                    if (string.Equals(Children[i].Name, name, StringComparison.Ordinal))
                    {
                        return Children[i];
                    }
                }

                var child = new KeyNode(name, prefix);
                Children.Add(child);
                return child;
            }

            internal void Add(string path, int id)
            {
                var segments = path.Split('.');
                var node = this;
                var prefix = string.Empty;

                for (var i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var isLast = i == segments.Length - 1;

                    if (IsAllDigits(segment))
                    {
                        node.IsArray = true;

                        // 数组元素一律带 Item 关键字：
                        //   Menus.0      → MenusItem0        （前缀 = 数组名 Menus）
                        //   Data.0.0     → DataItem0Item0    （嵌套时把上一层元素名并入）
                        // 上层普通对象名不参与（否则会出现 MainWindowTabsItem0）。
                        var className = prefix.Length == 0
                            ? (node.Name ?? string.Empty) + "Item" + segment
                            : prefix + "Item" + segment;
                        var constantName = className;

                        if (isLast)
                        {
                            // 字符串数组：元素本身就是叶子
                            var leaf = node.GetOrAddChild(constantName, className);
                            leaf.ClassName = className;
                            leaf.IsArrayElement = true;
                            leaf.Id = id;
                            return;
                        }

                        var child = node.GetOrAddChild(constantName, className);
                        child.ClassName = className;
                        child.IsArrayElement = true;
                        node = child;
                        prefix = className;
                        continue;
                    }

                    if (isLast)
                    {
                        node.GetOrAddChild(segment, prefix + segment).Id = id;
                        return;
                    }

                    node = node.GetOrAddChild(segment, prefix + segment);

                    // 进入普通对象后重置前缀：数组元素命名只认数组名与嵌套层，
                    // 不继承 MainWindow 这类外层对象名。
                    prefix = string.Empty;
                }
            }

            /// <summary>
            /// 深度优先给每个「数组节点」和「数组元素」各分配一个结构 id，
            /// 每个节点只分配一次，因此编号连续、无空洞：
            ///   Data=14 → DataItem0=15 → DataItem0Item0=16 → DataItem0Item1=17
            ///   → DataItem1=18 → DataItem1Item0=19 → DataItem1Item1=20 → Menus=21 ...
            ///
            /// 所有非叶子节点都占一个：数组、数组元素、普通对象（App / MainWindow / ToolInfo）。
            /// 因为类名统一带 Object 后缀，常量与类不会撞名（CS0102）。
            /// </summary>
            internal int AssignContainerIds(int next)
            {
                for (var i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];

                    // 叶子（含字符串数组的元素）已有真实下标，不参与；
                    // 其余每个非叶子节点都占一个结构 id。
                    if (child.Children.Count > 0)
                    {
                        child.Id = next++;
                    }

                    next = child.AssignContainerIds(next);
                }

                return next;
            }
        }

        private static void EmitKeyNodes(StringBuilder sb, KeyNode node, string indent, string ownerName)
        {
            foreach (var child in node.Children)
            {
                var name = child.Name!;

                // ── 叶子：一个具体字符串 ──
                if (child.Children.Count == 0)
                {
                    sb.Append(indent);
                    sb.Append("public const int ");
                    sb.Append(name);
                    sb.Append(" = ");
                    sb.Append(child.Id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine(";");
                    continue;
                }

                // ── 数组：容器常量 + {名}Object 类 ──
                if (child.IsArray)
                {
                    sb.Append(indent);
                    sb.Append("public const int ");
                    sb.Append(name);
                    sb.Append(" = ");
                    sb.Append(child.Id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine(";");

                    sb.Append(indent);
                    sb.Append("public static class ");
                    sb.Append(child.ClassName);
                    sb.AppendLine("Object");
                    sb.Append(indent);
                    sb.AppendLine("{");

                    var inner = indent + "    ";

                    foreach (var element in child.Children)
                    {
                        // 所有元素统一用累计前缀命名（带 Item 关键字）：
                        //   MenusItem0 / DataItem0Item0
                        // 类名再加 Object 后缀。
                        var elementName = element.Name!;

                        // 元素是纯字符串：直接产出常量
                        if (element.Children.Count == 0)
                        {
                            sb.Append(inner);
                            sb.Append("public const int ");
                            sb.Append(elementName);
                            sb.Append(" = ");
                            sb.Append(element.Id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                            sb.AppendLine(";");
                            continue;
                        }

                        // 元素是对象或数组：常量 + {名}Object 类
                        sb.Append(inner);
                        sb.Append("public const int ");
                        sb.Append(elementName);
                        sb.Append(" = ");
                        sb.Append(element.Id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine(";");

                        var elementClass = element.ClassName + "Object";

                        sb.Append(inner);
                        sb.Append("public static class ");
                        sb.Append(elementClass);
                        sb.AppendLine();
                        sb.Append(inner);
                        sb.AppendLine("{");

                        EmitKeyNodes(sb, element, inner + "    ", elementClass);
                        sb.Append(inner);
                        sb.AppendLine("}");
                    }

                    sb.Append(indent);
                    sb.AppendLine("}");
                    continue;
                }

                // ── 普通对象：常量 + {名}Object 类 ──
                // 与数组完全同构：常量名不带后缀，类名带 Object 后缀。
                var objectClassName = child.ClassName + "Object";

                sb.Append(indent);
                sb.Append("public const int ");
                sb.Append(Sanitize(child.Name!));
                sb.Append(" = ");
                sb.Append(child.Id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(";");

                sb.Append(indent);
                sb.Append("public static class ");
                sb.Append(objectClassName);
                sb.AppendLine();
                sb.Append(indent);
                sb.AppendLine("{");

                EmitKeyNodes(sb, child, indent + "    ", objectClassName);
                sb.Append(indent);
                sb.AppendLine("}");
            }
        }

        /// <summary>
        /// 遍历命名树，产出「容器 id → 直接子 id 数组」。
        ///
        /// 只有带 id 的节点（非叶子）才会成为键；子 id 按树中的原始顺序排列，
        /// 因此数组节点的子 id 顺序就是数组下标顺序。
        /// </summary>
        private static void CollectChildren(KeyNode node, Dictionary<int, List<int>> sink)
        {
            if (node.Id.HasValue && node.Children.Count > 0)
            {
                var list = new List<int>(node.Children.Count);
                sink[node.Id.Value] = list;

                foreach (var child in node.Children)
                {
                    if (child.Id.HasValue)
                    {
                        // 叶子与容器都有 id，统一记录，调用方按 id 大小区分。
                        list.Add(child.Id.Value);
                    }
                }
            }

            foreach (var child in node.Children)
            {
                CollectChildren(child, sink);
            }
        }

        private static string EmitCatalog(ResourceModel model)
        {
            var sb = new StringBuilder(256 * 1024);
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();
            sb.Append("namespace ");
            sb.Append(model.Namespace);
            sb.AppendLine(";");
            sb.AppendLine();

            sb.AppendLine("public static class Catalog");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>唯一的 catalog 实例。生成器已把全部语言编译进程序集。</summary>");
            sb.AppendLine("    public static readonly global::LocalizationRuntime.ILocalizationCatalog Instance = new GeneratedCatalog();");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("internal sealed class GeneratedCatalog : global::LocalizationRuntime.ILocalizationCatalog");
            sb.AppendLine("{");
            sb.AppendLine("    private static readonly string[] AllKeys =");
            sb.AppendLine("    {");
            foreach (var key in model.Keys)
            {
                sb.Append("        ");
                sb.Append(Literal(key.Path));
                sb.AppendLine(",");
            }

            sb.AppendLine("    };");
            sb.AppendLine();
            sb.Append("    private static readonly string[] AllCultures = { ");
            for (var i = 0; i < model.Cultures.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(Literal(model.Cultures[i].Name));
            }

            sb.AppendLine(" };");
            sb.AppendLine();
            sb.Append("    public string DefaultCultureName => ");
            sb.Append(Literal(model.DefaultCulture));
            sb.AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<string> Keys => AllKeys;");
            sb.AppendLine();
            sb.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<string> CultureNames => AllCultures;");
            sb.AppendLine();

            // ── 父子表：容器 id → 直接子 id ──
            // 用交错数组而不是 Dictionary：只在编译期固定内容，查表退化为一次索引。
            var tree = BuildKeyTree(model);
            var childSink = new Dictionary<int, List<int>>();
            CollectChildren(tree, childSink);

            var totalIds = model.Keys.Length;
            foreach (var containerId in childSink.Keys)
            {
                if (containerId >= totalIds)
                {
                    totalIds = containerId + 1;
                }
            }

            string[] childTable;
            {
                var map = new string[totalIds];
                for (var i = 0; i < totalIds; i++)
                {
                    List<int>? children;
                    map[i] = childSink.TryGetValue(i, out children)
                        ? "new int[] { " + string.Join(", ", children) + " }"
                        : "global::System.Array.Empty<int>()";
                }

                childTable = map;
            }

            sb.AppendLine("    /// <summary>容器 id → 直接子 id。叶子与无子节点者为空数组。</summary>");
            sb.AppendLine("    private static readonly int[][] ChildIndex =");
            sb.AppendLine("    {");
            for (var i = 0; i < childTable.Length; i++)
            {
                sb.Append("        ");
                sb.Append(childTable[i]);
                sb.AppendLine(",");
            }

            sb.AppendLine("    };");
            sb.AppendLine();
            sb.Append("    public int TotalIdCount => ");
            sb.Append(totalIds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("    public int[] GetChildren(int id)");
            sb.AppendLine("    {");
            sb.AppendLine("        if ((uint)id >= (uint)ChildIndex.Length)");
            sb.AppendLine("        {");
            sb.AppendLine("            return global::System.Array.Empty<int>();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return ChildIndex[id];");
            sb.AppendLine("    }");
            sb.AppendLine();

            for (var i = 0; i < model.Cultures.Length; i++)
            {
                EmitTable(sb, model.Cultures[i], i);
            }

            sb.AppendLine("    public string?[]? GetTable(string cultureName)");
            sb.AppendLine("    {");
            sb.AppendLine("        string?[]? t;");
            sb.AppendLine("        switch (cultureName)");
            sb.AppendLine("        {");
            for (var i = 0; i < model.Cultures.Length; i++)
            {
                sb.Append("            case ");
                sb.Append(Literal(model.Cultures[i].Name));
                sb.Append(": t = Table");
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("; break;");
            }

            sb.AppendLine("            default: return null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (t != null) return t;");
            sb.AppendLine();
            sb.AppendLine("        switch (cultureName)");
            sb.AppendLine("        {");
            for (var i = 0; i < model.Cultures.Length; i++)
            {
                var suffix = i.ToString(CultureInfo.InvariantCulture);
                sb.Append("            case ");
                sb.Append(Literal(model.Cultures[i].Name));
                sb.Append(": return Table");
                sb.Append(suffix);
                sb.AppendLine(" = CreateTable" + suffix + "();");
            }

            sb.AppendLine("            default: return null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// 每语言一张表，惰性创建：只有该语言第一次被请求时才填充数组，
        /// 因此启动开销与"当前语言"成正比，而不是与"语言总数"成正比。
        /// 元素多时拆成多个填充方法：单个 IL 方法体上限 64KB。
        /// </summary>
        private static void EmitTable(StringBuilder sb, CultureTable culture, int cultureIndex)
        {
            var suffix = cultureIndex.ToString(CultureInfo.InvariantCulture);
            var field = "Table" + suffix;
            var values = culture.Values;
            var total = values.Length;

            sb.Append("    private static string?[]? ");
            sb.Append(field);
            sb.AppendLine(";");
            sb.AppendLine();

            var chunk = total <= 400 ? Math.Max(1, total) : 400;
            var chunks = (total + chunk - 1) / chunk;

            sb.Append("    private static string?[] Create");
            sb.Append(field);
            sb.AppendLine("()");
            sb.AppendLine("    {");
            sb.Append("        var a = new string?[");
            sb.Append(total.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("];");
            for (var c = 0; c < chunks; c++)
            {
                sb.Append("        Fill");
                sb.Append(field);
                sb.Append('_');
                sb.Append(c.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("(a);");
            }

            sb.AppendLine("        return a;");
            sb.AppendLine("    }");
            sb.AppendLine();

            for (var c = 0; c < chunks; c++)
            {
                var start = c * chunk;
                var end = Math.Min(start + chunk, total);

                sb.Append("    private static void Fill");
                sb.Append(field);
                sb.Append('_');
                sb.Append(c.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("(string?[] a)");
                sb.AppendLine("    {");

                for (var i = start; i < end; i++)
                {
                    if (values[i] == null)
                    {
                        continue;
                    }

                    sb.Append("        a[");
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    sb.Append("] = ");
                    sb.Append(Literal(values[i]!));
                    sb.AppendLine(";");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        private static bool IsAllDigits(string s)
        {
            if (s.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static string Sanitize(string path)
        {
            var sb = new StringBuilder(path.Length);
            for (var i = 0; i < path.Length; i++)
            {
                var c = path[i];
                sb.Append(c == '.' || !(char.IsLetterOrDigit(c) || c == '_') ? '_' : c);
            }

            var result = sb.ToString();
            if (result.Length == 0)
            {
                return "_";
            }

            return char.IsDigit(result[0]) ? "_" + result : result;
        }

        /// <summary>
        /// 把点分键路径转成嵌套静态类的访问路径，与 <see cref="KeyNode.Add"/> 保持一致：
        ///   App.Title          → App.Title
        ///   MainWindow.Tabs.0  → MainWindow.Tabs_0
        /// </summary>
        private static string KeyAccessPath(string path)
        {
            var segments = path.Split('.');
            var sb = new StringBuilder(path.Length + 8);

            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];

                if (sb.Length > 0)
                {
                    sb.Append('.');
                }

                // 与 KeyNode.Add 保持一致：数字段 → Item{N}
                sb.Append(IsAllDigits(segment) ? "Item" + segment : Sanitize(segment));
            }

            return sb.ToString();
        }

        private static string Literal(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }

    internal readonly struct RawFile
    {
        private RawFile(string path, string culture, string? content)
        {
            Path = path;
            Culture = culture;
            Content = content;
        }

        public string Path { get; }

        public string Culture { get; }

        public string? Content { get; }

        public bool HasValue
        {
            get { return Path != null; }
        }

        /// <summary>
        /// 约定：文件名就是 culture（zh-CN.json）。文件名不是合法 culture 的 json 直接忽略，
        /// 这样资源目录里可以放说明文件或随手笔记。
        /// </summary>
        public static RawFile? TryCreate(AdditionalText file, CancellationToken ct)
        {
            var path = file.Path;
            if (path == null || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var culture = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!IsCultureName(culture))
            {
                return null;
            }

            var text = file.GetText(ct);
            return new RawFile(path, culture, text?.ToString());
        }

        private static bool IsCultureName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!(char.IsLetter(c) || c == '-' || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal readonly struct Settings
    {
        public Settings(string rootNamespace, string className, string namespaceSuffix, string defaultCulture)
        {
            Namespace = rootNamespace + namespaceSuffix;
            ClassName = className;
            DefaultCulture = defaultCulture;
        }

        public string Namespace { get; }

        public string ClassName { get; }

        public string DefaultCulture { get; }
    }

    internal sealed class ParsedFile
    {
        public ParsedFile(string culture, string path, List<ResourceFlattener.Entry> entries)
        {
            Culture = culture;
            Path = path;
            Entries = entries;
        }

        public string Culture { get; }

        public string Path { get; }

        public List<ResourceFlattener.Entry> Entries { get; }
    }
}
