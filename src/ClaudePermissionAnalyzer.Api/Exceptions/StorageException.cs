namespace ClaudePermissionAnalyzer.Api.Exceptions;

/// <summary>
/// Exception thrown when session or configuration storage operations fail.
/// </summary>
public class StorageException : InvalidOperationException
{
    public StorageException(string message) : base(message)
    {
    }

    public StorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
