using System.Globalization;

namespace PCL.Cli;

/// <summary>
/// 手写参数解析器（不引 System.CommandLine，见计划 §4）。
/// <para/>
/// 支持三种写法：<c>--key value</c>、<c>--key=value</c>、<c>--flag</c>。
/// 判定规则：若某个 <c>--key</c> 后面紧跟的 token 仍以 <c>-</c> 开头，则该 key 视为开关；
/// 因此 <c>--url a --url b --sha1 x</c> 与 <c>--quiet --url a</c> 都能正确切分。
/// </summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>未以 <c>-</c> 开头的参数（子命令名等）。</summary>
    public List<string> Positionals { get; } = [];

    /// <summary>解析一段参数。</summary>
    public static CliArguments Parse(IEnumerable<string> args)
    {
        var result = new CliArguments();
        var tokens = args.ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token)) continue;

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token[2..];
                var eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    result.AddValue(body[..eq], body[(eq + 1)..]);
                    continue;
                }

                var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                if (next is not null && IsValueToken(next))
                {
                    result.AddValue(body, next);
                    i++;
                }
                else
                {
                    result._flags.Add(body);
                }
                continue;
            }

            if (token.StartsWith('-') && token.Length > 1)
            {
                // 短开关：-h / -q / -v
                result._flags.Add(token[1..]);
                continue;
            }

            result.Positionals.Add(token);
        }

        return result;
    }

    /// <summary>
    /// 判断紧跟 <c>--key</c> 的 token 是「值」还是「下一个开关」。
    /// 负数是合法取值（如 <c>--speed-limit -1</c>），必须与开关区分开。
    /// </summary>
    private static bool IsValueToken(string token)
    {
        if (token.Length == 0) return false;
        if (!token.StartsWith('-')) return true;
        return token.Length > 1 && char.IsDigit(token[1]);
    }

    private void AddValue(string key, string value)
    {
        if (key.Length == 0) return;
        if (!_values.TryGetValue(key, out var list))
            _values[key] = list = [];
        list.Add(value);
    }

    /// <summary>是否存在该键（开关或带值均算）。</summary>
    public bool Has(string key) => _values.ContainsKey(key) || _flags.Contains(key);

    /// <summary>取最后一次给出的值；未给出时返回 null。</summary>
    public string? Get(string key)
        => _values.TryGetValue(key, out var list) && list.Count > 0 ? list[^1] : null;

    /// <summary>取全部值（支持 <c>--url</c> 重复）。</summary>
    public IReadOnlyList<string> GetAll(string key)
        => _values.TryGetValue(key, out var list) ? list : [];

    /// <summary>取必填值，缺失即抛 <see cref="CliUsageException"/>。</summary>
    public string Require(string key, string description)
        => Get(key) ?? throw new CliUsageException($"缺少必需参数 --{key}（{description}）");

    /// <summary>取整数值。</summary>
    public int? GetInt(string key)
    {
        var raw = Get(key);
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new CliUsageException($"参数 --{key} 需要整数，实际为「{raw}」");
        return value;
    }

    /// <summary>取长整数值。</summary>
    public long? GetLong(string key)
    {
        var raw = Get(key);
        if (raw is null) return null;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new CliUsageException($"参数 --{key} 需要整数，实际为「{raw}」");
        return value;
    }

    /// <summary>取布尔开关，支持 <c>--flag</c> 与 <c>--flag=false</c> 两种写法。</summary>
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!Has(key)) return defaultValue;
        var raw = Get(key);
        if (raw is null) return true;
        return raw.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => defaultValue,
        };
    }
}
