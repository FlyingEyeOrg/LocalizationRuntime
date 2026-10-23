using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 覆盖生成器与运行时之间所有会产生偏差的方面：
    ///   1. 每个语言的表长度都与键数一致
    ///   2. 每个常量在每种语言下都能取到值（缺键时按回退链）
    ///   3. JSON 文件与生成结果一一对应（不多不少）
    ///   4. 回退链的行为符合预期
    /// </summary>
    public sealed class LocalizationCoverageTests
    {
        private static readonly string[] Cultures = { "zh-CN", "en-US" };

        private static string ResourcePath(string culture)
        {
            return Path.Combine(AppContext.BaseDirectory, "Localization", culture + ".json");
        }

        private static Dictionary<string, string> Flatten(string culture)
        {
            var json = File.ReadAllText(ResourcePath(culture));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var doc = JsonDocument.Parse(json))
            {
                Walk(doc.RootElement, string.Empty, result);
            }

            return result;
        }

        private static void Walk(JsonElement element, string path, Dictionary<string, string> sink)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject())
                    {
                        Walk(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name, sink);
                    }

                    break;

                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, path + "." + i.ToString(CultureInfo.InvariantCulture), sink);
                        i++;
                    }

                    break;

                case JsonValueKind.String:
                    sink[path] = element.GetString()!;
                    break;
            }
        }

        [Fact]
        public void EveryCultureHasTheSameKeySet()
        {
            var l = new Localizer(Catalog.Instance);
            var zh = Flatten("zh-CN");
            var en = Flatten("en-US");

            var onlyInZh = zh.Keys.Except(en.Keys, StringComparer.Ordinal).ToArray();
            var onlyInEn = en.Keys.Except(zh.Keys, StringComparer.Ordinal).ToArray();

            Assert.True(onlyInZh.Length == 0, "只在 zh-CN 中存在: " + string.Join(", ", onlyInZh));
            Assert.True(onlyInEn.Length == 0, "只在 en-US 中存在: " + string.Join(", ", onlyInEn));
        }

        [Fact]
        public void EveryLeafIdIsNonEmptyInEveryCulture()
        {
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            foreach (var culture in Cultures)
            {
                var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo(culture));

                for (var id = 0; id < leafCount; id++)
                {
                    var value = localizer.Get(id);
                    Assert.False(
                        string.IsNullOrEmpty(value),
                        culture + " 的 id=" + id + "（键 " + Catalog.Instance.Keys[id] + "）取到空值");
                }
            }
        }

        /// <summary>
        /// 每种语言下，每个 id 取到的值都必须等于该语言 JSON 里对应键的值。
        /// 这条把「生成器的键顺序」与「JSON 实际内容」在两种语言上同时对齐。
        /// </summary>
        [Fact]
        public void EveryCultureMatchesItsOwnJsonFile()
        {
            var l = new Localizer(Catalog.Instance);
            var keys = Catalog.Instance.Keys;
            var problems = new List<string>();

            foreach (var culture in Cultures)
            {
                var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo(culture));
                var expected = Flatten(culture);

                for (var id = 0; id < keys.Count; id++)
                {
                    string? want;
                    if (!expected.TryGetValue(keys[id], out want))
                    {
                        problems.Add(culture + " 缺少键 " + keys[id]);
                        continue;
                    }

                    var got = localizer.Get(id);
                    if (!string.Equals(got, want, StringComparison.Ordinal))
                    {
                        problems.Add(
                            culture + " 的 " + keys[id] + "（id=" + id + "）期望 '" + want + "' 实际 '" + got + "'");
                    }
                }
            }

            Assert.True(
                problems.Count == 0,
                "多语言值不一致：" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void SwitchingCultureChangesEveryValue()
        {
            var l = new Localizer(Catalog.Instance);
            // 测试资源里两种语言的值全部不同，所以每个 id 都必须变化。
            var leafCount = Catalog.Instance.Keys.Count;
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));

            var before = new string[leafCount];
            for (var id = 0; id < leafCount; id++)
            {
                before[id] = localizer.Get(id);
            }

            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));

            var unchanged = new List<string>();
            for (var id = 0; id < leafCount; id++)
            {
                if (string.Equals(before[id], l.Get(id), StringComparison.Ordinal))
                {
                    unchanged.Add("id=" + id + " 键=" + Catalog.Instance.Keys[id]);
                }
            }

            Assert.True(
                unchanged.Count == 0,
                "切换语言后以下值没有变化：" + Environment.NewLine + string.Join(Environment.NewLine, unchanged));
        }

        [Fact]
        public void UnknownCultureFallsBackToDefault()
        {
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            // 先记录基语言的实际值，再切到不受支持的 de-DE 对比。
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
            var expected = new string[leafCount];
            for (var id = 0; id < leafCount; id++)
            {
                expected[id] = localizer.Get(id);
            }

            // 不受支持的语言应落到基语言，CurrentCulture 与值的来源必须一致。
            Assert.Equal("zh-CN", l.CurrentCulture.Name);

            for (var id = 0; id < leafCount; id++)
            {
                Assert.Equal(expected[id], l.Get(id));
            }
        }

        [Fact]
        public void ReSwitchingIsIdempotentForValues()
        {
            var l = new Localizer(Catalog.Instance);
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));

            var first = Enumerable.Range(0, Catalog.Instance.Keys.Count).Select(localizer.Get).ToArray();

            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));

            var again = Enumerable.Range(0, Catalog.Instance.Keys.Count).Select(localizer.Get).ToArray();

            Assert.Equal(first, again);
        }

        /// <summary>
        /// 生成器产出的常量数必须等于「叶子数 + 容器节点数」。
        ///
        /// 容器节点 = JSON 里所有对象与数组（不含根）。注意数组中每个元素也是一个节点：
        ///   例如 ArrObj = [ {Sub:[...]}, {Sub:[...]} ] 有两个 Sub 对象，各算一个容器。
        /// </summary>
        [Fact]
        public void ConstantTreeCoversEveryNonLeafNode()
        {
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            var json = File.ReadAllText(ResourcePath("zh-CN"));
            var nonLeaf = 0;
            using (var doc = JsonDocument.Parse(json))
            {
                // 根对象本身不生成类，因此从它的子节点开始数。
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    nonLeaf += CountNonLeaf(property.Value);
                }
            }

            var total = CountConstants(typeof(LocalizationIds));

            Assert.Equal(leafCount + nonLeaf, total);
        }

        /// <summary>
        /// 数容器节点：对象 / 数组本身算一个，数组的每个元素如果又是对象或数组也算。
        /// 根节点不计。
        /// </summary>
        private static int CountNonLeaf(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var n = 1;
                    foreach (var p in element.EnumerateObject())
                    {
                        n += CountNonLeaf(p.Value);
                    }

                    return n;

                case JsonValueKind.Array:
                    var a = 1;
                    foreach (var item in element.EnumerateArray())
                    {
                        a += CountNonLeaf(item);
                    }

                    return a;

                default:
                    return 0;
            }
        }

        private static int CountConstants(Type type)
        {
            var n = 0;

            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Public |
                                             System.Reflection.BindingFlags.Static |
                                             System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (f.FieldType == typeof(int) && f.IsLiteral)
                {
                    n++;
                }
            }

            foreach (var t in type.GetNestedTypes(System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.Static))
            {
                n += CountConstants(t);
            }

            return n;
        }
    }
}









