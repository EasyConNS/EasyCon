using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PTDevice
{
    static class Extensions
    {
        public static string GetName(this Enum self)
        {
            return Enum.GetName(self.GetType(), self);
        }

        public static T Clamp<T>(this T self, T min, T max) where T : IComparable<T>
        {
            if (self.CompareTo(min) < 0)
                return min;
            else if (self.CompareTo(max) > 0)
                return max;
            else
                return self;
        }
    }
}
