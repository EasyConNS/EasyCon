using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;

namespace EasyCon
{
    static class Extensions
    {
        public static double Compare(this Color self, Color color)
        {
            return 1 - (Math.Abs(self.R - color.R) + Math.Abs(self.G - color.G) + Math.Abs(self.B - color.B)) / 255.0 / 3;
        }

        public static string GetName(this Enum self)
        {
            return Enum.GetName(self.GetType(), self);
        }

        public static string GetDesc(this Enum self)
        {
            Type type = self.GetType();
            string name = self.GetName();
            FieldInfo field = type.GetField(name);
            DescriptionAttribute attr = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attr?.Description;
        }

        public static T GetCustomAttribute<T>(this MemberInfo self)
            where T : class
        {
            return Attribute.GetCustomAttribute(self, typeof(T)) as T;
        }

        public static string ToString<T>(this IEnumerable<T> self, string separator)
        {
            return string.Join(separator, self);
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

        public static void ForEach<T>(this IEnumerable<T> self, Action<T> action)
        {
            foreach (T element in self)
                action(element);
        }

        public static void ForEach<T>(this IEnumerable<T> self, Action<int, T> action)
        {
            var i = 0;
            foreach (T element in self)
            {
                action(i, element);
                i++;
            }
        }

        public static string GetName(this Delegate self)
        {
            return $"{self.Method.DeclaringType.Name}.{self.Method.Name}";
        }
    }
}
