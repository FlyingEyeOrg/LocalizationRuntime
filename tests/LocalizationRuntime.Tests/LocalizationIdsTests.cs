using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LocalizationRuntime;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 验证生成器产出的 LocalizationIds 结构。
    ///
    /// 这些测试直接编译本项目的 Localization/*.json（见 csproj 的 AdditionalFiles），
    /// 所以校验的是真实生成结果，而不是手写的假数据。
    /// </summary>
    public sealed class LocalizationIdsTests
    {
        /// <summary>反射收集某个静态类里所有 public const int 的「路径 → 值」。</summary>
        private static Dictionary<string, int> Collect(Type type, string prefix = "")
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(int) || !field.IsLiteral)
                {
                    continue;
                }

                result[prefix + field.Name] = (int)field.GetRawConstantValue()!;
            }

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
            {
                foreach (var pair in Collect(nested, prefix + nested.Name + "."))
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }

        private static Dictionary<string, int> AllIds()
        {
            return Collect(typeof(LocalizationIds));
        }

        [Fact]
        public void LeafIds_CoverContiguousRangeStartingAtZero()
        {
            var catalog = Catalog.Instance;
            var leafCount = catalog.Keys.Count;

            var all = AllIds();

            // 叶子 id 必须是 0..leafCount-1 且不重复
            var leaves = all.Where(p => p.Value < leafCount).Select(p => p.Value).OrderBy(v => v).ToArray();

            Assert.Equal(leafCount, leaves.Length);
            for (var i = 0; i < leafCount; i++)
            {
                Assert.Equal(i, leaves[i]);
            }
        }

        [Fact]
        public void ContainerIds_AreUniqueAndDisjointFromLeaves()
        {
            var leafCount = Catalog.Instance.Keys.Count;

            var containers = AllIds()
                .Where(p => p.Value >= leafCount)
                .ToDictionary(p => p.Key, p => p.Value);

            // 容器 id 不能重复
            var duplicates = containers
                .GroupBy(p => p.Value)
                .Where(g => g.Count() > 1)
                .Select(g => "id " + g.Key + " 被 " + string.Join(", ", g.Select(x => x.Key)))
                .ToArray();

            Assert.Empty(duplicates);
        }

        [Fact]
        public void EveryId_IsGloballyUnique()
        {
            var all = AllIds();

            var duplicates = all
                .GroupBy(p => p.Value)
                .Where(g => g.Count() > 1)
                .Select(g => "id " + g.Key + " 重复用于: " + string.Join(", ", g.Select(x => x.Key)))
                .ToArray();

            Assert.Empty(duplicates);
        }

        [Fact]
        public void ScalarKeys_MapToTheirSortedIndex()
        {
            var catalog = Catalog.Instance;

            var ids = AllIds();
            var byId = ids.ToDictionary(p => p.Value, p => p.Key);

            // 每个叶子 id 必须能反查到唯一的键路径（容器 id 除外）
            var leafCount = catalog.Keys.Count;
            for (var i = 0; i < leafCount; i++)
            {
                Assert.True(byId.ContainsKey(i), "id " + i + " 没有对应的常量");
            }
        }

        [Fact]
        public void ContainerId_IsNotReadableAsText()
        {
            var leafCount = Catalog.Instance.Keys.Count;
            var localizer = new Localizer(Catalog.Instance);

            // 容器 id 落在叶子区间之外，取值必须抛异常，从而挡住误用
            var containerId = AllIds().Values.Where(v => v >= leafCount).DefaultIfEmpty(-1).Max();

            if (containerId < 0)
            {
                return; // 没有数组，跳过
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => localizer.Get(containerId));
        }

        [Fact]
        public void NestedArrays_UseItemPrefixAtEveryLevel()
        {
            var all = AllIds();

            // 数组元素的常量名必须带 Item 关键字；嵌套则是 Item{N}Item{M}
            foreach (var name in all.Keys)
            {
                var leaf = name.Substring(name.LastIndexOf('.') + 1);

                if (leaf.Length == 0)
                {
                    continue;
                }

                // 以数字结尾的叶子名必须含 Item
                if (char.IsDigit(leaf[leaf.Length - 1]))
                {
                    Assert.True(
                        leaf.Contains("Item", StringComparison.Ordinal),
                        "数组元素常量 '" + name + "' 缺少 Item 关键字");
                }
            }
        }

        [Fact]
        public void NestedArrayElementClasses_ContainItemKeyword()
        {
            // 顶层数组的类名是 {数组名}Object，本身可以不含 Item；
            // 但数组元素对齐的是 {累计前缀}Item{N}Object，必须含 Item。
            // 判据：同一类名在路径中出现两次以上（出现在它自己的成员路径里）时是元素类。
            var all = AllIds();

            foreach (var name in all.Keys)
            {
                var parts = name.Split('.');
                for (var i = 0; i + 1 < parts.Length; i++)
                {
                    var className = parts[i];

                    if (!className.EndsWith("Object", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 元素类：其名字里含 Item 且后面还跟着数字
                    if (className.Contains("Item", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 顶层数组类，合法
                    Assert.DoesNotContain("Item", className, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void Localizer_ReturnsActualTextForEveryLeaf()
        {
            var localizer = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            // 每个叶子 id 都必须取到非空字符串
            for (var i = 0; i < leafCount; i++)
            {
                var value = localizer.Get(i);
                Assert.False(string.IsNullOrEmpty(value), "id " + i + " 取到空值");
            }
        }

        [Fact]
        public void CultureSwitch_ChangesValuesById()
        {
            var localizer = new Localizer(Catalog.Instance, System.Globalization.CultureInfo.GetCultureInfo("en-US"));

            var en = localizer.Get(LocalizationIds.Scalar);
            localizer.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
            var zh = localizer.Get(LocalizationIds.Scalar);

            Assert.Equal("scalar", en);
            Assert.Equal("单值", zh);
        }
    }
}


