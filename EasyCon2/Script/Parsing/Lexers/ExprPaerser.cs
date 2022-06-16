﻿using EasyCon2.Script.Parsing.Statements;
using ECDevice;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Lexers
{
    internal class ExprPaerser : IStatementParser
    {
        const string GPKey = "^[ABXYLR]|^Z[LR]|^[LR]CLICK|^HOME|^CAPTURE|^PLUS|^MINUS|^LEFT|^RIGHT";
        const string GPUPDOWN = "^UP|^DOWN";
        Statement? IStatementParser.Parse(ParserArgument args)
        {
            // empty
            if (args.Text.Length == 0)
                return new Empty();
            // key or mov
            return KeyParse(args) ?? MovParse(args);
        }

        private static Statement KeyParse(ParserArgument args)
        {
            var tokens = Parser.Lexer.Parse(args.Text).ToList();
            ECKey key;
            var m = Regex.Match(args.Text, GPKey + "|" + GPUPDOWN + "$", RegexOptions.IgnoreCase);
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
            m = Regex.Match(args.Text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                key = ScripterUtil.GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(args.Text, @"^([lr]s{1,2})\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                key = ScripterUtil.GetKey(keyname, direction);
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
                key = ScripterUtil.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, args.Formatter.GetValueEx(duration));
            }
            return null;
        }

        private static Statement MovParse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(FormatterUtil.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value));
            return null;
        }
    }
}
