using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace Veyra.Core;

/// <summary>Gemeinsamer HTTP-Zugang der Widgets (Wetter, öffentliche IP): kurzer Timeout, Fehler werden protokolliert statt geworfen.</summary>
public static class Web
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Veyra", version.ToString(3)));
        return client;
    }

    /// <summary>Inhalt der Adresse als Text; null bei Fehler oder ohne Verbindung.</summary>
    public static async Task<string?> GetStringAsync(string url, string area, CancellationToken cancel = default)
    {
        try
        {
            using var response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug(area, $"{new Uri(url).Host}: HTTP {(int)response.StatusCode}");
                return null;
            }
            return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            Log.Debug(area, $"{new Uri(url).Host}: {ex.Message}");
            return null;
        }
    }
}
