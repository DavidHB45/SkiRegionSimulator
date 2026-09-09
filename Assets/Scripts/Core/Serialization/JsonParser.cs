using System;
using System.Globalization;
using System.Text;

namespace AlpineSim.Core.Serialization
{
    public sealed class JsonParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public JsonParseException(string message, int line, int column) : base(message + " (line " + line + ", col " + column + ")")
        {
            Line = line; Column = column;
        }
    }

    /// <summary>Strict RFC 8259 JSON parser with one tolerance: // and /* */ comments are skipped so data files can be annotated.</summary>
    public sealed class JsonParser
    {
        private readonly string _s;
        private int _i;
        private int _line = 1;
        private int _lineStart;

        private JsonParser(string s) { _s = s ?? string.Empty; }

        public static JsonNode Parse(string text)
        {
            var p = new JsonParser(text);
            p.SkipWs();
            var node = p.ParseValue();
            p.SkipWs();
            if (p._i != p._s.Length) p.Fail("Trailing characters after JSON value");
            return node;
        }

        private void Fail(string msg) => throw new JsonParseException(msg, _line, _i - _lineStart + 1);

        private void SkipWs()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '\n') { _i++; _line++; _lineStart = _i; }
                else if (c == ' ' || c == '\t' || c == '\r') _i++;
                else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    while (_i < _s.Length && _s[_i] != '\n') _i++;
                }
                else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                {
                    _i += 2;
                    while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/'))
                    {
                        if (_s[_i] == '\n') { _line++; _lineStart = _i + 1; }
                        _i++;
                    }
                    _i = System.Math.Min(_s.Length, _i + 2);
                }
                else break;
            }
        }

        private JsonNode ParseValue()
        {
            if (_i >= _s.Length) Fail("Unexpected end of input");
            char c = _s[_i];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return JsonNode.FromString(ParseString());
                case 't': Expect("true"); return JsonNode.FromBool(true);
                case 'f': Expect("false"); return JsonNode.FromBool(false);
                case 'n': Expect("null"); return JsonNode.Null();
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    Fail("Unexpected character '" + c + "'");
                    return null;
            }
        }

        private void Expect(string lit)
        {
            if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0) Fail("Expected '" + lit + "'");
            _i += lit.Length;
        }

        private JsonNode ParseObject()
        {
            var obj = JsonNode.NewObject();
            _i++; // {
            SkipWs();
            if (_i < _s.Length && _s[_i] == '}') { _i++; return obj; }
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length || _s[_i] != '"') Fail("Expected string key");
                string key = ParseString();
                SkipWs();
                if (_i >= _s.Length || _s[_i] != ':') Fail("Expected ':'");
                _i++;
                SkipWs();
                obj.Set(key, ParseValue());
                SkipWs();
                if (_i >= _s.Length) Fail("Unterminated object");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return obj; }
                Fail("Expected ',' or '}'");
            }
        }

        private JsonNode ParseArray()
        {
            var arr = JsonNode.NewArray();
            _i++; // [
            SkipWs();
            if (_i < _s.Length && _s[_i] == ']') { _i++; return arr; }
            while (true)
            {
                SkipWs();
                arr.Add(ParseValue());
                SkipWs();
                if (_i >= _s.Length) Fail("Unterminated array");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return arr; }
                Fail("Expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (_i >= _s.Length) Fail("Unterminated string");
                char c = _s[_i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (_i >= _s.Length) Fail("Bad escape");
                    char e = _s[_i++];
                    switch (e)
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
                            if (_i + 4 > _s.Length) Fail("Bad unicode escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: Fail("Bad escape '\\" + e + "'"); break;
                    }
                }
                else
                {
                    if (c == '\n') { _line++; _lineStart = _i; }
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private JsonNode ParseNumber()
        {
            int start = _i;
            if (_s[_i] == '-') _i++;
            while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            if (_i < _s.Length && _s[_i] == '.') { _i++; while (_i < _s.Length && char.IsDigit(_s[_i])) _i++; }
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }
            string raw = _s.Substring(start, _i - start);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) Fail("Bad number '" + raw + "'");
            return JsonNode.FromNumberToken(raw, d);
        }
    }
}
