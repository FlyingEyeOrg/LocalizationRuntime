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
    /// 全量 JSON 对照：资源文件里的每个叶子都必须能从 id 取到，且值完全一致；
    /// 反之每个叶子 id 也必须在 JSON 里有出处。
    ///
    /// 与前几个测试的区别：
    ///   · LocalizationIdsValueTests 用硬编码期望，覆盖典型路径
    ///   · 这里不用任何硬编码，而是把整棵 JSON 展开成「路径 → 值」，
    ///     再用容器 API 从根开始遍历生成的 id 树，两边做集合运算。
    ///     新增资源或新增形状时不需要改测试。
    ///
    /// 遍历走的是 GetChildren / Get(id)，即用户实际会用的那条路，
    /// 因此容器表的正确性也一并被覆盖。
    /// </summary>
    public sealed class LocalizationJsonParityTests
    {
        private static readonly string[] Cultures = { "zh-CN", "en-US" };

        // ─────────────────────────── JSON 侧 ───────────────────────────

        /// <summary>把 JSON 展开成「点分路径 → 叶子文本」，数组下标用数字段。</summary>
        private static Dictionary<string, string> FlattenJson(string culture)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", culture + ".json");
            Assert.True(File.Exists(path), "找不到测试资源: " + path);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
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
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(
                            property.Value,
                            path.Length == 0 ? property.Name : path + "." + property.Name,
                            sink);
                    }

                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, path + "." + index.ToString(CultureInfo.InvariantCulture), sink);
                        index++;
                    }

                    break;

                case JsonValueKind.String:
                    Assert.True(path.Length > 0, "JSON 出现空路径的叶子");
                    sink[path] = element.GetString()!;
                    break;
            }
        }

        // ─────────────────────────── id 树侧 ───────────────────────────

        /// <summary>
        /// 建立「JSON 路径 → id」的反查表。
        ///
        /// Catalog 不再暴露键路径（既然全部用 id，字符串路径就是多余的），
        /// 因此这里换一种方式：测试资源里每个叶子值都是全局唯一的，
        /// 于是可以先用 id 取值、再用值反查它在 JSON 中的路径。
        /// </summary>
        private static Dictionary<string, int> FlattenIds()
        {
            var localizer = new Localizer(Catalog.Instance);

            // 值 → JSON 路径
            var byValue = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in FlattenJson("zh-CN"))
            {
                byValue[pair.Value] = pair.Key;
            }

            var result = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var id = 0; id < LocalizationIds.TextCount; id++)
            {
                var path = byValue[localizer.Get(id)];
                result[path] = id;
            }

            return result;
        }

        // ─────────────────────────── 测试 ───────────────────────────

        [Fact]
        public void EveryJsonLeafHasAnIdAndRoundTripsItsValue()
        {
            var ids = FlattenIds();
            var problems = new List<string>();

            foreach (var culture in Cultures)
            {
                var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo(culture));
                var expected = FlattenJson(culture);

                foreach (var pair in expected)
                {
                    int id;
                    if (!ids.TryGetValue(pair.Key, out id))
                    {
                        problems.Add(culture + " 的键 '" + pair.Key + "' 没有对应的 id");
                        continue;
                    }

                    var got = localizer.Get(id);
                    if (!string.Equals(got, pair.Value, StringComparison.Ordinal))
                    {
                        problems.Add(
                            culture + " 键 '" + pair.Key + "'（id=" + id + "）期望 '" +
                            pair.Value + "' 实际 '" + got + "'");
                    }
                }
            }

            Assert.True(
                problems.Count == 0,
                "JSON 与 id 不一致：" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void EveryLeafIdHasAJsonOrigin()
        {
            var l = new Localizer(Catalog.Instance);
            var ids = FlattenIds();
            var jsonKeys = FlattenJson("zh-CN").Keys.ToHashSet(StringComparer.Ordinal);

            var orphans = ids.Keys.Where(k => !jsonKeys.Contains(k)).ToArray();

            Assert.True(
                orphans.Length == 0,
                "以下 id 在 base 语言 JSON 里找不到出处: " + string.Join(", ", orphans));
        }

        /// <summary>
        /// 用容器 API 从每个顶层容器往下走，确认遍历到的叶子集合
        /// 与该容器键前缀下的 JSON 叶子集合完全一致。
        /// </summary>
        [Fact]
        public void WalkingContainersReachesExactlyTheSubtreeLeaves()
        {
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));
            var leafCount = LocalizationIds.TextCount;
            var expected = FlattenJson("zh-CN");

            foreach (var pair in TopLevelContainers())
            {
                var containerId = pair.Value;
                var prefix = pair.Key;

                var reached = new List<string>();
                CollectLeaves(containerId, leafCount, prefix, expected, reached);

                // 按值对照（键路径已不再暴露）。
                var wanted = expected
                    .Where(kv => kv.Key.StartsWith(prefix + ".", StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray();

                var got = reached.OrderBy(v => v, StringComparer.Ordinal).ToArray();

                Assert.True(
                    wanted.SequenceEqual(got, StringComparer.Ordinal),
                    "容器 " + prefix + " 的子树遍历结果不符。缺少: [" +
                    string.Join(", ", wanted.Except(got, StringComparer.Ordinal)) +
                    "] 多出: [" + string.Join(", ", got.Except(wanted, StringComparer.Ordinal)) + "]");
            }
        }

        /// <summary>遍历容器。数组下标按子 id 的出现顺序，对象成员用键的最后一段。</summary>
        private static void CollectLeaves(
            int containerId,
            int leafCount,
            string pathPrefix,
            Dictionary<string, string> expected,
            List<string> sink)
        {
            var children = new Localizer(Catalog.Instance).GetChildren(containerId);

            // 子节点全是容器 → 是数组，下标按顺序编；否则是对象，用键名。
            var allLeaves = children.All(c => c < leafCount);

            for (var i = 0; i < children.Length; i++)
            {
                var childId = children[i];

                if (childId < leafCount)
                {
                    // 没有键路径可查，改用值反查：测试资源的值全局唯一。
                    var value = new Localizer(Catalog.Instance).Get(childId);
                    Assert.True(value.Length > 0, "id=" + childId + " 取到空值");

                    sink.Add(value);
                    continue;
                }

                // 容器：继续下探。
                CollectLeaves(childId, leafCount, pathPrefix, expected, sink);
            }

            _ = allLeaves;
        }

        private static IEnumerable<KeyValuePair<string, int>> TopLevelContainers()
        {
            yield return new KeyValuePair<string, int>("Group", LocalizationIds.Group);
            yield return new KeyValuePair<string, int>("StrArr", LocalizationIds.StrArr);
            yield return new KeyValuePair<string, int>("OneStrArr", LocalizationIds.OneStrArr);
            yield return new KeyValuePair<string, int>("ObjArr", LocalizationIds.ObjArr);
            yield return new KeyValuePair<string, int>("ArrArr", LocalizationIds.ArrArr);
            yield return new KeyValuePair<string, int>("ObjArrArr", LocalizationIds.ObjArrArr);
            yield return new KeyValuePair<string, int>("ArrObj", LocalizationIds.ArrObj);
            yield return new KeyValuePair<string, int>("Cube", LocalizationIds.Cube);
            yield return new KeyValuePair<string, int>("Grid", LocalizationIds.Grid);
            yield return new KeyValuePair<string, int>("DeepObjArr", LocalizationIds.DeepObjArr);
            yield return new KeyValuePair<string, int>("ObjWithObj", LocalizationIds.ObjWithObj);
        }

        [Fact]
        public void EveryTopLevelContainerFlattensToItsJsonValues()
        {
            var l = new Localizer(Catalog.Instance);
            var localizer = new Localizer(Catalog.Instance, CultureInfo.GetCultureInfo("zh-CN"));

            foreach (var pair in TopLevelContainers())
            {
                var prefix = pair.Key;
                var flattened = l.GetAllValues(pair.Value);

                // 期望值：JSON 里该前缀下所有叶子的文本（多重集合比较，顺序不限）
                var expected = FlattenJson("zh-CN")
                    .Where(kv => kv.Key.StartsWith(prefix + ".", StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .ToArray();

                Assert.Equal(
                    expected.OrderBy(v => v, StringComparer.Ordinal),
                    flattened.OrderBy(v => v, StringComparer.Ordinal));
            }
        }

        [Fact]
        public void TextCountEqualsJsonLeafCount()
        {
            foreach (var culture in Cultures)
            {
                Assert.Equal(LocalizationIds.TextCount, FlattenJson(culture).Count);
            }
        }
    }
}






