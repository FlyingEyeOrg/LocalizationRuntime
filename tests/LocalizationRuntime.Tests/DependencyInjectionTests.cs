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
            public Consumer(Localizer localizer)
            {
                Localizer = localizer;
            }

            public Localizer Localizer { get; }
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
                var localizer = provider.GetRequiredService<Localizer>();

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
                Assert.Same(provider.GetRequiredService<Localizer>(), consumer.Localizer);
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
        public void Localizer_IsSingleton()
        {
            var services = new ServiceCollection();
            services.AddLocalizationRuntime();

            using (var provider = services.BuildServiceProvider())
            {
                Assert.Same(
                    provider.GetRequiredService<Localizer>(),
                    provider.GetRequiredService<Localizer>());
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
                var first = provider.GetRequiredService<Localizer>();
                var second = provider.GetRequiredService<Localizer>();

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
                var localizer = provider.GetRequiredService<Localizer>();

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
            Assert.Single(services, d => d.ServiceType == typeof(ILocalizationCatalog));

            using (var provider = services.BuildServiceProvider())
            {
                Assert.NotNull(provider.GetRequiredService<Localizer>());
            }
        }

        [Fact]
        public void NullServices_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => LocalizationServiceCollectionExtensions.AddLocalizationRuntime(null!));
        }
    }
}
