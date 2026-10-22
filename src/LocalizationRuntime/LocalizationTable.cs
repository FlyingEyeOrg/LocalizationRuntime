using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>
    /// 运行期状态。唯一可变的是 <see cref="_snapshot"/> 这一个引用：
    /// 表、culture、版本号一起换，读者只会看到完整的旧值或完整的新值，不会读到撕裂的中间态。
    /// 切语言无 IO，因此是同步的。
    /// </summary>
    public sealed class LocalizationTable
    {
        private sealed class Snapshot
        {
            internal Snapshot(string[] values, CultureInfo culture, int revision)
            {
                Values = values;
                Culture = culture;
                Revision = revision;
            }

            internal readonly string[] Values;
            internal readonly CultureInfo Culture;
            internal readonly int Revision;
        }

        private readonly ILocalizationCatalog _catalog;
        private readonly IReadOnlyList<string> _keys;
        private readonly Dictionary<string, int> _index;
        private readonly HashSet<string> _supported;
        private readonly Dictionary<string, string[]> _resolved = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private readonly object _unitsGate = new object();

        /// <summary>
        /// 注册进表的响应单元。整体以不可变数组发布，读路径只需一次引用读；
        /// 追加时复制并在锁内发布新数组，避免"旧数组 + 新长度"的撕裂。
        /// </summary>
        private LocalizedTextBase[] _units = new LocalizedTextBase[0];

        private Snapshot _snapshot;
        private int _nextRevision;

        public LocalizationTable(ILocalizationCatalog catalog)
            : this(catalog, null)
        {
        }

        public LocalizationTable(ILocalizationCatalog catalog, CultureInfo? initialCulture)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _keys = catalog.Keys;

            _index = new Dictionary<string, int>(_keys.Count, StringComparer.Ordinal);
            for (var i = 0; i < _keys.Count; i++)
            {
                _index[_keys[i]] = i;
            }

            _supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < catalog.CultureNames.Count; i++)
            {
                _supported.Add(catalog.CultureNames[i]);
            }

            var culture = initialCulture ?? DetectSystemCulture();
            _snapshot = new Snapshot(Resolve(culture.Name), culture, 0);
            _nextRevision = 1;
        }

        /// <summary>当前快照版本号。每成功切换一次语言递增一次。</summary>
        public int Revision
        {
            get { return Volatile.Read(ref _snapshot).Revision; }
        }

        public CultureInfo CurrentCulture
        {
            get { return Volatile.Read(ref _snapshot).Culture; }
        }

        public IReadOnlyList<string> Keys
        {
            get { return _keys; }
        }

        public bool IsSupported(string cultureName)
        {
            return !string.IsNullOrEmpty(cultureName) && _supported.Contains(cultureName);
        }

        /// <summary>
        /// 按编译期键 id 取值。热路径：一次快照读 + 一次数组索引，零分配、无分支、无锁。
        /// 缺键在编译期已由诊断拦下；运行期若仍未命中，返回键路径本身。
        /// </summary>
        public string Get(int id)
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if ((uint)id >= (uint)snapshot.Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "键 id 超出范围。");
            }

            return snapshot.Values[id];
        }

        /// <summary>按点分键取值，供 XAML 绑定等拿不到编译期 id 的场景使用。</summary>
        public bool TryGet(string key, out string? value)
        {
            int id;
            if (key != null && _index.TryGetValue(key, out id))
            {
                value = Get(id);
                return true;
            }

            value = null;
            return false;
        }

        public string Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return _index.TryGetValue(key, out var id) ? Get(id) : key;
        }

        /// <summary>按当前语言格式化。缺键或模板损坏时返回原文，不抛异常。</summary>
        public string Format(int id, params object?[] args)
        {
            return args == null || args.Length == 0
                ? Get(id)
                : FormatCore(id, args);
        }

        /// <summary>单参数重载：避免 <c>params</c> 数组分配。</summary>
        public string Format(int id, object? arg0)
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if ((uint)id >= (uint)snapshot.Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "键 id 超出范围。");
            }

            var template = snapshot.Values[id];

            // 没有占位符时跳过 string.Format：绝大多数文案属于这一类
            if (template.IndexOf('{') < 0)
            {
                return template;
            }

            try
            {
                return string.Format(snapshot.Culture, template, arg0);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private string FormatCore(int id, object?[] args)
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if ((uint)id >= (uint)snapshot.Values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "键 id 超出范围。");
            }

            var template = snapshot.Values[id];
            if (template.IndexOf('{') < 0)
            {
                return template;
            }

            try
            {
                return string.Format(snapshot.Culture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>
        /// 切换语言。无 IO，同步完成；返回是否真的发生了切换。
        /// 状态提交之后才通知，且单个订阅者抛异常不影响其余订阅者。
        /// </summary>
        public bool SetCulture(CultureInfo culture)
        {
            if (culture == null)
            {
                throw new ArgumentNullException(nameof(culture));
            }

            CultureInfo previous;
            CultureInfo current;
            int revision;

            lock (_gate)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                if (string.Equals(snapshot.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                previous = snapshot.Culture;
                current = culture.Name.Length == 0
                    ? CultureInfo.GetCultureInfo(_catalog.DefaultCultureName)
                    : culture;

                revision = _nextRevision++;
                Volatile.Write(ref _snapshot, new Snapshot(Resolve(current.Name), current, revision));
            }

            // 状态已提交。以下全部是"提交后动作"：任何异常都不能让调用方以为切换失败。
            var units = Volatile.Read(ref _units);
            for (var i = 0; i < units.Length; i++)
            {
                try
                {
                    units[i].RaiseChanged();
                }
                catch (Exception)
                {
                    // 单个订阅者出错不影响其余订阅者，也不影响已经生效的语言。
                }
            }

            var handler = CultureChanged;
            if (handler != null)
            {
                var args = new CultureChangedEventArgs(previous, current, revision);
                foreach (EventHandler<CultureChangedEventArgs> target in handler.GetInvocationList())
                {
                    try
                    {
                        target(this, args);
                    }
                    catch (Exception)
                    {
                        // 同上：逐个隔离。
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 语言已切换。文本本身通过各访问器的 PropertyChanged 通知；
        /// 这个事件用于文本之外需要跟随语言变化的东西（流向、排序、日期格式等）。
        /// </summary>
        public event EventHandler<CultureChangedEventArgs>? CultureChanged;

        /// <summary>当前语言的 键 → 值 快照，供导出或诊断使用。</summary>
        public IReadOnlyDictionary<string, string> ExportCurrent()
        {
            var snapshot = Volatile.Read(ref _snapshot);
            var result = new Dictionary<string, string>(snapshot.Values.Length, StringComparer.Ordinal);
            for (var i = 0; i < snapshot.Values.Length; i++)
            {
                result[_keys[i]] = snapshot.Values[i];
            }

            return result;
        }

        /// <summary>列表重建时用的锁。与 <see cref="_gate"/> 分开，避免与切语言互相阻塞。</summary>
        internal object UnitLock
        {
            get { return _unitsGate; }
        }

        internal void Register(LocalizedTextBase unit)
        {
            if (unit == null)
            {
                return;
            }

            lock (_unitsGate)
            {
                var current = _units;
                var next = new LocalizedTextBase[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = unit;
                Volatile.Write(ref _units, next);
            }
        }

        private CultureInfo DetectSystemCulture()
        {
            var system = CultureInfo.CurrentUICulture;
            for (var c = system; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                if (_supported.Contains(c.Name))
                {
                    return CultureInfo.GetCultureInfo(c.Name);
                }
            }

            return CultureInfo.GetCultureInfo(_catalog.DefaultCultureName);
        }

        /// <summary>
        /// 沿 Parent 链合并成一张扁平表：ja-JP → ja → 基语言，逐键取第一个非 null 的值。
        /// 仍然缺失的槽位填入键路径，使热路径无需分支。
        /// </summary>
        private string[] Resolve(string cultureName)
        {
            lock (_resolved)
            {
                string[]? cached;
                if (_resolved.TryGetValue(cultureName, out cached))
                {
                    return cached;
                }

                var built = BuildResolved(cultureName);
                _resolved[cultureName] = built;
                return built;
            }
        }

        private string[] BuildResolved(string cultureName)
        {
            var length = _keys.Count;

            // 基语言本身：补齐空槽即可，无需逐键走链
            if (string.Equals(cultureName, _catalog.DefaultCultureName, StringComparison.OrdinalIgnoreCase))
            {
                return Materialize(_catalog.GetTable(cultureName), length);
            }

            var chain = BuildChain(cultureName);

            // 链上只有一层（常见于二级 culture，如 ja-JP 直接命中）
            if (chain.Count == 1)
            {
                return Materialize(_catalog.GetTable(chain[0]), length);
            }

            // 预先取表，避免在内层循环里反复 switch 查表
            var tables = new string?[chain.Count][];
            for (var i = 0; i < chain.Count; i++)
            {
                tables[i] = _catalog.GetTable(chain[i])!;
            }

            var result = new string[length];

            for (var id = 0; id < length; id++)
            {
                var value = (string?)null;

                for (var level = 0; level < tables.Length; level++)
                {
                    var table = tables[level];
                    if (table != null && id < table.Length && table[id] != null)
                    {
                        value = table[id];
                        break;
                    }
                }

                result[id] = value ?? _keys[id];
            }

            return result;
        }

        /// <summary>把一张可能含 null 的原始表补成不含 null 的数组。</summary>
        private string[] Materialize(string?[]? source, int length)
        {
            var result = new string[length];

            if (source == null)
            {
                for (var i = 0; i < length; i++)
                {
                    result[i] = _keys[i];
                }

                return result;
            }

            var copy = Math.Min(length, source.Length);
            for (var i = 0; i < copy; i++)
            {
                result[i] = source[i] ?? _keys[i];
            }

            for (var i = copy; i < length; i++)
            {
                result[i] = _keys[i];
            }

            return result;
        }

        private List<string> BuildChain(string cultureName)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CultureInfo? culture = null;
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
                // 未知 culture 走纯回退链。
            }

            for (var c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                if (_supported.Contains(c.Name) && seen.Add(c.Name))
                {
                    chain.Add(c.Name);
                }
            }

            var fallback = _catalog.DefaultCultureName;
            if (seen.Add(fallback))
            {
                chain.Add(fallback);
            }

            return chain;
        }
    }
}
