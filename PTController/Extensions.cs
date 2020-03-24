using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;

namespace PTController
{
    static class Extensions
    {
        public static string GetName(this Enum self)
        {
            return Enum.GetName(self.GetType(), self);
        }
    }
}
