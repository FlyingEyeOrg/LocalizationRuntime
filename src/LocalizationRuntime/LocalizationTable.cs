using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>
    /// 运行期状态：持有当前语言，按 key 取值。
    ///
    /// 两件事：用 <see cref="Get(int)"/> / <see cref="Get(string)"/> 取当前语言的值，
    /// 用 <see cref="SetCulture(CultureInfo)"/> 切换语言。切换后再次取值自然得到新语言的值。
    ///
    /// 唯一可变的是 <see cref="_snapshot"/> 这一个引用：表、culture、版本号一起换，
    /// 读者只会看到完整的旧值或完整的新值，不会读到撕裂的中间态。
    /// 切换无 IO，因此是同步的。
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
        private readonly Dictionary<string, int> _index;
        private readonly HashSet<string> _supported;
        private readonly Dictionary<string, string[]> _resolved = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        private Snapshot _snapshot;
        private int _nextRevision;

        public LocalizationTable(ILocalizationCatalog catalog)
            : this(catalog, null)
        {
        }

        public LocalizationTable(ILocalizationCatalog catalog, CultureInfo? initialCulture)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            var keys = catalog.Keys;
            _index = new Dictionary<string, int>(keys.Count, StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                _index[keys[i]] = i;
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

        /// <summary>当前语言。</summary>
        public CultureInfo CurrentCulture
        {
            get { return Volatile.Read(ref _snapshot).Culture; }
        }

        /// <summary>当前快照版本号，每成功切换一次语言递增一次。</summary>
        public int Revision
        {
            get { return Volatile.Read(ref _snapshot).Revision; }
        }

        /// <summary>
        /// 按编译期键 id 取值。热路径：一次快照读 + 一次数组索引，零分配、无锁。
        /// 这是生成器产出的访问器所走的路。
        /// </summary>
        public string Get(int id)
        {
            var values = Volatile.Read(ref _snapshot).Values;
            if ((uint)id >= (uint)values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "键 id 超出范围。");
            }

            return values[id];
        }

        /// <summary>
        /// 按点分键取值（如 <c>"App.Title"</c>），供拿不到编译期 id 的场景使用。
        /// 键不存在时返回键本身。
        /// </summary>
        public string Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            int id;
            return _index.TryGetValue(key, out id) ? Get(id) : key;
        }

        /// <summary>
        /// 切换语言。无 IO，同步完成；返回是否真的发生了切换。
        /// 切换后再次取值即得到新语言的值。
        /// </summary>
        public bool SetCulture(CultureInfo culture)
        {
            if (culture == null)
            {
                throw new ArgumentNullException(nameof(culture));
            }

            lock (_gate)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                if (string.Equals(snapshot.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var current = culture.Name.Length == 0
                    ? CultureInfo.GetCultureInfo(_catalog.DefaultCultureName)
                    : culture;

                var revision = _nextRevision++;
                Volatile.Write(ref _snapshot, new Snapshot(Resolve(current.Name), current, revision));
                return true;
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
            var keys = _catalog.Keys;
            var length = keys.Count;

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

            // 预先取表，避免在内层循环里反复查表
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

                result[id] = value ?? keys[id];
            }

            return result;
        }

        /// <summary>把一张可能含 null 的原始表补成不含 null 的数组。</summary>
        private string[] Materialize(string?[]? source, int length)
        {
            var result = new string[length];
            var keys = _catalog.Keys;

            if (source == null)
            {
                for (var i = 0; i < length; i++)
                {
                    result[i] = keys[i];
                }

                return result;
            }

            var copy = Math.Min(length, source.Length);
            for (var i = 0; i < copy; i++)
            {
                result[i] = source[i] ?? keys[i];
            }

            for (var i = copy; i < length; i++)
            {
                result[i] = keys[i];
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
