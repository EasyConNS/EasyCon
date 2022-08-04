using EasyScript.Parsing.Statements;
using ECDevice;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers
{
    internal class ExprPaerser : IStatementParser
    {
        const string GPKey = "[ABXYLR]|Z[LR]|[LR]CLICK|HOME|CAPTURE|PLUS|MINUS|LEFT|RIGHT|UP|DOWN";

        Statement? IStatementParser.Parse(ParserArgument args)
        {
            // empty
            if (args.Text.Length == 0)
                return new Empty();
            // key or mov
            return KeyParse(args) ?? MovParse(args);
        }

        private static Statement? KeyParse(ParserArgument args)
        {
            ECKey key;
            var m = Regex.Match(args.Text, $"^({GPKey})$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key);
            m = Regex.Match(args.Text, $@"^({GPKey})\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key, args.Formatter.GetValueEx(m.Groups[2].Value));
            m = Regex.Match(args.Text, $@"^({GPKey})\s+(up|down)$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
            {
                return m.Groups[2].Value.ToUpper() switch
                {
                    "UP"=> new KeyUp(key),
                    "DOWN"=> new KeyDown(key),
                    _ => null,
                };
            }

            // stick
            m = Regex.Match(args.Text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                key = NSKeys.GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(args.Text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                key = NSKeys.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickDown(key, keyname, direction);
            }
            m = Regex.Match(args.Text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var duration = m.Groups[3].Value;
                key = NSKeys.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, args.Formatter.GetValueEx(duration));
            }
            return null;
        }

        private static Statement? MovParse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(FormatterUtil.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value));
            return null;
        }
    }
}
