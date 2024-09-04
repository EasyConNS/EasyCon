using System.Text.RegularExpressions;
using System.Text;

namespace ECScript.Tests;

static class ScriptUtil
{
    private static string compat(string text)
        {
            var builder = new StringBuilder();
            foreach (var line in Regex.Split(text, "\r\n|\r|\n"))
            {
                var _text = line.TrimStart();
                var m = Regex.Match(_text, @"(\s*#.*)$");
                string comment = string.Empty;
                if (m.Success)
                {
                    comment = m.Groups[1].Value;
                    _text = _text[..^comment.Length];
                }
                _text = _text.Trim();
                
                var mp = Regex.Match(_text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
                if (mp.Success)
                {
                    builder.Append($"PRINT \"{mp.Groups[1].Value}\";");
                }
                else
                {
                    builder.Append(_text);
                    if (_text.StartsWith("for", true, null))
                    {
                        builder.Append(':');
                    }
                    else if(_text.StartsWith("if", true, null))
                    {
                        builder.Append(':');
                    }
                    else if (_text.StartsWith("elif", true, null))
                    {
                        builder.Append(':');
                    }
                    else if (_text.StartsWith("else", true, null))
                    {
                        builder.Append(':');
                    }
                    else if (_text.StartsWith("function", true, null))
                    {
                        builder.Append(':');
                    }
                    else if (_text == String.Empty)
                    {
                        // ignore empty line
                    }
                    else
                    {
                        builder.Append(';');
                    }
                }

                builder.Append(comment);
                builder.Append("\r\n");
            }
            return builder.ToString();
        }
}