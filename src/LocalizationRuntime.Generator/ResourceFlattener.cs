using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LocalizationRuntime.Generator
{
    /// <summary>把一份 JSON 扁平化成 点分键 → 值。</summary>
    internal static class ResourceFlattener
    {
        internal sealed class Entry
        {
            public Entry(string path, string? value, int line, int column, bool isArrayElement)
            {
                Path = path;
                Value = value;
                Line = line;
                Column = column;
                IsArrayElement = isArrayElement;
            }

            public string Path { get; }

            public string? Value { get; }

            public int Line { get; }

            public int Column { get; }

            public bool IsArrayElement { get; }
        }

        internal static List<Entry> Flatten(JsonNode root)
        {
            var result = new List<Entry>();
            Walk(root, string.Empty, result, false);
            return result;
        }

        private static void Walk(JsonNode node, string prefix, List<Entry> sink, bool inArray)
        {
            if (node.Kind == JsonKind.Object)
            {
                if (node.Members == null)
                {
                    return;
                }

                foreach (var pair in node.Members)
                {
                    var childPrefix = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
                    Walk(pair.Value, childPrefix, sink, false);
                }

                return;
            }

            if (node.Kind == JsonKind.Array)
            {
                if (node.Items == null)
                {
                    return;
                }

                for (var i = 0; i < node.Items.Count; i++)
                {
                    var childPrefix = prefix + "." + i.ToString(CultureInfo.InvariantCulture);
                    Walk(node.Items[i], childPrefix, sink, true);
                }

                return;
            }

            if (node.Kind == JsonKind.String)
            {
                sink.Add(new Entry(prefix, node.Text, node.Line, node.Column, inArray));
                return;
            }

            if (node.Kind == JsonKind.Null)
            {
                sink.Add(new Entry(prefix, null, node.Line, node.Column, inArray));
                return;
            }

            // 数字/布尔是配置错误，交给调用方按"类型不对"处理。
            sink.Add(new Entry(prefix, null, node.Line, node.Column, inArray));
        }
    }

    /// <summary>
    /// 键的排序规则：按段比较，纯数字段按数值比。
    /// 这样 MainWindow.Tabs.2 排在 .10 前面，数组元素的下标保持连续。
    /// </summary>
    internal sealed class NaturalKeyComparer : IComparer<string>
    {
        public static readonly NaturalKeyComparer Instance = new NaturalKeyComparer();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            var ax = x.Split('.');
            var ay = y.Split('.');
            var min = Math.Min(ax.Length, ay.Length);

            for (var i = 0; i < min; i++)
            {
                var cmp = CompareSegment(ax[i], ay[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return ax.Length.CompareTo(ay.Length);
        }

        private static int CompareSegment(string a, string b)
        {
            var aNum = IsAllDigits(a);
            var bNum = IsAllDigits(b);

            if (aNum && bNum)
            {
                var na = int.Parse(a, CultureInfo.InvariantCulture);
                var nb = int.Parse(b, CultureInfo.InvariantCulture);
                return na.CompareTo(nb);
            }

            if (aNum)
            {
                return -1;
            }

            if (bNum)
            {
                return 1;
            }

            return string.CompareOrdinal(a, b);
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
    }

    /// <summary>占位符扫描：从格式串里抽出 {n} 的索引集合，正确处理 {{ 与 }} 转义。</summary>
    internal static class PlaceholderScanner
    {
        internal static SortedSet<int> Scan(string? format)
        {
            var result = new SortedSet<int>();
            if (string.IsNullOrEmpty(format))
            {
                return result;
            }

            var text = format!;
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];
                if (c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{')
                    {
                        i += 2;
                        continue;
                    }

                    var close = text.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        break;
                    }

                    var inner = text.Substring(i + 1, close - i - 1);
                    var comma = inner.IndexOf(',');
                    var colon = inner.IndexOf(':');
                    var end = inner.Length;

                    if (comma >= 0 && comma < end)
                    {
                        end = comma;
                    }

                    if (colon >= 0 && colon < end)
                    {
                        end = colon;
                    }

                    var indexText = inner.Substring(0, end).Trim();
                    int index;
                    if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    {
                        result.Add(index);
                    }

                    i = close + 1;
                    continue;
                }

                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
                {
                    i += 2;
                    continue;
                }

                i++;
            }

            return result;
        }

        internal static string Describe(SortedSet<int> set)
        {
            var sb = new StringBuilder();
            foreach (var i in set)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('{').Append(i.ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            return sb.Length == 0 ? "(无)" : sb.ToString();
        }
    }
}

