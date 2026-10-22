using System.Globalization;
using System.Text.Json;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Weather;

/// <summary>Treffer der Ortssuche.</summary>
public sealed record WeatherLocation(string Name, string Region, string Country, double Latitude, double Longitude)
{
    /// <summary>"Berlin, Deutschland" – wird als Ortsname gespeichert.</summary>
    public string ShortName => Country.Length > 0 ? $"{Name}, {Country}" : Name;

    /// <summary>Mit Bundesland/Region, damit gleichnamige Orte unterscheidbar sind.</summary>
    public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(p => p.Length > 0)) +
                             string.Format(CultureInfo.CurrentCulture, "  ({0:0.00}°, {1:0.00}°)", Latitude, Longitude);

    public override string ToString() => Display;
}

public sealed record WeatherHour(DateTime Time, double Temperature, int Code, bool IsDay, int? PrecipitationChance);

public sealed record WeatherDay(DateTime Date, int Code, double Max, double Min, int? PrecipitationChance, DateTime? Sunrise, DateTime? Sunset);

/// <summary>Ein Abruf: aktuelle Werte, die nächsten Stunden und die nächsten Tage – in den gewünschten Einheiten.</summary>
public sealed record WeatherReport(
    double Temperature, double FeelsLike, int Humidity, double WindSpeed, double Precipitation, int Code, bool IsDay,
    IReadOnlyList<WeatherHour> Hours, IReadOnlyList<WeatherDay> Days, DateTime FetchedAt);

/// <summary>
/// Wetterdaten von Open-Meteo (open-meteo.com, ohne Konto und Schlüssel; Lizenz CC BY 4.0 – im Popup genannt).
/// Abrufe für denselben Ort und dieselben Einheiten werden ein paar Minuten zwischengespeichert, damit mehrere
/// Widgets nicht doppelt fragen.
/// </summary>
public static class WeatherService
{
    private static readonly Dictionary<string, WeatherReport> Cache = [];
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(5);

    public static async Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(string query)
    {
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "de" or "en" or "fr" or "es" or "it"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : "de";
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=8&language={language}&format=json";
        if (await Web.GetStringAsync(url, "Weather") is not { } json) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)) return [];
            return results.EnumerateArray().Select(r => new WeatherLocation(
                    Text(r, "name"), Text(r, "admin1"), Text(r, "country"),
                    r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Warning("Weather", $"Ortssuche: unerwartete Antwort ({ex.Message})");
            return [];
        }
    }

    public static async Task<WeatherReport?> GetAsync(WeatherSettings weather, bool force = false)
    {
        if (weather.Latitude is not { } lat || weather.Longitude is not { } lon) return null;
        string temperature = weather.Unit == TemperatureUnit.Fahrenheit ? "fahrenheit" : "celsius";
        string wind = weather.WindUnit switch
        {
            WindSpeedUnit.MetersPerSecond => "ms",
            WindSpeedUnit.MilesPerHour => "mph",
            _ => "kmh",
        };
        string key = string.Create(CultureInfo.InvariantCulture, $"{lat:0.####}|{lon:0.####}|{temperature}|{wind}");
        if (!force && Cache.TryGetValue(key, out var cached) && DateTime.Now - cached.FetchedAt < CacheTime) return cached;

        const string fields =
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m,precipitation" +
            "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset" +
            "&timezone=auto&forecast_days=7";
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.####}&longitude={lon:0.####}{fields}&temperature_unit={temperature}&wind_speed_unit={wind}");
        if (await Web.GetStringAsync(url, "Weather") is not { } json) return null;
        try
        {
            var report = Parse(json);
            Cache[key] = report;
            return report;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warning("Weather", $"Vorhersage: unerwartete Antwort ({ex.Message})");
            return null;
        }
    }

    private static WeatherReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var current = root.GetProperty("current");

        var hourly = root.GetProperty("hourly");
        var hourTimes = hourly.GetProperty("time").EnumerateArray().Select(t => Time(t.GetString())).ToList();
        var hourTemps = Numbers(hourly, "temperature_2m");
        var hourCodes = Numbers(hourly, "weather_code");
        var hourRain = Numbers(hourly, "precipitation_probability");
        var hourDay = Numbers(hourly, "is_day");
        // Zeiten sind Ortszeit des Orts (timezone=auto); "jetzt" dort steht in current.time.
        var now = Time(current.GetProperty("time").GetString());
        var hours = new List<WeatherHour>();
        for (int i = 0; i < hourTimes.Count && hours.Count < 12; i++)
        {
            if (hourTimes[i] < now.AddMinutes(-now.Minute)) continue;
            hours.Add(new WeatherHour(hourTimes[i], hourTemps.ElementAtOrDefault(i) ?? 0, (int)(hourCodes.ElementAtOrDefault(i) ?? 0),
                (hourDay.ElementAtOrDefault(i) ?? 1) > 0, (int?)hourRain.ElementAtOrDefault(i)));
        }

        var daily = root.GetProperty("daily");
        var dayTimes = daily.GetProperty("time").EnumerateArray().Select(t => Time(t.GetString())).ToList();
        var dayCodes = Numbers(daily, "weather_code");
        var dayMax = Numbers(daily, "temperature_2m_max");
        var dayMin = Numbers(daily, "temperature_2m_min");
        var dayRain = Numbers(daily, "precipitation_probability_max");
        var sunrise = daily.GetProperty("sunrise").EnumerateArray().Select(t => (DateTime?)Time(t.GetString())).ToList();
        var sunset = daily.GetProperty("sunset").EnumerateArray().Select(t => (DateTime?)Time(t.GetString())).ToList();
        var days = dayTimes.Select((date, i) => new WeatherDay(date, (int)(dayCodes.ElementAtOrDefault(i) ?? 0),
            dayMax.ElementAtOrDefault(i) ?? 0, dayMin.ElementAtOrDefault(i) ?? 0, (int?)dayRain.ElementAtOrDefault(i),
            sunrise.ElementAtOrDefault(i), sunset.ElementAtOrDefault(i))).ToList();

        return new WeatherReport(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            (int)Math.Round(current.GetProperty("relative_humidity_2m").GetDouble()),
            current.GetProperty("wind_speed_10m").GetDouble(),
            current.GetProperty("precipitation").GetDouble(),
            current.GetProperty("weather_code").GetInt32(),
            current.GetProperty("is_day").GetInt32() > 0,
            hours, days, DateTime.Now);
    }

    private static List<double?> Numbers(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array)
            ? array.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToList()
            : [];

    private static DateTime Time(string? text) =>
        DateTime.ParseExact(text ?? "", ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // ---- WMO-Wettercodes ----------------------------------------------------------------------

    /// <summary>Wetterlage als Text, z. B. „Leichter Regen“.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Klar",
        1 => "Überwiegend klar",
        2 => "Teilweise bewölkt",
        3 => "Bewölkt",
        45 or 48 => "Nebel",
        51 => "Leichter Nieselregen",
        53 => "Nieselregen",
        55 => "Starker Nieselregen",
        56 or 57 => "Gefrierender Nieselregen",
        61 => "Leichter Regen",
        63 => "Regen",
        65 => "Starker Regen",
        66 or 67 => "Gefrierender Regen",
        71 => "Leichter Schneefall",
        73 => "Schneefall",
        75 => "Starker Schneefall",
        77 => "Schneegriesel",
        80 => "Leichte Regenschauer",
        81 => "Regenschauer",
        82 => "Heftige Regenschauer",
        85 or 86 => "Schneeschauer",
        95 => "Gewitter",
        96 or 99 => "Gewitter mit Hagel",
        _ => "Unbekannt",
    };

    /// <summary>Symbol aus „Segoe UI Symbol“ (einfarbig, folgt der Schriftfarbe).</summary>
    public static string Glyph(int code, bool isDay) => code switch
    {
        0 => isDay ? "☀" : "\U0001F319",
        1 => isDay ? "\U0001F324" : "\U0001F319",
        2 => isDay ? "⛅" : "☁",
        3 => "☁",
        45 or 48 => "\U0001F32B",
        >= 51 and <= 57 => "\U0001F326",
        >= 61 and <= 67 => "\U0001F327",
        >= 71 and <= 77 => "\U0001F328",
        80 or 81 or 82 => isDay ? "\U0001F326" : "\U0001F327",
        85 or 86 => "\U0001F328",
        >= 95 => "⛈",
        _ => "☁",
    };

    public const string GlyphFont = "Segoe UI Symbol";

    public static string UnitSymbol(TemperatureUnit unit) => unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    public static string WindSymbol(WindSpeedUnit unit) => unit switch
    {
        WindSpeedUnit.MetersPerSecond => "m/s",
        WindSpeedUnit.MilesPerHour => "mph",
        _ => "km/h",
    };

    /// <summary>"12°" – ganze Grad, mit echtem Minuszeichen.</summary>
    public static string FormatTemperature(double value) =>
        (Math.Round(value) + 0.0).ToString("0", CultureInfo.CurrentCulture).Replace("-", "−") + "°"; // + 0.0: kein „−0°“
}
