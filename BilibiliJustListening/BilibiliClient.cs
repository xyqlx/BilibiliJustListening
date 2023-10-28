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
using System.Diagnostics;

namespace BilibiliJustListening
{
    internal class BilibiliClient
    {
        private IBrowser Browser { get; set; }
        public Queue<BVideo> PlayList { get; private set; } = new Queue<BVideo>();
        public List<BVideo> SearchList { get; private set; } = new List<BVideo>();
        public List<BVideo> RecommandList { get; private set; } = new List<BVideo>();
        public DateTime LastStartPlay { get; private set; } = DateTime.MinValue;
        public String LastSearchKeywords { get; private set; } = "";
        public int LastSearchPageNumber { get; private set; } = 1;
        private IPage PlayPage { get; set; }
        private Timer? PlayTimer { get; set; } = null;

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
                var url = new Uri(e.Url);
                var path = url.AbsolutePath;
                // 然而这个并不会在直接访问的时候触发（只有在自动播放或者点击推荐视频时才会触发）
                if (path == "/x/web-interface/view/detail")
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
                if(path == "/x/web-interface/wbi/view/detail"){
                    var body =  Encoding.UTF8.GetString(await e.BodyAsync());
                    var json = JsonDocument.Parse(body);
                    var videoInfo = json.RootElement.GetProperty("data").GetProperty("View");
                    var id = videoInfo.GetProperty("bvid").GetString();
                    var title = videoInfo.GetProperty("title").GetString();
                    var author = videoInfo.GetProperty("owner").GetProperty("name").GetString();
                    var duration = videoInfo.GetProperty("duration").GetInt32();
                    AnsiConsole.MarkupLine($"监测到播放 {id} {title}({author}) {duration}s".EscapeMarkup());
                }
            };
            playPage.Request += async (o, e) => {
                var url = new Uri(e.Url);
                var path = url.AbsolutePath;
                if(path == "/x/passport-login/web/qrcode/generate"){
                    // click on close button
                    try {
                        await playPage.WaitForSelectorAsync(".bili-mini-close-icon", new PageWaitForSelectorOptions{ Timeout = 60000 });
                        await playPage.ClickAsync(".bili-mini-close-icon");
                        await playPage.ClickAsync(".bpx-player-video-perch");
                        AnsiConsole.MarkupLine("关闭自动弹出的登录对话框");
                    }catch{ }
                }
            };
            return client;
        }

        public async Task<List<BVideo>> SearchVideosAsync(string keyword, int pageNumber=1, StatusContext? ctx = null)
        {
            LastSearchKeywords = keyword;
            LastSearchPageNumber = pageNumber;
            // 按照默认综合排序
            var page = await Browser.NewPageAsync();
            var pagePart = pageNumber > 1 ? $"&page={pageNumber}" : "";
            await page.GotoAsync($"https://search.bilibili.com/all?keyword={HttpUtility.UrlEncode(keyword)}{pagePart}");
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
        public async Task<List<BVideo>> SearchNextPage(){
            return await SearchVideosAsync(LastSearchKeywords, LastSearchPageNumber + 1);
        }
        public async Task<List<BVideo>> SearchPrevPage(){
            if(LastSearchPageNumber <= 1){
                return new List<BVideo>();
            }
            return await SearchVideosAsync(LastSearchKeywords, LastSearchPageNumber - 1);
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
            if(PlayTimer != null)
            {
                PlayTimer.Dispose();
            }
            this.PlayTimer = new Timer((o) =>
            {
                AnsiConsole.MarkupLine($"预计播放结束（{video.Title}）".EscapeMarkup());
            }, null, time * 1000, Timeout.Infinite);
            AnsiConsole.MarkupLine($"总时间 {time}s");
            // 读取推荐列表
            ctx?.Status("读取推荐列表");
            this.RecommandList = await GetRecommandListOnPlay();
        }
        private async Task<List<BVideo>> GetRecommandListOnPlay()
        {
            var result = new List<BVideo>();
            var collections = await PlayPage.QuerySelectorAllAsync(".video-page-card-small");
            foreach(var item in collections)
            {
                var idA = await item.QuerySelectorAsync(".info a");
                var titleA = await item.QuerySelectorAsync(".info a .title");
                var upnameA = await item.QuerySelectorAsync(".upname a");
                if(idA == null || upnameA == null || titleA == null)
                {
                    continue;
                }
                var idHref = await idA.GetAttributeAsync("href");
                var titleText = await titleA.GetAttributeAsync("title");
                var upnameHref = await upnameA.GetAttributeAsync("href");
                if(BVideo.ExtractId(idHref ?? "", out var id))
                {
                    result.Add(new BVideo(id) { Title = titleText });
                }
            }
            return result;
        }
        private async Task<(bool success, int rawVolume, int newVolume)> CloseMute()
        {
            var volume = int.Parse(await PlayPage.InnerTextAsync("div.bpx-player-ctrl-volume-number"));
            if (volume == 0)
            {
                await PlayPage.Keyboard.PressAsync("m");
                var newVolume = int.Parse(await PlayPage.InnerTextAsync("div.bpx-player-ctrl-volume-number"));
                return (newVolume != 0, volume, newVolume);
            }
            return (true, volume, volume);
        }
        private readonly Regex UploaderPattern = new Regex(@"space.bilibili.com/(\d+)");
        private async Task<string> GetUploadersOnPlay()
        {
            // 显示UP信息
            var upinfo = "";
            var upPanelContainer = await PlayPage.QuerySelectorAllAsync(".up-panel-container");
            var upIds = new List<string>();
            var upNames = new List<string>();
            if(upPanelContainer.Count != 0)
            {
                var anchors = await upPanelContainer[0].QuerySelectorAllAsync("a");
                foreach(var a in anchors)
                {
                    // check if is "up-name" or "staff-name" class
                    var classes = await a.GetAttributeAsync("class");
                    if(classes == null || (!classes.Contains("up-name") && !classes.Contains("staff-name")))
                    {
                        continue;
                    }
                    var href = await a.GetAttributeAsync("href");
                    var match = UploaderPattern.Match(href ?? "");
                    if(match.Success)
                    {
                        upIds.Add(match.Groups[1].Value);
                        upNames.Add(await a.InnerTextAsync());
                    }
                }
                // zip upIds and upNames
                upinfo = string.Join(", ", upIds.Zip(upNames, (x, y) => $"{y} ({x})"));
            }
            // 试着从元信息里找UP信息
            if (upinfo == "")
            {
                foreach (var meta in await PlayPage.QuerySelectorAllAsync("meta")) {
                    try
                    {
                        var metaName = await meta.GetAttributeAsync("name");
                        if (metaName == "author")
                        {
                            upNames = new List<string> { await meta.GetAttributeAsync("content") ?? "" };
                            foreach (var a in await PlayPage.QuerySelectorAllAsync("a"))
                            {
                                if (upNames[0] == await a.InnerTextAsync())
                                {
                                    var match = UploaderPattern.Match(await a.GetAttributeAsync("href") ?? "");
                                    if (match.Success)
                                    {
                                        upIds = new List<string> { match.Groups[1].Value };
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
                if (upIdsList.Count == upNames.Count)
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
                var fullTimeText = await PlayPage.InnerTextAsync("span.bpx-player-ctrl-time-duration");
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
        /// 排行榜
        /// </summary>
        /// <param name="partition">分区</param>
        /// <returns></returns>
        public async Task<List<BVideo>> ShowRankVideos(string partition){
            var page = await Browser.NewPageAsync();
            await page.GotoAsync($"https://www.bilibili.com/v/popular/rank/{partition}");
            await page.WaitForSelectorAsync(".rank-list .info a.title");
            var collection = await page.QuerySelectorAllAsync(".rank-list .info a.title");
            var result = new List<BVideo>();
            foreach(var item in collection){
                var href = await item.GetAttributeAsync("href");
                if(href != null){
                    if(BVideo.ExtractId(href, out var id)){
                        var title = await item.InnerTextAsync() ?? "";
                        result.Add(new BVideo(id){Title = title});
                    }
                }
            }
            SearchList = new List<BVideo>(result);
            await page.CloseAsync();
            return result;
        }

        public bool IsWatchingLiveDanmaku { get; private set; } = false;
        private Timer? LiveDanmakuTimer { get; set; } = null;
        private List<LiveDanmaku> LastDanmakusPool = new List<LiveDanmaku>();
        public void WatchDanmaku()
        {
            // 是否在直播
            var isLive = PlayPage.Url.Contains("live.bilibili.com");
            if (!isLive)
            {
                AnsiConsole.MarkupLine("目前仅支持直播弹幕监听");
                return;
            }
            if (LiveDanmakuTimer != null)
            {
                LiveDanmakuTimer.Dispose();
                LiveDanmakuTimer = null;
            }
            if (IsWatchingLiveDanmaku)
            {
                IsWatchingLiveDanmaku = false;
                AnsiConsole.MarkupLine("停止监听直播弹幕");
                return;
            }
            else
            {   
                IsWatchingLiveDanmaku = true;
                LiveDanmakuTimer = new Timer(async (o) =>
                {
                    // 检查是否在直播
                    var isLive = PlayPage.Url.Contains("live.bilibili.com");
                    if (!isLive)
                    {
                        IsWatchingLiveDanmaku = false;
                        return;
                    }
                    var chatItemsContainer = await PlayPage.QuerySelectorAsync("#chat-items");                    
                    if(chatItemsContainer == null)
                    {
                        return;
                    }
                    var chatItems = await chatItemsContainer.QuerySelectorAllAsync(".chat-item");
                    if(chatItems == null || chatItems.Count == 0)
                    {
                        return;
                    }
                    var danmakus = (await Task.WhenAll(chatItems.Select(async (item) =>
                    {
                        var username = await item.GetAttributeAsync("data-uname");
                        var content = await item.GetAttributeAsync("data-danmaku");
                        if (username == null || content == null)
                        {
                            return null;
                        }
                        return new LiveDanmaku(username, content);
                    }))).Where(x=>x != null).Select(x=>x!).ToList();
                    // merge
                    // 找到danmakus中最后一个和pool中最后一个相同的
                    if(LastDanmakusPool.Count != 0)
                    {
                        var lastDanmaku = LastDanmakusPool.Last();
                        // record默认是值相等
                        var lastSameIndex = danmakus.FindLastIndex(x => x==lastDanmaku);
                        LastDanmakusPool.Clear();
                        LastDanmakusPool.AddRange(danmakus);
                        if(lastSameIndex != -1)
                        {
                            danmakus = danmakus.GetRange(lastSameIndex + 1, danmakus.Count - lastSameIndex - 1);
                        }
                    }
                    else
                    {
                        LastDanmakusPool.AddRange(danmakus);
                    }
                    // 去除重复的
                    danmakus = danmakus.Distinct().ToList();
                    foreach (var danmaku in danmakus)
                    {
                        var userName = danmaku.userName;
                        if (userName.EndsWith("***"))
                        {
                            userName = userName.Substring(0, userName.Length - 3) + "*";
                        }
                        AnsiConsole.MarkupLineInterpolated($"[bold]{userName}[/] {danmaku.content}");
                    }
                }, null, 0, 1000);
                AnsiConsole.MarkupLine("开始监听直播弹幕");
            }
        }

        /// <summary>
        /// 打开直播间
        /// </summary>
        /// <param name="liveId">直播间号</param>
        /// <returns></returns>
        public async Task<bool> OpenLive(string liveId)
        {
            await PlayPage.GotoAsync($"https://live.bilibili.com/{liveId}");
            // 似乎现在的版本重定向失效了，先删了
            try
            {
                var title = await PlayPage.InnerTextAsync("div.live-title div.text");
                if (title != null)
                {
                    AnsiConsole.MarkupLine("进入直播间：" + title);
                    // 等待播放器加载
                    await PlayPage.WaitForSelectorAsync("#live-player", new (){ Timeout = 30000 });
                    AnsiConsole.Markup("播放器加载完成...");
                    // 检查是否已经结束直播
                    var endingDiv = await PlayPage.QuerySelectorAsync("div.web-player-ending-panel");
                    if (endingDiv != null)
                    {
                        AnsiConsole.MarkupLine("直播已结束");
                        return false;
                    }
                    await PlayPage.DispatchEventAsync("#live-player", "mousemove");
                    // AnsiConsole.Markup("模拟鼠标移动");
                    await PlayPage.HoverAsync(".volume");
                    // AnsiConsole.Markup("锁定音量控制按钮");
                    var volume = await PlayPage.InnerTextAsync(".volume-control .number");
                    AnsiConsole.MarkupLine("音量：" + volume);
                    if (volume == "0")
                    {
                        AnsiConsole.MarkupLine("尝试打开声音");
                        await PlayPage.ClickAsync(".volume");
                        // 显示音量的变化
                        var newVolume = await PlayPage.InnerTextAsync(".volume-control .number");
                        AnsiConsole.MarkupLine($"音量：{volume} -> {newVolume}");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                AnsiConsole.MarkupLine("读取直播间信息出现错误");
                return false;
            }
            
        }
    }
}
