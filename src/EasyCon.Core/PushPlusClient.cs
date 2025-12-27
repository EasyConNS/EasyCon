using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace EC.Core;

public class PushPlusClient(string token)
{
    private readonly string _token = token;

    public async Task<string> SendMessage(string content, string title = "伊机控消息")
    {
        if (_token == "") throw new ArgumentException("token配置为空");

        try
        {
            content = HttpUtility.UrlEncode(content);
            var address = $"https://www.pushplus.plus/send/{_token}?content={content}&title={title}";
            using var client = new HttpClient();
            var result = await client.GetAsync(address).Result.Content.ReadAsStringAsync();
            Response? resp = JsonSerializer.Deserialize<Response>(result);
            return $"{resp?.Message ?? $"推送结果解析异常：{result}"}";
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