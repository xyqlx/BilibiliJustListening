using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;

namespace BilibiliJustListening
{
    internal class BilibiliCommandModel: IInjectable
    {
        public string Command { get; set; }
        public string Parameter { get; set; }
        public BilibiliClient? Client { get; set; }
        public BilibiliCommandModel(string command, string parameter, BilibiliClient client)
        {
            Command = command;
            Parameter = parameter;
            Client = client;
        }
        public BilibiliCommandModel()
        {
            Command = "";
            Parameter = "";
            Client = null;
        }

        [Command("")]
        public void DefaultCommand()
        {
            Console.WriteLine($"不支持{Command}命令");
        }

        [Command("help")]
        public void GetHelp()
        {
            Console.WriteLine("完善中……");
        }

        [Command("exit")]
        public void Exit()
        {
            Console.WriteLine("退出中……");
            Environment.Exit(0);
        }

        [Command("search")]
        public async Task Search()
        {
            // check null
            if (Client == null)
            {
                Console.WriteLine("网页实例化失败");
                return;
            }
            await AnsiConsole.Status()
                .StartAsync("搜索中...", async ctx =>
                {
                    var videos = await Client.SearchVideosAsync(Parameter);
                    ctx.Status("搜索完成").Spinner(Spinner.Known.Star).SpinnerStyle(Style.Parse("green"));
                    // 限制显示数量
                    videos = videos.Take(20).ToList();
                    for (var i = 0; i < videos.Count; i++)
                    {
                        Console.WriteLine($"{i} {videos[i].ShortDescription}");
                    }
                });
        }

        [Command("play")]
        public async Task Play()
        {
            // check null
            if (Client == null)
            {
                Console.WriteLine("网页实例化失败");
                return;
            }
            if (BVideo.ExtractId(Parameter, out var id))
            {
                Client.PlayList.Enqueue(new BVideo(id));
                await Client.PlayNext();
            }
            else if (int.TryParse(Parameter, out var index))
            {
                Client.PlayList.Enqueue(Client.SearchList[index]);
                await Client.PlayNext();
            }
            else
            {
                Speaker.SpeakAndPrint("播放失败");
            }
        }
    }
}
