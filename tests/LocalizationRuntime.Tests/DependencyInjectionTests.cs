using System;
using System.Globalization;
using LocalizationRuntime.Tests.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LocalizationRuntime.Tests
{
    /// <summary>
    /// 依赖注入接入的测试。
    ///
    /// 注册代码由生成器产出，所以这里用到的 <c>AddLocalizationRuntime()</c>
    /// 就是使用者实际会调用的那个——无需传入 Catalog。
    /// </summary>
    public sealed class DependencyInjectionTests
    {
        /// <summary>典型调用方：只在构造函数里声明需要什么，自己不构造。</summary>
        private sealed class Consumer
        {
            public Consumer(ILocalizer localizer)
            {
                Localizer = localizer;
            }

            public ILocalizer Localizer { get; }
        }

        /// <summary>二级依赖：验证注入链能继续往下解析。</summary>
        private sealed class Outer
        {
            public Outer(Consumer inner)
            {
                Inner = inner;
            }

            public Consumer Inner { get; }
        }

        [Fact]
        public void AddLocalizationRuntime_NeedsNoArguments()
        {
            var services = new ServiceCollection();

            // 数据源是本程序集生成的 Catalog，调用方不必传
            services.AddLocalizationRuntime();

            using (var provider = services.BuildServiceProvider())
            {
                var localizer = provider.GetRequiredService<ILocalizer>();

                Assert.NotNull(localizer);
                Assert.False(string.IsNullOrEmpty(localizer.Get(LocalizationIds.Scalar)));
            }
        }

        [Fact]
        public void Catalog_IsResolvableByItsInterface()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();

            using (var provider = services.BuildServiceProvider())
            {
                var catalog = provider.GetRequiredService<ILocalizationCatalog>();

                Assert.Equal(LocalizationIds.TextCount, catalog.TextCount);
            }
        }

        [Fact]
        public void ConstructorInjection_SuppliesTheLocalizer()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();
            services.AddSingleton<Consumer>();

            using (var provider = services.BuildServiceProvider())
            {
                // 只声明构造参数，业务代码里没有任何 new Localizer
                var consumer = provider.GetRequiredService<Consumer>();

                Assert.NotNull(consumer.Localizer);
                Assert.Same(provider.GetRequiredService<ILocalizer>(), consumer.Localizer);
            }
        }

        [Fact]
        public void ConstructorInjection_WorksThroughNestedDependencies()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();

            // 真实的依赖链：Outer → Consumer → Localizer。
            // 只有叶子由本库注册，中间层是业务自己注册的。
            services.AddSingleton<Consumer>();
            services.AddSingleton<Outer>();

            using (var provider = services.BuildServiceProvider())
            {
                var outer = provider.GetRequiredService<Outer>();

                Assert.NotNull(outer.Inner.Localizer);
                Assert.False(string.IsNullOrEmpty(
                    outer.Inner.Localizer.Get(LocalizationIds.Scalar)));
            }
        }

        [Fact]
        public void ILocalizer_IsSingleton()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();

            using (var provider = services.BuildServiceProvider())
            {
                Assert.Same(
                    provider.GetRequiredService<ILocalizer>(),
                    provider.GetRequiredService<ILocalizer>());
            }
        }

        /// <summary>
        /// 接口与具体类型必须解析到同一个对象，否则「切语言全局生效」就不成立了。
        /// </summary>
        [Fact]
        public void ILocalizer_AndConcreteType_AreTheSameInstance()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();

            using (var provider = services.BuildServiceProvider())
            {
                Assert.Same(
                    provider.GetRequiredService<Localizer>(),
                    provider.GetRequiredService<ILocalizer>());
            }
        }

        /// <summary>
        /// 单例意味着切语言是全局的：这符合库的设计（当前语言是进程级状态）。
        /// </summary>
        [Fact]
        public void SingletonMeansCultureSwitchIsShared()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime(CultureInfo.GetCultureInfo("zh-CN"));

            using (var provider = services.BuildServiceProvider())
            {
                var first = provider.GetRequiredService<ILocalizer>();
                var second = provider.GetRequiredService<ILocalizer>();

                first.SetCulture(CultureInfo.GetCultureInfo("en-US"));

                Assert.Equal("en-US", second.CurrentCulture.Name);
                Assert.Equal("e01-scalar", second.Get(LocalizationIds.Scalar));
            }
        }

        [Fact]
        public void InitialCulture_IsHonored()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime(CultureInfo.GetCultureInfo("en-US"));

            using (var provider = services.BuildServiceProvider())
            {
                var localizer = provider.GetRequiredService<ILocalizer>();

                Assert.Equal("en-US", localizer.CurrentCulture.Name);
                Assert.Equal("e01-scalar", localizer.Get(LocalizationIds.Scalar));
            }
        }

        [Fact]
        public void CalledTwice_DoesNotDuplicateRegistrations()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();
            services.AddLocalizationRuntime();

            // TryAdd 语义：每样只有一个注册
            Assert.Single(services, d => d.ServiceType == typeof(Localizer));
            Assert.Single(services, d => d.ServiceType == typeof(ILocalizer));
            Assert.Single(services, d => d.ServiceType == typeof(ILocalizationCatalog));

            using (var provider = services.BuildServiceProvider())
            {
                Assert.NotNull(provider.GetRequiredService<ILocalizer>());
            }
        }

        [Fact]
        public void NullServices_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => LocalizationServiceCollectionExtensions.AddLocalizationRuntime(null!));
        }

        // ─────────────── 接口带来的可替身性 ───────────────

        /// <summary>
        /// 手写替身：只需实现接口，不必建立 Catalog、不必碰源生成器。
        /// 这正是业务代码依赖 ILocalizer 而不是具体类型的意义。
        /// </summary>
        private sealed class StubLocalizer : ILocalizer
        {
            public CultureInfo CurrentCulture { get; private set; } =
                CultureInfo.GetCultureInfo("en-US");

            public int Revision { get; private set; }

            public string Get(int id)
            {
                return "stub:" + id;
            }

            public int GetChildCount(int containerId)
            {
                return 0;
            }

            public int[] GetChildren(int containerId)
            {
                return Array.Empty<int>();
            }

            public string[] GetValues(int containerId)
            {
                return Array.Empty<string>();
            }

            public string[] GetAllValues(int containerId)
            {
                return Array.Empty<string>();
            }

            public bool SetCulture(CultureInfo culture)
            {
                CurrentCulture = culture;
                Revision++;
                return true;
            }
        }

        [Fact]
        public void BusinessCodeCanBeTestedWithAStubLocalizer()
        {
            var services = new ServiceCollection();

            // 不注册本库，直接塞一个替身
            services.AddSingleton<ILocalizer>(new StubLocalizer());
            services.AddSingleton<Consumer>();

            using (var provider = services.BuildServiceProvider())
            {
                var consumer = provider.GetRequiredService<Consumer>();

                Assert.Equal("stub:0", consumer.Localizer.Get(0));
                Assert.IsType<StubLocalizer>(consumer.Localizer);
            }
        }
    }
}
