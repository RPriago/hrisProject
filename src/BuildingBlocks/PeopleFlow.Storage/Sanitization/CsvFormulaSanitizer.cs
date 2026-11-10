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

        // Count leading spaces (preserve original whitespace if non-formula)
        var leadingSpaceCount = 0;
        while (leadingSpaceCount < input.Length && input[leadingSpaceCount] == ' ')
        {
            leadingSpaceCount++;
        }

        if (leadingSpaceCount == input.Length)
        {
            // All spaces
            return input;
        }

        var firstNonSpace = input[leadingSpaceCount];
        var candidate = input;

        if (FormulaTriggers.Contains(firstNonSpace))
        {
            // Only '+' and '-' can be numeric literals.
            // '=', '@', '\t', '\r' are ALWAYS formulas and must be prepended with '''.
            var isNumeric = (firstNonSpace == '+' || firstNonSpace == '-')
                && IsNumericLiteral(input[leadingSpaceCount..]);

            if (!isNumeric)
            {
                candidate = "'" + input[leadingSpaceCount..];
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
