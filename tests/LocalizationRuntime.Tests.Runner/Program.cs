using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LocalizationRuntime.Tests.Runner
{
    /// <summary>
    /// 极简 xunit 执行器：反射找到所有 [Fact]，逐个跑，汇总结果。
    /// 存在的理由是本机环境无法启动 VSTest testhost；CI 里仍可用 dotnet test。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            // 可选：用环境变量强制 UI 语言，用来复现 CI（en-US）与本地（zh-CN）
            // 下的行为差异。取值应为 culture 名，例如 en-US。
            var forcedUiCulture = Environment.GetEnvironmentVariable("LOCALIZATION_TEST_UI_CULTURE");
            if (!string.IsNullOrWhiteSpace(forcedUiCulture))
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(forcedUiCulture!);
                System.Globalization.CultureInfo.CurrentUICulture = culture;
                System.Globalization.CultureInfo.CurrentCulture = culture;
                Console.WriteLine("（UI 语言已被强制为 " + culture.Name + "）");
                Console.WriteLine();
            }

            var assembly = typeof(LocalizerTests).Assembly;
            var facts = new List<(Type Type, MethodInfo Method)>();

            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.GetCustomAttribute<FactAttribute>() == null)
                    {
                        continue;
                    }

                    if (method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    facts.Add((type, method));
                }
            }

            var passed = 0;
            var failed = 0;
            var failures = new List<string>();

            foreach (var (type, method) in facts)
            {
                var name = type.Name + "." + method.Name;

                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(type);
                    var result = method.Invoke(instance, null);

                    var task = result as System.Threading.Tasks.Task;
                    if (task != null)
                    {
                        task.GetAwaiter().GetResult();
                    }

                    passed++;
                    Console.WriteLine("  PASS  " + name);
                }
                catch (Exception ex)
                {
                    var actual = ex is TargetInvocationException tie && tie.InnerException != null
                        ? tie.InnerException
                        : ex;

                    failed++;
                    failures.Add(name + Environment.NewLine + actual);
                    Console.WriteLine("  FAIL  " + name);
                    Console.WriteLine("        " + actual.Message);
                }
                finally
                {
                    (instance as IDisposable)?.Dispose();
                }
            }

            Console.WriteLine();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "总计 {0}，通过 {1}，失败 {2}",
                facts.Count,
                passed,
                failed));

            if (failed > 0)
            {
                Console.WriteLine();
                foreach (var f in failures)
                {
                    Console.WriteLine("----");
                    Console.WriteLine(f);
                }
            }

            return failed == 0 ? 0 : 1;
        }
    }
}
