using System;
using System.ComponentModel;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Sample.Localization;

namespace LocalizationRuntime.Sample
{
    internal static class Program
    {
        private static int Main()
        {
            // 1) 生成器已把三种语言编译进程序集；构造表时按系统 culture 自动选语言。
            var table = new LocalizationTable(L.Catalog);
            var l = new L(table);

            Console.WriteLine("系统探测语言: " + table.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", L.Catalog.CultureNames));
            Console.WriteLine();

            Dump(table, l, "初次加载");

            // 2) 运行时切换，无需重启、无需读文件
            table.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Dump(table, l, "切到 en-US");

            table.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump(table, l, "切到 ja-JP（Common.Cancel 未翻译，应回退到 zh-CN）");

            // 3) 响应式：绑定到 Value 的对象在切语言时收到通知
            var notifications = 0;
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName == nameof(LocalizedText.Value))
                {
                    notifications++;
                }
            };

            l.App.Title.PropertyChanged += handler;

            table.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            l.App.Title.PropertyChanged -= handler;

            Console.WriteLine();
            Console.WriteLine("切回 zh-CN 后 App.Title 收到 PropertyChanged 次数: " + notifications);
            Console.WriteLine("列表引用是否在未切语言时保持不变: " + ReferenceEquals(l.MainWindow.Tabs.Value, l.MainWindow.Tabs.Value));
            Console.WriteLine();

            // 4) 文本之外的东西跟随语言变化
            var cultureEvents = 0;
            table.CultureChanged += (_, e) =>
            {
                cultureEvents++;
                Console.WriteLine("  [CultureChanged] " + e.OldCulture.Name + " → " + e.NewCulture.Name +
                                  " (rev " + e.Revision + ")");
            };

            table.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            table.SetCulture(CultureInfo.GetCultureInfo("en-US"));   // 幂等，不应触发
            Console.WriteLine("CultureChanged 触发次数（第二次同语言应被忽略）: " + cultureEvents);
            Console.WriteLine();
            Console.WriteLine("当前 App.Title.Value = " + l.App.Title.Value);

            return 0;
        }

        private static void Dump(LocalizationTable table, L l, string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture      : " + table.CurrentCulture.Name);
            Console.WriteLine("App.Title    : " + l.App.Title.Value);
            Console.WriteLine("ConfirmDelete: " + string.Format(table.CurrentCulture, l.App.ConfirmDelete.Value, "刀具 A"));
            Console.WriteLine("MainWindow   : " + l.MainWindow.Title.Value);
            Console.WriteLine("Tabs[0..2]   : " + string.Join(" | ", l.MainWindow.Tabs[0], l.MainWindow.Tabs[1], l.MainWindow.Tabs[2]));
            Console.WriteLine("Common.Cancel: " + l.Common.Cancel.Value);
            Console.WriteLine();
        }
    }
}
