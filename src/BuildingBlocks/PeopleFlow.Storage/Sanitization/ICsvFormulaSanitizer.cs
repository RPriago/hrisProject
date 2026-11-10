namespace PeopleFlow.Storage.Sanitization;

/// <summary>
/// Sanitizes CSV cells against formula injection (OWASP) and formats according to RFC 4180.
/// </summary>
public interface ICsvFormulaSanitizer
{
    string SanitizeCell(string? input);
}
