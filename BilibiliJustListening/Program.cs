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
                Console.WriteLine("已配置代理");
            }

            var client = await BilibiliClient.CreateAsync(proxy);
            Console.WriteLine("命令模式，有疑问请输入help");
            var worker = CommandWorker.Create<BilibiliCommandModel>(client);
            while (true)
            {
                var command = Console.ReadLine();
                await worker.Run(command ?? "");
            }
        }
    }
}