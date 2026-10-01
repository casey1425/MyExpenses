using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace MyExpenses.Data;

public sealed record ExpenseCsvRow(int RowNumber, DateTime Date, long Amount, string Category, string Memo,
    string PaymentMethodName = "", string PaymentMethodType = "");

public sealed record ExpenseCsvIssue(int RowNumber, string Message);

public sealed record ExpenseCsvParseResult(
    IReadOnlyList<ExpenseCsvRow> Rows,
    IReadOnlyList<ExpenseCsvIssue> Issues);

public static class ExpenseCsvImporter
{
    public const int MaxRows = 1_000;
    private static readonly string[] Header = ["날짜", "금액(원)", "카테고리", "메모"];
    private static readonly string[] PaymentHeader = ["날짜", "금액(원)", "카테고리", "메모", "결제수단", "결제유형"];
    private static readonly HashSet<string> Categories = ["식비", "카페", "교통", "쇼핑", "생활", "기타"];

    public static ExpenseCsvParseResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var rows = new List<ExpenseCsvRow>();
        var issues = new List<ExpenseCsvIssue>();
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        string[]? actualHeader;
        try
        {
            actualHeader = parser.ReadFields();
        }
        catch (MalformedLineException)
        {
            return new ExpenseCsvParseResult(rows, [new ExpenseCsvIssue(1, "CSV 제목 행을 읽을 수 없습니다.")]);
        }

        if (actualHeader is null || (!actualHeader.SequenceEqual(Header, StringComparer.Ordinal) && !actualHeader.SequenceEqual(PaymentHeader, StringComparer.Ordinal)))
            return new ExpenseCsvParseResult(rows,
                [new ExpenseCsvIssue(1, "제목 행은 날짜,금액(원),카테고리,메모 또는 여기에 결제수단,결제유형을 추가한 순서여야 합니다.")]);

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            if (rowNumber > MaxRows + 1)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, $"한 번에 최대 {MaxRows:N0}건까지 가져올 수 있습니다."));
                break;
            }

            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "따옴표가 올바르게 닫히지 않았습니다."));
                break;
            }

            if (fields is null || fields.Length != actualHeader.Length)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, $"열이 {actualHeader.Length}개여야 합니다."));
                continue;
            }

            if (!DateTime.TryParseExact(fields[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "날짜는 yyyy-MM-dd 형식이어야 합니다."));
                continue;
            }

            if (!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "금액은 1원 이상의 정수여야 합니다."));
                continue;
            }

            if (!Categories.Contains(fields[2]))
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "지원하지 않는 카테고리입니다."));
                continue;
            }

            var memo = RemoveExportProtection(fields[3]);
            if (memo.Length > 100)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "메모는 100자 이하여야 합니다."));
                continue;
            }

            var methodName = fields.Length == 6 ? RemoveExportProtection(fields[4]).Trim() : "";
            var methodType = fields.Length == 6 ? fields[5] : "";
            if (methodName.Length > 50 || (methodName == "" ? methodType != "" : !PaymentMethod.Types.Contains(methodType)))
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "결제수단 이름은 50자 이하이고 결제유형과 함께 입력해야 합니다. 미지정은 두 열을 비워 주세요."));
                continue;
            }
            rows.Add(new ExpenseCsvRow(rowNumber, date.Date, amount, fields[2], memo, methodName, methodType));
        }

        if (rows.Count == 0 && issues.Count == 0)
            issues.Add(new ExpenseCsvIssue(2, "가져올 지출 기록이 없습니다."));

        return new ExpenseCsvParseResult(rows, issues);
    }

    private static string RemoveExportProtection(string value)
    {
        if (value.Length > 1 && value[0] == '\t' &&
            value[1..].TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')
            return value[1..];

        return value;
    }
}
