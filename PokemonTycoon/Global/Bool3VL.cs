using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    public struct Bool3VL
    {
        public static readonly Bool3VL False = new Bool3VL(0);
        public static readonly Bool3VL True = new Bool3VL(1);
        public static readonly Bool3VL Unknown = new Bool3VL(2);

        readonly int _val;
        public bool IsUnknown => this == Unknown;
        public bool IsKnown => this != Unknown;
        public bool Boolean => this == True;

        Bool3VL(int val)
        {
            _val = val;
        }

        public override bool Equals(object obj)
        {
            return obj is Bool3VL && _val == ((Bool3VL)obj)._val;
        }

        public override int GetHashCode()
        {
            return _val.GetHashCode();
        }

        public override string ToString()
        {
            if (this == False)
                return "False";
            if (this == True)
                return "True";
            return "Unknown";
        }

        public static implicit operator Bool3VL(bool b)
        {
            if (b)
                return True;
            return False;
        }

        public static bool operator ==(Bool3VL lhs, Bool3VL rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Bool3VL lhs, Bool3VL rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static Bool3VL operator &(Bool3VL lhs, Bool3VL rhs)
        {
            if (lhs == False || rhs == False)
                return False;
            if (lhs == True && rhs == True)
                return True;
            return Unknown;
        }

        public static Bool3VL operator |(Bool3VL lhs, Bool3VL rhs)
        {
            if (lhs == True || rhs == True)
                return True;
            if (lhs == False && rhs == False)
                return False;
            return Unknown;
        }

        public static Bool3VL operator !(Bool3VL val)
        {
            if (val == True)
                return False;
            if (val == False)
                return True;
            return Unknown;
        }
    }
}
