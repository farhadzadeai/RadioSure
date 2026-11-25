using System.Windows;

namespace RadioClassic.Core;

/// <summary>
/// Localization helper. UI strings live in Lang/Strings.*.xaml resource dictionaries and are
/// referenced from XAML with DynamicResource, so the language can be switched at runtime.
/// </summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("fa", "فارسی")];

    private static ResourceDictionary? _current;

    public static string CurrentLanguage { get; private set; } = "en";
    public static event Action? LanguageChanged;

    public static string T(string key) => Application.Current?.TryFindResource(key) as string ?? key;

    public static string T(string key, params object?[] args)
    {
        try { return string.Format(T(key), args); }
        catch (FormatException) { return T(key); }
    }

    public static void SetLanguage(string code)
    {
        if (!Languages.Any(l => l.Code == code)) code = "en";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"/RadioClassic;component/Lang/Strings.{code}.xaml", UriKind.Relative)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        CurrentLanguage = code;
        LanguageChanged?.Invoke();
    }
}
