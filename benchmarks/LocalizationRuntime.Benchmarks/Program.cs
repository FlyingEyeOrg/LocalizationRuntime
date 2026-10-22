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
            var table = new LocalizationTable(L.Catalog, CultureInfo.GetCultureInfo("zh-CN"));
            var l = new L(table);

            Console.WriteLine("键数量      : " + table.Keys.Count);
            Console.WriteLine("语言数量    : " + L.Catalog.CultureNames.Count);
            Console.WriteLine("迭代次数    : " + Iterations.ToString("N0", CultureInfo.InvariantCulture));
            Console.WriteLine();

            // 预热，避免把 JIT 时间算进去
            RunGet(l, 1_000_000);
            RunGetByKey(table, 200_000);
            RunFormat(l, 100_000);

            Measure("Get(id) 命中", Iterations, () => RunGet(l, Iterations));
            Measure("Get(string) 命中", Iterations / 4, () => RunGetByKey(table, Iterations / 4));
            Measure("Format(1 参数)", Iterations / 20, () => RunFormat(l, Iterations / 20));

            Measure("LocalizedList.Value 命中缓存", Iterations / 4, () => RunList(l, Iterations / 4));

            var switches = 200_000;
            var names = new[] { "en-US", "ja-JP", "zh-CN" };
            var cultures = new[]
            {
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("ja-JP"),
                CultureInfo.GetCultureInfo("zh-CN")
            };

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < switches; i++)
            {
                table.SetCulture(cultures[i % 3]);
            }

            sw.Stop();
            Console.WriteLine("SetCulture 平均: " +
                              (sw.Elapsed.TotalMilliseconds * 1000 / switches).ToString("N3", CultureInfo.InvariantCulture) +
                              " µs（culture 已预解析，单元 " + (390 * 3 + 1) + " 个）");
        }

        private static void RunGet(L l, int n)
        {
            var sink = 0;
            for (var i = 0; i < n; i++)
            {
                sink ^= l.Common.Key0.Value.Length;
            }

            Sink = sink;
        }

        private static void RunGetByKey(LocalizationTable t, int n)
        {
            var sink = 0;
            for (var i = 0; i < n; i++)
            {
                sink ^= t.Get("Common.Key1").Length;
            }

            Sink = sink;
        }

        private static void RunFormat(L l, int n)
        {
            var sink = 0;
            for (var i = 0; i < n; i++)
            {
                sink ^= l.Common.Key2.Format(i).Length;            }

            Sink = sink;
        }

        private static void RunList(L l, int n)
        {
            var sink = 0;
            for (var i = 0; i < n; i++)
            {
                sink ^= l.Menus.Items.Value.Count;
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
