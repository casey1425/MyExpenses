using System.Globalization;
using System.Text;

namespace MyExpenses.Data;

public static class ExpenseCsvExporter
{
    public static byte[] Create(IEnumerable<ExpenseRecord> expenses)
    {
        var csv = new StringBuilder("날짜,금액(원),카테고리,메모\r\n");

        foreach (var expense in expenses)
        {
            csv.Append(expense.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(expense.Amount.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(EscapeText(expense.Category))
                .Append(',')
                .Append(EscapeText(expense.Memo))
                .Append("\r\n");
        }

        // The BOM helps spreadsheet apps recognize Korean text as UTF-8.
        var contents = Encoding.UTF8.GetBytes(csv.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + contents.Length];
        preamble.CopyTo(result, 0);
        contents.CopyTo(result, preamble.Length);
        return result;
    }

    private static string EscapeText(string? value)
    {
        value ??= string.Empty;

        // Prevent spreadsheet apps from evaluating user-entered text as a formula.
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')
            value = "\t" + value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
