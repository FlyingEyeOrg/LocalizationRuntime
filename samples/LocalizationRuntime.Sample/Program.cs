using LocalizationRuntime.Sample.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace LocalizationRuntime.Sample
{
    /// <summary>
    /// 演示 ASP.NET Core 风格的依赖注入。
    ///
    /// 整个接入只有一个动作：<c>AddLocalizationRuntime(Catalog.Instance)</c>。
    /// 之后任何在构造函数里声明 <see cref="LocalizationRuntime.Localizer"/> 的类型
    /// 都会被容器自动注入——见 <see cref="LocalizationDemo"/>。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            var services = new ServiceCollection();

            // ① 注册本地化运行时。数据源是本程序集生成的 Catalog，无需传入任何东西。
            services.AddLocalizationRuntime();

            // ② 注册业务服务。它自己不构造 Localizer，只声明需要。
            services.AddSingleton<LocalizationDemo>();

            using (var provider = services.BuildServiceProvider())
            {
                // ③ 容器负责把 Localizer 递进构造函数
                provider.GetRequiredService<LocalizationDemo>().Run();
            }

            return 0;
        }
    }
}
