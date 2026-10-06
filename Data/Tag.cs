using System.Text;

namespace MyExpenses.Data;

public sealed class Tag
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;

    // 사용자가 입력한 표시 이름입니다.
    public string Name { get; set; } = string.Empty;

    // 대소문자를 무시한 비교용 이름입니다. 같은 사용자 안에서 중복될 수 없습니다.
    public string NormalizedName { get; set; } = string.Empty;

    public List<ExpenseTag> ExpenseLinks { get; set; } = [];
}

public sealed class ExpenseTag
{
    public int ExpenseId { get; set; }
    public ExpenseRecord? Expense { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}

public sealed record TagUsage(int Id, string Name, int ExpenseCount, long Amount);

// 태그 이름 규칙과 입력 문자열 해석입니다. 화면·CSV·백업이 같은 규칙을 씁니다.
public static class TagNames
{
    public const int MaxLength = 20;
    public const int MaxPerExpense = 5;
    public const int MaxPerOwner = 100;

    // 앞의 #과 앞뒤·중복 공백을 정리합니다. 규칙에 맞지 않으면 null입니다.
    public static string? Clean(string? value)
    {
        if (value is null) return null;
        var text = value.Trim().TrimStart('#', '＃').Trim();
        var builder = new StringBuilder(text.Length);
        var previousSpace = false;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) || ch is ',' or ';' or '，' or '；') return null;
            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace) builder.Append(' ');
                previousSpace = true;
                continue;
            }
            previousSpace = false;
            builder.Append(ch);
        }
        var result = builder.ToString();
        return result.Length is >= 1 and <= MaxLength ? result : null;
    }

    public static string Key(string name) => name.ToUpperInvariant();

    // "여행, #경조사 ,여행" → ["여행", "경조사"]. 비어 있는 항목은 무시하고 같은 태그는 한 번만 둡니다.
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var names = new List<string>();
        var keys = new HashSet<string>();
        foreach (var piece in text.Split([',', '，', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(piece.Trim().TrimStart('#', '＃'))) continue;
            var name = Clean(piece) ?? throw new ArgumentException($"태그는 {MaxLength}자 이하이며 쉼표·세미콜론을 쓸 수 없습니다: ‘{piece.Trim()}’");
            if (keys.Add(Key(name))) names.Add(name);
        }
        if (names.Count > MaxPerExpense) throw new ArgumentException($"태그는 지출 하나에 {MaxPerExpense}개까지 붙일 수 있습니다.");
        return names;
    }

    public static string Join(IEnumerable<string> names) => string.Join(", ", names);
}
