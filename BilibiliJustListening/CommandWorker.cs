using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BilibiliJustListening
{
    internal class CommandWorker
    {
        private Dictionary<string, Action<string, string>> _commands = new Dictionary<string, Action<string, string>>();
        private Dictionary<string, Func<string, string, Task>> _asyncCommands = new Dictionary<string, Func<string, string, Task>>();

        public CommandWorker()
        {
        }

        public void AddCommand(string command, Action<string, string> action)
        {
            _commands[command] = action;
        }

        public void AddCommand(string command, Func<string, string, Task> action)
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
                    _commands[key].Invoke(command, parameter);
                    return;
                }
            }
            foreach (var key in _asyncCommands.Keys)
            {
                if (key == command)
                {
                    await _asyncCommands[key].Invoke(command, parameter);
                    return;
                }
            }
            foreach (var key in _commands.Keys)
            {
                if (key.StartsWith(command))
                {
                    _commands[key].Invoke(command, parameter);
                    return;
                }
            }
            foreach (var key in _asyncCommands.Keys)
            {
                if (key.StartsWith(command))
                {
                    await _asyncCommands[key].Invoke(command, parameter);
                    return;
                }
            }
            // default action
            if (_commands.ContainsKey(""))
            {
                _commands[""].Invoke(command, parameter);
            }
            else if (_asyncCommands.ContainsKey(""))
            {
                await _asyncCommands[""].Invoke(command, parameter);
            }
        }

        public static CommandWorker Create<T>(BilibiliClient client) where T: IInjectable, new()
        {
            // get T's methods' attribute
            var methods = typeof(T).GetMethods();
            var commandWorker = new CommandWorker();
            foreach (var method in methods)
            {
                if(method is null)
                {
                    continue;
                }
                var attributes = method.GetCustomAttributes(typeof(CommandAttribute), false);
                if (attributes.Length == 0)
                {
                    continue;
                }
                var attribute = (CommandAttribute)attributes[0];
                // check if method is async
                if (method.ReturnType == typeof(Task))
                {
                    commandWorker.AddCommand(attribute.Command, async (string command, string parameter) =>
                    {
                        var injectable = new T() { Client = client, Command = command, Parameter = parameter };
                        var task = (Task?)method.Invoke(injectable, new object[] { });
                        if (task is not null)
                        {
                            await task;
                        }
                    });
                }
                else
                {
                    commandWorker.AddCommand(attribute.Command, (string command, string parameter) =>
                    {
                        var injectable = new T() { Client = client, Command = command, Parameter = parameter };
                        method.Invoke(injectable, new object[] { });
                    });
                }
            }
            return commandWorker;
        }
    }
}
