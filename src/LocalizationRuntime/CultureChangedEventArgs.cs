using System;
using System.Globalization;

namespace LocalizationRuntime
{
    /// <summary>语言切换成功后的事件参数。</summary>
    public sealed class CultureChangedEventArgs : EventArgs
    {
        public CultureChangedEventArgs(CultureInfo oldCulture, CultureInfo newCulture, int revision)
        {
            OldCulture = oldCulture;
            NewCulture = newCulture;
            Revision = revision;
        }

        public CultureInfo OldCulture { get; }

        public CultureInfo NewCulture { get; }

        /// <summary>切换后的版本号，与 <see cref="LocalizationTable.Revision"/> 一致。</summary>
        public int Revision { get; }
    }
}
