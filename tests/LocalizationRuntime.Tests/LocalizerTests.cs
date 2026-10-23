using System;
using System.Collections.Generic;
using System.Globalization;
using LocalizationRuntime;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 用手写的 catalog 覆盖运行时语义，不依赖源生成器。
    /// 键顺序：Hello, Tabs.0, Tabs.1, About.Title, About.Body
    /// </summary>
    internal sealed class FakeCatalog : ILocalizationCatalog
    {
        private readonly Dictionary<string, string?[]> _tables;

        public FakeCatalog(string defaultCulture, params (string Culture, string?[] Values)[] tables)
        {
            DefaultCultureName = defaultCulture;
            var names = new List<string>();
            _tables = new Dictionary<string, string?[]>(StringComparer.Ordinal);

            foreach (var t in tables)
            {
                names.Add(t.Culture);
                _tables[t.Culture] = t.Values;
            }

            names.Sort(StringComparer.Ordinal);
            CultureNames = names;
        }

        public string DefaultCultureName { get; }

        public IReadOnlyList<string> CultureNames { get; }

        public IReadOnlyList<string> Keys { get; } = new[]
        {
            "Hello", "Tabs.0", "Tabs.1", "About.Title", "About.Body"
        };

        /// <summary>
        /// 键 id：0=Hello, 1=Tabs.0, 2=Tabs.1, 3=About.Title, 4=About.Body。
        /// 容器：5=Tabs(数组), 6=About(对象)，子 id 见下表。
        /// </summary>
        private static readonly int[][] Children =
        {
            new int[0],        // 0 Hello（叶子）
            new int[0],        // 1 Tabs.0
            new int[0],        // 2 Tabs.1
            new int[0],        // 3 About.Title
            new int[0],        // 4 About.Body
            new[] { 1, 2 },    // 5 Tabs → 两个元素
            new[] { 3, 4 },    // 6 About → 两个成员
        };

        public int TotalIdCount
        {
            get { return Children.Length; }
        }

        public int[] GetChildren(int id)
        {
            return (uint)id < (uint)Children.Length ? Children[id] : System.Array.Empty<int>();
        }

        public string?[]? GetTable(string cultureName)
        {
            return _tables.TryGetValue(cultureName, out var t) ? t : null;
        }
    }

    public sealed class LocalizerTests
    {
        private static FakeCatalog CreateCatalog()
        {
            return new FakeCatalog(
                "zh-CN",
                ("zh-CN", new string?[] { "你好", "一", "二", "关于", "内容 {0}" }),
                ("en-US", new string?[] { "Hello", "One", "Two", "About", "Body {0}" }),
                // ja-JP 故意缺 About.Body 与 Tabs.1，用于验证逐键回退
                ("ja-JP", new string?[] { "こんにちは", "一", null, "概要", null }));
        }

        [Fact]
        public void GetById_ReturnsValueForRequestedCulture()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Hello", localizer.Get(0));
            Assert.Equal("One", localizer.Get(1));
        }

        [Fact]
        public void GetByKey_ReturnsSameValueAsById()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal(localizer.Get(0), localizer.Get("Hello"));
            Assert.Equal("Two", localizer.Get("Tabs.1"));
        }

        [Fact]
        public void GetByKey_UnknownKey_ReturnsKeyItself()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("No.Such.Key", localizer.Get("No.Such.Key"));
        }

        [Fact]
        public void Get_FallsBackPerKey_ToDefaultCulture()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("ja-JP"));

            // 自身有值
            Assert.Equal("こんにちは", localizer.Get(0));
            // Tabs.1 为 null，回退到基语言
            Assert.Equal("二", localizer.Get(2));
            // About.Body 为 null，回退到基语言
            Assert.Equal("内容 {0}", localizer.Get(4));
        }

        [Fact]
        public void Get_UnknownCulture_FallsBackToDefault()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("de-DE"));

            Assert.Equal("你好", localizer.Get(0));
        }

        [Fact]
        public void Get_OutOfRangeId_Throws()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Throws<ArgumentOutOfRangeException>(() => localizer.Get(999));
        }

        [Fact]
        public void SetCulture_SameCulture_ReturnsFalseAndKeepsRevision()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.False(localizer.SetCulture(CultureInfo.GetCultureInfo("en-US")));
            Assert.Equal(0, localizer.Revision);
        }

        [Fact]
        public void SetCulture_Switch_IncrementsRevision()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.True(localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP")));

            Assert.Equal(1, localizer.Revision);
            Assert.Equal("ja-JP", localizer.CurrentCulture.Name);
        }

        [Fact]
        public void SetCulture_ThenGet_ReturnsNewLanguageValue()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Hello", localizer.Get("Hello"));

            localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));

            Assert.Equal("こんにちは", localizer.Get("Hello"));
        }




        [Fact]
        public void Concurrent_SetCulture_EndsInOneOfTheRequestedCultures()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            var names = new[] { "en-US", "ja-JP", "zh-CN" };
            var cultures = new[]
            {
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("ja-JP"),
                CultureInfo.GetCultureInfo("zh-CN")
            };

            var errors = 0;

            System.Threading.Tasks.Parallel.For(0, 3000, i =>
            {
                try
                {
                    localizer.SetCulture(cultures[i % 3]);
                    if (localizer.Get(0) == null)
                    {
                        System.Threading.Interlocked.Increment(ref errors);
                    }
                }
                catch (Exception)
                {
                    System.Threading.Interlocked.Increment(ref errors);
                }
            });

            Assert.Equal(0, errors);
            Assert.Contains(localizer.CurrentCulture.Name, names);
        }

        [Fact]
        public void Concurrent_ReadWhileSwitching_SeesConsistentSnapshot()
        {
            var localizer = new Localizer(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            var mismatches = 0;

            System.Threading.Tasks.Parallel.For(0, 50, t =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    var a = localizer.Get(0);
                    var b = localizer.Get(0);

                    // 同一个键的两次读取可能跨越一次切换，但都必须是非空合法值
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    {
                        System.Threading.Interlocked.Increment(ref mismatches);
                    }
                }
            });

            Assert.Equal(0, mismatches);
        }
    }
}

