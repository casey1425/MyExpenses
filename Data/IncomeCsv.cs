using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace MyExpenses.Data;

public sealed record IncomeCsvRow(int RowNumber, DateTime Date, long Amount, string Source, string Memo);

public sealed record IncomeCsvParseResult(
    IReadOnlyList<IncomeCsvRow> Rows,
    IReadOnlyList<ExpenseCsvIssue> Issues);

public static class IncomeCsvExporter
{
    public const string HeaderLine = "날짜,금액(원),분류,메모";

    public static byte[] Create(IEnumerable<IncomeRecord> incomes)
    {
        var csv = new StringBuilder(HeaderLine).Append("\r\n");
        foreach (var income in incomes)
        {
            csv.Append(income.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(',').Append(income.Amount.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(ExpenseCsvExporter.EscapeText(income.Source))
                .Append(',').Append(ExpenseCsvExporter.EscapeText(income.Memo))
                .Append("\r\n");
        }

        return ExpenseCsvExporter.Utf8WithBom(csv.ToString());
    }
}

public static class IncomeCsvImporter
{
    private static readonly string[] Header = HeaderFields();

    public static IncomeCsvParseResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var rows = new List<IncomeCsvRow>();
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
            return new IncomeCsvParseResult(rows, [new ExpenseCsvIssue(1, "CSV 제목 행을 읽을 수 없습니다.")]);
        }

        if (actualHeader is null || !actualHeader.SequenceEqual(Header, StringComparer.Ordinal))
            return new IncomeCsvParseResult(rows,
                [new ExpenseCsvIssue(1, $"제목 행은 {IncomeCsvExporter.HeaderLine} 순서여야 합니다. 지출 CSV는 CSV 가져오기 화면에서 가져와 주세요.")]);

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            if (rowNumber > ExpenseCsvImporter.MaxRows + 1)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, $"한 번에 최대 {ExpenseCsvImporter.MaxRows:N0}건까지 가져올 수 있습니다."));
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

            if (fields is null || fields.Length != Header.Length)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, $"열이 {Header.Length}개여야 합니다."));
                continue;
            }

            if (!DateTime.TryParseExact(fields[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "날짜는 yyyy-MM-dd 형식이어야 합니다."));
                continue;
            }

            if (!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "금액은 1원 이상의 정수여야 합니다."));
                continue;
            }

            var source = ExpenseCsvImporter.RemoveExportProtection(fields[2]);
            if (!IncomeRecord.Sources.Contains(source))
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, $"분류는 {string.Join(", ", IncomeRecord.Sources)} 중 하나여야 합니다."));
                continue;
            }

            var memo = ExpenseCsvImporter.RemoveExportProtection(fields[3]).Trim();
            if (memo.Length > 100)
            {
                issues.Add(new ExpenseCsvIssue(rowNumber, "메모는 100자 이하여야 합니다."));
                continue;
            }

            rows.Add(new IncomeCsvRow(rowNumber, date.Date, amount, source, memo));
        }

        if (rows.Count == 0 && issues.Count == 0)
            issues.Add(new ExpenseCsvIssue(2, "가져올 수입 기록이 없습니다."));

        return new IncomeCsvParseResult(rows, issues);
    }

    private static string[] HeaderFields() => IncomeCsvExporter.HeaderLine.Split(',');
}
