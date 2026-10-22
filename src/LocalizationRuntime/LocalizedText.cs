using System;
using System.Collections.Generic;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>
    /// 单个文本的取值器。编译期固定键 id，读一次就是一次数组索引。
    /// 切换语言后再次读取 <see cref="Value"/> 自然得到新语言的值。
    /// </summary>
    public sealed class LocalizedText
    {
        private readonly Localizer _table;
        private readonly int _id;

        public LocalizedText(Localizer localizer, int id)
        {
            _table = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _id = id;
        }

        /// <summary>当前语言下的值。一次快照读加一次数组索引，零分配。</summary>
        public string Value
        {
            get { return _table.Get(_id); }
        }

        public override string ToString()
        {
            return Value;
        }
    }

    /// <summary>
    /// 数组型文本（菜单项、页签标题等）的取值器。
    /// 长度编译期已知；内容按版本号缓存，切一次语言只重建一次，
    /// 未切换时重复读取返回同一个实例。
    /// </summary>
    public sealed class LocalizedList
    {
        private sealed class Cache
        {
            internal Cache(int revision, string[] items)
            {
                Revision = revision;
                Items = items;
            }

            internal readonly int Revision;
            internal readonly string[] Items;
        }

        private readonly Localizer _table;
        private readonly int _baseId;
        private readonly int _length;
        private readonly object _sync = new object();

        private Cache? _cache;

        public LocalizedList(Localizer localizer, int baseId, int length)
        {
            _table = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _baseId = baseId;
            _length = length;
        }

        public int Length
        {
            get { return _length; }
        }

        public IReadOnlyList<string> Value
        {
            get
            {
                var revision = _table.Revision;
                var cache = Volatile.Read(ref _cache);

                if (cache != null && cache.Revision == revision)
                {
                    return cache.Items;
                }

                lock (_sync)
                {
                    cache = _cache;
                    if (cache != null && cache.Revision == revision)
                    {
                        return cache.Items;
                    }

                    var items = new string[_length];
                    for (var i = 0; i < _length; i++)
                    {
                        items[i] = _table.Get(_baseId + i);
                    }

                    _cache = new Cache(revision, items);
                    return items;
                }
            }
        }

        public string this[int index]
        {
            get { return _table.Get(_baseId + index); }
        }
    }
}
