using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace LocalizationRuntime
{
    /// <summary>
    /// 本地化入口：持有当前语言，按键取值。
    ///
    /// 两件事：用 <see cref="Get(int)"/> 取当前语言的值，
    /// 用 <see cref="SetCulture(CultureInfo)"/> 切换语言。切换后再次取值自然得到新语言的值。
    ///
    /// 数据通过构造函数注入——生成器在目标程序集里产出的 <c>Catalog.Instance</c>：
    /// <code>
    /// var localizer = new Localizer(Catalog.Instance);
    /// localizer.Get(LocalizationIds.AppObject.Title);
    /// </code>
    ///
    /// 唯一可变的是 <see cref="_snapshot"/> 这一个引用：值数组、culture、版本号一起换，
    /// 读者只会看到完整的旧值或完整的新值，不会读到撕裂的中间态。
    /// 切换无 IO，因此是同步的。
    /// </summary>
    public sealed class Localizer : ILocalizer
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
            if (culture.Name.Length > 0 && !_supported.Contains(culture.Name))
            {
                culture = CultureInfo.GetCultureInfo(catalog.DefaultCultureName);
            }

            _snapshot = new Snapshot(Resolve(culture.Name), culture, 0);
            _nextRevision = 1;
        }

        /// <summary>
        /// 语言切换完成时触发。快照此时已经换好，因此处理函数里取值拿到的已是新语言。
        ///
        /// 注意本类是单例：订阅者若是短生命周期对象，务必在销毁时退订。
        /// </summary>
        public event EventHandler<CultureChangedEventArgs>? CultureChanged;

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
        /// 按编译期 id 取当前语言的文本。热路径：一次快照读 + 一次数组索引，零分配、无锁。
        ///
        /// 只有叶子有文本。传入容器 id 会抛 <see cref="InvalidOperationException"/>：
        /// 那是用法错误而非越界——该 id 是合法的常量，只是代表数组或对象，
        /// 应该改用 <see cref="GetChildren"/> / <see cref="GetValues"/> / <see cref="GetAllValues"/>。
        /// 传入完全不存在于 id 空间的 id 才会抛 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>
        public string Get(int id)
        {
            var values = Volatile.Read(ref _snapshot).Values;
            if ((uint)id < (uint)values.Length)
            {
                return values[id];
            }

            // 落在叶子区间之外，但确有子节点 → 这是一个真实的容器 id。
            // （生成器不允许空容器，见 LR1010，所以"有子节点"即可判定为容器。）
            if (_catalog.GetChildren(id).Length > 0)
            {
                throw new InvalidOperationException(
                    "id " + id + " 是容器（数组 / 对象），没有对应的文本。" +
                    "取容器请用 GetChildren / GetValues / GetAllValues。");
            }

            throw new ArgumentOutOfRangeException(
                nameof(id), id,
                "键 id 不存在：叶子 id 的有效区间是 0.." +
                (values.Length - 1) + "，更大的 id 必须是容器。");
        }


        // ───────────────────────── 容器（数组 / 对象）─────────────────────────

        /// <summary>
        /// 取容器的直接子 id 个数。数组返回元素个数；对象返回成员个数；叶子返回 0。
        /// 例如 <c>GetChildCount(LocalizationIds.Menus)</c> 得到菜单项数。
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
            var table = Volatile.Read(ref _snapshot).Values;

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
        /// 递归展开容器的整棵子树，返回所有叶子文本。
        /// 适合"把一个多维数组拍平"这类场景。
        /// </summary>
        public string[] GetAllValues(int containerId)
        {
            var sink = new List<string>();
            CollectLeaves(containerId, sink, Volatile.Read(ref _snapshot).Values, 0);
            return sink.ToArray();
        }

        private void CollectLeaves(
            int id,
            List<string> sink,
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
        ///
        /// 切换成功后触发 <see cref="CultureChanged"/>。事件在锁外触发，
        /// 处理函数里可以安全取值、也可以再次调用本方法；处理函数抛出的异常会向外传播。
        /// </summary>
        public bool SetCulture(CultureInfo culture)
        {
            if (culture == null)
            {
                throw new ArgumentNullException(nameof(culture));
            }

            CultureInfo previous;
            CultureInfo effective;
            int revision;

            lock (_gate)
            {
                var snapshot = Volatile.Read(ref _snapshot);

                effective = culture.Name.Length == 0
                    ? CultureInfo.GetCultureInfo(_catalog.DefaultCultureName)
                    : culture;

                // 不属于任何受支持语言（含其 Parent 链）时，落到基语言，
                // 保证 CurrentCulture 与值的来源一致。
                if (!_supported.Contains(effective.Name))
                {
                    effective = CultureInfo.GetCultureInfo(_catalog.DefaultCultureName);
                }

                if (string.Equals(snapshot.Culture.Name, effective.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                previous = snapshot.Culture;
                revision = _nextRevision++;
                Volatile.Write(ref _snapshot, new Snapshot(Resolve(effective.Name), effective, revision));
            }

            // 出锁再通知：快照此时已经是新语言，订阅者读到的一致；
            // 也不会因为某个订阅者慢而拖住别的线程切语言。
            // 没有订阅者时连事件参数都不必分配。
            var handler = CultureChanged;
            if (handler != null)
            {
                handler(this, new CultureChangedEventArgs(previous, effective, revision));
            }

            return true;
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

        /// <summary>
        /// 沿 Parent 链合并成一维文本数组：ja-JP → ja → 基语言，逐 id 取第一个非 null 的值。
        /// 仍然缺失的槽位填空串，使热路径无需分支。
        /// </summary>
        private string[] BuildResolved(string cultureName)
        {
            var length = _catalog.TextCount;

            // 基语言本身：补齐空槽即可，无需逐 id 走链
            if (string.Equals(cultureName, _catalog.DefaultCultureName, StringComparison.OrdinalIgnoreCase))
            {
                return Materialize(_catalog.GetTextValues(cultureName), length);
            }

            var chain = BuildChain(cultureName);

            // 链上只有一层（常见于二级 culture，如 ja-JP 直接命中）
            if (chain.Count == 1)
            {
                return Materialize(_catalog.GetTextValues(chain[0]), length);
            }

            // 预先取各语言的文本数组，避免在内层循环里反复查表
            var textValues = new string?[chain.Count][];
            for (var i = 0; i < chain.Count; i++)
            {
                textValues[i] = _catalog.GetTextValues(chain[i])!;
            }

            var result = new string[length];

            for (var id = 0; id < length; id++)
            {
                var value = (string?)null;

                for (var level = 0; level < textValues.Length; level++)
                {
                    var values = textValues[level];
                    if (values != null && id < values.Length && values[id] != null)
                    {
                        value = values[id];
                        break;
                    }
                }

                result[id] = value ?? string.Empty;
            }

            return result;
        }

        /// <summary>把可能含 null 的文本数组补成不含 null 的数组。</summary>
        private string[] Materialize(string?[]? source, int length)
        {
            var result = new string[length];

            if (source == null)
            {
                // 该语言的文本数组整体缺失：全部填空串，热路径不用再判 null。
                for (var i = 0; i < length; i++)
                {
                    result[i] = string.Empty;
                }

                return result;
            }

            var copy = Math.Min(length, source.Length);
            for (var i = 0; i < copy; i++)
            {
                result[i] = source[i] ?? string.Empty;
            }

            for (var i = copy; i < length; i++)
            {
                result[i] = string.Empty;
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




