using System.Globalization;

namespace PeopleFlow.Storage.Sanitization;

/// <summary>
/// Implements CSV cell formula injection sanitization and RFC 4180 escaping.
/// Prevents DDE/formula execution in Excel, LibreOffice, and Google Sheets.
/// </summary>
public class CsvFormulaSanitizer : ICsvFormulaSanitizer
{
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    public string SanitizeCell(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Advance past non-trigger whitespace/control/BOM characters (e.g. ' ', '\n', '\v', '\f', NBSP, U+FEFF)
        // while stopping immediately if an explicit formula trigger ('\t', '\r', '=', '+', '-', '@') is reached.
        var leadingIdx = 0;
        while (leadingIdx < input.Length &&
               !FormulaTriggers.Contains(input[leadingIdx]) &&
               (input[leadingIdx] <= ' ' || char.IsWhiteSpace(input[leadingIdx]) || input[leadingIdx] == '\uFEFF'))
        {
            leadingIdx++;
        }

        if (leadingIdx == input.Length)
        {
            return input;
        }

        var firstTriggerCandidate = input[leadingIdx];
        var candidate = input;

        if (FormulaTriggers.Contains(firstTriggerCandidate))
        {
            // Only '+' and '-' can be numeric literals.
            // '=', '@', '\t', '\r' are ALWAYS formulas and must be prepended with '''.
            var slice = input[leadingIdx..];
            var isNumeric = (firstTriggerCandidate == '+' || firstTriggerCandidate == '-')
                && IsNumericLiteral(slice);

            if (!isNumeric)
            {
                candidate = "'" + slice;
            }
        }

        // RFC 4180 escaping: if cell contains quotes, commas, or newlines, enclose in double quotes and double internal quotes
        if (candidate.Contains('"') || candidate.Contains(',') || candidate.Contains('\r') || candidate.Contains('\n'))
        {
            var escaped = candidate.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        return candidate;
    }

    private static bool IsNumericLiteral(string value)
    {
        if (string.IsNullOrEmpty(value) || (value[0] != '+' && value[0] != '-'))
        {
            return false;
        }

        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out _);
    }
}
