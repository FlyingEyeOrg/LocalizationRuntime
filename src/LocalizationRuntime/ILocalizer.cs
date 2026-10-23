using System;
using System.Globalization;

namespace LocalizationRuntime
{
    /// <summary>
    /// 本地化取值入口。业务代码依赖这个接口即可，不必知道具体实现
    /// （<see cref="Localizer"/>），换实现或做测试替身都不牵动调用方。
    ///
    /// 只有两项能力：<see cref="Get(int)"/> 按编译期 id 取当前语言的文本，
    /// <see cref="SetCulture(CultureInfo)"/> 切换当前语言。
    /// 容器相关的 <see cref="GetChildren"/> 等用于展开数组 / 对象。
    /// </summary>
    public interface ILocalizer
    {
        /// <summary>当前语言。</summary>
        CultureInfo CurrentCulture { get; }

        /// <summary>当前快照版本号，每成功切换一次语言递增一次。</summary>
        int Revision { get; }

        /// <summary>
        /// 按编译期 id 取当前语言的文本。一次快照读加一次数组索引，零分配、无锁。
        ///
        /// 只有叶子有文本。传入容器 id 会抛 <see cref="InvalidOperationException"/>，
        /// 传入不存在的 id 会抛 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>
        string Get(int id);

        /// <summary>
        /// 取容器的直接子 id 个数。数组返回元素个数；对象返回成员个数；叶子返回 0。
        /// </summary>
        int GetChildCount(int containerId);

        /// <summary>
        /// 取容器的直接子 id。数组即各元素 id，逐项传给 <see cref="Get(int)"/>
        /// 或继续传给本方法即可展开整棵子树。
        ///
        /// 返回的数组由实现方缓存，只读，请勿修改。
        /// </summary>
        int[] GetChildren(int containerId);

        /// <summary>
        /// 取「数组」的全部元素文本。仅当子节点全是叶子时可用（字符串数组）；
        /// 若元素本身是对象或数组，会抛 <see cref="InvalidOperationException"/>，
        /// 此时应改用 <see cref="GetChildren"/> 逐层展开。
        /// </summary>
        string[] GetValues(int containerId);

        /// <summary>递归展开容器的整棵子树，返回所有叶子文本。</summary>
        string[] GetAllValues(int containerId);

        /// <summary>
        /// 切换语言。无 IO，同步完成；返回是否真的发生了切换。
        /// 传入不受支持的语言时，实际生效的是回退链的终点（基语言）。
        /// </summary>
        bool SetCulture(CultureInfo culture);
    }
}
