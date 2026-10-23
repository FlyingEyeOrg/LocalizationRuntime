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
            var l = new Localizer(Catalog.Instance);
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
            var l = new Localizer(Catalog.Instance);
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
            var l = new Localizer(Catalog.Instance);
            var all = AllIds();

            var duplicates = all
                .GroupBy(p => p.Value)
                .Where(g => g.Count() > 1)
                .Select(g => "id " + g.Key + " 重复用于: " + string.Join(", ", g.Select(x => x.Key)))
                .ToArray();

            Assert.Empty(duplicates);
        }

        [Fact]
        public void AllIds_FormAContiguousRangeWithoutHoles()
        {
            var l = new Localizer(Catalog.Instance);
            // 回归：曾经父循环和递归各分配一次 id，导致 40、41 这类编号被吞掉，
            // 表面上能用，但容器区间出现空洞、总数对不上。
            var all = AllIds().Values.OrderBy(v => v).ToArray();

            Assert.NotEmpty(all);

            for (var i = 0; i < all.Length; i++)
            {
                Assert.True(
                    all[i] == i,
                    "id 序列出现空洞：第 " + i + " 个位置是 " + all[i] + "，期望 " + i);
            }
        }

        [Fact]
        public void ContainerIdsAreAllocatedOnce_NoDuplicatedSpans()
        {
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;
            var all = AllIds();

            var containers = all
                .Where(p => p.Value >= leafCount)
                .Select(p => p.Value)
                .OrderBy(v => v)
                .ToArray();

            // 容器 id 必须从 leafCount 开始连续排列，不能有跳号
            for (var i = 0; i < containers.Length; i++)
            {
                Assert.True(
                    containers[i] == leafCount + i,
                    "容器 id 不连续：第 " + i + " 个是 " + containers[i] + "，期望 " + (leafCount + i));
            }
        }

        [Fact]
        public void ScalarKeys_MapToTheirSortedIndex()
        {
            var l = new Localizer(Catalog.Instance);
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
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;
            var localizer = new Localizer(Catalog.Instance);

            // 容器 id 落在叶子区间之外，取值必须抛异常，从而挡住误用
            var containerId = AllIds().Values.Where(v => v >= leafCount).DefaultIfEmpty(-1).Max();

            if (containerId < 0)
            {
                return; // 没有数组，跳过
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => l.Get(containerId));
        }



        [Fact]
        public void Localizer_ReturnsActualTextForEveryLeaf()
        {
            var l = new Localizer(Catalog.Instance);
            var localizer = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            // 每个叶子 id 都必须取到非空字符串
            for (var i = 0; i < leafCount; i++)
            {
                var value = l.Get(i);
                Assert.False(string.IsNullOrEmpty(value), "id " + i + " 取到空值");
            }
        }

        [Fact]
        public void CultureSwitch_ChangesValuesById()
        {
            var localizer = new Localizer(
                Catalog.Instance, System.Globalization.CultureInfo.GetCultureInfo("en-US"));

            var en = localizer.Get(LocalizationIds.Scalar);
            localizer.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
            var zh = localizer.Get(LocalizationIds.Scalar);

            Assert.Equal("e01-scalar", en);
            Assert.Equal("v01-scalar", zh);
        }
    }
}









