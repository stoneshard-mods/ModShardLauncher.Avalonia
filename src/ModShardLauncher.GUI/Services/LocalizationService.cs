using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace ModShardLauncher.GUI.Services;

/// <summary>
/// A single translated string that raises change notifications when the UI culture changes.
/// XAML binds to <see cref="Value"/>, so switching language updates every visible label live.
/// </summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private readonly LocalizationService _service;

    internal LocalizedText(LocalizationService service, string key)
    {
        _service = service;
        Key = key;
    }

    public string Key { get; }

    public string Value => _service[Key];

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

/// <summary>
/// Standard .NET resource based localization (resx + satellite assemblies).
/// Translations live in <c>Resources/Strings.*.resx</c>; the neutral resource is English.
/// </summary>
public sealed class LocalizationService
{
    public static LocalizationService Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("ModShardLauncher.GUI.Resources.Strings", typeof(LocalizationService).Assembly);

    private readonly Dictionary<string, LocalizedText> _texts = new(StringComparer.Ordinal);

    private LocalizationService()
    {
    }

    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public event Action? LanguageChanged;

    public string this[string key]
    {
        get
        {
            try
            {
                return _resources.GetString(key, Culture) ?? key;
            }
            catch
            {
                return key;
            }
        }
    }

    /// <summary>Returns the cached, observable translation for <paramref name="key"/>.</summary>
    public LocalizedText Text(string key)
    {
        lock (_texts)
        {
            if (!_texts.TryGetValue(key, out LocalizedText? text))
            {
                text = new LocalizedText(this, key);
                _texts.Add(key, text);
            }

            return text;
        }
    }

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    public void SetLanguage(string cultureName)
    {
        Culture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;

        lock (_texts)
        {
            foreach (LocalizedText text in _texts.Values)
            {
                text.Refresh();
            }
        }

        LanguageChanged?.Invoke();
    }

    /// <summary>Maps the value stored in <c>Settings.json</c> to a resource culture.</summary>
    public static string CultureFor(string setting) => setting switch
    {
        "Chinese" or "zh-Hans" or "zh-cn" => "zh-Hans",
        "Russian" or "Русский" or "ru" => "ru",
        _ => "en",
    };

    /// <summary>
    /// Picks a supported resource culture from the Windows display language.
    /// Only English, Simplified Chinese and Russian resources exist; anything else falls back
    /// to English.
    /// </summary>
    public static string DetectSystemCulture()
    {
        for (CultureInfo? culture = CultureInfo.CurrentUICulture;
             culture is not null && !string.IsNullOrEmpty(culture.Name);
             culture = culture.Parent)
        {
            string name = culture.Name.ToLowerInvariant();
            if (name.StartsWith("zh", StringComparison.Ordinal))
            {
                return "zh-Hans";
            }

            if (name.StartsWith("ru", StringComparison.Ordinal))
            {
                return "ru";
            }

            if (name.StartsWith("en", StringComparison.Ordinal))
            {
                return "en";
            }
        }

        return "en";
    }
}

/// <summary>XAML sugar: <c>Text="{loc:Tr AppTitle}"</c>.</summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedText.Value))
        {
            Source = LocalizationService.Instance.Text(Key),
            Mode = BindingMode.OneWay,
        };
}
