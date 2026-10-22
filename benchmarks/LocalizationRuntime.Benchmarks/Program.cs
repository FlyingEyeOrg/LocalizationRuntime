using System;
using System.Diagnostics;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Benchmarks.Localization;

namespace LocalizationRuntime.Benchmarks
{
    /// <summary>
    /// 不依赖 BenchmarkDotNet 的轻量基准：只测本库自己的几条热路径。
    /// 用法：dotnet run -c Release --project benchmarks\LocalizationRuntime.Benchmarks
    /// </summary>
    internal static class Program
    {
        private const int Iterations = 20_000_000;

        private static void Main()
        {
            // ---- 启动成本：客户端最敏感的一段 ----
            var swStartup = Stopwatch.StartNew();
            var catalog = Catalog.Instance;                // 触发 GeneratedCatalog 的静态构造
            var localizer = new Localizer(catalog, CultureInfo.GetCultureInfo("zh-CN"));
            swStartup.Stop();

            Console.WriteLine("========== 启动 ==========");
            Console.WriteLine("首次访问 catalog + 建表: " +
                              swStartup.Elapsed.TotalMilliseconds.ToString("N2", CultureInfo.InvariantCulture) + " ms");
            Console.WriteLine("语言数量    : " + catalog.CultureNames.Count);
            Console.WriteLine("键数量      : " + catalog.Keys.Count);
            Console.WriteLine();

            // 首次解析一张未缓存的语言表（模拟切到从未用过的语言）
            var swFirstSwitch = Stopwatch.StartNew();
            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            swFirstSwitch.Stop();
            Console.WriteLine("首次切到未缓存语言: " +
                              swFirstSwitch.Elapsed.TotalMilliseconds.ToString("N3", CultureInfo.InvariantCulture) + " ms");
            Console.WriteLine();
            Console.WriteLine("========== 热路径 ==========");
            Console.WriteLine("迭代次数    : " + Iterations.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine();

            // 预热，避免把 JIT 时间算进去
            RunGetById(localizer, 1_000_000);
            RunGetByKey(localizer, 200_000);

            Measure("Get(id) 命中", Iterations, () => RunGetById(localizer, Iterations));
            Measure("Get(string) 命中", Iterations / 4, () => RunGetByKey(localizer, Iterations / 4));

            var switches = 200_000;
            var cultures = new[]
            {
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("ja-JP"),
                CultureInfo.GetCultureInfo("zh-CN")
            };

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < switches; i++)
            {
                localizer.SetCulture(cultures[i % 3]);
            }

            sw.Stop();
            Console.WriteLine("SetCulture 平均: " +
                              (sw.Elapsed.TotalMilliseconds * 1000 / switches).ToString("N3", CultureInfo.InvariantCulture) +
                              " µs");
        }

        private static void RunGetById(Localizer localizer, int n)
        {
            var sink = 0;
            var id = LocalizationIds.CommonObject.Key0;

            for (var i = 0; i < n; i++)
            {
                sink ^= localizer.Get(id).Length;
            }

            Sink = sink;
        }

        private static void RunGetByKey(Localizer localizer, int n)
        {
            var sink = 0;
            for (var i = 0; i < n; i++)
            {
                sink ^= localizer.Get("Common.Key1").Length;
            }

            Sink = sink;
        }

        private static void Measure(string name, long iterations, Action body)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            body();
            sw.Stop();
            var after = GC.GetAllocatedBytesForCurrentThread();

            var nsPerOp = sw.Elapsed.TotalMilliseconds * 1_000_000 / iterations;
            var bytesPerOp = (after - before) / (double)iterations;

            Console.WriteLine(
                name.PadRight(30) +
                nsPerOp.ToString("N2", CultureInfo.InvariantCulture).PadLeft(10) + " ns/op" +
                bytesPerOp.ToString("N2", CultureInfo.InvariantCulture).PadLeft(12) + " B/op");
        }

        private static int Sink;
    }
}


