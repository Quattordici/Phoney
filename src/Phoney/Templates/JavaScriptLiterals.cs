using System.Text;

namespace Phoney.Templates;

/// <summary>
/// Rewrites the JavaScript literals of template arguments as JSON: <c>'text'</c> becomes <c>"text"</c> and the unquoted key of <c>{length: 3}</c> a quoted one.
/// Everything else is copied, so the result is JSON exactly when the text was JSON apart from these two spellings.
/// </summary>
internal static class JavaScriptLiterals
{
    /// <summary>The JSON spelling of <paramref name="text"/>, or <see langword="null"/> when it has nothing to rewrite or a quote is not closed.</summary>
    public static string? ToJson(string text)
    {
        var json = new StringBuilder(text.Length + 8);
        var rewritten = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var end = ClosingQuote(text, i);
                if (end < 0)
                    return null;
                json.Append(text, i, end - i + 1);
                i = end + 1;
            }
            else if (c == '\'')
            {
                var end = ClosingQuote(text, i);
                if (end < 0)
                    return null;
                AppendDoubleQuoted(json, text.AsSpan(i + 1, end - i - 1));
                rewritten = true;
                i = end + 1;
            }
            else if (IsKeyStart(c))
            {
                var end = i + 1;
                while (end < text.Length && IsKeyPart(text[end]))
                    end++;
                var next = end;
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (next < text.Length && text[next] == ':')
                {
                    json.Append('"').Append(text, i, end - i).Append('"');
                    rewritten = true;
                }
                else
                {
                    json.Append(text, i, end - i); // true, false, null or no literal at all
                }

                i = end;
            }
            else
            {
                json.Append(c);
                i++;
            }
        }

        return rewritten ? json.ToString() : null;
    }

    /// <summary>The index of the quote that closes the string opened at <paramref name="start"/>, skipping escaped characters, or -1.</summary>
    private static int ClosingQuote(string text, int start)
    {
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
                i++;
            else if (text[i] == quote)
                return i;
        }

        return -1;
    }

    /// <summary>Appends the body of a single-quoted string as a double-quoted one: <c>\'</c> needs no escape there, a bare <c>"</c> does.</summary>
    private static void AppendDoubleQuoted(StringBuilder json, ReadOnlySpan<char> body)
    {
        json.Append('"');
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                i++;
                if (body[i] != '\'')
                    json.Append('\\');
                json.Append(body[i]);
            }
            else if (c == '"')
            {
                json.Append("\\\"");
            }
            else
            {
                json.Append(c);
            }
        }

        json.Append('"');
    }

    private static bool IsKeyStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$';

    private static bool IsKeyPart(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$';
}
