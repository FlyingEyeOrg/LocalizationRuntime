using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
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
        private readonly Dictionary<string, string?[]> _textValues;

        public FakeCatalog(string defaultCulture, params (string Culture, string?[] Values)[] cultures)
        {
            DefaultCultureName = defaultCulture;
            var names = new List<string>();
            _textValues = new Dictionary<string, string?[]>(StringComparer.Ordinal);

            foreach (var culture in cultures)
            {
                names.Add(culture.Culture);
                _textValues[culture.Culture] = culture.Values;
            }

            names.Sort(StringComparer.Ordinal);
            CultureNames = names;
        }

        public string DefaultCultureName { get; }

        public IReadOnlyList<string> CultureNames { get; }

        /// <summary>叶子 id 个数：Hello, Tabs.0, Tabs.1, About.Title, About.Body。</summary>
        public int TextCount
        {
            get { return 5; }
        }

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


        public int[] GetChildren(int id)
        {
            return (uint)id < (uint)Children.Length ? Children[id] : System.Array.Empty<int>();
        }

        public string?[]? GetTextValues(string cultureName)
        {
            return _textValues.TryGetValue(cultureName, out var values) ? values : null;
        }
    }

    /// <summary>
    /// 运行时语义测试。Localizer 通过构造函数接收 catalog，每个用例用 FakeCatalog 隔离。
    /// </summary>
    public sealed class LocalizerTests
    {
        private static Localizer Use(FakeCatalog catalog, string culture)
        {
            return new Localizer(catalog, CultureInfo.GetCultureInfo(culture));
        }

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
            var l = Use(CreateCatalog(), "en-US");

            Assert.Equal("Hello", l.Get(0));
            Assert.Equal("One", l.Get(1));
        }



        [Fact]
        public void Get_FallsBackPerKey_ToDefaultCulture()
        {
            var l = Use(CreateCatalog(), "ja-JP");

            // 自身有值
            Assert.Equal("こんにちは", l.Get(0));
            // Tabs.1 为 null，回退到基语言
            Assert.Equal("二", l.Get(2));
            // About.Body 为 null，回退到基语言
            Assert.Equal("内容 {0}", l.Get(4));
        }

        [Fact]
        public void Get_UnknownCulture_FallsBackToDefault()
        {
            var l = Use(CreateCatalog(), "de-DE");

            Assert.Equal("你好", l.Get(0));
            Assert.Equal("zh-CN", l.CurrentCulture.Name);
        }

        [Fact]
        public void Get_OutOfRangeId_ThrowsArgumentOutOfRange()
        {
            var l = Use(CreateCatalog(), "en-US");

            // 负数和远超 id 空间的数字：确实越界
            Assert.Throws<ArgumentOutOfRangeException>(() => l.Get(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => l.Get(99));
        }

        [Fact]
        public void Get_ContainerId_ThrowsInvalidOperation()
        {
            var l = Use(CreateCatalog(), "en-US");

            // FakeCatalog 的 TextCount 是 5；5=Tabs(数组), 6=About(对象) 都是容器。
            // 这不是越界，而是"合法 id 但没有文本"，异常类型要能把两件事分开。
            Assert.NotEmpty(l.GetChildren(5));
            Assert.NotEmpty(l.GetChildren(6));

            Assert.Throws<InvalidOperationException>(() => l.Get(5));
            Assert.Throws<InvalidOperationException>(() => l.Get(6));
        }

        [Fact]
        public void SetCulture_SameCulture_ReturnsFalseAndKeepsRevision()
        {
            var l = Use(CreateCatalog(), "zh-CN");

            var before = l.Revision;
            Assert.False(l.SetCulture(CultureInfo.GetCultureInfo("zh-CN")));
            Assert.Equal(before, l.Revision);
        }

        [Fact]
        public void SetCulture_Switch_IncrementsRevision()
        {
            var l = Use(CreateCatalog(), "zh-CN");

            var before = l.Revision;
            Assert.True(l.SetCulture(CultureInfo.GetCultureInfo("en-US")));
            Assert.Equal(before + 1, l.Revision);
        }

        [Fact]
        public void SetCulture_ThenGet_ReturnsNewLanguageValue()
        {
            var l = Use(CreateCatalog(), "zh-CN");
            Assert.Equal("你好", l.Get(0));

            l.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal("Hello", l.Get(0));
        }

        [Fact]
        public void SetCulture_UnknownCulture_LandsOnDefault()
        {
            var l = Use(CreateCatalog(), "en-US");

            Assert.True(l.SetCulture(CultureInfo.GetCultureInfo("de-DE")));
            Assert.Equal("zh-CN", l.CurrentCulture.Name);
            Assert.Equal("你好", l.Get(0));
        }

        // ── 容器 ──

        [Fact]
        public void GetChildCount_ReturnsZeroForLeaf()
        {
            var l = Use(CreateCatalog(), "en-US");
            Assert.Equal(0, l.GetChildCount(0));
        }

        [Fact]
        public void GetChildCount_ReturnsArrayLength()
        {
            var l = Use(CreateCatalog(), "en-US");
            Assert.Equal(2, l.GetChildCount(5));   // Tabs
            Assert.Equal(2, l.GetChildCount(6));   // About
        }

        [Fact]
        public void GetChildren_ReturnsChildIds()
        {
            var l = Use(CreateCatalog(), "en-US");

            var tabs = l.GetChildren(5);
            Assert.Equal(new[] { 1, 2 }, tabs);

            var about = l.GetChildren(6);
            Assert.Equal(new[] { 3, 4 }, about);
        }

        [Fact]
        public void GetValues_ReturnsAllTexts()
        {
            var l = Use(CreateCatalog(), "en-US");
            Assert.Equal(new[] { "One", "Two" }, l.GetValues(5));
        }

        [Fact]
        public void GetAllValues_FlattensObject()
        {
            var l = Use(CreateCatalog(), "en-US");
            Assert.Equal(new[] { "About", "Body {0}" }, l.GetAllValues(6));
        }

        [Fact]
        public void GetChildren_OutOfRange_IsEmpty()
        {
            var l = Use(CreateCatalog(), "en-US");
            Assert.Empty(l.GetChildren(999));
            Assert.Empty(l.GetChildren(-1));
        }

        // ── 并发 ──

        [Fact]
        public void Concurrent_SetCulture_EndsInOneOfTheRequestedCultures()
        {
            var l = Use(CreateCatalog(), "zh-CN");

            var cultures = new[]
            {
                CultureInfo.GetCultureInfo("zh-CN"),
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("ja-JP"),
            };

            var threads = new Thread[6];
            for (var i = 0; i < threads.Length; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    for (var n = 0; n < 2000; n++)
                    {
                        l.SetCulture(cultures[(index + n) % cultures.Length]);
                    }
                });
                threads[i].Start();
            }

            foreach (var t in threads)
            {
                t.Join();
            }

            var final = l.CurrentCulture.Name;
            Assert.Contains(final, new[] { "zh-CN", "en-US", "ja-JP" });
        }

        /// <summary>
        /// 并发读与切语言时，单次 <c>Get</c> 必须返回某个完整语言的值——不能撕裂成半新半旧。
        ///
        /// 注意不能断言「连续两次 Get 属于同一语言」：两次调用之间可能发生切换，
        /// 这是全局状态下的正常语义。要验证快照一致性，须在一次调用内比对，
        /// 所以这里改为检查每个取到的值都属于已知集合，且不同语言的值不会相互污染。
        /// </summary>
        [Fact]
        public void Concurrent_ReadWhileSwitching_NeverSeesATornValue()
        {
            var l = Use(CreateCatalog(), "en-US");

            var stop = false;
            var bad = 0;

            // 每个 id 在各语言下的合法值
            var legal = new Dictionary<int, HashSet<string>>
            {
                [0] = new HashSet<string>(StringComparer.Ordinal) { "你好", "Hello", "こんにちは" },
                [1] = new HashSet<string>(StringComparer.Ordinal) { "一", "One" },
                [3] = new HashSet<string>(StringComparer.Ordinal) { "关于", "About", "概要" },
                [4] = new HashSet<string>(StringComparer.Ordinal) { "内容 {0}", "Body {0}" },
            };

            var reader = new Thread(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    for (var id = 0; id < 5; id++)
                    {
                        HashSet<string>? allowed;
                        if (!legal.TryGetValue(id, out allowed))
                        {
                            continue;
                        }

                        var value = l.Get(id);
                        if (!allowed.Contains(value))
                        {
                            // 出现了不属于任何语言的字符串：说明读到了撕裂的中间态
                            Interlocked.Increment(ref bad);
                        }
                    }
                }
            });

            reader.Start();

            for (var n = 0; n < 3000; n++)
            {
                l.SetCulture(CultureInfo.GetCultureInfo(n % 2 == 0 ? "zh-CN" : "en-US"));
            }

            Volatile.Write(ref stop, true);
            reader.Join();

            Assert.Equal(0, bad);
        }

        /// <summary>
        /// 快照一致性：一次 Get 内部读的是同一张数组，因此同一 id 反复取值
        /// 在切换发生的瞬间也不会返回空或错位的内容。
        /// </summary>
        [Fact]
        public void Concurrent_RepeatedReadOfSameId_IsAlwaysOneOfTheLanguages()
        {
            var l = Use(CreateCatalog(), "zh-CN");

            var stop = false;
            var bad = 0;

            var reader = new Thread(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    var value = l.Get(0);
                    if (value != "你好" && value != "Hello")
                    {
                        Interlocked.Increment(ref bad);
                    }
                }
            });

            reader.Start();

            for (var n = 0; n < 5000; n++)
            {
                l.SetCulture(CultureInfo.GetCultureInfo(n % 2 == 0 ? "zh-CN" : "en-US"));
            }

            Volatile.Write(ref stop, true);
            reader.Join();

            Assert.Equal(0, bad);
        }
    }
}







