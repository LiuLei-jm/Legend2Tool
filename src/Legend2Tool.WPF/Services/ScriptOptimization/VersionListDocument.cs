using Legend2Tool.WPF.Models.ScriptOptimizations;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Legend2Tool.WPF.Services.ScriptOptimization;

// Parse static JavaScript literals without executing the selected site's script.
// Source positions let edits preserve unrelated settings, comments and extra fields.
internal sealed class VersionListDocument
{
    private sealed record Token(string Value, int Start, int End, bool IsString = false);
    private sealed record Item(DropRateVersion Version, int Start, int End, int? Comma);
    private readonly string _text;
    private readonly List<Token> _tokens;
    private readonly List<Item> _items = [];
    private readonly int _arrayStart;
    private readonly int _arrayEnd;
    private int _position;

    public IReadOnlyList<DropRateVersion> Versions => _items.Select(item => item.Version).ToArray();

    public VersionListDocument(string text)
    {
        _text = text;
        _tokens = Tokenize(text);
        var declarations = Enumerable.Range(0, Math.Max(0, _tokens.Count - 1))
            .Where(i => !_tokens[i].IsString && _tokens[i].Value == "version_list"
                && _tokens[i + 1].Value == "=").ToArray();
        if (declarations.Length != 1)
            throw Invalid("需要唯一的 version_list 数组赋值");
        _position = declarations[0] + 2;
        _arrayStart = Current.Start;
        Expect("[");
        while (!At("]"))
        {
            int start = Current.Start;
            Expect("{");
            string? name = null;
            string? data = null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            while (!At("}"))
            {
                Token key = Current;
                if (!key.IsString && !IsIdentifier(key.Value))
                    throw Invalid("版本项目需要普通属性名");
                _position++;
                Expect(":");
                if (!keys.Add(key.Value))
                    throw Invalid("版本项目包含重复属性");
                if (key.Value is "name" or "data")
                {
                    if (!Current.IsString)
                        throw Invalid("name 和 data 必须是字符串");
                    if (key.Value == "name") name = Current.Value;
                    else data = Current.Value;
                    _position++;
                }
                else
                {
                    ReadLiteral(0);
                }
                if (At("}")) break;
                Expect(",");
            }
            int end = Current.End;
            Expect("}");
            if (name is null || string.IsNullOrWhiteSpace(data))
                throw Invalid("版本项目缺少 name 或有效的 data");
            int? comma = null;
            if (At(","))
            {
                comma = Current.Start;
                _position++;
            }
            else if (!At("]"))
                throw Invalid("版本项目之间缺少逗号");
            _items.Add(new Item(new DropRateVersion(name, data)
            {
                Index = _items.Count,
                SourceText = text
            }, start, end, comma));
        }
        _arrayEnd = Current.Start;
    }

    public string Append(string name, string data)
    {
        string value = $"{{ name: {JsonSerializer.Serialize(name)}, data: {JsonSerializer.Serialize(data)}, gllink: \"\" }}";
        var matches = _items.Where(item => string.Equals(item.Version.Name, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 0)
        {
            // Keep the first matching position and remove duplicates left by earlier generations.
            VersionListDocument document = this;
            foreach (Item duplicate in matches.Skip(1).Reverse())
            {
                document = new VersionListDocument(document.Delete(document._items[duplicate.Version.Index].Version));
            }
            Item existing = document._items[matches[0].Version.Index];
            string replaced = document._text.Remove(existing.Start, existing.End - existing.Start)
                .Insert(existing.Start, value);
            return document.RemoveBlankLines(replaced);
        }
        string newline = _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string entry = $"  {value},";
        int insertionPoint = _arrayEnd;
        while (insertionPoint > _arrayStart + 1 && _text[insertionPoint - 1] is ' ' or '\t')
            insertionPoint--;
        bool startsOnNewLine = _text[insertionPoint - 1] is '\r' or '\n';
        string result = _text.Insert(insertionPoint, (startsOnNewLine ? string.Empty : newline) + entry + newline);
        if (_items.Count > 0 && _items[^1].Comma is null)
            result = result.Insert(_items[^1].End, ",");
        return RemoveBlankLines(result);
    }

    public string Delete(DropRateVersion version)
    {
        if (version.SourceText != _text || version.Index < 0 || version.Index >= _items.Count
            || _items[version.Index].Version != version)
            throw new InvalidOperationException("custom.js 已发生变化，请重新选择目录刷新列表后再删除。");
        Item item = _items[version.Index];
        string result = _text;
        if (item.Comma is int following)
            result = result.Remove(following, 1);
        result = result.Remove(item.Start, item.End - item.Start);
        if (item.Comma is null && version.Index > 0 && _items[version.Index - 1].Comma is int preceding)
            result = result.Remove(preceding, 1);
        return RemoveBlankLines(result);
    }

    private string RemoveBlankLines(string updated)
    {
        int arrayEnd = _arrayEnd + updated.Length - _text.Length;
        var result = new StringBuilder(updated.Length);
        int cursor = 0;
        // Keep strings and comments as whole tokens, including any blank lines inside them.
        foreach (Token token in Tokenize(updated, includeComments: true))
        {
            if (token.Start < _arrayStart || token.Start > arrayEnd) continue;
            ReadOnlySpan<char> gap = updated.AsSpan(cursor, token.Start - cursor);
            int firstBreak = gap.IndexOfAny('\r', '\n');
            int lastBreak = gap.LastIndexOfAny('\r', '\n');
            if (cursor > _arrayStart && firstBreak >= 0)
            {
                int firstBreakEnd = firstBreak + 1;
                if (gap[firstBreak] == '\r' && firstBreakEnd < gap.Length && gap[firstBreakEnd] == '\n')
                    firstBreakEnd++;
                result.Append(gap[..firstBreakEnd]);
                result.Append(gap[(lastBreak + 1)..]);
            }
            else result.Append(gap);
            result.Append(updated.AsSpan(token.Start, token.End - token.Start));
            cursor = token.End;
        }
        result.Append(updated.AsSpan(cursor));
        return result.ToString();
    }

    private void ReadLiteral(int depth)
    {
        if (depth > 64) throw Invalid("属性嵌套过深");
        if (Current.IsString) { _position++; return; }
        if (At("{") || At("["))
        {
            bool isObject = At("{");
            string close = isObject ? "}" : "]";
            _position++;
            while (!At(close))
            {
                if (isObject)
                {
                    if (!Current.IsString && !IsIdentifier(Current.Value))
                        throw Invalid("不支持计算属性");
                    _position++;
                    Expect(":");
                }
                ReadLiteral(depth + 1);
                if (At(close)) break;
                Expect(",");
            }
            _position++;
            return;
        }
        if (At("-")) _position++;
        string value = Current.Value;
        if (value is not ("true" or "false" or "null")
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            throw Invalid("version_list 仅支持静态对象，不支持表达式或函数");
        _position++;
    }

    private Token Current => _position < _tokens.Count ? _tokens[_position] : throw Invalid("数组或对象未闭合");
    private bool At(string value) => !Current.IsString && Current.Value == value;
    private void Expect(string value)
    {
        if (!At(value)) throw Invalid($"预期字符 {value}");
        _position++;
    }
    private static bool IsIdentifier(string value) => value.Length > 0
        && (char.IsLetter(value[0]) || value[0] is '_' or '$')
        && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '$');
    private static FormatException Invalid(string message) => new($"custom.js 的 version_list 格式无效：{message}。");

    private static List<Token> Tokenize(string text, bool includeComments = false)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int commentStart = i;
                while (i < text.Length && text[i] is not ('\r' or '\n')) i++;
                if (includeComments) tokens.Add(new Token(string.Empty, commentStart, i));
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw Invalid("注释未闭合");
                if (includeComments) tokens.Add(new Token(string.Empty, i, end + 2));
                i = end + 2;
                continue;
            }
            int start = i++;
            if (c is '\'' or '"' or '`')
            {
                var value = new StringBuilder();
                bool closed = false;
                while (i < text.Length)
                {
                    char next = text[i++];
                    if (next == c) { closed = true; break; }
                    if (next != '\\') { value.Append(next); continue; }
                    if (i == text.Length) throw Invalid("字符串转义未完成");
                    next = text[i++];
                    if (next is 'u' or 'x')
                    {
                        int length = next == 'u' ? 4 : 2;
                        if (i + length > text.Length || !ushort.TryParse(text.AsSpan(i, length), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out ushort code)) throw Invalid("字符串转义无效");
                        value.Append((char)code);
                        i += length;
                    }
                    else if (next == '\r') { if (i < text.Length && text[i] == '\n') i++; }
                    else if (next != '\n') value.Append(next switch
                    {
                        'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', 'v' => '\v', '0' => '\0', _ => next
                    });
                }
                if (!closed) throw Invalid("字符串未闭合");
                // Template literals may contain executable expressions; never treat them as static strings.
                tokens.Add(new Token(c == '`' ? "`" : value.ToString(), start, i, c != '`'));
            }
            else if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'
                    || (char.IsDigit(c) && text[i] == '.'))) i++;
                tokens.Add(new Token(text[start..i], start, i));
            }
            else tokens.Add(new Token(c.ToString(), start, i));
        }
        return tokens;
    }
}
