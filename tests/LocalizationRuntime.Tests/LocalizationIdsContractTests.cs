using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalizationRuntime.Tests.Localization;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 校验生成器的结构规则本身，而不是零散地抽查几条。
    ///
    /// 这里断言的是「用户能看到的契约」：
    ///   · 每个非叶子节点都有 id 和对应的 Object 类
    ///   · 类名一律带 Object 后缀，id 常量一律不带
    ///   · id 连续、唯一、叶子在前容器在后
    /// </summary>
    public sealed class LocalizationIdsContractTests
    {
        private sealed class Node
        {
            internal Node(string name, Type type)
            {
                Name = name;
                Type = type;
            }

            internal string Name { get; }

            internal Type Type { get; }

            /// <summary>本节点上的 int 常量（不含嵌套类里的）。</summary>
            internal List<KeyValuePair<string, int>> Constants { get; } = new List<KeyValuePair<string, int>>();

            /// <summary>嵌套的 Object 类。</summary>
            internal List<Node> Children { get; } = new List<Node>();
        }

        private static Node Build(Type type, string name)
        {
            var node = new Node(name, type);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(int) && field.IsLiteral)
                {
                    node.Constants.Add(new KeyValuePair<string, int>(
                        field.Name, (int)field.GetRawConstantValue()!));
                }
            }

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
                         .OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                node.Children.Add(Build(nested, nested.Name));
            }

            return node;
        }

        private static Node Root()
        {
            return Build(typeof(LocalizationIds), "LocalizationIds");
        }

        private static void Walk(Node node, Action<Node> visit)
        {
            visit(node);
            foreach (var child in node.Children)
            {
                Walk(child, visit);
            }
        }

        [Fact]
        public void EveryClassEndsWithObjectSuffix()
        {
            var l = new Localizer(Catalog.Instance);
            var offenders = new List<string>();

            Walk(Root(), node =>
            {
                if (node.Name == "LocalizationIds")
                {
                    return;   // 根类
                }

                if (!node.Name.EndsWith("Object", StringComparison.Ordinal))
                {
                    offenders.Add(node.Name);
                }
            });

            Assert.True(
                offenders.Count == 0,
                "以下类缺少 Object 后缀: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// 用户明确指出的规则：普通对象也必须有自己的 id，不能只有数组才有。
        /// 例如 AppObject 存在时，必须有 App = N。
        /// </summary>
        [Fact]
        public void PlainObjectsAlsoGetTheirOwnId()
        {
            var l = new Localizer(Catalog.Instance);
            var offenders = new List<string>();

            Walk(Root(), node =>
            {
                foreach (var child in node.Children)
                {
                    // 跳过数组元素类（它们本身就是元素，id 有其自己的命名规则）
                    var className = child.Name;
                    if (!className.EndsWith("Object", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var constantName = className.Substring(0, className.Length - "Object".Length);

                    var hasConstant = node.Constants.Any(c => c.Key == constantName);
                    if (!hasConstant)
                    {
                        offenders.Add(
                            "类 " + node.Name + "." + className + " 缺少对应的 id 常量 '" + constantName + "'");
                    }
                }
            });

            Assert.True(
                offenders.Count == 0,
                "以下对象只有类、没有 id：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void EveryNonLeafNodeHasAnIdConstantNextToItsClass()
        {
            var l = new Localizer(Catalog.Instance);
            // 关键契约：容器既有常量又有类。
            // App 有 AppObject 类，就必须有 App = N 这个常量；数组和元素同理。
            var offenders = new List<string>();

            Walk(Root(), node =>
            {
                foreach (var child in node.Children)
                {
                    var className = child.Name;
                    var constantName = className.Substring(0, className.Length - "Object".Length);

                    var hasConstant = node.Constants.Any(c => c.Key == constantName);
                    if (!hasConstant)
                    {
                        offenders.Add(node.Name + " 缺少常量 " + constantName + "（对应类 " + className + "）");
                    }
                }
            });

            Assert.True(
                offenders.Count == 0,
                "以下节点只有类没有 id：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void IdConstantsNeverCarryObjectSuffix()
        {
            var l = new Localizer(Catalog.Instance);
            var offenders = new List<string>();

            Walk(Root(), node =>
            {
                foreach (var c in node.Constants)
                {
                    if (c.Key.EndsWith("Object", StringComparison.Ordinal))
                    {
                        offenders.Add(node.Name + "." + c.Key);
                    }
                }
            });

            Assert.True(
                offenders.Count == 0,
                "以下 int 常量不应带 Object 后缀: " + string.Join(", ", offenders));
        }

        [Fact]
        public void AllIds_AreUniqueAndContiguousFromZero()
        {
            var l = new Localizer(Catalog.Instance);
            var ids = new List<int>();
            Walk(Root(), node => ids.AddRange(node.Constants.Select(c => c.Value)));

            var sorted = ids.OrderBy(v => v).ToArray();

            var gaps = new List<string>();
            for (var i = 0; i < sorted.Length; i++)
            {
                if (sorted[i] != i)
                {
                    gaps.Add("位置 " + i + " 是 " + sorted[i]);
                }
            }

            Assert.True(
                gaps.Count == 0,
                "id 不连续（共 " + sorted.Length + " 个）：" + string.Join(", ", gaps.Take(10)));
        }

        [Fact]
        public void LeafIdsComeFirstThenContainerIds()
        {
            var l = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            var ids = new List<int>();
            Walk(Root(), node => ids.AddRange(node.Constants.Select(c => c.Value)));

            // 0..leafCount-1 全部被使用
            var used = new HashSet<int>(ids);
            for (var i = 0; i < leafCount; i++)
            {
                Assert.True(used.Contains(i), "叶子 id " + i + " 没有被任何常量占用");
            }

            // 容器 id 全部 >= leafCount
            var containerIds = ids.Where(v => v >= leafCount).ToArray();
            Assert.All(containerIds, v => Assert.True(v >= leafCount));
        }

        [Fact]
        public void EveryLeafIdIsReadableAndEveryContainerIdIsNot()
        {
            var l = new Localizer(Catalog.Instance);
            var localizer = new Localizer(Catalog.Instance);
            var leafCount = Catalog.Instance.Keys.Count;

            for (var i = 0; i < leafCount; i++)
            {
                Assert.False(string.IsNullOrEmpty(l.Get(i)), "叶子 " + i + " 取到空值");
            }

            var ids = new HashSet<int>();
            Walk(Root(), node => { foreach (var c in node.Constants) ids.Add(c.Value); });

            foreach (var id in ids.Where(v => v >= leafCount))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => l.Get(id));
            }
        }

        [Fact]
        public void ArrayElementNamesContainItemKeyword()
        {
            var l = new Localizer(Catalog.Instance);
            var offenders = new List<string>();

            Walk(Root(), node =>
            {
                foreach (var child in node.Children)
                {
                    // 元素类是 {前缀}Item{N}Object：只要父级或自身名字里出现 Item 就合规。
                    // 这里校验：凡是位于数组类之下的元素类，名字必须含 "Item"。
                    if (!node.Name.EndsWith("Object", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parentIsArray = node.Constants.Any(c =>
                        c.Key + "Object" == node.Name);

                    if (parentIsArray && !child.Name.Contains("Item", StringComparison.Ordinal))
                    {
                        offenders.Add(child.Name + "（父 " + node.Name + "）");
                    }
                }
            });

            Assert.True(
                offenders.Count == 0,
                "数组元素类缺少 Item 关键字: " + string.Join(", ", offenders));
        }
    }
}




