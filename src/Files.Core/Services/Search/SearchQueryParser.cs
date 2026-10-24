using System.Globalization;

namespace Files.Core.Services.Search;

/// <summary>
/// 把用户输入解析成筛选条件（AQS 语法的一个子集）。
/// </summary>
/// <remarks>
/// 支持的语法见 SEARCH.md §2.2：
/// <code>
/// ext:.log              kind:picture          size:&gt;10mb
/// size:1kb..1mb         datemodified:thisweek ~ext:.tmp
/// "带 空格 的短语"       普通文字（按名称子串）
/// </code>
/// <b>未识别的属性前缀按普通文本处理</b>（如 <c>foo:bar</c> 当作名称子串），
/// 而不是静默返回零结果 —— 这是 PRD 已定的原则，避免用户以为"没有匹配项"。
/// </remarks>
public static class SearchQueryParser
{
    /// <summary>解析输入文本。</summary>
    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return SearchQuery.Empty;
        }

        var predicates = new List<SearchPredicate>();
        var plainWords = new List<string>();

        foreach (string token in Tokenize(text))
        {
            if (TryParseNegation(token, out string inner))
            {
                if (TryParsePredicate(inner, out SearchPredicate? negated))
                {
                    predicates.Add(new Not(negated!));
                }
                else
                {
                    plainWords.Add(token);
                }

                continue;
            }

            if (TryParsePredicate(token, out SearchPredicate? predicate))
            {
                predicates.Add(predicate!);
            }
            else
            {
                plainWords.Add(token);
            }
        }

        // 普通词合并为一个"名称包含"条件：多个词要求同时出现，
        // 用单个条件配合全部包含判断，避免拆成多个 AND 时语义混乱
        if (plainWords.Count == 1)
        {
            predicates.Add(new NameContains(plainWords[0]));
        }
        else if (plainWords.Count > 1)
        {
            predicates.Add(new NameContains(string.Join(' ', plainWords)));
        }

        return new SearchQuery(text, predicates);
    }

    /// <summary>
    /// 按空格切分，但保留引号内的空格。
    /// </summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static bool TryParseNegation(string token, out string inner)
    {
        inner = string.Empty;

        if (token.Length < 2 || token[0] != '~')
        {
            return false;
        }

        inner = token[1..];
        return true;
    }

    private static bool TryParsePredicate(string token, out SearchPredicate? predicate)
    {
        predicate = null;

        int colon = token.IndexOf(':');

        if (colon <= 0 || colon == token.Length - 1)
        {
            return false;
        }

        string key = token[..colon].ToLowerInvariant();
        string value = token[(colon + 1)..];

        switch (key)
        {
            case "ext":
                string ext = value.StartsWith('.') ? value : "." + value;
                predicate = new ExtensionIs(ext);
                return true;

            case "kind":
                if (TryParseKind(value, out EntryKindFilter kind))
                {
                    predicate = new KindIs(kind);
                    return true;
                }

                return false;

            case "size":
                if (TryParseSizeRange(value, out long? min, out long? max))
                {
                    predicate = new SizeInRange(min, max);
                    return true;
                }

                return false;

            case "datemodified":
                if (TryParseDateRange(value, out RelativeDateRange range))
                {
                    predicate = new ModifiedIn(range);
                    return true;
                }

                return false;

            default:
                // 未识别的属性前缀：交给调用方按普通文本处理
                return false;
        }
    }

    private static bool TryParseKind(string value, out EntryKindFilter kind)
    {
        kind = EntryKindFilter.Other;

        return value.ToLowerInvariant() switch
        {
            "folder" => Set(EntryKindFilter.Folder, out kind),
            "picture" or "image" => Set(EntryKindFilter.Picture, out kind),
            "document" => Set(EntryKindFilter.Document, out kind),
            "video" => Set(EntryKindFilter.Video, out kind),
            "audio" or "music" => Set(EntryKindFilter.Audio, out kind),
            _ => false,
        };

        static bool Set(EntryKindFilter value, out EntryKindFilter target)
        {
            target = value;
            return true;
        }
    }

    private static bool TryParseSizeRange(string value, out long? minBytes, out long? maxBytes)
    {
        minBytes = null;
        maxBytes = null;

        // 范围形式 a..b
        int rangeIndex = value.IndexOf("..", StringComparison.Ordinal);

        if (rangeIndex > 0)
        {
            string left = value[..rangeIndex];
            string right = value[(rangeIndex + 2)..];

            if (left.Length > 0 && TryParseSize(left, out long rangeMin))
            {
                minBytes = rangeMin;
            }

            if (right.Length > 0 && TryParseSize(right, out long rangeMax))
            {
                maxBytes = rangeMax;
            }

            return minBytes is not null || maxBytes is not null;
        }

        if (value.StartsWith('>') || value.StartsWith(">=", StringComparison.Ordinal))
        {
            string number = value.TrimStart('>', '=');

            if (TryParseSize(number, out long min))
            {
                minBytes = min;
                return true;
            }

            return false;
        }

        if (value.StartsWith('<') || value.StartsWith("<=", StringComparison.Ordinal))
        {
            string number = value.TrimStart('<', '=');

            if (TryParseSize(number, out long max))
            {
                maxBytes = max;
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>解析形如 <c>10mb</c> / <c>500kb</c> / <c>1024</c> 的大小。</summary>
    private static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;

        text = text.Trim().ToLowerInvariant();

        if (text.Length == 0)
        {
            return false;
        }

        long multiplier = 1;
        string numberPart = text;

        foreach ((string suffix, long factor) in Suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                multiplier = factor;
                numberPart = text[..^suffix.Length];
                break;
            }
        }

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || number < 0)
        {
            return false;
        }

        bytes = (long)(number * multiplier);
        return true;
    }

    private static readonly (string Suffix, long Factor)[] Suffixes =
    [
        ("tb", 1024L * 1024 * 1024 * 1024),
        ("gb", 1024L * 1024 * 1024),
        ("mb", 1024L * 1024),
        ("kb", 1024L),
        ("b", 1L),
    ];

    private static bool TryParseDateRange(string value, out RelativeDateRange range)
    {
        range = RelativeDateRange.Today;

        return value.ToLowerInvariant() switch
        {
            "today" => Set(RelativeDateRange.Today, out range),
            "thisweek" => Set(RelativeDateRange.ThisWeek, out range),
            "thismonth" => Set(RelativeDateRange.ThisMonth, out range),
            "thisyear" => Set(RelativeDateRange.ThisYear, out range),
            _ => false,
        };

        static bool Set(RelativeDateRange value, out RelativeDateRange target)
        {
            target = value;
            return true;
        }
    }
}
