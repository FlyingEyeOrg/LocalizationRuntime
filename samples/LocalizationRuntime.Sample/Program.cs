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
            // 数据由生成器注入：Catalog.Instance 是整个程序集共享的静态单例。
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

            // 切回中文
            localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("切回 zh-CN: App.Title = " + localizer.Get(LocalizationIds.AppObject.Title));
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
            Console.WriteLine("culture          : " + localizer.CurrentCulture.Name);

            // ① 标量
            Console.WriteLine("App.Title        : " + localizer.Get(LocalizationIds.AppObject.Title));
            Console.WriteLine("App.ConfirmDelete: " +
                string.Format(localizer.CurrentCulture, localizer.Get(LocalizationIds.AppObject.ConfirmDelete), "刀具 A"));

            // ② 多层嵌套对象
            Console.WriteLine("MainWindow.Title : " + localizer.Get(LocalizationIds.MainWindowObject.Title));
            Console.WriteLine("ToolInfo.UserName: " + localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.UserName));
            Console.WriteLine("Dept.Name        : " +
                localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.DepartmentObject.Name));

            // ③ 字符串数组
            Console.WriteLine("MainWindow.Tabs  : " +
                string.Join(" | ", localizer.GetValues(LocalizationIds.MainWindowObject.Tabs)));

            // ④ 对象数组
            Console.WriteLine("Menus[0]         : " +
                localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Title) + " / " +
                localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Shortcut));
            Console.WriteLine("Menus[2]         : " +
                localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Title) + " / " +
                localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Shortcut));

            // ⑤ 二维数组（数组套数组）
            Console.WriteLine("Matrix           : " +
                string.Join(" | ", localizer.GetAllValues(LocalizationIds.Matrix)));

            // ⑥ 三维数组
            Console.WriteLine("Cube             : " +
                string.Join(" | ", localizer.GetAllValues(LocalizationIds.Cube)));

            // ⑦ 二维对象数组
            Console.WriteLine("Grid             : " +
                string.Join(" | ", localizer.GetAllValues(LocalizationIds.Grid)));

            // ⑧ 对象里套数组
            Console.WriteLine("Groups[0].Name   : " + localizer.Get(LocalizationIds.GroupsObject.GroupsItem0Object.Name));
            Console.WriteLine("Groups[0].Items  : " +
                string.Join(" | ", localizer.GetValues(LocalizationIds.GroupsObject.GroupsItem0Object.Items)));

            // ⑨ 回退：ja-JP 没有 Cancel，应显示 zh-CN 的"取消"
            Console.WriteLine("Common.Cancel    : " + localizer.Get(LocalizationIds.CommonObject.Cancel));

            // ⑩ 容器 id：取整个数组、按层展开
            Console.WriteLine("-- 容器（数组/对象）--");
            Console.WriteLine("Tabs 元素数      : " + localizer.GetChildCount(LocalizationIds.MainWindowObject.Tabs));
            Console.WriteLine("Menus 元素数     : " + localizer.GetChildCount(LocalizationIds.Menus));
            foreach (var itemId in localizer.GetChildren(LocalizationIds.Menus))
            {
                Console.WriteLine("  Menus 元素 id=" + itemId +
                                  " 子成员数=" + localizer.GetChildCount(itemId));
            }

            Console.WriteLine("Matrix 行数      : " + localizer.GetChildCount(LocalizationIds.Matrix));
            foreach (var rowId in localizer.GetChildren(LocalizationIds.Matrix))
            {
                Console.WriteLine("  行: " + string.Join(" | ", localizer.GetValues(rowId)));
            }

            Console.WriteLine("App 成员数       : " + localizer.GetChildCount(LocalizationIds.App));
            Console.WriteLine();
        }
    }
}

