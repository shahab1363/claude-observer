namespace ClaudePermissionAnalyzer.Api.Exceptions;

/// <summary>
/// Exception thrown when configuration loading or validation fails.
/// </summary>
public class ConfigurationException : InvalidOperationException
{
    public ConfigurationException(string message) : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
