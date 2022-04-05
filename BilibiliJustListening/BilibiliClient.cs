using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;
using Spectre.Console;

namespace BilibiliJustListening
{
    internal class BilibiliClient
    {
        private IBrowser Browser { get; set; }
        public Queue<BVideo> PlayList { get; private set; } = new Queue<BVideo>();
        public List<BVideo> SearchList { get; private set; } = new List<BVideo>();
        private IPage PlayPage { get; set; }

        private BilibiliClient(IBrowser browser, IPage playPage)
        {
            Browser = browser;
            PlayPage = playPage;
        }

        public static async Task<BilibiliClient> CreateAsync(Proxy? proxy = null)
        {
            var playwright = await Playwright.CreateAsync();
            var option = new BrowserTypeLaunchOptions { };
            if (proxy != null) { 
                option.Proxy = proxy;
            }
            option.Headless = false;
            var browser = await playwright.Firefox.LaunchAsync(option);
            var playPage = await browser.NewPageAsync();
            return new BilibiliClient(browser, playPage);
        }

        public async Task<List<BVideo>> SearchVideosAsync(string keyword, StatusContext? ctx = null)
        {
            // 按照默认综合排序
            var page = await Browser.NewPageAsync();
            await page.GotoAsync($"https://search.bilibili.com/all?keyword={HttpUtility.UrlEncode(keyword)}");
            ctx?.Status("等待页面元素加载……");
            await page.WaitForSelectorAsync(".bili-video-card__info--right");
            // all 不会等待出现
            var collections = await page.QuerySelectorAllAsync(".bili-video-card__info--right");
            var results = new List<BVideo>();
            foreach(var item in collections)
            {
                var links = await item.QuerySelectorAllAsync("a");
                if(links.Count != 2)
                {
                    continue;
                }
                BVideo video;
                var videoHref = await links[0].GetAttributeAsync("href");
                var upHref = await links[1].GetAttributeAsync("href");
                if(BVideo.ExtractId(videoHref ?? "", out var id))
                {
                    var h3 = await links[0].QuerySelectorAsync("h3");
                    if(h3 == null)
                    {
                        continue;
                    }
                    var title = await h3.GetAttributeAsync("title");
                    video = new BVideo(id) { Title = title ?? "" };
                    if (BUp.ExtractId(upHref ?? "", out var uid))
                    {
                        var upNameSpan = await links[1].QuerySelectorAsync(".bili-video-card__info--author");
                        var upDateSpan = await links[1].QuerySelectorAsync(".bili-video-card__info--date");
                        if (upNameSpan == null || upDateSpan == null)
                        {
                            continue;
                        }
                        var upName = await upNameSpan.InnerTextAsync();
                        var upDate = await upDateSpan.InnerTextAsync();
                        video.Uploader = new List<BUp> { new BUp { Id = uid, Name = upName } };
                        video.UpDate = upDate.Replace("·", "").Trim();
                    }
                    results.Add(video);
                }
            }
            await page.CloseAsync();
            SearchList = new List<BVideo>(results);
            return results;
        }
        private readonly Regex TitlePattern = new Regex(@"_哔哩哔哩_bilibili|_\s*热门视频");
        public async Task PlayNext()
        {
            if(PlayList == null || PlayList.Count == 0)
            {
                return;
            }
            var video = PlayList.Dequeue();
            await PlayPage.GotoAsync($"https://www.bilibili.com/video/{video.Id}");
            var title = TitlePattern.Replace(await PlayPage.TitleAsync(), "");
            video.Title = title;
            Console.WriteLine($"正在播放 {video.Id} {video.Title}");
            await CloseMute();
            await ShowUploadersOnPlay();
            var time = await GetFullTimeOnPlay();
            Console.WriteLine($"总时间 {time}s");
        }
        private async Task<bool> CloseMute()
        {
            var volume = await PlayPage.InnerTextAsync("div.bilibili-player-video-volume-num");
            Console.WriteLine($"音量: { volume }");
            if (volume == "0")
            {
                await PlayPage.Keyboard.PressAsync("m");
                var newVolume = await PlayPage.InnerTextAsync("div.bilibili-player-video-volume-num");
                Console.WriteLine($"试图调节音量: { newVolume }");                
                if (newVolume == "0")
                {
                    return false;
                }
            }
            return true;
        }
        private readonly Regex DigitPattern = new Regex(@"\d+");
        private async Task ShowUploadersOnPlay()
        {
            // 显示UP信息
            var upUrlCollection = await PlayPage.QuerySelectorAllAsync(".up-card>a");
            var upUrls = await Task.WhenAll(upUrlCollection.Select(async x => await x.GetAttributeAsync("href")));
            var upIds = upUrls.Where(x => x != null).Select(x => DigitPattern.Match(x!).Value);
            var upNameCollection = await PlayPage.QuerySelectorAllAsync(".up-card>.avatar-name__container>a");
            var upNames = await Task.WhenAll(upNameCollection.Select(async x => await x.InnerTextAsync()));
            var upinfo = string.Join(", ", upIds.Select((v, i) => $"{ upNames[i]} ({ v})"));
            // 试着从元信息里找UP信息
            if (upinfo == "")
            {
                foreach (var meta in await PlayPage.QuerySelectorAllAsync("meta")) {
                    try
                    {
                        var metaName = await meta.GetAttributeAsync("name");
                        if (metaName == "author")
                        {
                            upNames = new string[] { await meta.GetAttributeAsync("content") ?? "" };
                            foreach (var a in await PlayPage.QuerySelectorAllAsync("a"))
                            {
                                if (upNames[0] == await a.InnerTextAsync())
                                {
                                    var match = DigitPattern.Match(await a.GetAttributeAsync("href") ?? "");
                                    if (match.Success)
                                    {
                                        upIds = new string[] { match.Groups[0].Value };
                                        break;
                                    }
                                }
                            }
                            break;
                        }
                    }
                    catch (Exception ex) {
                        continue;
                    }
                    
                }
                upinfo = string.Join(", ", upIds.Select((v, i) => $"{ upNames[i]} ({ v})"));
            }
            Console.WriteLine($"UP主：{ upinfo}\n");
        }
        private static readonly Regex TimePattern = new Regex(@"((?<hour>\d+):)?(?<minute>\d+):(?<second>\d+)");
        private static int ExtractTime(string text)
        {
            var match = TimePattern.Match(text);
            if (match.Success && match.Groups.Count > 0)
            {
                int hour, minute, second;
                if(!int.TryParse(match.Groups["hour"].Value, out hour))
                {
                    hour = 0;
                }
                if(!int.TryParse(match.Groups["minute"].Value, out minute))
                {
                    minute = 0;
                }
                if(!int.TryParse(match.Groups["second"].Value, out second))
                {
                    second = 0;
                }
                return hour * 3600 + minute * 60 + second;
            }
            return 0;
        }
        private async Task<int> GetFullTimeOnPlay()
        {
            await PlayPage.Keyboard.PressAsync("f");
            var cnt = 0;
            // may infinite loop
            while (true)
            {
                cnt = (cnt + 1) % 4;
                await PlayPage.Mouse.MoveAsync(100 + cnt/2*20, 100 + (cnt % 2)*20);
                var fullTimeText = await PlayPage.InnerTextAsync("span.bilibili-player-video-time-total");
                var time = ExtractTime(fullTimeText);
                if(time > 0)
                {
                    return time;
                }
            }
        }
    }
}
