using System.Collections.Generic;

namespace LocalizationRuntime
{
    /// <summary>
    /// 由源生成器产出的资源目录，把编译期固化的每语言数组交给运行时。
    /// 用户不应手写此接口的实现。
    /// </summary>
    public interface ILocalizationCatalog
    {
        /// <summary>基语言名，例如 zh-CN。其它语言缺键时回退到它。</summary>
        string DefaultCultureName { get; }

        /// <summary>本次编译包含的语言名，按 Ordinal 升序。</summary>
        IReadOnlyList<string> CultureNames { get; }

        /// <summary>
        /// 叶子 id 的个数，也就是可取值的 id 区间 <c>0 .. TextCount-1</c> 的上界。
        /// 大于等于它的 id 是容器（数组 / 对象），不能直接取值。
        /// </summary>
        int TextCount { get; }

        /// <summary>
        /// 取某个容器 id 的直接子 id（按数组/对象中的原始顺序）。
        ///
        /// 对数组节点：子 id 依次是各个元素的 id，长度即数组长度。
        /// 对对象节点：子 id 依次是各成员的 id。
        /// 对叶子：返回空数组。
        /// id 越界时返回空数组。
        ///
        /// 返回的数组由实现方缓存，调用方不得修改。
        /// </summary>
        int[] GetChildren(int id);

        /// <summary>
        /// 取某语言的原始表，长度等于 <see cref="TextCount"/>，缺键位置为 null。
        /// 该语言不存在时返回 null。返回的数组只读，调用方不得修改。
        /// </summary>
        string?[]? GetTable(string cultureName);
    }
}

