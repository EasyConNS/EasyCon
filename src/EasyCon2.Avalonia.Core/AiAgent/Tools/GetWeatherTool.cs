using EasyCon.Core.LLM.Tools;
using System.Net.Http;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.AiAgent.Tools;

/// <summary>
/// get_weather 工具：查询指定城市的当前天气信息。
/// 使用 wttr.in 免费天气 API，无需 API Key。
/// </summary>
public class GetWeatherTool : IAiTool
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public string Name => "get_weather";

    public string Description => "获取指定城市或地点的当前天气信息，包括温度、湿度、风速、天气描述等。";

    public JsonSchema Parameters => new()
    {
        Type = "object",
        Properties = new()
        {
            ["location"] = new JsonSchemaProperty
            {
                Type = "string",
                Description = "城市名称或地点（如 '北京'、'Tokyo'、'New York'）"
            }
        },
        Required = ["location"]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        if (!args.TryGetValue("location", out var locationEl) || locationEl.ValueKind != JsonValueKind.String)
            return ToolResult.Error("[错误] 缺少必需参数: location");

        var location = locationEl.GetString();
        if (string.IsNullOrWhiteSpace(location))
            return ToolResult.Error("[错误] location 参数不能为空");

        try
        {
            var encodedLocation = Uri.EscapeDataString(location);
            var url = $"https://wttr.in/{encodedLocation}?format=j1";

            var response = await _httpClient.GetStringAsync(url, ct);
            var weatherData = JsonSerializer.Deserialize<WeatherResponse>(response);

            if (weatherData?.current_condition is not { Length: > 0 })
                return ToolResult.Error($"[错误] 无法获取 {location} 的天气数据");

            var current = weatherData.current_condition[0];
            var result = new
            {
                location,
                temperature_C = current.temp_C,
                temperature_F = current.temp_F,
                feels_like_C = current.FeelsLikeC,
                feels_like_F = current.FeelsLikeF,
                humidity = current.humidity,
                description = current.weatherDesc is { Length: > 0 } ? current.weatherDesc[0].value : "未知",
                wind_speed_kmph = current.windspeedKmph,
                wind_direction = current.winddir16Point,
                visibility_km = current.visibility,
                pressure_mb = current.pressure,
                uv_index = current.uvIndex,
                observation_time = current.observation_time
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            return ToolResult.Ok($"🌤️ {location} 当前天气：\n{json}");
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Error($"[错误] 天气 API 请求失败: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Error($"[错误] 天气查询超时，请稍后重试");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"[错误] 天气查询失败: {ex.Message}");
        }
    }

    private class WeatherResponse
    {
        public CurrentCondition[]? current_condition { get; set; }
    }

    private class CurrentCondition
    {
        public string? temp_C { get; set; }
        public string? temp_F { get; set; }
        public string? FeelsLikeC { get; set; }
        public string? FeelsLikeF { get; set; }
        public string? humidity { get; set; }
        public string? windspeedKmph { get; set; }
        public string? winddir16Point { get; set; }
        public string? visibility { get; set; }
        public string? pressure { get; set; }
        public string? uvIndex { get; set; }
        public string? observation_time { get; set; }
        public WeatherDesc[]? weatherDesc { get; set; }
    }

    private class WeatherDesc
    {
        public string? value { get; set; }
    }
}