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

    public static string Language => Volatile.Read(ref _language);

    public static void SetLanguage(string language) => Volatile.Write(ref _language, NormalizeLanguage(language));

    public static string NormalizeLanguage(string? language) => language switch
    {
        "zh-TW" => "zh-TW",
        "en-US" => "en-US",
        _ => "zh-CN"
    };

    public static string Text(string simplifiedChinese)
    {
        if (string.IsNullOrEmpty(simplifiedChinese)) return simplifiedChinese ?? string.Empty;
        if (Language == "zh-CN" || !Entries.TryGetValue(simplifiedChinese, out Translation entry))
            return simplifiedChinese;
        return Language == "zh-TW" ? entry.TraditionalChinese : entry.English;
    }

    public static string Format(string simplifiedChineseFormat, params object[] args)
        => string.Format(CultureInfo.GetCultureInfo(Language), Text(simplifiedChineseFormat), args);

    public static bool HasTranslation(string simplifiedChinese) => Entries.ContainsKey(simplifiedChinese);

    /// <summary>Checks that both translations exist and preserve numbered format arguments.</summary>
    public static IReadOnlyList<string> GetTranslationIssues()
    {
        var issues = new List<string>();
        foreach (var pair in Entries)
        {
            string arguments = FormatArguments(pair.Key);
            if (string.IsNullOrWhiteSpace(pair.Value.TraditionalChinese) ||
                FormatArguments(pair.Value.TraditionalChinese) != arguments)
                issues.Add("zh-TW: " + pair.Key);
            if (string.IsNullOrWhiteSpace(pair.Value.English) ||
                FormatArguments(pair.Value.English) != arguments)
                issues.Add("en-US: " + pair.Key);
        }
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
        return entries;
    }

    static partial void AddSettingsTranslations(Dictionary<string, Translation> entries);
    static partial void AddApplicationTranslations(Dictionary<string, Translation> entries);
    static partial void AddSystemTranslations(Dictionary<string, Translation> entries);
}
