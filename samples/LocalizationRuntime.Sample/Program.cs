using System;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Sample.Localization;

namespace LocalizationRuntime.Sample
{
    /// <summary>
    /// 演示各种 JSON 形状如何映射成强类型 id：
    ///   标量、嵌套对象、字符串数组、对象数组、二维数组、三维数组、对象里套数组。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            // 生成器已把三种语言编译进程序集；构造 localizer 时按系统 culture 自动选语言。
            var localizer = new Localizer(Catalog.Instance);

            Console.WriteLine("系统探测语言: " + localizer.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", Catalog.Instance.CultureNames));
            Console.WriteLine("键总数      : " + Catalog.Instance.Keys.Count);
            Console.WriteLine();

            Dump(localizer, "初次加载");

            // 运行时切换，无需重启、无需读文件
            localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Dump(localizer, "切到 en-US");

            localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump(localizer, "切到 ja-JP（Common.Cancel 未翻译，回退到 zh-CN）");

            // 切回中文：同一个 Localizer 实例，再读就是中文
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("切回 zh-CN: App.Title = " + localizer.Get(LocalizationIds.App.Title));
            Console.WriteLine();

            // 幂等：同语言不递增版本号
            var revisionBefore = localizer.Revision;
            var changed = localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("重复切同一语言: 返回 " + changed + "，版本号 " +
                              revisionBefore + " → " + localizer.Revision);

            return 0;
        }

        private static void Dump(Localizer l, string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture          : " + l.CurrentCulture.Name);

            // ① 标量
            Console.WriteLine("App.Title        : " + l.Get(LocalizationIds.App.Title));
            Console.WriteLine("App.ConfirmDelete: " +
                string.Format(l.CurrentCulture, l.Get(LocalizationIds.App.ConfirmDelete), "刀具 A"));

            // ② 多层嵌套对象
            Console.WriteLine("MainWindow.Title : " + l.Get(LocalizationIds.MainWindow.Title));
            Console.WriteLine("ToolInfo.UserName: " + l.Get(LocalizationIds.MainWindow.ToolInfo.UserName));
            Console.WriteLine("Dept.Name        : " +
                l.Get(LocalizationIds.MainWindow.ToolInfo.Department.Name));

            // ③ 字符串数组
            Console.Write("MainWindow.Tabs  : ");
            Console.WriteLine(string.Join(" | ", new[]
            {
                l.Get(LocalizationIds.MainWindow.TabsObject.TabsItem0),
                l.Get(LocalizationIds.MainWindow.TabsObject.TabsItem1),
                l.Get(LocalizationIds.MainWindow.TabsObject.TabsItem2)
            }));

            // ④ 对象数组
            Console.WriteLine("Menus[0]         : " +
                l.Get(LocalizationIds.MenusObject.MenusItem0Object.Title) + " / " +
                l.Get(LocalizationIds.MenusObject.MenusItem0Object.Shortcut));
            Console.WriteLine("Menus[2]         : " +
                l.Get(LocalizationIds.MenusObject.MenusItem2Object.Title) + " / " +
                l.Get(LocalizationIds.MenusObject.MenusItem2Object.Shortcut));

            // ⑤ 二维数组（数组套数组）
            Console.Write("Matrix           : ");
            Console.WriteLine(string.Join(" | ", new[]
            {
                l.Get(LocalizationIds.MatrixObject.MatrixItem0Object.MatrixItem0Item0),
                l.Get(LocalizationIds.MatrixObject.MatrixItem0Object.MatrixItem0Item1),
                l.Get(LocalizationIds.MatrixObject.MatrixItem1Object.MatrixItem1Item0)
            }));

            // ⑥ 三维数组（数组套数组套数组）
            Console.Write("Cube             : ");
            Console.WriteLine(string.Join(" | ", new[]
            {
                l.Get(LocalizationIds.CubeObject.CubeItem0Object.CubeItem0Item0Object.CubeItem0Item0Item0),
                l.Get(LocalizationIds.CubeObject.CubeItem0Object.CubeItem0Item1Object.CubeItem0Item1Item0),
                l.Get(LocalizationIds.CubeObject.CubeItem1Object.CubeItem1Item0Object.CubeItem1Item0Item0)
            }));

            // ⑦ 二维对象数组
            Console.Write("Grid             : ");
            Console.WriteLine(string.Join(" | ", new[]
            {
                l.Get(LocalizationIds.GridObject.GridItem0Object.GridItem0Item0Object.Header),
                l.Get(LocalizationIds.GridObject.GridItem0Object.GridItem0Item1Object.Header),
                l.Get(LocalizationIds.GridObject.GridItem1Object.GridItem1Item0Object.Header)
            }));

            // ⑧ 对象里套数组
            Console.WriteLine("Groups[0].Name   : " + l.Get(LocalizationIds.GroupsObject.GroupsItem0Object.Name));
            Console.Write("Groups[0].Items  : ");
            Console.WriteLine(string.Join(" | ", new[]
            {
                l.Get(LocalizationIds.GroupsObject.GroupsItem0Object.ItemsObject.ItemsItem0),
                l.Get(LocalizationIds.GroupsObject.GroupsItem0Object.ItemsObject.ItemsItem1)
            }));

            // ⑨ 回退：ja-JP 没有 Cancel，应显示 zh-CN 的"取消"
            Console.WriteLine("Common.Cancel    : " + l.Get(LocalizationIds.Common.Cancel));
            Console.WriteLine();
        }
    }
}
