using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

const int DefaultTimeoutSeconds = 30;

async Task<int> RunAsync()
{
    // Hardcoded API URL and API key (embedded per user request)
    var apiUrl = "https://next.joinlane.com/graphql?o=createInteraction";
    var apiKey = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6Ik1NRXNfc2ljQTdNZHZYdDZaLTVxQiJ9.eyJrZXkiOiJiaGFyYXRoLmthcnVwcGFubmFuQGFnaWx5c3lzLmNvbSIsIl9pZCI6ImU2MjdmYjRkLTIyNzQtNDIyNi1iMDk1LTY0MzQzMjE2YWY5MyIsImFjdGl2YXRlX3Nlc3Npb25faWQiOiJkM2QwZGE1MC1kMzI0LTQ5ZDktYjk2OC00MTM1YTFmMjBmN2IiLCJuaWNrbmFtZSI6IkJoYXJhdGggayIsIm5hbWUiOiJiaGFyYXRoLmthcnVwcGFubmFuQGFnaWx5c3lzLmNvbSIsInBpY3R1cmUiOiJodHRwczovL3MuZ3JhdmF0YXIuY29tL2F2YXRhci9hNjFjMmQ2MWVhZWJlY2QxZDVjNDAwNzBlMmM1MjU4ZD9zPTQ4MCZyPXBnJmQ9aHR0cHMlM0ElMkYlMkZjZG4uYXV0aDAuY29tJTJGYXZhdGFycyUyRmJoLnBuZyIsInVwZGF0ZWRfYXQiOiIyMDI2LTA1LTAxVDEyOjQzOjUwLjIxMloiLCJlbWFpbCI6ImJoYXJhdGgua2FydXBwYW5uYW5AYWdpbHlzeXMuY29tIiwiZW1haWxfdmVyaWZpZWQiOnRydWUsImlzcyI6Imh0dHBzOi8vYXV0aC52dHMuY29tLyIsImF1ZCI6IkVSOTVOUGNIcXBEVWNuOXNVWXdrUmdyWGV6QjhEODQ5Iiwic3ViIjoiYXV0aDB8ZTYyN2ZiNGQtMjI3NC00MjI2LWIwOTUtNjQzNDMyMTZhZjkzIiwiaWF0IjoxNzc4Mjk4NjkxLCJleHAiOjE3OTQwNjg2OTEsInNpZCI6IkhWbXZ5SWJKb3dGOHlldm5RNmpCWnVZY2Y4SDI2WnJpIiwib3JnX2lkIjoib3JnXzViZFFFQVBNNnhSRkxlb1UiLCJvcmdfbmFtZSI6ImludGVsbGlvbnBsdXNieXRhdGFyZWFsdHkifQ.AiPxZsoYl_MPrqIjj_5zDzEy74g_Yraf7elUzA7ka1u0XtUatzb4ZELjT5JMhUGW0ITdLyS9VSyeNenT2ietfM0Pp-KQap_l8P3IM0jRS4h8dtI83RzDngMFO0Gjembdx-Hbu6xgP57Pg5cbLCURJAzRbJuXa75bWyl_sXm6R8SVamPVFmi0NZBSDgFSrnMaDvY304XdPAsLnYreSUsRx_l5abYJ-GKffDQzj9eZR8PnOlaYxQkTCzGH5YJxCj0hyBCYHGYFO8yl5pIyWje0_339nhKQgzcQYodYTGzvxGcX309Ud7mrZCNbIkszo4V4aMWfN-kqWjquzm-40VlA3Q";

    if (string.IsNullOrWhiteSpace(apiUrl)) 
    {
        Console.Error.WriteLine("ERROR: API_URL environment variable is not set."); 
        return 1;
    }

    // Default reservation: next day 07:00 - 08:00 India Standard Time (IST)
    // Convert IST defaults to UTC for the payload (e.g. 2026-05-12T01:30:00.000Z is 07:00 IST)
    // Time zone handling: try Windows and IANA IDs so this runs on both Windows (dev) and Linux (CI)
    TimeZoneInfo tzIndia = null;
    var tzCandidates = new[] { "India Standard Time", "Asia/Kolkata" };
    foreach (var id in tzCandidates)
    {
        try
        {
            tzIndia = TimeZoneInfo.FindSystemTimeZoneById(id);
            Console.WriteLine($"Using time zone ID: {id}");
            break;
        }
        catch { }
    }
    if (tzIndia == null)
    {
        Console.Error.WriteLine("ERROR: Unable to find India time zone on this machine (tried 'India Standard Time' and 'Asia/Kolkata').");
        return 4;
    }

    // Compute current IST using a fixed +05:30 offset to avoid platform TZ issues
    var tzOffset = tzIndia.GetUtcOffset(DateTime.UtcNow);
    Console.WriteLine($"Time zone reported offset: {tzOffset}");

    var fixedIstOffset = TimeSpan.FromMinutes(330); // +5:30

    DateTimeOffset nowIndiaDto;
    try
    {
        // Prefer using the system time zone mapping to get an accurate DateTimeOffset in IST
        nowIndiaDto = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzIndia);
    }
    catch
    {
        // Fallback to fixed offset if conversion fails
        Console.WriteLine("Warning: falling back to fixed +05:30 offset for IST conversion.");
        nowIndiaDto = DateTimeOffset.UtcNow.ToOffset(fixedIstOffset);
    }

    // Compute target date: the same weekday on the next week (strictly one week after today)
    var todayIndiaDate = nowIndiaDto.Date;
    var targetDate = todayIndiaDate.AddDays(7); // next week, same weekday

    // Choose slot hour based on the hour when the scheduler runs (round down to hour)
    var slotHour = nowIndiaDto.Hour; // e.g. running at 11:04 IST -> slotHour = 11
    var startIndia = new DateTimeOffset(targetDate.Year, targetDate.Month, targetDate.Day, slotHour, 0, 0, fixedIstOffset);
    var endIndia = startIndia.AddHours(1);

    var defaultStart = startIndia.ToUniversalTime().UtcDateTime;
    var defaultEnd = endIndia.ToUniversalTime().UtcDateTime;

    Console.WriteLine($"Scheduling for next {todayIndiaDate.DayOfWeek} (IST date): {targetDate:yyyy-MM-dd}");
    Console.WriteLine($"Start (IST): {startIndia:yyyy-MM-ddTHH:mm:ss} -> Start (UTC): {defaultStart:yyyy-MM-ddTHH:mm:ss.fffZ}");
    Console.WriteLine($"End   (IST): {endIndia:yyyy-MM-ddTHH:mm:ss} -> End   (UTC): {defaultEnd:yyyy-MM-ddTHH:mm:ss.fffZ}");

    // Non-interactive mode for GitHub Actions: use the computed default start/end (current IST -> UTC)
    DateTime start = defaultStart, end = defaultEnd;

    // Build GraphQL request body matching the curl
    var variables = new
    {
        contentId = "2DVtVkWba2cOWDTGmKC9WH",
        interaction = new
        {
            data = new { },
            state = new { },
            features = new
            {
                Statuses = new { },
                UseCompanyPermissions = new { },
                SubmitOnBehalfOf = new { user = new { _id = "2643bafd-9671-44df-9615-b400aab4537d" } },
                SocialOptions = new { },
                Cancelable = new { },
                Reservable = new
                {
                    reservation = new
                    {
                        startDate = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        endDate = end.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    },
                    userNotes = ""
                },
                TimeAvailability = new { }
            }
        },
        meChannelId = "3TW1s7lTs6oiKfpbRpj2fi",
        submittingAsWorkplaceMember = true
    };

    var query = @"mutation createInteraction($contentId: UUID!, $interaction: UserContentInteractionInput!, $meChannelId: UUID, $submittingAsWorkplaceMember: Boolean) {
  createContentInteraction(
    contentId: $contentId
    interaction: $interaction
    meChannelId: $meChannelId
    submittingAsWorkplaceMember: $submittingAsWorkplaceMember
  ) {
    _id
    _created
    _updated
    geo
    state
    features
    actions
    version
    status
    data
    contentData
    status
    startDate
    endDate
    version
    content {
      _id
      data
      __typename
    }
    user {
      _id
      profile {
        _id
        name
        __typename
      }
      __typename
    }
    __typename
  }
}";

    var gqlPayload = new { operationName = "createInteraction", variables, query };
    var json = JsonSerializer.Serialize(gqlPayload);

    Console.WriteLine($"Calling API: {apiUrl}");
    Console.WriteLine("Payload preview (truncated to 2000 chars):");
    Console.WriteLine(json.Length > 2000 ? json.Substring(0, 2000) + "..." : json);

    // Simple file logging (keeps logs next to the executable). Does not log secrets explicitly.
    var logDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
    var logFile = Path.Combine(logDir, "scheduler.log");
    void AppendLog(string message)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O} - {message}{Environment.NewLine}";
            File.AppendAllText(logFile, line, Encoding.UTF8);
        }
        catch { /* swallow logging errors */ }
    }

    string TruncateForLog(string s, int max = 2000)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length > max ? s.Substring(0, max) + "..." : s;
    }

    AppendLog($"Prepared payload length={json.Length}");
    AppendLog($"TargetDate={targetDate:yyyy-MM-dd}, StartUTC={defaultStart:O}, EndUTC={defaultEnd:O}");

    // Non-interactive: send payload as-is

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
    using var http = new HttpClient();
    try
    {
        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // Add common API key headers. Adjust as needed by the target API.
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("x-api-key", apiKey);
        }
        // Add additional headers to mimic the Postman/browser request
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
        request.Headers.TryAddWithoutValidation("Origin", "https://intellionplus.tatarealty.in");
        request.Headers.TryAddWithoutValidation("Referer", "https://intellionplus.tatarealty.in/");
        request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Microsoft Edge\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "cross-site");
        request.Headers.TryAddWithoutValidation("sec-fetch-storage-access", "active");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36 Edg/147.0.0.0");
        request.Headers.TryAddWithoutValidation("x-client-version", "5.596.0");
        request.Headers.TryAddWithoutValidation("x-device", "Browser Unknown");
        request.Headers.TryAddWithoutValidation("x-geo-location", "80.17426150309365, 13.040536407190979");
        request.Headers.TryAddWithoutValidation("x-lane-instance", "lane");
        request.Headers.TryAddWithoutValidation("x-os-version", "147.0.0.0");
        request.Headers.TryAddWithoutValidation("x-platform", "web");
        request.Headers.TryAddWithoutValidation("x-primary-channel-id", "3TW1s7lTs6oiKfpbRpj2fi");
        request.Headers.TryAddWithoutValidation("priority", "u=1, i");

        var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
        var respBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        Console.WriteLine($"Response: {(int)response.StatusCode} {response.StatusCode}");
        if (!string.IsNullOrWhiteSpace(respBody))
            Console.WriteLine($"Body: {respBody}");

        // Log response (truncated)
        AppendLog($"ResponseStatus={(int)response.StatusCode} {response.StatusCode}");
        AppendLog($"ResponseBody={TruncateForLog(respBody, 4000)}");

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine("API call failed.");
            return 2;
        }

        Console.WriteLine("API call succeeded.");
        AppendLog("API call succeeded");
        return 0;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("ERROR: Request timed out.");
        return 3;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        return 4;
    }
}

var exit = await RunAsync();
Environment.Exit(exit);
