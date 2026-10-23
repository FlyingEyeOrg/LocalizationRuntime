using System;
using System.Globalization;
using LocalizationRuntime;
using LocalizationRuntime.Sample.Localization;

namespace LocalizationRuntime.Sample
{
    /// <summary>
    /// 业务服务：构造函数声明的是 <see cref="ILocalizer"/> 接口，由 DI 容器注入，
    /// 不需要自己 new，也不需要知道 Catalog 的存在。
    ///
    /// 这里演示各种 JSON 形状如何映射成强类型 id：
    /// 标量、嵌套对象、字符串数组、对象数组、二维数组、三维数组、对象里套数组。
    /// </summary>
    internal sealed class LocalizationDemo
    {
        private readonly ILocalizer _localizer;

        /// <summary>缓存过的标题：切语言时靠通知失效，而不是每次读取都重新拼。</summary>
        private string? _cachedTitle;

        public LocalizationDemo(ILocalizer localizer)
        {
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            // 订阅切换通知：这是"缓存过的属性该重读了"的信号。
            _localizer.CultureChanged += OnCultureChanged;
        }

        /// <summary>典型场景：属性把文本缓存下来，只在语言变化时失效。</summary>
        internal string CachedTitle
        {
            get { return _cachedTitle ??= _localizer.Get(LocalizationIds.AppObject.Title); }
        }

        private void OnCultureChanged(object? sender, CultureChangedEventArgs e)
        {
            _cachedTitle = null;
            Console.WriteLine("  [通知] 语言已切换: " + e);
        }

        internal void Run()
        {
            Console.WriteLine("系统探测语言: " + _localizer.CurrentCulture.Name);
            Console.WriteLine("支持的语言  : " + string.Join(", ", Catalog.Instance.CultureNames));
            Console.WriteLine("叶子数      : " + LocalizationIds.TextCount);
            Console.WriteLine();

            Console.WriteLine("缓存标题（切语言前）: " + CachedTitle);
            Dump("初次加载");

            // 运行时切换，无需重启、无需读文件
            _localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Console.WriteLine("缓存标题（切语言后）: " + CachedTitle);
            Dump("切到 en-US");

            _localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
            Dump("切到 ja-JP（Common.Cancel 未翻译，回退到 zh-CN）");

            // 切回中文
            _localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("切回 zh-CN: App.Title = " +
                              _localizer.Get(LocalizationIds.AppObject.Title));
            Console.WriteLine();

            // 幂等：同语言不递增版本号
            var revisionBefore = _localizer.Revision;
            var changed = _localizer.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
            Console.WriteLine("重复切同一语言: 返回 " + changed + "，版本号 " +
                              revisionBefore + " → " + _localizer.Revision);
            Console.WriteLine();

            // 演示两种错误：容器 id 与真正不存在的 id 抛出不同异常
            ProbeContainerMistake();
        }

        /// <summary>
        /// 把数组当字符串用是最容易犯的错，异常信息会直接告诉你该怎么改。
        /// </summary>
        private void ProbeContainerMistake()
        {
            Console.WriteLine("=== 误用容器的报错 ===");

            try
            {
                _localizer.Get(LocalizationIds.Menus);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("  Get(Menus)      → InvalidOperationException: " + ex.Message);
            }

            try
            {
                _localizer.Get(9999);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine("  Get(9999)       → ArgumentOutOfRangeException: " + ex.Message);
            }
        }

        private void Dump(string title)
        {
            Console.WriteLine("=== " + title + " ===");
            Console.WriteLine("culture          : " + _localizer.CurrentCulture.Name);

            // ① 标量
            Console.WriteLine("App.Title        : " + _localizer.Get(LocalizationIds.AppObject.Title));
            Console.WriteLine("App.ConfirmDelete: " +
                string.Format(_localizer.CurrentCulture,
                              _localizer.Get(LocalizationIds.AppObject.ConfirmDelete), "刀具 A"));

            // ② 多层嵌套对象
            Console.WriteLine("MainWindow.Title : " + _localizer.Get(LocalizationIds.MainWindowObject.Title));
            Console.WriteLine("ToolInfo.UserName: " +
                _localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.UserName));
            Console.WriteLine("Dept.Name        : " +
                _localizer.Get(LocalizationIds.MainWindowObject.ToolInfoObject.DepartmentObject.Name));

            // ③ 字符串数组
            Console.WriteLine("MainWindow.Tabs  : " +
                string.Join(" | ", _localizer.GetValues(LocalizationIds.MainWindowObject.Tabs)));

            // ④ 对象数组
            Console.WriteLine("Menus[0]         : " +
                _localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Title) + " / " +
                _localizer.Get(LocalizationIds.MenusObject.MenusItem0Object.Shortcut));
            Console.WriteLine("Menus[2]         : " +
                _localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Title) + " / " +
                _localizer.Get(LocalizationIds.MenusObject.MenusItem2Object.Shortcut));

            // ⑤ 二维数组（数组套数组）
            Console.WriteLine("Matrix           : " +
                string.Join(" | ", _localizer.GetAllValues(LocalizationIds.Matrix)));

            // ⑥ 三维数组
            Console.WriteLine("Cube             : " +
                string.Join(" | ", _localizer.GetAllValues(LocalizationIds.Cube)));

            // ⑦ 二维对象数组
            Console.WriteLine("Grid             : " +
                string.Join(" | ", _localizer.GetAllValues(LocalizationIds.Grid)));

            // ⑧ 对象里套数组
            Console.WriteLine("Groups[0].Name   : " +
                _localizer.Get(LocalizationIds.GroupsObject.GroupsItem0Object.Name));
            Console.WriteLine("Groups[0].Items  : " +
                string.Join(" | ", _localizer.GetValues(LocalizationIds.GroupsObject.GroupsItem0Object.Items)));

            // ⑨ 回退：ja-JP 没有 Cancel，应显示 zh-CN 的"取消"
            Console.WriteLine("Common.Cancel    : " + _localizer.Get(LocalizationIds.CommonObject.Cancel));

            // ⑩ 容器 id：取整个数组、按层展开
            Console.WriteLine("-- 容器（数组/对象）--");
            Console.WriteLine("Tabs 元素数      : " + _localizer.GetChildCount(LocalizationIds.MainWindowObject.Tabs));
            Console.WriteLine("Menus 元素数     : " + _localizer.GetChildCount(LocalizationIds.Menus));
            foreach (var itemId in _localizer.GetChildren(LocalizationIds.Menus))
            {
                Console.WriteLine("  Menus 元素 id=" + itemId +
                                  " 子成员数=" + _localizer.GetChildCount(itemId));
            }

            Console.WriteLine("Matrix 行数      : " + _localizer.GetChildCount(LocalizationIds.Matrix));
            foreach (var rowId in _localizer.GetChildren(LocalizationIds.Matrix))
            {
                Console.WriteLine("  行: " + string.Join(" | ", _localizer.GetValues(rowId)));
            }

            Console.WriteLine("App 成员数       : " + _localizer.GetChildCount(LocalizationIds.App));
            Console.WriteLine();
        }
    }
}
