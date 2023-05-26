using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;
using Spectre.Console;
using System.Text.Json;

namespace BilibiliJustListening
{
    internal class BilibiliClient
    {
        private IBrowser Browser { get; set; }
        public Queue<BVideo> PlayList { get; private set; } = new Queue<BVideo>();
        public List<BVideo> SearchList { get; private set; } = new List<BVideo>();
        public List<BVideo> RecommandList { get; private set; } = new List<BVideo>();
        public DateTime LastStartPlay { get; private set; } = DateTime.MinValue;
        private IPage PlayPage { get; set; }

        private BilibiliClient(IBrowser browser, IPage playPage)
        {
            Browser = browser;
            PlayPage = playPage;
        }

        public static async Task<BilibiliClient> CreateAsync(Proxy? proxy = null, bool headless = true)
        {
            var playwright = await Playwright.CreateAsync();
            var option = new BrowserTypeLaunchOptions {
                Headless = headless
            };
            if (proxy != null) { 
                option.Proxy = proxy;
            }
            var browser = await playwright.Firefox.LaunchAsync(option);
            var playPage = await browser.NewPageAsync();
            var client = new BilibiliClient(browser, playPage);
            playPage.Response += async (o, e) =>
            {
                var url = e.Url;
                // 然而这个并不会在直接访问的时候触发（只有在自动播放或者点击推荐视频时才会触发）
                if (url.StartsWith("https://api.bilibili.com/x/web-interface/view/detail?"))
                {
                    var body =  Encoding.UTF8.GetString(await e.BodyAsync());
                    var json = JsonDocument.Parse(body);
                    var videoInfo = json.RootElement.GetProperty("data").GetProperty("View");
                    var id = videoInfo.GetProperty("bvid").GetString();
                    var title = videoInfo.GetProperty("title").GetString();
                    var author = videoInfo.GetProperty("owner");
                    var duration = videoInfo.GetProperty("duration").GetInt32();
                    var authorId = author.GetProperty("mid").GetInt64();
                    var authorName = author.GetProperty("name").GetString();
                    AnsiConsole.MarkupLine($"监测到播放 {id} {title}({authorId} {authorName}) {duration}s".EscapeMarkup());
                    client.LastStartPlay = DateTime.Now;
                    client.RecommandList.Clear();
                    foreach (var item in json.RootElement.GetProperty("data").GetProperty("Related").EnumerateArray())
                    {
                        var video = new BVideo(item.GetProperty("bvid").GetString() ?? String.Empty)
                        {
                            Title = item.GetProperty("title").GetString(),
                            Uploader = new List<BUp>() {
                                new BUp {
                                    Id = item.GetProperty("owner").GetProperty("mid").GetInt64().ToString(),
                                    Name = item.GetProperty("owner").GetProperty("name").GetString()
                                }
                            }
                        };
                        client.RecommandList.Add(video);
                    }
                }
            };
            return client;
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
                var link0 = await links[0].InnerHTMLAsync();
                var videoHref = await links[0].GetAttributeAsync("href");
                var upHref = await links[1].GetAttributeAsync("href");
                if (BVideo.ExtractId(videoHref ?? "", out var id))
                {
                    // 如果用h3有可能会报错……给我整无语了
                    //var h3 = await links[0].QuerySelectorAsync("h3");
                    //if(h3 == null)
                    //{
                    //    continue;
                    //}
                    string? title;
                    //try
                    //{
                    //    title = await h3.GetAttributeAsync("title");
                    //}
                    title = Regex.Match(link0, @"title=""(.*?)""").Groups[1].Value;
                    video = new BVideo(id) { Title = title };
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
        public async Task PlayNext(StatusContext? ctx = null)
        {
            if(PlayList == null || PlayList.Count == 0)
            {
                AnsiConsole.MarkupLine("播放列表为空");
                return;
            }
            var video = PlayList.Dequeue();
            ctx?.Status("打开页面");
            await PlayPage.GotoAsync($"https://www.bilibili.com/video/{video.Id}");
            var title = TitlePattern.Replace(await PlayPage.TitleAsync(), "");
            video.Title = title;
            AnsiConsole.MarkupLine($"正在播放 {video.Id} {video.Title}".EscapeMarkup());
            Speaker.Speak("播放开始");
            ctx?.Status("音量处理").Spinner(Spinner.Known.GrowVertical);
            (_, var oldVolume, var newVolume) = await CloseMute();
            AnsiConsole.MarkupLine(oldVolume == newVolume ? $"音量：{oldVolume}" : $"音量： {oldVolume} -> {newVolume}");
            ctx?.Status("显示UP主信息");
            string upinfo = await GetUploadersOnPlay();
            AnsiConsole.MarkupLine($"UP主：{upinfo}");
            ctx?.Status("计算视频时间");
            var time = await GetFullTimeOnPlay();
            LastStartPlay = DateTime.Now;
            var timer = new Timer((o) =>
            {
                AnsiConsole.MarkupLine($"预计播放结束（{video.Title}）".EscapeMarkup());
            }, null, time * 1000, Timeout.Infinite);
            AnsiConsole.MarkupLine($"总时间 {time}s");
        }
        private async Task<(bool success, int rawVolume, int newVolume)> CloseMute()
        {
            var volume = int.Parse(await PlayPage.InnerTextAsync("div.bilibili-player-video-volume-num"));
            if (volume == 0)
            {
                await PlayPage.Keyboard.PressAsync("m");
                var newVolume = int.Parse(await PlayPage.InnerTextAsync("div.bilibili-player-video-volume-num"));
                return (newVolume != 0, volume, newVolume);
            }
            return (true, volume, volume);
        }
        private readonly Regex DigitPattern = new Regex(@"\d+");
        private async Task<string> GetUploadersOnPlay()
        {
            // 显示UP信息
            var upUrlCollection = await PlayPage.QuerySelectorAllAsync(".up-card>a");
            var upUrls = await Task.WhenAll(upUrlCollection.Select(async x => await x.GetAttributeAsync("href")));
            var upIds = upUrls.Where(x => x != null).Select(x => DigitPattern.Match(x!).Value);
            var upNameCollection = await PlayPage.QuerySelectorAllAsync(".up-card>.avatar-name__container>a");
            var upNames = await Task.WhenAll(upNameCollection.Select(async x => await x.InnerTextAsync()));
            // zip upIds and upNames
            var upinfo = string.Join(", ", upIds.Zip(upNames, (x, y) => $"{y} ({x})"));
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
                    catch (Exception) {
                        continue;
                    }
                    
                }
                var upIdsList = upIds.ToList();
                if (upIdsList.Count == upNames.Length)
                {
                    upinfo = string.Join(", ", upIdsList.Zip(upNames, (x, y) => $"{y} ({x})"));
                }
                else
                {
                    upinfo = string.Join(", ", upIds.Select((v) => $"({v})"));
                }
            }
            return upinfo;
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

        public async Task<byte[]> ScreenShot()
        {
            return await PlayPage.ScreenshotAsync();
        }

        /// <summary>
        /// 列举UP主的视频
        /// </summary>
        /// <param name="upId">up主的id</param>
        /// <param name="isLatest">按照时间或热度排序</param>
        /// <returns></returns>
        public async Task<List<BVideo>> SearchUpVideos(string upId, bool isLatest){
            var page = await Browser.NewPageAsync();
            await page.GotoAsync($"https://space.bilibili.com/{upId}/video");
            List<BVideo> result;
            if(isLatest){
                await page.WaitForSelectorAsync(".cube-list>li>a.title");
                var collection = await page.QuerySelectorAllAsync(".cube-list>li>a.title");
                result = new List<BVideo>();
                foreach(var item in collection){
                    var href = await item.GetAttributeAsync("href");
                    if(href != null){
                        if(BVideo.ExtractId(href, out var id)){
                            var title = await item.GetAttributeAsync("title") ?? "";
                            result.Add(new BVideo(id){Title = title});
                        }
                    }
                }
            }else{
                await page.ClickAsync("ul.be-tab-inner>li:nth-child(2)>input");
                var response = await page.WaitForResponseAsync(x => x.Url.Contains("search") && x.Status == 200);
                var jsonElement = await response.JsonAsync();
                if(!jsonElement.HasValue){
                    return new List<BVideo>();
                };
                var videos = jsonElement.Value.GetProperty("data").GetProperty("list").GetProperty("vlist").EnumerateArray();
                result = videos.Select(x => new BVideo(x.GetProperty("bvid").GetString() ?? ""){Title = x.GetProperty("title").GetString()}).ToList();
            }
            SearchList = new List<BVideo>(result);
            await page.CloseAsync();
            return result;
        }
        
        /// <summary>
        /// 打开直播间
        /// </summary>
        /// <param name="liveId">直播间号</param>
        /// <returns></returns>
        public async Task OpenLive(string liveId)
        {
            await PlayPage.GotoAsync($"https://live.bilibili.com/{liveId}");
            var iframes = await PlayPage.QuerySelectorAllAsync("iframe");
            if(iframes.Count != 0)
            {
                foreach (var iframe in iframes)
                {
                    var src = await iframe.GetAttributeAsync("src");
                    if(src == null)
                    {
                        continue;
                    }
                    if (src.StartsWith("//"))
                    {
                        src = "https:" + src;
                    }
                    if (src.Contains("live"))
                    {
                        AnsiConsole.MarkupLine("重定向至" + src);
                        await PlayPage.GotoAsync(src);
                    }
                }
            }
            try
            {
                var title = await PlayPage.InnerTextAsync("div.live-title div.text");
                if (title != null)
                {
                    AnsiConsole.MarkupLine("进入直播间：" + title);
                    await PlayPage.DispatchEventAsync("#live-player", "mousemove");
                    await PlayPage.HoverAsync(".volume");
                    var volume = await PlayPage.InnerTextAsync(".volume-control .number");
                    AnsiConsole.MarkupLine("音量：" + volume);
                    if (volume == "0")
                    {
                        AnsiConsole.MarkupLine("尝试打开音量");
                        await PlayPage.ClickAsync(".volume");
                    }
                }
            }
            catch (Exception)
            {
                AnsiConsole.MarkupLine("读取直播间信息出现错误");
            }
            
        }
    }
}
