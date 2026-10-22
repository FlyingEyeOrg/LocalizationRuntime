using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LocalizationRuntime.Generator
{
    internal enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null
    }

    internal sealed class JsonNode
    {
        public JsonKind Kind;

        public Dictionary<string, JsonNode>? Members;

        public List<JsonNode>? Items;

        public string? Text;

        public int Line;

        public int Column;
    }

    internal sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int line, int column)
            : base(message)
        {
            LineNumber = line;
            ColumnNumber = column;
        }

        public int LineNumber { get; }

        public int ColumnNumber { get; }
    }

    /// <summary>
    /// 手写的极简 JSON 读取器。不用 System.Text.Json：分析器同时跑在 VS 的
    /// .NET Framework 进程和 dotnet build 里，引第三方 JSON 库是已知的版本打架源。
    /// 只支持资源文件需要的子集：对象、数组、字符串、数字、true/false/null。
    /// </summary>
    internal sealed class JsonReader
    {
        private readonly string _text;
        private int _pos;
        private int _line = 1;
        private int _column = 1;

        private JsonReader(string text)
        {
            _text = text;
        }

        public static JsonNode Parse(string text)
        {
            var reader = new JsonReader(text);
            reader.SkipTrivia();
            var node = reader.ReadValue();
            reader.SkipTrivia();
            if (!reader.AtEnd)
            {
                throw reader.Error("JSON 结束后仍有多余内容");
            }

            return node;
        }

        private bool AtEnd
        {
            get { return _pos >= _text.Length; }
        }

        private char Peek
        {
            get { return _pos < _text.Length ? _text[_pos] : '\0'; }
        }

        private Exception Error(string message)
        {
            return new JsonParseException(message, _line, _column);
        }

        private void Advance()
        {
            if (_pos < _text.Length)
            {
                if (_text[_pos] == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }

                _pos++;
            }
        }

        private void SkipTrivia()
        {
            while (!AtEnd)
            {
                var c = Peek;
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }
        }

        private JsonNode ReadValue()
        {
            SkipTrivia();
            if (AtEnd)
            {
                throw Error("意外的文件结束");
            }

            var c = Peek;
            switch (c)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return new JsonNode { Kind = JsonKind.String, Text = ReadString(), Line = _line, Column = _column };
                case 't' when Matches("true"):
                    Consume("true");
                    return new JsonNode { Kind = JsonKind.Bool, Text = "true" };
                case 'f' when Matches("false"):
                    Consume("false");
                    return new JsonNode { Kind = JsonKind.Bool, Text = "false" };
                case 'n' when Matches("null"):
                    Consume("null");
                    return new JsonNode { Kind = JsonKind.Null };
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ReadNumber();
                    }

                    throw Error("无法识别的 JSON 值起始字符 '" + c + "'");
            }
        }

        private bool Matches(string literal)
        {
            if (_pos + literal.Length > _text.Length)
            {
                return false;
            }

            return string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) == 0;
        }

        private void Consume(string literal)
        {
            for (var i = 0; i < literal.Length; i++)
            {
                Advance();
            }
        }

        private JsonNode ReadObject()
        {
            var node = new JsonNode
            {
                Kind = JsonKind.Object,
                Members = new Dictionary<string, JsonNode>(StringComparer.Ordinal),
                Line = _line,
                Column = _column
            };

            Advance();
            SkipTrivia();
            if (Peek == '}')
            {
                Advance();
                return node;
            }

            while (true)
            {
                SkipTrivia();
                if (Peek != '"')
                {
                    throw Error("期望属性名");
                }

                var nameLine = _line;
                var nameColumn = _column;
                var name = ReadString();

                SkipTrivia();
                if (Peek != ':')
                {
                    throw Error("期望 ':'");
                }

                Advance();
                var value = ReadValue();

                if (node.Members!.ContainsKey(name))
                {
                    throw new JsonParseException(
                        "键 ' " + name + "' 重复出现", nameLine, nameColumn);
                }

                node.Members[name] = value;

                SkipTrivia();
                if (Peek == ',')
                {
                    Advance();
                    continue;
                }

                if (Peek == '}')
                {
                    Advance();
                    return node;
                }

                throw Error("期望 ',' 或 '}'");
            }
        }

        private JsonNode ReadArray()
        {
            var node = new JsonNode
            {
                Kind = JsonKind.Array,
                Items = new List<JsonNode>(),
                Line = _line,
                Column = _column
            };

            Advance();
            SkipTrivia();
            if (Peek == ']')
            {
                Advance();
                return node;
            }

            while (true)
            {
                node.Items!.Add(ReadValue());

                SkipTrivia();
                if (Peek == ',')
                {
                    Advance();
                    continue;
                }

                if (Peek == ']')
                {
                    Advance();
                    return node;
                }

                throw Error("期望 ',' 或 ']'");
            }
        }

        private string ReadString()
        {
            Advance();
            var sb = new StringBuilder();

            while (true)
            {
                if (AtEnd)
                {
                    throw Error("字符串未闭合");
                }

                var c = Peek;
                if (c == '"')
                {
                    Advance();
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    Advance();
                    if (AtEnd)
                    {
                        throw Error("转义序列未结束");
                    }

                    var esc = Peek;
                    Advance();
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length)
                            {
                                throw Error("\\u 转义不足 4 位");
                            }

                            var hex = _text.Substring(_pos, 4);
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                throw Error("\\u 转义不是合法十六进制");
                            }

                            for (var i = 0; i < 4; i++)
                            {
                                Advance();
                            }

                            sb.Append((char)code);
                            break;
                        default:
                            throw Error("不支持的转义 '\\" + esc + "'");
                    }

                    continue;
                }

                if (c == '\n' || c == '\r')
                {
                    throw Error("字符串内出现裸换行");
                }

                sb.Append(c);
                Advance();
            }
        }

        private JsonNode ReadNumber()
        {
            var start = _pos;
            while (!AtEnd)
            {
                var c = Peek;
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }

            var raw = _text.Substring(start, _pos - start);
            return new JsonNode { Kind = JsonKind.Number, Text = raw };
        }
    }
}
