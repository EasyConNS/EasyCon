using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class ECValue
    {
        string _str;
        double? _val;

        public ECValue(string str)
        {
            _str = str;
            _val = null;
        }

        public ECValue(double val)
        {
            _str = val.ToString();
            _val = val;
        }

        public double GetNumber()
        {
            if (_val == null)
                _val = double.Parse(_str);
            return _val.Value;
        }

        public string GetStr()
        {
            return _str;
        }

        public sealed override string ToString()
        {
            return GetStr();
        }

        public static implicit operator ECValue(string str)
        {
            return new ECValue(str);
        }

        public static implicit operator ECValue(double val)
        {
            return new ECValue(val);
        }
    }
}
