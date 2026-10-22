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
            // 生成器已把三种语言编译进程序集；构造 localizer 时按系统 culture 自动选语言。
            var localizer = new Localizer(Catalog.Instance);

            Console.WriteLine("系统探测语言: " + localizer.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", Catalog.Instance.CultureNames));
            Console.WriteLine();

            Dump(localizer, "初次加载");

            // 运行时切换，无需重启、无需读文件
            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Dump(localizer, "切到 en-US");

            localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump(localizer, "切到 ja-JP（Common.Cancel 未翻译，应回退到 zh-CN）");

            // 切回中文：同一个 Localizer 实例，再读就是中文
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));

            Console.WriteLine("切回 zh-CN 后重读: " + localizer.Get(LocalizationIds.App.Title));
            Console.WriteLine();

            // 幂等：同语言不递增版本号
            var revisionBefore = localizer.Revision;
            var changed = localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("重复切同一语言: 返回 " + changed + "，版本号 " +
                              revisionBefore + " → " + localizer.Revision);

            return 0;
        }

        private static void Dump(Localizer localizer, string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture      : " + localizer.CurrentCulture.Name);
            Console.WriteLine("App.Title    : " + localizer.Get(LocalizationIds.App.Title));
            Console.WriteLine("ConfirmDelete: " +
                string.Format(localizer.CurrentCulture,
                    localizer.Get(LocalizationIds.App.ConfirmDelete), "刀具 A"));
            Console.WriteLine("MainWindow   : " + localizer.Get(LocalizationIds.MainWindow.Title));
            Console.WriteLine("Common.Cancel: " + localizer.Get(LocalizationIds.Common.Cancel));
            Console.WriteLine();

            Console.Write("Tabs         : ");
            for (var i = 0; i < 3; i++)
            {
                if (i > 0)
                {
                    Console.Write(" | ");
                }

                Console.Write(localizer.Get(TabsId(i)));
            }

            Console.WriteLine();
            Console.WriteLine();
        }

        /// <summary>
        /// 字符串数组的元素 id 是编译期常量，逐个写死；这里用下标选，
        /// 只是为了让示例能循环打印，实际代码直接写 LocalizationIds.MainWindow.TabsObject.TabsItem0。
        /// </summary>
        private static int TabsId(int index)
        {
            switch (index)
            {
                case 0: return LocalizationIds.MainWindow.TabsObject.TabsItem0;
                case 1: return LocalizationIds.MainWindow.TabsObject.TabsItem1;
                case 2: return LocalizationIds.MainWindow.TabsObject.TabsItem2;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
