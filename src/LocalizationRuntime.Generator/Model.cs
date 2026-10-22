using System;

namespace LocalizationRuntime.Generator
{
    /// <summary>一个键的定义：点分路径 + 编译期 id。</summary>
    internal sealed class KeyDef : IEquatable<KeyDef>
    {
        public KeyDef(int id, string path, int line, int column)
        {
            Id = id;
            Path = path;
            Line = line;
            Column = column;
        }

        public int Id { get; }

        public string Path { get; }

        public int Line { get; }

        public int Column { get; }

        public bool Equals(KeyDef? other)
        {
            return other != null
                && Id == other.Id
                && string.Equals(Path, other.Path, StringComparison.Ordinal)
                && Line == other.Line
                && Column == other.Column;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as KeyDef);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    /// <summary>一个语言的原始表。缺键位置为 null。</summary>
    internal sealed class CultureTable : IEquatable<CultureTable>
    {
        public CultureTable(string name, string?[] values)
        {
            Name = name;
            Values = values;
        }

        public string Name { get; }

        public string?[] Values { get; }

        public bool Equals(CultureTable? other)
        {
            if (other == null || !string.Equals(Name, other.Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (Values.Length != other.Values.Length)
            {
                return false;
            }

            for (var i = 0; i < Values.Length; i++)
            {
                if (!string.Equals(Values[i], other.Values[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as CultureTable);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    /// <summary>待报告的诊断，解析阶段先收集，发射阶段统一报。</summary>
    internal sealed class DiagnosticItem : IEquatable<DiagnosticItem>
    {
        public DiagnosticItem(string id, string message, int line, int column, string file)
        {
            Id = id;
            Message = message;
            Line = line;
            Column = column;
            File = file;
        }

        public string Id { get; }

        public string Message { get; }

        public int Line { get; }

        public int Column { get; }

        public string File { get; }

        public bool Equals(DiagnosticItem? other)
        {
            return other != null
                && string.Equals(Id, other.Id, StringComparison.Ordinal)
                && string.Equals(Message, other.Message, StringComparison.Ordinal)
                && Line == other.Line
                && Column == other.Column
                && string.Equals(File, other.File, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as DiagnosticItem);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() ^ Line;
        }
    }

    /// <summary>生成器的完整输入模型。必须值可比较，增量缓存才有意义。</summary>
    internal sealed class ResourceModel : IEquatable<ResourceModel>
    {
        public ResourceModel(
            string ns,
            string className,
            string defaultCulture,
            KeyDef[] keys,
            CultureTable[] cultures,
            DiagnosticItem[] diagnostics)
        {
            Namespace = ns;
            ClassName = className;
            DefaultCulture = defaultCulture;
            Keys = keys;
            Cultures = cultures;
            Diagnostics = diagnostics;
        }

        public string Namespace { get; }

        public string ClassName { get; }

        public string DefaultCulture { get; }

        public KeyDef[] Keys { get; }

        public CultureTable[] Cultures { get; }

        public DiagnosticItem[] Diagnostics { get; }

        public bool HasKeys
        {
            get { return Keys.Length > 0; }
        }

        public bool Equals(ResourceModel? other)
        {
            if (other == null
                || !string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                || !string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                || !string.Equals(DefaultCulture, other.DefaultCulture, StringComparison.Ordinal)
                || Keys.Length != other.Keys.Length
                || Cultures.Length != other.Cultures.Length
                || Diagnostics.Length != other.Diagnostics.Length)
            {
                return false;
            }

            for (var i = 0; i < Keys.Length; i++)
            {
                if (!Keys[i].Equals(other.Keys[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < Cultures.Length; i++)
            {
                if (!Cultures[i].Equals(other.Cultures[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < Diagnostics.Length; i++)
            {
                if (!Diagnostics[i].Equals(other.Diagnostics[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ResourceModel);
        }

        public override int GetHashCode()
        {
            return (Namespace.GetHashCode() * 397) ^ Keys.Length;
        }
    }
}
