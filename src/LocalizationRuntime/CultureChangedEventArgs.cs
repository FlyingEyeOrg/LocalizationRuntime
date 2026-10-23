using System;
using System.Globalization;

namespace LocalizationRuntime
{
    /// <summary>
    /// 语言切换完成的通知参数。
    ///
    /// 事件在快照已经换好之后才触发，因此处理函数里调用
    /// <see cref="ILocalizer.Get(int)"/> 拿到的已经是新语言的文本——
    /// 这正是"刷新其它属性值"需要的时机。
    /// </summary>
    public sealed class CultureChangedEventArgs : EventArgs
    {
        public CultureChangedEventArgs(CultureInfo oldCulture, CultureInfo newCulture, int revision)
        {
            OldCulture = oldCulture ?? throw new ArgumentNullException(nameof(oldCulture));
            NewCulture = newCulture ?? throw new ArgumentNullException(nameof(newCulture));
            Revision = revision;
        }

        /// <summary>切换前的语言。</summary>
        public CultureInfo OldCulture { get; }

        /// <summary>切换后实际生效的语言。不受支持的语言会落到基语言。</summary>
        public CultureInfo NewCulture { get; }

        /// <summary>本次切换后的版本号，可用于判断是否比上次处理的事件更新。</summary>
        public int Revision { get; }

        /// <summary>方便日志输出，例如 <c>zh-CN → en-US</c>。</summary>
        public override string ToString()
        {
            return OldCulture.Name + " → " + NewCulture.Name;
        }
    }
}