using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Playwright;
using System.Threading.Tasks;
using System.Speech.Synthesis;

namespace BilibiliJustListening
{
    class Program
    {
        public static async Task Main()
        {
            // show emoji in Windows (use powershell&wt)
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // load config.json
            var builder = new ConfigurationBuilder().Add(new JsonConfigurationSource { Path = "config.json", Optional = true });
            IConfiguration config = builder.Build();
            // could be null
            var proxy = config.GetSection("proxy").Get<Proxy>();
            if(proxy != null)
            {
                Console.WriteLine("已配置代理");
            }

            var client = await BilibiliClient.CreateAsync(proxy);
            Console.WriteLine("命令模式，有疑问请输入help");

            var worker = new CommandWorker((command, parameter) =>
            {
                Console.WriteLine($"不支持{command}命令");
            });

            worker.AddCommand("help", (p) =>
            {
                Console.WriteLine("完善中……");
            });
            worker.AddCommand("exit", (p) =>
            {
                Console.WriteLine("退出程序");
                Environment.Exit(0);
            });

            worker.AddCommand("search", async (p) =>
            {
                var videos = await client.SearchVideosAsync(p);
                // 限制显示数量
                videos = videos.Take(20).ToList();
                for(var i = 0; i < videos.Count; i++)
                {
                    Console.WriteLine($"{i} {videos[i].ShortDescription}");
                }
            });

            worker.AddCommand("play", async (p) =>
            {
                if (BVideo.ExtractId(p, out var id))
                {
                    client.PlayList.Enqueue(new BVideo(id));
                    await client.PlayNext();
                }
                else if (int.TryParse(p, out var index))
                {
                    client.PlayList.Enqueue(client.SearchList[index]);
                    await client.PlayNext();
                }
                else
                {
                    Speaker.SpeakAndPrint("播放失败");
                }
            });

            while (true)
            {
                var command = Console.ReadLine();
                await worker.Run(command ?? "");
            }
        }
    }
}