# 诊断规则的发布版本跟踪
#
# 规则一旦随包发布，ID 与语义就不能再改，否则使用方的 .editorconfig 抑制会失效。
# 新增规则时，在 ## Unreleased 下追加一行；发布时把该段改成 ## Release X.Y.Z。
#
# 格式：规则 ID 与标题（与分析器里 DiagnosticDescriptor.Title 完全一致）。
# 格式说明见 https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Unreleased

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LR0001  | Localization | Error | 未找到资源文件
LR1001  | Localization | Error | 资源文件 JSON 无效
LR1002  | Localization | Error | 键重复
LR1003  | Localization | Error | 键名归一化后冲突
LR1004  | Localization | Error | 缺少基语言资源文件
LR1005  | Localization | Error | 存在基语言没有的键
LR1006  | Localization | Warning | 缺少键
LR1007  | Localization | Error | 占位符与基语言不一致
LR1008  | Localization | Warning | 值为空字符串
LR1010  | Localization | Error | 空对象或空数组
LR1011  | Localization | Error | 值必须是非 null 字符串
LR1012  | Localization | Error | 键名不能包含点号
LR1013  | Localization | Error | 数组元素类型必须一致
LR1014  | Localization | Error | 键名不是合法的 C# 标识符
