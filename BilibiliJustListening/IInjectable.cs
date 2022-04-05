using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal interface IInjectable
    {
        public string Command { get; set; }
        public string Parameter { get; set; }
        public BilibiliClient? Client { get; set; }
    }
}
