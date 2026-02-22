using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class PromptTemplateServiceTests : IDisposable
{
    private readonly string _testPromptsDir;
    private readonly PromptTemplateService _service;

    public PromptTemplateServiceTests()
    {
        _testPromptsDir = Path.Combine(Path.GetTempPath(), $"test-prompts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testPromptsDir);

        // Create some test templates
        File.WriteAllText(Path.Combine(_testPromptsDir, "bash-prompt.txt"), "Analyze bash command: {COMMAND}");
        File.WriteAllText(Path.Combine(_testPromptsDir, "file-read-prompt.txt"), "Analyze file read: {FILE_PATH}");

        var mockLogger = new Mock<ILogger<PromptTemplateService>>();
        _service = new PromptTemplateService(_testPromptsDir, mockLogger.Object);
    }

    [Fact]
    public void GetTemplate_ShouldReturnExistingTemplate()
    {
        // Act
        var template = _service.GetTemplate("bash-prompt.txt");

        // Assert
        Assert.NotNull(template);
        Assert.Contains("{COMMAND}", template);
    }

    [Fact]
    public void GetTemplate_ShouldReturnNull_ForMissingTemplate()
    {
        // Act
        var template = _service.GetTemplate("nonexistent.txt");

        // Assert
        Assert.Null(template);
    }

    [Fact]
    public void GetTemplate_ShouldAddTxtExtension_WhenMissing()
    {
        // Act
        var template = _service.GetTemplate("bash-prompt");

        // Assert
        Assert.NotNull(template);
        Assert.Contains("{COMMAND}", template);
    }

    [Fact]
    public void GetAllTemplates_ShouldReturnAllLoadedTemplates()
    {
        // Act
        var templates = _service.GetAllTemplates();

        // Assert
        Assert.Equal(2, templates.Count);
        Assert.True(templates.ContainsKey("bash-prompt.txt"));
        Assert.True(templates.ContainsKey("file-read-prompt.txt"));
    }

    [Fact]
    public void GetTemplateNames_ShouldReturnSortedNames()
    {
        // Act
        var names = _service.GetTemplateNames();

        // Assert
        Assert.Equal(2, names.Count);
        Assert.Equal("bash-prompt.txt", names[0]);
        Assert.Equal("file-read-prompt.txt", names[1]);
    }

    [Fact]
    public void SaveTemplate_ShouldPersistTemplate()
    {
        // Act
        var result = _service.SaveTemplate("new-template.txt", "New content: {TOOL_NAME}");

        // Assert
        Assert.True(result);
        var loaded = _service.GetTemplate("new-template.txt");
        Assert.NotNull(loaded);
        Assert.Contains("{TOOL_NAME}", loaded);
    }

    [Fact]
    public void SaveTemplate_ShouldRejectPathTraversal()
    {
        // Act
        var result = _service.SaveTemplate("../evil.txt", "malicious content");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SaveTemplate_ShouldRejectEmptyName()
    {
        // Act
        var result = _service.SaveTemplate("", "content");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetTemplate_ShouldReturnNull_ForEmptyName()
    {
        // Act
        var template = _service.GetTemplate("");

        // Assert
        Assert.Null(template);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testPromptsDir))
                Directory.Delete(_testPromptsDir, true);
        }
        catch { }
    }
}
