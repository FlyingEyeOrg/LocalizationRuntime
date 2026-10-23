using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>
    /// 本地化入口：持有当前语言，按 key 取值。
    ///
    /// 两件事：用 <see cref="Get(int)"/> / <see cref="Get(string)"/> 取当前语言的值，
    /// 用 <see cref="SetCulture(CultureInfo)"/> 切换语言。切换后再次取值自然得到新语言的值。
    ///
    /// 唯一可变的是 <see cref="_snapshot"/> 这一个引用：值数组、culture、版本号一起换，
    /// 读者只会看到完整的旧值或完整的新值，不会读到撕裂的中间态。
    /// 切换无 IO，因此是同步的。
    /// </summary>
    public sealed class Localizer
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

        public Localizer(ILocalizationCatalog catalog)
            : this(catalog, null)
        {
        }

        public Localizer(ILocalizationCatalog catalog, CultureInfo? initialCulture)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            var keys = catalog.Keys;
            _index = new Dictionary<string, int>(keys.Count, StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                _index[keys[i]] = i;
            }

            // 把每种受支持语言及其整条 Parent 链都登记进去，
            // 这样判断"是否受支持"退化为一次哈希查找，不必在 SetCulture 里走链。
            _supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < catalog.CultureNames.Count; i++)
            {
                var name = catalog.CultureNames[i];
                _supported.Add(name);

                for (var parent = CultureInfo.GetCultureInfo(name).Parent;
                     parent != null && !string.IsNullOrEmpty(parent.Name);
                     parent = parent.Parent)
                {
                    _supported.Add(parent.Name);
                }
            }

            // 基语言本身永远算受支持。
            _supported.Add(catalog.DefaultCultureName);

            var culture = initialCulture ?? DetectSystemCulture();

            // 与 SetCulture 保持一致：不受支持的语言落到基语言，
            // 否则 CurrentCulture 与值的来源会自相矛盾。
            if (culture.Name.Length > 0 && !IsSupportedOrAncestor(culture.Name))
            {
                culture = CultureInfo.GetCultureInfo(catalog.DefaultCultureName);
            }

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

        // ───────────────────────── 容器（数组 / 对象）─────────────────────────

        /// <summary>
        /// 取容器的直接子 id 个数。数组返回元素个数；对象返回成员个数；叶子返回 0。
        /// 传容器 id 即可，例如 <c>GetChildCount(LocalizationIds.Menus)</c> 得到菜单项数。
        /// </summary>
        public int GetChildCount(int containerId)
        {
            return _catalog.GetChildren(containerId).Length;
        }

        /// <summary>
        /// 取容器的直接子 id。数组即各元素 id，逐项传给 <see cref="Get(int)"/> 或
        /// 继续传给本方法即可展开整棵子树。
        ///
        /// 返回的数组由 catalog 缓存，只读，请勿修改。
        /// </summary>
        public int[] GetChildren(int containerId)
        {
            return _catalog.GetChildren(containerId);
        }

        /// <summary>
        /// 取「数组」的全部元素文本。仅当子节点全是叶子时可用（字符串数组）；
        /// 若元素本身是对象或数组，会抛 <see cref="InvalidOperationException"/>，
        /// 此时应改用 <see cref="GetChildren"/> 逐层展开。
        /// </summary>
        public string[] GetValues(int containerId)
        {
            var children = _catalog.GetChildren(containerId);
            if (children.Length == 0)
            {
                // 叶子或空容器：返回空数组，避免调用方还要判空。
                return Array.Empty<string>();
            }

            var values = new string[children.Length];
            var snapshot = Volatile.Read(ref _snapshot);
            var table = snapshot.Values;

            for (var i = 0; i < children.Length; i++)
            {
                var childId = children[i];
                if ((uint)childId >= (uint)table.Length)
                {
                    throw new InvalidOperationException(
                        "id " + containerId + " 的子节点不是文本（而是对象或数组），请改用 GetChildren 逐层展开。");
                }

                values[i] = table[childId];
            }

            return values;
        }

        /// <summary>
        /// 递归展开容器的整棵子树，按 JSON 中的顺序返回所有叶子文本。
        /// 适合"把一个多维数组拍平"这类场景。
        /// </summary>
        public string[] GetAllValues(int containerId)
        {
            var sink = new System.Collections.Generic.List<string>();
            CollectLeaves(containerId, sink, Volatile.Read(ref _snapshot).Values, 0);
            return sink.ToArray();
        }

        private void CollectLeaves(
            int id,
            System.Collections.Generic.List<string> sink,
            string[] table,
            int depth)
        {
            // 防呆：树深度正常不会超过几十层，出现异常环时立刻报错而不是爆栈。
            if (depth > 64)
            {
                throw new InvalidOperationException("容器层级过深，疑似数据成环。");
            }

            if ((uint)id < (uint)table.Length)
            {
                sink.Add(table[id]);
                return;
            }

            var children = _catalog.GetChildren(id);
            for (var i = 0; i < children.Length; i++)
            {
                CollectLeaves(children[i], sink, table, depth + 1);
            }
        }

        /// <summary>
        /// 切换语言。无 IO，同步完成；返回是否真的发生了切换。
        ///
        /// 传入不受支持的语言时，实际生效的是回退链的终点（基语言），
        /// 因此 <see cref="CurrentCulture"/> 报的是真正生效的语言，不会出现
        /// "报 de-DE、值是 zh-CN" 这种自相矛盾的状态。
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

                var current = culture.Name.Length == 0
                    ? CultureInfo.GetCultureInfo(_catalog.DefaultCultureName)
                    : culture;

                // 不属于任何受支持语言（含其 Parent 链）时，落到基语言，
                // 保证 CurrentCulture 与值的来源一致。
                if (!IsSupportedOrAncestor(current.Name))
                {
                    current = CultureInfo.GetCultureInfo(_catalog.DefaultCultureName);
                }

                if (string.Equals(snapshot.Culture.Name, current.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var revision = _nextRevision++;
                Volatile.Write(ref _snapshot, new Snapshot(Resolve(current.Name), current, revision));
                return true;
            }
        }

        /// <summary>
        /// 该 culture 或其某个 Parent 是否在资源里存在。
        /// _supported 在构造时已展开整条 Parent 链，所以这里只是一次哈希查找。
        /// </summary>
        private bool IsSupportedOrAncestor(string cultureName)
        {
            return _supported.Contains(cultureName);
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
