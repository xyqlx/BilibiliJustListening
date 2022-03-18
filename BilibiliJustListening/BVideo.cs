using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal class BVideo
    {
        public BVideo(string id)
        {
            Id = id;
        }

        public string Id { get; set; }
        public string? Title { get; set; }
        public List<BUp> Uploader { get; set; } = new List<BUp>();
        public string? UpDate { get; set; }

        private static readonly Regex IdPattern = new(@"[AaBb][Vv]\w+");
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

        public string ShortDescription { get => Id + " " + Title ?? ""; }
    }
}
