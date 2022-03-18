using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal class BUp
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        private static readonly Regex IdPattern = new(@"\d+");
        public static bool ExtractId(string text, out string id)
        {
            id = "";
            var match = IdPattern.Match(text);
            if (match.Success)
            {
                id = match.Groups[0].Value;
            }
            return match.Success;
        }
    }
}
