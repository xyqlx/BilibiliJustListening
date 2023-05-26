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
        public string Description;
        public CommandAttribute(string command, string description = "")
        {
            this.Command = command;
            this.Description = description;
        }
    }
}
