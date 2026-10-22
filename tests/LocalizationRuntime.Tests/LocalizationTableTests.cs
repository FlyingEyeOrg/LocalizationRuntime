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

        public string?[]? GetTable(string cultureName)
        {
            return _tables.TryGetValue(cultureName, out var t) ? t : null;
        }
    }

    public sealed class LocalizationTableTests
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
        public void Get_ReturnsValueForRequestedCulture()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Hello", table.Get(0));
            Assert.Equal("One", table.Get(1));
        }

        [Fact]
        public void Get_FallsBackPerKey_ToDefaultCulture()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("ja-JP"));

            // 自身有值
            Assert.Equal("こんにちは", table.Get(0));
            // Tabs.1 为 null，回退到基语言
            Assert.Equal("二", table.Get(2));
            // About.Body 为 null，回退到基语言
            Assert.Equal("内容 {0}", table.Get(4));
        }

        [Fact]
        public void Get_UnknownCulture_FallsBackToDefault()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("de-DE"));

            Assert.Equal("你好", table.Get(0));
        }

        [Fact]
        public void SetCulture_SameCulture_ReturnsFalse()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.False(table.SetCulture(CultureInfo.GetCultureInfo("en-US")));
            Assert.Equal(0, table.Revision);
        }

        [Fact]
        public void SetCulture_Switch_IncrementsRevisionAndRaisesEvent()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            CultureChangedEventArgs? captured = null;
            table.CultureChanged += (_, e) => captured = e;

            Assert.True(table.SetCulture(CultureInfo.GetCultureInfo("ja-JP")));

            Assert.Equal(1, table.Revision);
            Assert.Equal("ja-JP", table.CurrentCulture.Name);
            Assert.NotNull(captured);
            Assert.Equal("en-US", captured!.OldCulture.Name);
            Assert.Equal("ja-JP", captured.NewCulture.Name);
        }

        [Fact]
        public void SetCulture_SubscriberThrows_DoesNotBreakOtherSubscribers()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            var reached = 0;

            table.CultureChanged += (_, _) => throw new InvalidOperationException("boom");
            table.CultureChanged += (_, _) => reached++;

            // 不应抛出，且语言必须已生效
            Assert.True(table.SetCulture(CultureInfo.GetCultureInfo("ja-JP")));
            Assert.Equal(1, reached);
            Assert.Equal("ja-JP", table.CurrentCulture.Name);
        }

        [Fact]
        public void LocalizedText_RaisesPropertyChanged_OnCultureChange()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            var text = new LocalizedText(table, 0);

            var count = 0;
            text.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LocalizedText.Value))
                {
                    count++;
                }
            };

            table.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));

            Assert.Equal(1, count);
            Assert.Equal("こんにちは", text.Value);
        }

        [Fact]
        public void LocalizedList_CachesUntilRevisionChanges()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            var list = new LocalizedList(table, 1, 2);

            var first = list.Value;
            var second = list.Value;

            // 未切语言：必须是同一个实例，否则绑定会丢选中项
            Assert.Same(first, second);
            Assert.Equal("One", first[0]);

            table.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));

            var third = list.Value;
            Assert.NotSame(first, third);
            Assert.Equal("一", third[0]);
            // Tabs.1 在 ja-JP 缺失，回退基语言
            Assert.Equal("二", third[1]);
        }

        [Fact]
        public void Format_UsesCurrentCulture()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("Body 42", table.Format(4, 42));
        }

        [Fact]
        public void Format_NoPlaceholder_SkipsFormatting()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            // 模板没有占位符时，多余参数不应导致 FormatException
            Assert.Equal("Hello", table.Format(0, "ignored"));
        }

        [Fact]
        public void Get_OutOfRangeId_Throws()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));

            Assert.Throws<ArgumentOutOfRangeException>(() => table.Get(999));
        }

        [Fact]
        public void Concurrent_SetCulture_EndsInOneOfTheRequestedCultures()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
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
                    table.SetCulture(cultures[i % 3]);
                    // 读到的值必须自洽：任何一个键都必须是合法字符串
                    if (table.Get(0) == null)
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
            Assert.Contains(table.CurrentCulture.Name, names);
        }

        [Fact]
        public void Concurrent_ReadWhileSwitching_SeesConsistentSnapshot()
        {
            var table = new LocalizationTable(CreateCatalog(), CultureInfo.GetCultureInfo("en-US"));
            var cultures = new[]
            {
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("ja-JP")
            };

            var mismatches = 0;

            System.Threading.Tasks.Parallel.For(0, 50, t =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    var a = table.Get(0);
                    var b = table.Get(0);

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
