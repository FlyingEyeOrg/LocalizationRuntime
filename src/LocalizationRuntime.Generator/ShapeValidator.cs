using System;
using System.Collections.Generic;

namespace LocalizationRuntime.Generator
{
    /// <summary>
    /// JSON 资源的结构与命名约束校验。
    ///
    /// 这些规则是硬性的而不是警告，原因是它们要么让代码无法生成，
    /// 要么会在运行期静默产生错误的值：
    ///   · 空对象 / 空数组 —— 不产生任何键，节点会从树上消失，无意义
    ///   · null 值        —— 我们只支持纯字符串
    ///   · 键名含点号      —— 无法与层级区分（"a.b" 与 { "a": { "b": ... } }）
    ///   · 混合数组        —— 元素的键集合不同，无法表达成同一个元素类型
    ///   · 非大驼峰        —— 键名直接成为生成的类型名与成员名
    /// </summary>
    internal static class ShapeValidator
    {
        /// <summary>
        /// 校验一整棵树。命中任何规则就追加诊断，返回值表示是否有错。
        /// </summary>
        internal static void Validate(
            JsonNode node,
            string path,
            string filePath,
            IList<DiagnosticItem> sink)
        {
            switch (node.Kind)
            {
                case JsonKind.Object:
                    ValidateObject(node, path, filePath, sink);
                    return;

                case JsonKind.Array:
                    ValidateArray(node, path, filePath, sink);
                    return;

                case JsonKind.Null:
                    sink.Add(new DiagnosticItem(
                        Diagnostics.NullValue.Id,
                        Describe(path) + " 的值是 null。资源值必须是字符串，" +
                        "不需要的条目请直接删除。",
                        node.Line, node.Column, filePath));
                    return;

                case JsonKind.String:
                    return;

                // 数字 / 布尔：不是字符串，属于类型错误
                default:
                    sink.Add(new DiagnosticItem(
                        Diagnostics.NullValue.Id,
                        Describe(path) + " 的值不是字符串。资源值只支持字符串字面量。",
                        node.Line, node.Column, filePath));
                    return;
            }
        }

        private static void ValidateObject(
            JsonNode node,
            string path,
            string filePath,
            IList<DiagnosticItem> sink)
        {
            if (node.Members == null || node.Members.Count == 0)
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.EmptyContainer.Id,
                    Describe(path) + " 是空对象。空节点不产生任何键，" +
                    "会从生成的结构中消失，请删掉它。",
                    node.Line, node.Column, filePath));
                return;
            }

            foreach (var pair in node.Members)
            {
                var name = pair.Key;
                var childPath = path.Length == 0 ? name : path + "." + name;

                // 键名本身不能含点号：否则与层级无法区分
                if (name.IndexOf('.') >= 0)
                {
                    sink.Add(new DiagnosticItem(
                        Diagnostics.KeyContainsDot.Id,
                        "键名 '" + name + "' 含点号。点号用于表示层级，" +
                        "键名里出现它会让 \"" + name + "\" 与嵌套对象无法区分，请改用其它字符。",
                        pair.Value.Line, pair.Value.Column, filePath));
                    continue;
                }

                ValidateName(name, childPath, pair.Value.Line, pair.Value.Column, filePath, sink);
                Validate(pair.Value, childPath, filePath, sink);
            }
        }

        private static void ValidateArray(
            JsonNode node,
            string path,
            string filePath,
            IList<DiagnosticItem> sink)
        {
            if (node.Items == null || node.Items.Count == 0)
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.EmptyContainer.Id,
                    Describe(path) + " 是空数组。空数组不产生任何键，" +
                    "会从生成的结构中消失，请删掉它。",
                    node.Line, node.Column, filePath));
                return;
            }

            // 同构检查：所有元素必须是同一种 JSON 类型。
            // 字符串数组和对象数组都能表达成强类型集合，混合则不行。
            var kind = node.Items[0].Kind;
            var isArrayOfArrays = kind == JsonKind.Array;

            for (var i = 0; i < node.Items.Count; i++)
            {
                var item = node.Items[i];

                if (item.Kind != kind)
                {
                    sink.Add(new DiagnosticItem(
                        Diagnostics.HeterogeneousArray.Id,
                        Describe(path) + " 的元素类型不一致：第 0 个是 " + KindName(kind) +
                        "，第 " + i + " 个是 " + KindName(item.Kind) + "。" +
                        "数组元素必须是同一种类型。",
                        item.Line, item.Column, filePath));
                    return;
                }

                // 对象数组：每个元素的对象必须拥有完全相同的键集合，
                // 否则无法用同一个类型表达。
                if (kind == JsonKind.Object && i > 0)
                {
                    var mismatch = FindShapeMismatch(node.Items[0], item, path + "." + i);
                    if (mismatch != null)
                    {
                        sink.Add(new DiagnosticItem(
                            Diagnostics.HeterogeneousArray.Id,
                            Describe(path) + " 的元素结构不一致：" + mismatch,
                            item.Line, item.Column, filePath));
                        return;
                    }
                }
            }

            // 递归下去继续校验每个元素内部
            for (var i = 0; i < node.Items.Count; i++)
            {
                Validate(node.Items[i], path + "." + i, filePath, sink);
            }
        }

        /// <summary>比较两个对象元素的键集合是否完全一致。返回差异描述，一致则返回 null。</summary>
        private static string? FindShapeMismatch(JsonNode first, JsonNode other, string path)
        {
            if (first.Members == null || other.Members == null)
            {
                return null;
            }

            foreach (var pair in first.Members)
            {
                JsonNode? found;
                if (!other.Members.TryGetValue(pair.Key, out found))
                {
                    return path + " 缺少键 '" + pair.Key + "'（第 0 个元素有）。";
                }

                if (found.Kind != pair.Value.Kind)
                {
                    return path + " 的键 '" + pair.Key + "' 类型不同：第 0 个元素是 " +
                           KindName(pair.Value.Kind) + "，这里是 " + KindName(found.Kind) + "。";
                }
            }

            foreach (var pair in other.Members)
            {
                if (!first.Members.ContainsKey(pair.Key))
                {
                    return path + " 多出键 '" + pair.Key + "'（第 0 个元素没有）。";
                }
            }

            return null;
        }

        /// <summary>
        /// 命名约束：必须是合法的 C# 标识符，且使用大驼峰。
        /// </summary>
        private static void ValidateName(
            string name,
            string path,
            int line,
            int column,
            string filePath,
            IList<DiagnosticItem> sink)
        {
            if (name.Length == 0)
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.InvalidIdentifier.Id,
                    Describe(path) + " 的键名为空。",
                    line, column, filePath));
                return;
            }

            // ① 首字符必须是字母（允许中文等 Unicode 字母），不能是数字或下划线
            var first = name[0];
            if (!char.IsLetter(first))
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.InvalidIdentifier.Id,
                    "键名 '" + name + "' 必须以字母开头" +
                    (char.IsDigit(first) ? "（不能以数字开头）" : string.Empty) +
                    "，并且使用大驼峰，例如 '" + SuggestPascal(name) + "'。",
                    line, column, filePath));
                return;
            }

            // ② 其余字符只能是字母或数字（不允许下划线、连字符、空格）
            for (var i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c))
                {
                    sink.Add(new DiagnosticItem(
                        Diagnostics.InvalidIdentifier.Id,
                        "键名 '" + name + "' 含有非法字符 '" + c + "'。" +
                        "只允许字母和数字，并使用大驼峰，例如 '" + SuggestPascal(name) + "'。",
                        line, column, filePath));
                    return;
                }
            }

            // ③ 大驼峰：首字母必须大写
            if (char.IsLower(first))
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.InvalidIdentifier.Id,
                    "键名 '" + name + "' 不是大驼峰。请改为 '" + SuggestPascal(name) + "'。",
                    line, column, filePath));
                return;
            }

            // ④ C# 关键字（大写形式不会撞关键字，但保留检查以防将来放宽大小写规则）
            if (IsKeyword(name))
            {
                sink.Add(new DiagnosticItem(
                    Diagnostics.InvalidIdentifier.Id,
                    "键名 '" + name + "' 是 C# 关键字，不能作为生成的成员名。请改为 '" + name + "Key'。",
                    line, column, filePath));
            }
        }

        /// <summary>把不合法的名字转成大驼峰形式，用于诊断消息里给出修改建议。</summary>
        private static string SuggestPascal(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 1);
            var upperNext = true;

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (!char.IsLetterOrDigit(c))
                {
                    upperNext = true;
                    continue;
                }

                if (sb.Length == 0 && char.IsDigit(c))
                {
                    sb.Append("Key");
                }

                if (upperNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    upperNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.Length == 0 ? "Item" : sb.ToString();
        }

        private static string KindName(JsonKind kind)
        {
            switch (kind)
            {
                case JsonKind.Object: return "对象";
                case JsonKind.Array: return "数组";
                case JsonKind.String: return "字符串";
                case JsonKind.Number: return "数字";
                case JsonKind.Bool: return "布尔";
                default: return "null";
            }
        }

        private static string Describe(string path)
        {
            return path.Length == 0 ? "(根节点)" : "'" + path + "'";
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "Abstract", "As", "Base", "Bool", "Break", "Byte", "Case", "Catch", "Char", "Checked",
            "Class", "Const", "Continue", "Decimal", "Default", "Delegate", "Do", "Double", "Else",
            "Enum", "Event", "Explicit", "Extern", "False", "Finally", "Fixed", "Float", "For",
            "Foreach", "Goto", "If", "Implicit", "In", "Int", "Interface", "Internal", "Is", "Lock",
            "Long", "Namespace", "New", "Null", "Object", "Operator", "Out", "Override", "Params",
            "Private", "Protected", "Public", "Readonly", "Ref", "Return", "Sbyte", "Sealed", "Short",
            "Sizeof", "Stackalloc", "Static", "String", "Struct", "Switch", "This", "Throw", "True",
            "Try", "Typeof", "Uint", "Ulong", "Unchecked", "Unsafe", "Ushort", "Using", "Virtual",
            "Void", "Volatile", "While"
        };

        private static bool IsKeyword(string name)
        {
            // 大驼峰下只有少数几个能撞上（C# 关键字全小写），保留以便未来放宽
            return Keywords.Contains(name);
        }
    }
}
