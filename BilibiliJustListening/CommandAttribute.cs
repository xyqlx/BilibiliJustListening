using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class CommandAttribute: Attribute
    {
        public string Command;
        public CommandAttribute(string command)
        {
            this.Command = command;
        }
    }
}
