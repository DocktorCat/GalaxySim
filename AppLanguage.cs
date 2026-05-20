namespace GalaxySim;

public enum AppLanguage
{
    Russian,
    English
}

public static class AppLanguageState
{
    public static AppLanguage Current { get; set; } = AppLanguage.Russian;

    public static bool IsEnglish => Current == AppLanguage.English;
}
