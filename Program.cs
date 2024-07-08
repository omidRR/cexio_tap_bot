using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Web;
using ConsoleTables;

class Program
{
    static List<UrlInfo> urls;
    static List<Timer> timers;
    static List<RequestInfo> requestInfos = new List<RequestInfo>();
    static Random random = new Random();
    static List<Timer> claimTimers;
    static bool isDisplaying = false;

    static async Task Main(string[] args)
    {
        try
        {
            var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var filePath = Path.Combine(currentDirectory, "urls.txt");

            Console.WriteLine("Development by omidRR");
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "");
                Console.WriteLine("input Url token in 'urls.txt'");
            }

            urls = File.ReadAllLines(filePath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .Select(line => new UrlInfo { Path = line, Token = "" })
                .ToList();

            timers = new List<Timer>();
            claimTimers = new List<Timer>();
            await InitTokens();

            Console.ReadLine();
        }
        catch (Exception ex)
        {
            LogError($"An error occurred in Main: {ex.Message}");
        }
    }

    static async Task InitTokens()
    {
        try
        {
            var tasks = urls.Select(async url =>
            {
                try
                {
                    await SendRequest(url);
                }
                catch (Exception ex)
                {
                    LogError($"An error occurred while processing URL: {url.Path}. Error: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            LogError($"An error occurred in InitTokens: {ex.Message}");
        }
    }

    static async Task SendRequest(UrlInfo url)
    {
        try
        {
            Thread.Sleep(1000);
            var uri = new Uri(url.Path);
            var query = HttpUtility.ParseQueryString(uri.Fragment.TrimStart('#'));
            var tgWebAppData = query["tgWebAppData"];
            if (string.IsNullOrEmpty(tgWebAppData))
            {
                LogError($"Invalid tgWebAppData in URL: {url.Path}");
                return;
            }

            var decodedData = HttpUtility.UrlDecode(tgWebAppData);
            var keyValuePairs = HttpUtility.ParseQueryString(decodedData);
            var userDataJson = keyValuePairs["user"];
            var userData = JObject.Parse(userDataJson);
            var devAuthData = (long)userData["id"];
            var firstName = (string)userData["first_name"];

            var claimTimer = new Timer(async _ => await SendClaimTapsRequest(devAuthData, tgWebAppData, firstName), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            claimTimers.Add(claimTimer);

            var client = new RestClient("https://cexp.cex.io");
            var request = new RestRequest("/api/startFarm", Method.Post);
            request.AddHeader("accept", "application/json, text/plain, */*");
            request.AddHeader("accept-language", "en-US,en;q=0.9,fa;q=0.8");
            request.AddHeader("content-type", "application/json");
            request.AddHeader("cookie", "SL_G_WPT_TO=fa; SL_GWPT_Show_Hide_tmp=1; SL_wptGlobTipTmp=1");
            request.AddHeader("dnt", "1");
            request.AddHeader("origin", "https://cexp.cex.io");
            request.AddHeader("priority", "u=1, i");
            request.AddHeader("referer", "https://cexp.cex.io/");
            request.AddHeader("sec-fetch-dest", "empty");
            request.AddHeader("sec-fetch-mode", "cors");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("user-agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1");

            var body = new
            {
                devAuthData = devAuthData,
                authData = tgWebAppData,
                data = new { }
            };

            request.AddJsonBody(body);

            var response = await client.ExecuteAsync(request);

            if (response.IsSuccessful)
            {
                var jsonResponse = JObject.Parse(response.Content);
                LogRequest(firstName, jsonResponse.ToString());
                Console.WriteLine($"[{firstName}] => {jsonResponse}");

                var farmStartedAt = (DateTime)jsonResponse["data"]["farmStartedAt"];
                var nextRequestTime = farmStartedAt.AddMinutes(242);

                if (nextRequestTime < DateTime.Now)
                {
                    nextRequestTime = DateTime.Now.AddMinutes(242);
                }

                UpdateRequestInfo(url, firstName, nextRequestTime);
                DisplayRemainingTimes();

                var timer = new Timer(async _ => await SendRequest(url), null, nextRequestTime - DateTime.Now, TimeSpan.FromMilliseconds(-1));
                timers.Add(timer);
            }
            else
            {
                var jsonResponse = JObject.Parse(response.Content);
                if ((string)jsonResponse["status"] == "error" && (string)jsonResponse["data"]["reason"] == "Farm is already started")
                {
                    Console.WriteLine($"[{firstName}] => Farm is already started. Will retry in the next cycle.");

                    var nextRequestTime = DateTime.Now.AddMinutes(242);
                    UpdateRequestInfo(url, firstName, nextRequestTime);
                    DisplayRemainingTimes();

                    var timer = new Timer(async _ => await SendRequest(url), null, nextRequestTime - DateTime.Now, TimeSpan.FromMilliseconds(-1));
                    timers.Add(timer);

                    await SendClaimRequest(devAuthData, tgWebAppData, firstName, url);
                }
                else
                {
                    LogRequest(firstName, $"Request failed for URL: {url.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"An error occurred while processing URL: {url.Path}. Error: {ex.Message}");
        }
    }

    static void UpdateRequestInfo(UrlInfo url, string firstName, DateTime nextRequestTime)
    {
        var existingInfo = requestInfos.FirstOrDefault(info => info.Url.Path == url.Path);
        if (existingInfo != null)
        {
            existingInfo.NextRequestTime = nextRequestTime;
        }
        else
        {
            requestInfos.Add(new RequestInfo
            {
                Url = url,
                FirstName = firstName,
                NextRequestTime = nextRequestTime
            });
        }
    }

    static void DisplayRemainingTimes()
    {
        if (isDisplaying)
            return;

        isDisplaying = true;
        Task.Run(async () =>
        {
            while (true)
            {
                Console.Clear();
                var table = new ConsoleTable("User", "Remaining Time");

                foreach (var info in requestInfos)
                {
                    var remainingTime = info.NextRequestTime - DateTime.Now;
                    if (remainingTime <= TimeSpan.Zero)
                    {
                        continue;
                    }

                    table.AddRow(info.FirstName, remainingTime.Humanize(precision: 2));
                }

                table.Write();
                Console.WriteLine($"Count: {requestInfos.Count}");
                Console.WriteLine();
                await Task.Delay(20000);
            }
        });
    }

    static async Task SendClaimRequest(long devAuthData, string tgWebAppData, string firstName, UrlInfo url)
    {
        try
        {
            Thread.Sleep(1000);
            var client = new RestClient("https://cexp.cex.io");
            var request = new RestRequest("/api/claimFarm", Method.Post);
            request.AddHeader("accept", "application/json, text/plain, */*");
            request.AddHeader("accept-language", "en-US,en;q=0.9,fa;q=0.8");
            request.AddHeader("content-type", "application/json");
            request.AddHeader("cookie", "SL_G_WPT_TO=fa; SL_GWPT_Show_Hide_tmp=1; SL_wptGlobTipTmp=1");
            request.AddHeader("dnt", "1");
            request.AddHeader("origin", "https://cexp.cex.io");
            request.AddHeader("priority", "u=1, i");
            request.AddHeader("referer", "https://cexp.cex.io/");
            request.AddHeader("sec-fetch-dest", "empty");
            request.AddHeader("sec-fetch-mode", "cors");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("user-agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1");

            var body = new
            {
                devAuthData = devAuthData,
                authData = tgWebAppData,
                data = new { }
            };

            request.AddJsonBody(body);

            var response = await client.ExecuteAsync(request);

            if (response.IsSuccessful)
            {
                var jsonResponse = JObject.Parse(response.Content);
                LogRequest(firstName, jsonResponse.ToString());
                Console.WriteLine($"[{firstName}] => {jsonResponse}");

                await SendRequest(url);
            }
            else
            {
                LogRequest(firstName, $"ClaimFarm Request failed. Response: {response.Content}");
                Console.WriteLine($"[{firstName}] => ClaimFarm Request failed. Response: {response.Content}");
            }
        }
        catch (Exception ex)
        {
            LogError($"An error occurred while sending ClaimFarm request. Error: {ex.Message}");
        }
    }

    static async Task SendClaimTapsRequest(long devAuthData, string tgWebAppData, string firstName)
    {
        try
        {
            Thread.Sleep(1000);
            var availableTaps = await GetAvailableTaps(devAuthData, tgWebAppData);
            if (availableTaps > 0)
            {
                int taps;
                if (availableTaps < 92)
                {
                    taps = availableTaps;
                }
                else
                {
                    taps = random.Next(60, 92);
                }

                var client = new RestClient("https://cexp.cex.io");
                var request = new RestRequest("/api/claimTaps", Method.Post);
                request.AddHeader("accept", "application/json, text/plain, */*");
                request.AddHeader("accept-language", "en-US,en;q=0.9,fa;q=0.8");
                request.AddHeader("content-type", "application/json");
                request.AddHeader("cookie", "SL_G_WPT_TO=fa; SL_GWPT_Show_Hide_tmp=1; SL_wptGlobTipTmp=1");
                request.AddHeader("dnt", "1");
                request.AddHeader("origin", "https://cexp.cex.io");
                request.AddHeader("priority", "u=1, i");
                request.AddHeader("referer", "https://cexp.cex.io/");
                request.AddHeader("sec-fetch-dest", "empty");
                request.AddHeader("sec-fetch-mode", "cors");
                request.AddHeader("sec-fetch-site", "same-origin");
                request.AddHeader("user-agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1");

                var body = new
                {
                    devAuthData = devAuthData,
                    authData = tgWebAppData,
                    data = new { taps = taps }
                };

                request.AddJsonBody(body);

                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    var availableTapsAfterClaim = await GetAvailableTaps(devAuthData, tgWebAppData);

                    LogRequest(firstName, $"Available Taps After Claim: {availableTapsAfterClaim}");

                    Console.WriteLine($"[{firstName}] => Available Taps After Claim: {availableTapsAfterClaim}");
                }
                else
                {
                    LogRequest(firstName, $"ClaimTaps Request failed. Response: {response.Content}");
                    Console.WriteLine($"[{firstName}] => ClaimTaps Request failed. Response: {response.Content}");
                }

                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            LogError($"An error occurred while sending ClaimTaps request. Error: {ex.Message}");
        }
    }

    static async Task<int> GetAvailableTaps(long devAuthData, string tgWebAppData)
    {
        try
        {
            Thread.Sleep(1300);
            var client = new RestClient("https://cexp.cex.io");
            var request = new RestRequest("/api/getUserInfo", Method.Post);
            request.AddHeader("accept", "application/json, text/plain, */*");
            request.AddHeader("accept-language", "en-US,en;q=0.9,fa;q=0.8");
            request.AddHeader("content-type", "application/json");
            request.AddHeader("cookie", "SL_G_WPT_TO=fa; SL_GWPT_Show_Hide_tmp=1; SL_wptGlobTipTmp=1");
            request.AddHeader("dnt", "1");
            request.AddHeader("origin", "https://cexp.cex.io");
            request.AddHeader("priority", "u=1, i");
            request.AddHeader("referer", "https://cexp.cex.io/");
            request.AddHeader("sec-fetch-dest", "empty");
            request.AddHeader("sec-fetch-mode", "cors");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("user-agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1");

            var body = new
            {
                devAuthData = devAuthData,
                authData = tgWebAppData,
                data = new { },
                platform = "ios"
            };

            request.AddJsonBody(body);

            var response = await client.ExecuteAsync(request);

            if (response.IsSuccessful)
            {
                var jsonResponse = JObject.Parse(response.Content);
                int availableTaps = (int)jsonResponse["data"]["availableTaps"];
                return availableTaps;
            }
            else
            {
                LogRequest("System", $"GetAvailableTaps Request failed. Response: {response.Content}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            LogError($"An error occurred while getting available taps. Error: {ex.Message}");
            return 0;
        }
    }

    static void LogRequest(string firstName, string message)
    {
        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var logFilePath = Path.Combine(currentDirectory, "log.txt");
        var logMessage = $"[{firstName}] => {message}";

        lock (requestInfos)
        {
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
    }

    static void LogError(string message)
    {
        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var logFilePath = Path.Combine(currentDirectory, "log.txt");
        var logMessage = $"[ERROR] => {message}";

        lock (requestInfos)
        {
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
    }

    class UrlInfo
    {
        public string Path { get; set; }
        public string Token { get; set; }
    }

    class RequestInfo
    {
        public UrlInfo Url { get; set; }
        public string FirstName { get; set; }
        public DateTime NextRequestTime { get; set; }
    }
}
