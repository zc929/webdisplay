using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace WebDisplay.Services;

/// <summary>Application-owned UI text. Keys are the original Simplified Chinese strings.</summary>
public static partial class L
{
    internal readonly record struct Translation(string TraditionalChinese, string English);

    private static string _language = "zh-CN";
    private static readonly Dictionary<string, Translation> Entries = CreateDictionary();
    private static readonly Dictionary<string, string> JapaneseEntries = CreateAdditionalDictionary("ja-JP");
    private static readonly Dictionary<string, string> KoreanEntries = CreateAdditionalDictionary("ko-KR");

    public static IReadOnlyList<string> SupportedLanguages { get; } =
        Array.AsReadOnly(new[] { "zh-CN", "zh-TW", "en-US", "ja-JP", "ko-KR" });

    public static string Language => Volatile.Read(ref _language);

    public static void SetLanguage(string language) => Volatile.Write(ref _language, NormalizeLanguage(language));

    public static bool IsSupportedLanguage(string? language) =>
        language is "zh-CN" or "zh-TW" or "en-US" or "ja-JP" or "ko-KR";

    public static string NormalizeLanguage(string? language) => IsSupportedLanguage(language) ? language! : "zh-CN";

    public static string Text(string simplifiedChinese)
    {
        if (string.IsNullOrEmpty(simplifiedChinese)) return simplifiedChinese ?? string.Empty;
        string language = Language;
        if (language == "ja-JP" && JapaneseEntries.TryGetValue(simplifiedChinese, out string? japanese)) return japanese;
        if (language == "ko-KR" && KoreanEntries.TryGetValue(simplifiedChinese, out string? korean)) return korean;
        if (language == "zh-CN" || !Entries.TryGetValue(simplifiedChinese, out Translation entry))
            return simplifiedChinese;
        return language == "zh-TW" ? entry.TraditionalChinese : entry.English;
    }

    public static string Format(string simplifiedChineseFormat, params object[] args)
        => string.Format(CultureInfo.GetCultureInfo(Language), Text(simplifiedChineseFormat), args);

    public static bool HasTranslation(string simplifiedChinese) => Entries.ContainsKey(simplifiedChinese);

    /// <summary>Checks every supported translation for missing text and numbered format arguments.</summary>
    public static IReadOnlyList<string> GetTranslationIssues()
    {
        var issues = new List<string>();
        foreach (var pair in Entries)
        {
            string arguments = FormatArguments(pair.Key);
            void Check(string language, string? translated)
            {
                if (string.IsNullOrWhiteSpace(translated) || FormatArguments(translated) != arguments)
                    issues.Add(language + ": " + pair.Key);
            }
            Check("zh-TW", pair.Value.TraditionalChinese);
            Check("en-US", pair.Value.English);
            JapaneseEntries.TryGetValue(pair.Key, out string? japanese);
            KoreanEntries.TryGetValue(pair.Key, out string? korean);
            Check("ja-JP", japanese);
            Check("ko-KR", korean);
        }
        foreach (string key in JapaneseEntries.Keys)
            if (!Entries.ContainsKey(key)) issues.Add("ja-JP: unknown key " + key);
        foreach (string key in KoreanEntries.Keys)
            if (!Entries.ContainsKey(key)) issues.Add("ko-KR: unknown key " + key);
        return issues;
    }

    private static string FormatArguments(string text) => string.Join(",",
        Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")
            .Select(match => match.Groups[1].Value).Distinct().OrderBy(value => value, StringComparer.Ordinal));

    private static Dictionary<string, Translation> CreateDictionary()
    {
        var entries = new Dictionary<string, Translation>(StringComparer.Ordinal);
        AddSettingsTranslations(entries);
        AddApplicationTranslations(entries);
        AddSystemTranslations(entries);
        AddUpdateTranslations(entries);
        return entries;
    }

    private static Dictionary<string, string> CreateAdditionalDictionary(string language)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language == "ja-JP") AddJapaneseTranslations(entries);
        else if (language == "ko-KR") AddKoreanTranslations(entries);
        return entries;
    }

    static partial void AddSettingsTranslations(Dictionary<string, Translation> entries);
    static partial void AddApplicationTranslations(Dictionary<string, Translation> entries);
    static partial void AddSystemTranslations(Dictionary<string, Translation> entries);
    static partial void AddUpdateTranslations(Dictionary<string, Translation> entries);
    static partial void AddJapaneseTranslations(Dictionary<string, string> entries);
    static partial void AddKoreanTranslations(Dictionary<string, string> entries);
}
