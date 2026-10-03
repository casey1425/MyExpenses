using System.Globalization;
using System.Text;

namespace MyExpenses.Data;

public static class ExpenseCsvExporter
{
    public static byte[] Create(IEnumerable<ExpenseRecord> expenses)
    {
        var csv = new StringBuilder("날짜,금액(원),카테고리,메모,결제수단,결제유형\r\n");

        foreach (var expense in expenses)
        {
            csv.Append(expense.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(expense.Amount.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(EscapeText(expense.Category))
                .Append(',')
                .Append(EscapeText(expense.Memo))
                .Append(',').Append(EscapeText(expense.PaymentMethod?.Name))
                .Append(',').Append(EscapeText(expense.PaymentMethod?.Type))
                .Append("\r\n");
        }

        return Utf8WithBom(csv.ToString());
    }

    // The BOM helps spreadsheet apps recognize Korean text as UTF-8.
    internal static byte[] Utf8WithBom(string text)
    {
        var contents = Encoding.UTF8.GetBytes(text);
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + contents.Length];
        preamble.CopyTo(result, 0);
        contents.CopyTo(result, preamble.Length);
        return result;
    }

    internal static string EscapeText(string? value)
    {
        value ??= string.Empty;

        // Prevent spreadsheet apps from evaluating user-entered text as a formula.
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')
            value = "\t" + value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
