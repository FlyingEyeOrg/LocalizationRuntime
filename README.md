# LocalizationRuntime

编译期固化多语言资源的 .NET 本地化运行时。运行时**不加载任何资源文件、不做 JSON 解析**，只做一次数组索引。

```csharp
var table = new LocalizationTable(L.Catalog);   // 按系统 culture 自动选语言
var l = new L(table);

l.App.Title.Value;                              // "退刀管控系统"
table.SetCulture(CultureInfo.GetCultureInfo("en-US"));
l.App.Title.Value;                              // "Tool Retraction Control"
```

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
internal static class LKeys            // 键 → 编译期 id，按自然序（Tabs.2 在 Tabs.10 之前）
internal sealed class GeneratedCatalog  // 每语言一个 string?[]（缺键为 null）
internal sealed class L                 // 强类型访问器
{
    public AppStrings App { get; }
    public MainWindowStrings MainWindow { get; }
}
```

访问器分两类：

| 形状 | 类型 | 说明 |
|---|---|---|
| `"Title": "..."` | `LocalizedText` | 单值，`.Value` 为一次数组索引 |
| `"Tabs": ["a","b","c"]` | `LocalizedList` | 数组，长度编译期已知；`.Value` 按版本号缓存，切一次语言只换一次引用 |

`LocalizedList` 缓存引用是有意的：绑到 `TabControl.ItemsSource` 时，切语言不重建控件，
`SelectedIndex` 和滚动位置都不会丢。

## 响应式

两类访问器都实现 `INotifyPropertyChanged`，切语言时由表统一唤醒。
它们由生成器做成**容器单例**，全局只有一份，因此不需要弱引用，也不存在忘记清理导致泄漏的问题。

```csharp
public MainViewModel(L l)
{
    _l = l;
    _l.App.Title.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
}
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

严重度可在 `.editorconfig` 里覆盖：

```ini
dotnet_diagnostic.LR1006.severity = error
```

## 可配置的 MSBuild 属性

| 属性 | 默认值 | 说明 |
|---|---|---|
| `LocalizationDirectory` | `Localization` | 资源目录，可写多级（`Resources\I18n`） |
| `LocalizationDefaultCulture` | `zh-CN` | 基语言：定义键集合，也是回退目标 |
| `LocalizationClassName` | `L` | 生成的门面类名 |
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
dotnet build -p:LocalizationClassName=Strings
```

默认值与挂载逻辑故意分开放：默认值在包内 `build/*.props`（csproj 之前求值），
`AdditionalFiles` 在 `build/*.targets`（csproj 之后求值）。否则用户覆盖的目录名在求值时还没生效。

## 性能

1220 键 × 3 语言，Release，`benchmarks/LocalizationRuntime.Benchmarks`：

| 操作 | 耗时 | 分配 |
|---|---|---|
| `Get(id)` 命中 | 1.18 ns | 0 B |
| `Get(string)` 命中 | 10.20 ns | 0 B |
| `LocalizedList.Value` 命中缓存 | 3.74 ns | 0 B |
| `Format(1 参数)` | 68.50 ns | 88 B |
| `SetCulture` | 2.65 µs | — |

几个关键点：

- `Get(id)` 是一次快照引用读加一次数组索引，热路径无锁、无字典哈希、零分配。
- `LocalizedList` 的缓存以不可变对象整体替换，命中时不加锁；未命中才在锁内重建。
- 通知列表以不可变数组发布，切语言时不复制单元集合。
- 模板不含 `{` 时跳过 `string.Format`，`Format` 的单参数重载避免 `params` 数组分配。

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
samples/LocalizationRuntime.Sample/    三种语言的示例
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
