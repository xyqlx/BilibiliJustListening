using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Playwright;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using Spectre.Console;

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
                AnsiConsole.MarkupLine("已配置代理");
            }
            BilibiliClient? client = null;
            await AnsiConsole.Status()
                .StartAsync("正在启动...", async ctx =>
                {
                    client = await BilibiliClient.CreateAsync(proxy);
                });
            AnsiConsole.MarkupLine("命令模式，有疑问请输入help");
            var worker = CommandWorker.Create<BilibiliCommandModel>(client!);
            while (true)
            {
                var command = Console.ReadLine();
                try
                {
                    await worker.Run(command ?? "");
                }
                catch (TimeoutException)
                {
                    AnsiConsole.MarkupLine("等待超时，请检查网络设置");
                }
            }
        }
    }
}