using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Spectre.Console;

namespace BilibiliJustListening
{
    internal class BilibiliCommandModel : IInjectable
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
            await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                .StartAsync("搜索中...", async ctx =>
                {
                    var videos = await Client.SearchVideosAsync(Parameter);
                    Speaker.Speak("搜索完成");
                    AnsiConsole.MarkupLine("搜索完成✅");
                    // 限制显示数量
                    videos = videos.Take(20).ToList();
                    for (var i = 0; i < videos.Count; i++)
                    {
                        AnsiConsole.MarkupLine($"[bold]{i,2}[/] {videos[i].ShortMarkupDescription}");
                    }
                });
        }

        [Command("play")]
        public async Task Play()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            if (BVideo.ExtractId(Parameter, out var id))
            {
                Client.PlayList.Enqueue(new BVideo(id));
                await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                    .StartAsync("准备播放", async ctx =>
                    {
                        await Client.PlayNext(ctx);
                    });
            }
            else if (int.TryParse(Parameter, out var index))
            {
                Client.PlayList.Enqueue(Client.SearchList[index]);
                await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                    .StartAsync("准备播放", async ctx =>
                    {
                        await Client.PlayNext(ctx);
                    });
            }
            else
            {
                var list = await Client.SearchVideosAsync(Parameter);
                if (list.Count == 0)
                {
                    AnsiConsole.MarkupLine("没有找到相关视频");
                    return;
                }
                Client.PlayList.Enqueue(list[0]);
                await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                    .StartAsync("准备播放", async ctx =>
                    {
                        await Client.PlayNext(ctx);
                    });
            }
        }

        [Command("recommand")]
        public void ShowRecommandation()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            var recommandation = Client.RecommandList.Take(20).ToList();
            if (recommandation.Count == 0)
            {
                AnsiConsole.MarkupLine("暂无推荐视频");
                return;
            }
            for (var i = 0; i < recommandation.Count; i++)
            {
                AnsiConsole.MarkupLine($"[bold]{i,2}[/] {recommandation[i].ShortMarkupDescription}");
            }
            Client.SearchList.Clear();
            Client.SearchList.AddRange(recommandation);
            AnsiConsole.MarkupLine("已替换搜索列表");
        }

        [Command("screenshot")]
        public async Task ScreenShot()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            var data = await Client.ScreenShot();
            var image = new CanvasImage(data);
            if (int.TryParse(Parameter, out var width))
            {
                image.MaxWidth(width);
            }
            else
            {
                image.MaxWidth(16);
            }
            AnsiConsole.Write(image);
        }

        [Command("latest")]
        public async Task GetUpLatestVideo()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                .StartAsync("加载中...", async ctx =>
                {
                    var videos = await Client.SearchUpVideos(Parameter, true);
                    Speaker.Speak("加载完成");
                    AnsiConsole.MarkupLine("加载完成✅");
                    // 限制显示数量
                    videos = videos.Take(20).ToList();
                    for (var i = 0; i < videos.Count; i++)
                    {
                        AnsiConsole.MarkupLine($"[bold]{i,2}[/] {videos[i].ShortMarkupDescription}");
                    }
                });
        }

        [Command("popular")]
        public async Task GetUpPopularVideo()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            await AnsiConsole.Status().Spinner(Spinner.Known.Earth)
                .StartAsync("加载中...", async ctx =>
                {
                    var videos = await Client.SearchUpVideos(Parameter, false);
                    Speaker.Speak("加载完成");
                    AnsiConsole.MarkupLine("加载完成✅");
                    // 限制显示数量
                    videos = videos.Take(20).ToList();
                    for (var i = 0; i < videos.Count; i++)
                    {
                        AnsiConsole.MarkupLine($"[bold]{i,2}[/] {videos[i].ShortMarkupDescription}");
                    }
                });
        }

        [Command("time")]
        public void ShowTime()
        {
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            if(Client.LastStartPlay == DateTime.MinValue)
            {
                AnsiConsole.MarkupLine("没有播放记录");
                return;
            }
            var time = DateTime.Now - Client.LastStartPlay;
            // get int seconds
            var seconds = (int)time.TotalSeconds;
            AnsiConsole.MarkupLine($"自上次开始播放已有{seconds}秒");
        }


        [Command("live")]
        public async Task OpenLive()
        {
            // check null
            if (Client == null)
            {
                AnsiConsole.MarkupLine("网页实例化失败");
                return;
            }
            var match = Regex.Match(Parameter, @"(?=https:\/\/live\.bilibili\.com\/)?\d+");
            if (match.Success)
            {
                var liveId = match.Value;
                await AnsiConsole.Status().Spinner(Spinner.Known.Monkey)
                    .StartAsync("正在进入直播间", async ctx =>
                    {
                        await Client.OpenLive(liveId);
                    });
            }
            else
            {
                Speaker.SpeakAndPrint("直播间参数错误");
            }
        }
    }
}
