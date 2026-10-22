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

        /// <summary>全部键的点分路径。下标即键 id，顺序与每张表一致。</summary>
        IReadOnlyList<string> Keys { get; }

        /// <summary>
        /// 取某语言的原始表，长度等于 <see cref="Keys"/> 的长度，缺键位置为 null。
        /// 该语言不存在时返回 null。返回的数组只读，调用方不得修改。
        /// </summary>
        string?[]? GetTable(string cultureName);
    }
}
