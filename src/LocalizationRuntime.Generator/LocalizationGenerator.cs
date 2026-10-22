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
    ///   · 强类型访问器（LocalizedText / LocalizedList 的实例）
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

            var model = files.Combine(settings)
                .Select(static (pair, ct) => Build(pair.Left.ToArray(), pair.Right, ct));

            context.RegisterSourceOutput(model, static (spc, m) => Emit(spc, m));
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

            // 3) 稳定排序后编号。数组元素 .0 .1 .2 因自然序而连续，可折叠成 LocalizedList。
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

        private static void Emit(SourceProductionContext context, ResourceModel model)
        {
            foreach (var d in model.Diagnostics)
            {
                var descriptor = Diagnostics.Find(d.Id);
                if (descriptor == null)
                {
                    continue;
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

                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, d.Message));
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
            sb.Append("internal static class ");
            sb.Append(model.ClassName);
            sb.AppendLine("Keys");
            sb.AppendLine("{");

            foreach (var key in model.Keys)
            {
                sb.Append("    public const int ");
                sb.Append(Sanitize(key.Path));
                sb.Append(" = ");
                sb.Append(key.Id.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(";");
            }

            sb.AppendLine("}");
            return sb.ToString();
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

            EmitAccessors(sb, model);
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

        private static void EmitAccessors(StringBuilder sb, ResourceModel model)
        {
            var keysClass = model.ClassName + "Keys";

            sb.Append("internal sealed partial class ");
            sb.Append(model.ClassName);
            sb.AppendLine();
            sb.AppendLine("{");

            // 分组的依据是"首段后面是否还有命名段"：
            //   App.Title      → 组 App，成员 Title
            //   Tabs.0         → 根级数组，remainder 是纯数字，直接作为根成员 Tabs
            //   App.Tabs.0     → 组 App，成员 Tabs（数组）
            var rootKeys = new List<KeyDef>();
            var groups = new Dictionary<string, List<KeyDef>>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var key in model.Keys)
            {
                var dot = key.Path.IndexOf('.');
                if (dot < 0)
                {
                    rootKeys.Add(key);
                    continue;
                }

                var remainder = key.Path.Substring(dot + 1);
                var remainderHead = remainder;
                var innerDot = remainder.IndexOf('.');
                if (innerDot >= 0)
                {
                    remainderHead = remainder.Substring(0, innerDot);
                }

                // 首段之后紧跟数字段，说明这是"以首段命名的数组"，属于根级成员
                if (IsAllDigits(remainderHead))
                {
                    rootKeys.Add(key);
                    continue;
                }

                var group = key.Path.Substring(0, dot);
                List<KeyDef> list;
                if (!groups.TryGetValue(group, out list))
                {
                    list = new List<KeyDef>();
                    groups[group] = list;
                    order.Add(group);
                }

                list.Add(key);
            }

            var rootMembers = DescribeMembers(rootKeys, false);
            var membersByGroup = new Dictionary<string, List<MemberInfo>>(StringComparer.Ordinal);
            foreach (var group in order)
            {
                membersByGroup[group] = DescribeMembers(groups[group], true);
            }

            // 分组：每组一个嵌套类型
            foreach (var group in order)
            {
                var members = membersByGroup[group];
                var typeName = GroupTypeName(group);

                sb.Append("    public sealed class ");
                sb.Append(typeName);
                sb.AppendLine();
                sb.AppendLine("    {");

                sb.AppendLine();
                sb.Append("        internal ");
                sb.Append(typeName);
                sb.AppendLine("(global::LocalizationRuntime.Localizer t)");
                sb.AppendLine("        {");
                sb.AppendLine("            _t = t;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        private readonly global::LocalizationRuntime.Localizer _t;");
                sb.AppendLine();

                // 惰性创建：只有真正被读到的键才分配响应单元，
                // 启动成本与"用到的键数"成正比，而不是与"总键数"成正比。
                foreach (var m in members)
                {
                    EmitLazyMember(sb, m, keysClass, "_t", "        ");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // 根级键：直接作为门面类的属性
            foreach (var m in rootMembers)
            {
                EmitLazyMember(sb, m, keysClass, "Localizer", "    ");
            }

            sb.Append("    public ");
            sb.Append(model.ClassName);
            sb.AppendLine("(global::LocalizationRuntime.Localizer localizer)");
            sb.AppendLine("    {");
            sb.AppendLine("        Localizer = localizer;");

            foreach (var group in order)
            {
                var name = Sanitize(GroupMemberName(group));
                sb.Append("        ");
                sb.Append(name);
                sb.Append(" = new ");
                sb.Append(GroupTypeName(group));
                sb.AppendLine("(localizer);");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public global::LocalizationRuntime.Localizer Localizer { get; }");
            sb.AppendLine();

            foreach (var group in order)
            {
                sb.Append("    public ");
                sb.Append(GroupTypeName(group));
                sb.Append(' ');
                sb.Append(Sanitize(GroupMemberName(group)));
                sb.AppendLine(" { get; }");
                sb.AppendLine();
            }

            sb.Append("    public static global::LocalizationRuntime.ILocalizationCatalog Catalog { get; } = new GeneratedCatalog();");
            sb.AppendLine();
            sb.AppendLine("}");
        }

        /// <summary>
        /// 生成一个惰性成员：字段初始为 null，首次读取时创建响应单元并注册到表。
        /// 这样启动成本只与"实际用到的键"成正比；一旦创建就常驻，后续读取只是一次字段读。
        /// </summary>
        private static void EmitLazyMember(
            StringBuilder sb,
            MemberInfo member,
            string keysClass,
            string tableField,
            string indent)
        {
            var name = Sanitize(member.Name);
            var type = member.IsList ? "LocalizedList" : "LocalizedText";

            sb.Append(indent);
            sb.Append("private global::LocalizationRuntime.");
            sb.Append(type);
            sb.Append("? _");
            sb.Append(name);
            sb.AppendLine(";");
            sb.AppendLine();

            sb.Append(indent);
            sb.Append("public global::LocalizationRuntime.");
            sb.Append(type);
            sb.Append(' ');
            sb.Append(name);
            sb.AppendLine();
            sb.Append(indent);
            sb.AppendLine("{");
            sb.Append(indent);
            sb.AppendLine("    get");
            sb.Append(indent);
            sb.AppendLine("    {");
            sb.Append(indent);
            sb.Append("        var v = _");
            sb.Append(name);
            sb.AppendLine(";");
            sb.Append(indent);
            sb.Append("        if (v != null) return v;");
            sb.AppendLine();
            sb.Append(indent);
            sb.AppendLine();
            sb.Append(indent);
            sb.Append("        v = new global::LocalizationRuntime.");
            sb.Append(type);
            sb.Append('(');
            sb.Append(tableField);
            sb.Append(", ");
            sb.Append(keysClass);
            sb.Append('.');
            sb.Append(Sanitize(member.FirstPath));
            if (member.IsList)
            {
                sb.Append(", ");
                sb.Append(member.Length.ToString(CultureInfo.InvariantCulture));
            }

            sb.AppendLine(");");
            sb.Append(indent);
            sb.Append("        _");
            sb.Append(name);
            sb.AppendLine(" = v;");
            sb.Append(indent);
            sb.AppendLine("        return v;");
            sb.Append(indent);
            sb.AppendLine("    }");
            sb.Append(indent);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        private sealed class MemberInfo
        {
            public MemberInfo(string name, string firstPath, bool isList, int length)
            {
                Name = name;
                FirstPath = firstPath;
                IsList = isList;
                Length = length;
            }
            public string Name { get; }

            public string FirstPath { get; }

            public bool IsList { get; }

            public int Length { get; }
        }

        /// <summary>
        /// 把一组已排序的键折叠成成员：连续的 .0 .1 .2 聚合成一个 LocalizedList，
        /// 其余一对一映射成 LocalizedText。
        /// </summary>
        /// <param name="stripGroup">
        /// true 表示这些键属于某个分组，成员名要去掉首段（App.Title → Title）；
        /// false 表示是门面类的根级成员，名字保留完整路径（Tabs.0 → Tabs）。
        /// </param>
        private static List<MemberInfo> DescribeMembers(List<KeyDef> members, bool stripGroup)
        {
            var result = new List<MemberInfo>();
            var i = 0;

            while (i < members.Count)
            {
                var path = members[i].Path;
                var dot = path.LastIndexOf('.');

                if (dot >= 0 && string.Equals(path.Substring(dot + 1), "0", StringComparison.Ordinal))
                {
                    var prefix = path.Substring(0, dot);
                    var count = 0;

                    while (i + count < members.Count)
                    {
                        var candidate = members[i + count].Path;
                        if (!candidate.StartsWith(prefix + ".", StringComparison.Ordinal))
                        {
                            break;
                        }

                        var segment = candidate.Substring(prefix.Length + 1);
                        if (!IsAllDigits(segment))
                        {
                            break;
                        }

                        if (int.Parse(segment, CultureInfo.InvariantCulture) != count)
                        {
                            break;
                        }

                        count++;
                    }

                    if (count >= 2)
                    {
                        result.Add(new MemberInfo(NameOf(prefix, stripGroup), path, true, count));
                        i += count;
                        continue;
                    }
                }

                result.Add(new MemberInfo(NameOf(path, stripGroup), path, false, 0));
                i++;
            }

            return result;
        }

        private static string NameOf(string path, bool stripGroup)
        {
            if (!stripGroup)
            {
                return path;
            }

            return MemberName(path);
        }

        /// <summary>去掉首段，剩下的就是嵌套类内部的成员名。</summary>
        private static string MemberName(string path)
        {
            var dot = path.IndexOf('.');
            return dot < 0 ? path : path.Substring(dot + 1);
        }

        private static string GroupMemberName(string group)
        {
            return group.Length == 0 ? "Strings" : group;
        }

        private static string GroupTypeName(string group)
        {
            return Sanitize(GroupMemberName(group)) + "Strings";
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


