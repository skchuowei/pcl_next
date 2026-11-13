namespace MeloongCore;

/// <summary>
/// 当 HTTP 请求的状态码不指示成功时引发的异常。
/// </summary>
public class HttpRequestCodeException(string message, HttpStatusCode statusCode, string? response) : HttpRequestException(message) {
    /// <summary>
    /// HTTP 请求的状态码。
    /// </summary>
    /// <remarks>
    /// 加了 <c>new</c>：<see cref="HttpRequestException"/> 自 .NET 5 起自带 <c>StatusCode</c>（可空），
    /// 这里是有意隐藏基类成员，让调用方在本异常类型上直接拿到非空状态码，避免 CS0108 语义歧义。
    /// </remarks>
    public new HttpStatusCode StatusCode { get; } = statusCode;
    /// <summary>
    /// 远程服务器给予的回复。
    /// </summary>
    public string? Response { get; } = response;
}
