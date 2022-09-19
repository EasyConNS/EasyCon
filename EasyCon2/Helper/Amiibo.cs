using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon2.Helper
{
    public class Amiibo
    {
        public string amiiboSeries { get; set; }
        public string character { get; set; }
        public string gameSeries { get; set; }
        public string head { get; set; }
        public string image { get; set; }
        public string name { get; set; }
        public string tail { get; set; }
        public string type { get; set; }
        public Dictionary<string, string> release { get; set; }
    }
}
