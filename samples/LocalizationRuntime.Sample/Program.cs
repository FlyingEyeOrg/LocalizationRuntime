using System;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Sample.Localization;

namespace LocalizationRuntime.Sample
{
    /// <summary>
    /// 演示各种 JSON 形状如何映射成强类型 id：
    ///   标量、嵌套对象、字符串数组、对象数组、二维数组、三维数组、对象里套数组。
    ///
    /// 注意 Localizer 是静态类，不需要任何初始化代码：
    /// 生成的 Catalog 会在模块加载时自动注册。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("系统探测语言: " + Localizer.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", Catalog.Instance.CultureNames));
            Console.WriteLine("键总数      : " + Catalog.Instance.Keys.Count);
            Console.WriteLine();

            Dump("初次加载");

            // 运行时切换，无需重启、无需读文件
            Localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Dump("切到 en-US");

            Localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump("切到 ja-JP（Common.Cancel 未翻译，回退到 zh-CN）");

            // 切回中文
            Localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("切回 zh-CN: App.Title = " + Localizer.Get(LocalizationIds.AppObject.Title));
            Console.WriteLine();

            // 幂等：同语言不递增版本号
            var revisionBefore = Localizer.Revision;
            var changed = Localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("重复切同一语言: 返回 " + changed + "，版本号 " +
                              revisionBefore + " → " + Localizer.Revision);

            return 0;
        }

        private static void Dump(string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture          : " + Localizer.CurrentCulture.Name);

            // ① 标量
            Console.WriteLine("App.Title        : " + Localizer.Get(LocalizationIds.AppObject.Title));
            Console.WriteLine("App.ConfirmDelete: " +
                string.Format(Localizer.CurrentCulture, Localizer.Get(LocalizationIds.AppObject.ConfirmDelete), "刀具 A"));

            // ② 多层嵌套对象
            Console.WriteLine("MainWindow.Title : " + Localizer.Get(LocalizationIds.MainWindowObject.Title));
            Console.WriteLine("ToolInfo.UserName: " + Localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.UserName));
            Console.WriteLine("Dept.Name        : " +
                Localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.DepartmentObject.Name));

            // ③ 字符串数组
            Console.WriteLine("MainWindow.Tabs  : " +
                string.Join(" | ", Localizer.GetValues(LocalizationIds.MainWindowObject.Tabs)));

            // ④ 对象数组
            Console.WriteLine("Menus[0]         : " +
                Localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Title) + " / " +
                Localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Shortcut));
            Console.WriteLine("Menus[2]         : " +
                Localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Title) + " / " +
                Localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Shortcut));

            // ⑤ 二维数组（数组套数组）
            Console.WriteLine("Matrix           : " +
                string.Join(" | ", Localizer.GetAllValues(LocalizationIds.Matrix)));

            // ⑥ 三维数组
            Console.WriteLine("Cube             : " +
                string.Join(" | ", Localizer.GetAllValues(LocalizationIds.Cube)));

            // ⑦ 二维对象数组
            Console.WriteLine("Grid             : " +
                string.Join(" | ", Localizer.GetAllValues(LocalizationIds.Grid)));

            // ⑧ 对象里套数组
            Console.WriteLine("Groups[0].Name   : " + Localizer.Get(LocalizationIds.GroupsObject.GroupsItem0Object.Name));
            Console.WriteLine("Groups[0].Items  : " +
                string.Join(" | ", Localizer.GetValues(LocalizationIds.GroupsObject.GroupsItem0Object.Items)));

            // ⑨ 回退：ja-JP 没有 Cancel，应显示 zh-CN 的"取消"
            Console.WriteLine("Common.Cancel    : " + Localizer.Get(LocalizationIds.CommonObject.Cancel));

            // ⑩ 容器 id：取整个数组、按层展开
            Console.WriteLine("-- 容器（数组/对象）--");
            Console.WriteLine("Tabs 元素数      : " + Localizer.GetChildCount(LocalizationIds.MainWindowObject.Tabs));
            Console.WriteLine("Menus 元素数     : " + Localizer.GetChildCount(LocalizationIds.Menus));
            foreach (var itemId in Localizer.GetChildren(LocalizationIds.Menus))
            {
                Console.WriteLine("  Menus 元素 id=" + itemId +
                                  " 子成员数=" + Localizer.GetChildCount(itemId));
            }

            Console.WriteLine("Matrix 行数      : " + Localizer.GetChildCount(LocalizationIds.Matrix));
            foreach (var rowId in Localizer.GetChildren(LocalizationIds.Matrix))
            {
                Console.WriteLine("  行: " + string.Join(" | ", Localizer.GetValues(rowId)));
            }

            Console.WriteLine("App 成员数       : " + Localizer.GetChildCount(LocalizationIds.App));
            Console.WriteLine();
        }
    }
}
