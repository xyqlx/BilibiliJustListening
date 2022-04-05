using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal static class Speaker
    {

        private static readonly SpeechSynthesizer? Synthesizer;

        static Speaker()
        {
            if (OperatingSystem.IsWindows())
            {
                Synthesizer = new SpeechSynthesizer();
                Synthesizer.SetOutputToDefaultAudioDevice();
            }
        }

        public static void Speak(string text)
        {
            if (OperatingSystem.IsWindows())
            {
                Synthesizer?.Speak(text);
            }
        }

        public static void SpeakAndPrint(string text)
        {
            Speak(text);
            AnsiConsole.MarkupLine(text);
        }
    }
}
