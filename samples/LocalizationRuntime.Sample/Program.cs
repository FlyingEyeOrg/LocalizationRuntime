using System;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Sample.Localization;

namespace LocalizationRuntime.Sample
{
    internal static class Program
    {
        private static int Main()
        {
            // 生成器已把三种语言编译进程序集；构造表时按系统 culture 自动选语言。
            var localizer = new Localizer(L.Catalog);
            var l = new L(localizer);

            Console.WriteLine("系统探测语言: " + localizer.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", L.Catalog.CultureNames));
            Console.WriteLine();

            Dump(localizer, l, "初次加载");

            // 运行时切换，无需重启、无需读文件
            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Dump(localizer, l, "切到 en-US");

            localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump(localizer, l, "切到 ja-JP（Common.Cancel 未翻译，应回退到 zh-CN）");

            // 同一个访问器实例，切语言后再读就是新值
            var title = l.App.Title;
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));

            Console.WriteLine("同一个 LocalizedText 实例切语言后重读: " + title.Value);
            Console.WriteLine("列表引用是否在未切语言时保持不变: " +
                              ReferenceEquals(l.MainWindow.Tabs.Value, l.MainWindow.Tabs.Value));
            Console.WriteLine();

            // 幂等：同语言不递增版本号
            var revisionBefore = localizer.Revision;
            var changed = localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("重复切同一语言: 返回 " + changed + "，版本号 " +
                              revisionBefore + " → " + localizer.Revision);

            return 0;
        }

        private static void Dump(Localizer localizer, L l, string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture      : " + localizer.CurrentCulture.Name);
            Console.WriteLine("App.Title    : " + l.App.Title.Value);
            Console.WriteLine("ConfirmDelete: " + string.Format(localizer.CurrentCulture, l.App.ConfirmDelete.Value, "刀具 A"));
            Console.WriteLine("MainWindow   : " + l.MainWindow.Title.Value);
            Console.WriteLine("Tabs[0..2]   : " + string.Join(" | ", l.MainWindow.Tabs[0], l.MainWindow.Tabs[1], l.MainWindow.Tabs[2]));
            Console.WriteLine("Common.Cancel: " + l.Common.Cancel.Value);
            Console.WriteLine();
        }
    }
}
