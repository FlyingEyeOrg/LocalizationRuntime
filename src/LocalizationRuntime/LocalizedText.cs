using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>响应单元基类。切换语言时由表统一唤醒。</summary>
    public abstract class LocalizedTextBase
    {
        internal abstract void RaiseChanged();
    }

    /// <summary>
    /// 单个文本的响应单元。全局只应存在一份（由生成器产出、宿主持有），
    /// 因此不需要弱引用，也不需要逐实例清理。
    /// </summary>
    public sealed class LocalizedText : LocalizedTextBase, INotifyPropertyChanged
    {
        private readonly LocalizationTable _table;
        private readonly int _id;

        public LocalizedText(LocalizationTable table, int id)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _id = id;
            _table.Register(this);
        }

        public int Id
        {
            get { return _id; }
        }

        /// <summary>当前语言下的值。一次快照读加一次数组索引，零分配。</summary>
        public string Value
        {
            get { return _table.Get(_id); }
        }

        /// <summary>按当前语言格式化。模板损坏时返回原文，不抛异常。</summary>
        public string Format(params object?[] args)
        {
            return _table.Format(_id, args);
        }

        /// <summary>单参数重载：避免 <c>params</c> 数组分配。</summary>
        public string Format(object? arg0)
        {
            return _table.Format(_id, arg0);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal override void RaiseChanged()
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public override string ToString()
        {
            return Value;
        }
    }

    /// <summary>
    /// 数组型文本（菜单项、页签标题等）的响应单元。
    /// 长度编译期已知；内容按版本号缓存，切一次语言只重建一次、只换一次引用，
    /// 因此绑定到它的控件不会因为"新数组"而丢失选中项或滚动位置。
    /// </summary>
    public sealed class LocalizedList : LocalizedTextBase, INotifyPropertyChanged
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

        private readonly LocalizationTable _table;
        private readonly int _baseId;
        private readonly int _length;

        /// <summary>
        /// 缓存用一个不可变对象整体替换，读路径因此无需加锁：
        /// 命中时只做一次引用比较，未命中才在锁内重建。
        /// </summary>
        private Cache? _cache;

        public LocalizedList(LocalizationTable table, int baseId, int length)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _baseId = baseId;
            _length = length;
            _table.Register(this);
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

                return Rebuild(revision);
            }
        }

        private string[] Rebuild(int revision)
        {
            lock (_table.UnitLock)
            {
                // 双重检查：并发读时可能有别的线程刚建好
                var cache = Volatile.Read(ref _cache);
                if (cache != null && cache.Revision == _table.Revision)
                {
                    return cache.Items;
                }

                var items = new string[_length];
                for (var i = 0; i < _length; i++)
                {
                    items[i] = _table.Get(_baseId + i);
                }

                Volatile.Write(ref _cache, new Cache(_table.Revision, items));
                return items;
            }
        }

        public string this[int index]
        {
            get { return _table.Get(_baseId + index); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal override void RaiseChanged()
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}
