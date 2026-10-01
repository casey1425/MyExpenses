namespace MyExpenses.Data;

public static class ExpenseCategories
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "식비", "카페", "교통", "쇼핑", "생활", "기타" });

    public static bool IsSupported(string category) => All.Contains(category, StringComparer.Ordinal);

    public static string Icon(string category) => category switch
    {
        "식비" => "🍽",
        "카페" => "☕",
        "교통" => "🚆",
        "쇼핑" => "🛍",
        "생활" => "🏠",
        _ => "▦"
    };
}
