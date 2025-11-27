using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace EC.Core;

internal class PushPlusClient(string token)
{
    private readonly string _token = token;

    public string Print(string message)
    {
        return Print(message, "伊机控消息");
    }

    public string Print(string message, string title)
    {
        try
        {
            message = HttpUtility.UrlEncode(message);
            var address = $"https://www.pushplus.plus/send/{_token}?content={message}&title={title}";
            using var client = new HttpClient();
            var result = client.GetAsync(address).Result.Content.ReadAsStringAsync().Result;
            Response? resp = JsonSerializer.Deserialize<Response>(result);
            return $"{resp?.Message ?? "推送结果解析异常"}";
        }
        catch (Exception ex)
        {
            return $"推送失败:{ex.Message}";
        }
    }
}

internal record class Response
{
    public int Code { get; set; }
    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}