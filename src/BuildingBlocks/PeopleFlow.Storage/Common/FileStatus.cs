namespace PeopleFlow.Storage.Common;

/// <summary>
/// Status state machine for quarantined, verified, or infected files.
/// </summary>
public static class FileStatus
{
    public const string Quarantined = "Quarantined";
    public const string Scanning = "Scanning";
    public const string Available = "Available";
    public const string RejectedMalware = "RejectedMalware";
    public const string RejectedInfected = "RejectedInfected";
    public const string RejectedInvalidType = "RejectedInvalidType";
    public const string DeletedByRetention = "DeletedByRetention";
}
