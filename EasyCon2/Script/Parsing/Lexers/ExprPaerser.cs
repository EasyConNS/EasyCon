using EasyCon2.Script.Parsing.Statements;
using ECDevice;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class ExprPaerser : IStatementParser
    {
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            // empty
            if (args.Text.Length == 0)
                return new Empty();
            // key
            return KeyParse(args);
        }

        private static Statement KeyParse(ParserArgument args)
        {
            NintendoSwitch.ECKey key;
            var m = Regex.Match(args.Text, @"^([a-z]+)$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key);
            m = Regex.Match(args.Text, $@"^([a-z]+)\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key, args.Formatter.GetValueEx(m.Groups[2].Value));
            m = Regex.Match(args.Text, @"^([a-z]+)\s+down$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyDown(key);
            m = Regex.Match(args.Text, @"^([a-z]+)\s+up$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyUp(key);
            // stick
            return StickParse(args);
        }

        private static Statement StickParse(ParserArgument args)
        {
            Match m;
            m = Regex.Match(args.Text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var key = ScripterUtil.GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(args.Text, @"^([lr]s{1,2})\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var key = ScripterUtil.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickDown(key, keyname, direction);
            }
            m = Regex.Match(args.Text, $@"^([lr]s{{1,2}})\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var duration = m.Groups[3].Value;
                var key = ScripterUtil.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, args.Formatter.GetValueEx(duration));
            }
            // mov
            return MovParse(args);
        }

        private static Statement MovParse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(Formatter.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value));
            return null;
        }
    }
}
