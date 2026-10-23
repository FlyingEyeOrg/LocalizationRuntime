# LocalizationRuntime

编译期固化多语言资源的 .NET 本地化运行时。运行时**不加载任何资源文件、不做 JSON 解析**，只做一次数组索引。

```csharp
// 唯一的准备工作：把生成好的 catalog 交给 Localizer
var localizer = new Localizer(Catalog.Instance);

localizer.Get(LocalizationIds.AppObject.Title);     // "退刀管控系统"
localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
localizer.Get(LocalizationIds.AppObject.Title);     // "Tool Retraction Control"

// 容器也能取：整个数组、元素个数、递归拍平
localizer.GetValues(LocalizationIds.MainWindowObject.Tabs);   // ["换刀", "设备信息", ...]
localizer.GetChildCount(LocalizationIds.Menus);               // 3
localizer.GetAllValues(LocalizationIds.Matrix);               // ["A1", "A2", "B1"]
```

核心只有两件事：**按 id 取当前语言的值**，**设置当前语言**。

全部按 id 取值，字符串键路径不对外暴露——`LocalizationIds` 已经把每一条文本都编成了常量。

切语言后再次取值自然就是新语言的值；缓存过文本的地方订阅 `CultureChanged` 即可知道该重读。
没有弱引用、没有登记表——只有一个普通事件，订阅与退订由调用方负责。

## 依赖注入

ASP.NET Core 风格的容器可以直接接入。注册之后，任何在构造函数里声明 `Localizer`
的类型都会由容器自动注入——业务代码不需要自己 `new`，也不需要知道 `Catalog` 的存在。

```csharp
// ① 注册，唯一一步，不需要传任何东西
builder.Services.AddLocalizationRuntime();

// ② 业务服务只声明需要什么——依赖的是接口，不是具体类型
public sealed class OrderService
{
    private readonly ILocalizer _localizer;

    public OrderService(ILocalizer localizer) => _localizer = localizer;

    public string Title => _localizer.Get(LocalizationIds.AppObject.Title);
}
```

注册方法本身也是生成器产出的：它和 `Catalog` 同属你的程序集，因此直接使用
`Catalog.Instance`，调用方既不用传 catalog，也不用知道它存在。

业务代码只依赖 `ILocalizer`：换实现、写单元测试替身都不牵动调用方。
`Localizer` 与 `ILocalizer` 解析到同一个实例。

`Localizer` 注册为**单例**：当前语言是进程级状态，`SetCulture` 对所有调用方生效。
这与库的设计一致——没有按请求隔离的语言，也就没有 `AsyncLocal`、没有隐藏的状态传递。
需要「每个用户自己的语言」时，应该在应用层按用户选择调用一次 `SetCulture`，
或者为每种语言各建一个独立的宿主。

可选参数指定初始语言，留空则按当前 UI culture 自动选择：

```csharp
builder.Services.AddLocalizationRuntime(CultureInfo.GetCultureInfo("en-US"));
```

重复注册是安全的（内部用 `TryAdd`），多个模块各自注册不会产生多份实例。

为此运行时包引用了 `Microsoft.Extensions.DependencyInjection.Abstractions`（仅接口与
`ServiceDescriptor`，体积很小）——生成的注册代码需要 `IServiceCollection`，
靠这个依赖传递到你的项目，你不必再引一次。不用 DI 的场景可以完全忽略它，
`new Localizer(Catalog.Instance)` 照旧可用。

## 两条设计决定

**一、值在编译期固化，运行期只做选择。**

源生成器读取 `Localization/<culture>.json`，为每种语言生成一个 `string?[]`，键被编译成 `const int`。
运行时切语言 = 换一个数组引用，无 IO、无解析、无锁、无分配。

因此 `SetCulture` 是**同步**的——没有任何可以失败的东西，也就不需要两阶段提交。

**二、值不是 `const`，是数组元素。**

`const string` 会被内联进调用方 IL：库更新翻译后下游不重编译仍是旧值，且无法跟随语言切换。
所以键是 `const int`（跨程序集内联无害），值是数组槽位。

## 目录约定

```
<project>/
  Localization/
    zh-CN.json     ← 基语言：定义键集合，也是缺键时的回退目标
    en-US.json
    ja-JP.json     ← 可以缺键，缺的部分回退到基语言
```

文件名即 culture。目录说明它是什么，文件名说明是哪一种语言，不再拼前缀。

基语言由 `LocalizationDefaultCulture` 指定（默认 `zh-CN`）：

```xml
<LocalizationDefaultCulture>en-US</LocalizationDefaultCulture>
```

## 生成物

对上面三个文件，生成器产出：

```csharp
public static class LocalizationIds        // 键 → 编译期 id，结构与 JSON 层级一致
{
    // 叶子 id 的个数：0..TextCount-1 可取文本，再往上是容器。
    // 容器 id 从它开始编号，所以它的值和第一个容器 id 相同——两者含义不同，别混用。
    public const int TextCount = 14;

    // 容器 id：数组与对象本身也有 id，能取整段
    public const int Menus = 21;
    public const int Data = 14;

    public static class MenusObject
    {
        public const int MenusItem0 = 22;     // 数组元素 → {前缀}Item{N}
        public const int MenusItem1 = 23;
        public const int MenusItem2 = 24;

        public static class MenusItem0Object
        {
            public const int Title = 8;       // 叶子：真实数组下标
            public const int Value = 9;
        }
    }

    public static class DataObject
    {
        public const int DataItem0 = 15;
        public static class DataItem0Object
        {
            public const int DataItem0Item0 = 16;   // 嵌套数组：Item 逐层累加
            public const int DataItem0Item1 = 17;

            public static class DataItem0Item0Object
            {
                public const int Value1 = 0;
                public const int Value2 = 1;
            }
        }
    }
}
internal sealed class GeneratedCatalog  // 每语言一个 string?[]（未翻译为 null）+ 父子表
public static class LocalizationServiceCollectionExtensions  // AddLocalizationRuntime()，见上文
```

键的层级与 JSON 完全对应，所以 `MainWindow.UserInfo.UserName` 就是 `LocalizationIds.MainWindowObject.UserInfoObject.UserName`。

数字段不能作标识符，统一映射成 `Item{N}`，且**每一层都保留 `Item`**：

| JSON | 生成的键 |
|---|---|
| `"Tabs": ["a", "b"]` | `LocalizationIds.TabsObject.TabsItem0` |
| `"Menus": [{ "Header": "文件" }]` | `LocalizationIds.MenusObject.MenusItem0Object.Header` |
| `"Data": [[{ "V": 1 }]]` | `LocalizationIds.DataObject.DataItem0Object.DataItem0Item0Object.V` |

命名规则只有一条：**类名一律以 `Object` 结尾，`int` 常量一律不带**。

## 取值

id 分三类，互不重叠：

| id | 含义 | `Get(id)` |
|---|---|---|
| `0 .. LocalizationIds.TextCount-1` | 叶子（具体文本） | 返回字符串 |
| `>= LocalizationIds.TextCount` | 容器（数组 / 对象） | `InvalidOperationException` |
| 不在上述任一区间 | 不存在的 id | `ArgumentOutOfRangeException` |

容器没有文本，所以传给 `Get` 会抛异常——这能挡住「把数组当字符串用」。
两种失败刻意用不同异常，避免混淆：

```csharp
string title = localizer.Get(LocalizationIds.AppObject.Title);   // 单值

localizer.Get(LocalizationIds.Menus);
// InvalidOperationException: id 21 是容器（数组 / 对象），没有对应的文本。
//   取容器请用 GetChildren / GetValues / GetAllValues。

localizer.Get(9999);
// ArgumentOutOfRangeException: 键 id 不存在：叶子 id 的有效区间是 0..TextCount-1，
//   更大的 id 必须是容器。
```

前者是用法错误（id 合法，只是没有文本），后者是真的不存在——据此可以直接判断
是自己调用写错了，还是传进来的数字来路不明。

## 容器：取整个数组

容器 id 不只是标签，它能拿到子 id、元素个数和整段文本。

```csharp
// 元素个数
int n = localizer.GetChildCount(LocalizationIds.Menus);          // 3

// 一维字符串数组：一次取全部
string[] tabs = localizer.GetValues(LocalizationIds.MainWindowObject.Tabs);

// 对象数组：先拿子 id，再逐个展开
foreach (int itemId in localizer.GetChildren(LocalizationIds.Menus))
{
    Console.WriteLine(localizer.GetChildCount(itemId));          // 该元素的成员数
}

// 二维数组：按层取，顺序严格等于下标顺序
foreach (int rowId in localizer.GetChildren(LocalizationIds.Matrix))
{
    Console.WriteLine(string.Join(" | ", localizer.GetValues(rowId)));
}

// 任意深度：递归拍平成一条
string[] all = localizer.GetAllValues(LocalizationIds.Cube);
```

| 方法 | 用途 |
|---|---|
| `GetChildCount(id)` | 直接子节点个数；叶子返回 0 |
| `GetChildren(id)` | 直接子 id 数组；叶子/越界返回空数组 |
| `GetValues(id)` | 子节点全是文本时，一次取全部；混有对象则抛异常并提示改用 `GetChildren` |
| `GetAllValues(id)` | 递归展开整棵子树，按顺序返回所有叶子 |

注意：数组顺序严格等于下标顺序；对象成员的顺序由键的排序决定，与 JSON 原文顺序可能不同，但每次运行都一致。

## 切语言后如何刷新界面

切语言后再次读取就是新值。如果某处把文本缓存了下来，订阅 `CultureChanged`
即可知道该失效重读：

```csharp
public sealed class HeaderViewModel
{
    private readonly ILocalizer _localizer;
    private string? _cachedTitle;

    public HeaderViewModel(ILocalizer localizer)
    {
        _localizer = localizer;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public string Title => _cachedTitle ??= _localizer.Get(LocalizationIds.AppObject.Title);

    private void OnCultureChanged(object? sender, CultureChangedEventArgs e)
    {
        _cachedTitle = null;      // 下次读取时按新语言重建
    }
}
```

事件在快照换好之后、锁外触发，所以处理函数里取值已经是新语言，
也可以放心地在里面再切一次语言。参数带 `OldCulture` / `NewCulture` / `Revision`，
`Revision` 可用于判断事件是否比上次处理的更新。

订阅的代价要自己承担：`Localizer` 是单例，**短生命周期的订阅者必须在销毁时退订**，
否则会被单例长期持有。只读文本、不缓存的调用方不必订阅，直接读就是新值。

不想订阅也可以，把刷新时机交给调用方：

```csharp
localizer.SetCulture(CultureInfo.GetCultureInfo("ja-JP"));
viewModel.ReloadTexts();
```

## 回退

```
ja-JP → ja → 基语言
```

沿 `CultureInfo.Parent` 链逐级查找，逐键取第一个非 null 的值，最后回落到基语言。
合并结果按 culture 缓存，切语言后第一次取值完成一次合并，之后仍是数组索引。

## 诊断

| ID | 级别 | 内容 |
|---|---|---|
| LR0001 | Error | 未找到任何资源文件 |
| LR1001 | Error | JSON 无效（定位到行列） |
| LR1002 | Error | 键重复 |
| LR1003 | Error | 键归一化后冲突（`title` / `Title`） |
| LR1004 | Error | 缺少基语言文件 |
| LR1005 | Error | 存在基语言没有的键 |
| LR1006 | Warning | 某语言缺键（运行期回退） |
| LR1007 | Error | 占位符与基语言不一致 |
| LR1008 | Warning | 值为空字符串 |
| LR1010 | Error | 空对象或空数组 |
| LR1011 | Error | 值必须是非 null 字符串 |
| LR1012 | Error | 键名不能包含点号 |
| LR1013 | Error | 数组元素类型必须一致 |
| LR1014 | Error | 键名不是合法的 C# 标识符 |

抑制与升级用两个 MSBuild 属性，逗号或分号分隔多个 ID：

```xml
<PropertyGroup>
  <!-- 让该诊断不再出现 -->
  <LocalizationSuppressedDiagnostics>LR1006</LocalizationSuppressedDiagnostics>
  <!-- 把警告升级为错误 -->
  <LocalizationDiagnosticsAsErrors>LR1008</LocalizationDiagnosticsAsErrors>
</PropertyGroup>
```

注意**不能用** `.editorconfig` 的 `dotnet_diagnostic.LR1006.severity`：源生成器通过
`SourceProductionContext.ReportDiagnostic` 上报的诊断不会经过编译器的诊断过滤层，
那条配置传不到生成器里（实测确认）。这是上面改用 MSBuild 属性的原因。

## 可配置的 MSBuild 属性

| 属性 | 默认值 | 说明 |
|---|---|---|
| `LocalizationDirectory` | `Localization` | 资源目录，可写多级（`Resources\I18n`） |
| `LocalizationDefaultCulture` | `zh-CN` | 基语言：定义键集合，也是回退目标 |
| `LocalizationNamespaceSuffix` | `.Localization` | 命名空间后缀，最终为 `RootNamespace` + 后缀 |
| `EnableLocalizationRuntimeAutoIncludes` | `true` | 设为 `false` 可关闭自动挂载，手写 `AdditionalFiles` |

三种覆盖方式都有效，优先级由低到高：

```xml
<!-- ① Directory.Build.props（全仓库） -->
<LocalizationDirectory>Resources\I18n</LocalizationDirectory>
```
```xml
<!-- ② csproj 内 -->
<PropertyGroup>
  <LocalizationDefaultCulture>en-US</LocalizationDefaultCulture>
</PropertyGroup>
```
```bash
# ③ 命令行
dotnet build -p:LocalizationDefaultCulture=en-US
```

默认值与挂载逻辑故意分开放：默认值在包内 `build/*.props`（csproj 之前求值），
`AdditionalFiles` 在 `build/*.targets`（csproj 之后求值）。否则用户覆盖的目录名在求值时还没生效。

## 性能

1220 键 × 3 语言，Release，`benchmarks/LocalizationRuntime.Benchmarks`：

| 操作 | 耗时 | 分配 |
|---|---|---|
| `Get(id)` 命中 | 1.5 ns | 0 B |
| `Get(string)` 命中 | 10.4 ns | 0 B |
| `Get(id)` 叶子 | 0.71 ns | 0 B |
| `GetValues(数组)` 一次取 N 个 | 视 N 而定 | 1 次分配 |
| `SetCulture`（已缓存） | 0.12 µs | — |
| 首次切到未缓存语言（含构建文本数组） | 3.1 ms | — |
| **启动**（访问 catalog + 构建文本数组） | **8.4 ms** | — |

几个关键点：

- `Get(id)` 是一次快照引用读加一次数组索引，热路径无锁、无字典哈希、零分配。
- **文本数组惰性填充**：只有被请求的语言才建数组，启动时只付当前语言的代价。
- **父子表是编译期常量**：容器 → 子 id 是一张 `int[][]`，`GetChildren` 退化为一次索引，不建字典。
- `GetValues` / `GetAllValues` 每次调用分配一个新数组，适合「切语言后重建一次列表」，
  不适合放进逐帧渲染的热路径。
- `SetCulture` 只做一次受支持性哈希查找加一次快照替换；没有订阅者时不分配事件参数。

## 测试

```bash
# 用 xunit 执行器直接跑，不依赖 VSTest
dotnet run --project tests/LocalizationRuntime.Tests.Runner

# 标准方式（需要 testhost 能正常运行）
dotnet test tests/LocalizationRuntime.Tests
```

覆盖运行时语义（回退链、幂等切换、订阅者异常隔离、缓存引用稳定性）与并发
（多线程切语言、切换期间读值一致性）。

## 项目结构

```
src/LocalizationRuntime/               运行时（netstandard2.0 + net8.0）
src/LocalizationRuntime.Generator/     Roslyn 增量生成器 + 包内 build/ 目标
samples/LocalizationRuntime.Sample/    三种语言的示例（依赖注入接入）
tests/LocalizationRuntime.Tests/       xunit 测试
tests/LocalizationRuntime.Tests.Runner/ 不依赖 VSTest 的测试执行器
benchmarks/LocalizationRuntime.Benchmarks/ 1220 键规模的热路径基准
```

## 尚未实现

- WPF 适配层（`LocExtension`、绑定源）
- 现场覆盖（`OverrideDirectory`）与热重载
- 伪本地化
- 复数规则
- CLI 导出/导入

## 已评估并否决的方案

记录在这里，避免重复讨论。

### 按语言拆分独立程序集（`MyApp.Localization.ja-JP.dll`）

**否决。**

技术上是可行的，实测确认：静态引用 + 类型触碰是惰性的，`deps.json` 完整，
trim / 单文件发布都安全。启动只加载当前语言，切语言时才加载对应程序集。

否决理由是收益与成本不匹配：

- 实测收益：3 语言全量加载在 8.9 ms 启动里只占约 1–2 ms；
- 实际成本：1 个项目变 1 + N 个项目，生成器要支持单语言模式并产出跨程序集引用，
  示例、测试、基准、CI 打包全部要改；
- 拐点：语言数超过 8 种、或字符串总量超过约 500 KB 时才值得。

另有两条实测得到的实现约束，将来若重启此方案可直接复用：

1. **不能用 `Csc` 任务从同一项目产出第二个程序集**。内容不会真正分离
   （主程序集仍会带上被排除类型的副本），必须每种语言一个独立 csproj。
2. **程序集名与命名空间必须分开推导**。`ja-JP` 里的连字符在命名空间里非法
   （`error CS0246`），所以程序集名用 `MyApp.Localization.ja-JP`，
   命名空间用 `MyApp.Localization.JaJP`。

### 发布时只生成选定语言的资源

**否决。**

这会让产品失去运行时切换语言的能力，也失去按系统 culture 自动探测的能力——
用户装到日语系统上只能看到中文。除非走多语言安装包路线，否则不该这么做。
安装包层面按语言分发 dll 是另一回事，与库无关。






