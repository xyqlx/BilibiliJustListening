using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal class CommandWorker
    {
        private Dictionary<string, Action<string>> _commands = new Dictionary<string, Action<string>>();
        private Dictionary<string, Func<string, Task>> _asyncCommands = new Dictionary<string, Func<string, Task>>();
        private Action<string, string> defaultAction;

        public CommandWorker(Action<string, string> defaultAction)
        {
            this.defaultAction = defaultAction;
        }

        public void AddCommand(string command, Action<string> action)
        {
            _commands[command] = action;
        }

        public void AddCommand(string command, Func<string, Task> action)
        {
            _asyncCommands[command] = action;
        }

        public async Task Run(string line)
        {
            var split = line.Split(' ', 2);
            string command = split[0];
            string parameter = "";
            if(split.Length > 1)
            {
                parameter = split[1];
            }
            foreach (var key in _commands.Keys)
            {
                if(key == command)
                {
                    _commands[key].Invoke(parameter);
                    return;
                }
            }
            foreach (var key in _asyncCommands.Keys)
            {
                if (key == command)
                {
                    await _asyncCommands[key].Invoke(parameter);
                    return;
                }
            }
            foreach (var key in _commands.Keys)
            {
                if (key.StartsWith(command))
                {
                    _commands[key].Invoke(parameter);
                    return;
                }
            }
            foreach (var key in _asyncCommands.Keys)
            {
                if (key.StartsWith(command))
                {
                    await _asyncCommands[key].Invoke(parameter);
                    return;
                }
            }
            defaultAction.Invoke(command, parameter);
        }
    }
}
