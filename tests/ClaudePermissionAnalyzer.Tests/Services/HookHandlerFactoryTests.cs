using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class HookHandlerFactoryTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly HookHandlerFactory _factory;
    private readonly string _testStorageDir;
    private readonly SessionManager _sessionManager;

    public HookHandlerFactoryTests()
    {
        _testStorageDir = Path.Combine(Path.GetTempPath(), $"test-factory-{Guid.NewGuid():N}");
        var promptsDir = Path.Combine(Path.GetTempPath(), $"test-prompts-{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        _serviceProvider = services.BuildServiceProvider();

        _sessionManager = new SessionManager(_testStorageDir, 50, new MemoryCache(new MemoryCacheOptions()));
        var promptService = new PromptTemplateService(promptsDir, _serviceProvider.GetRequiredService<ILogger<PromptTemplateService>>());

        var configManager = new ConfigurationManager(new Configuration());
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var llmClientProvider = new LLMClientProvider(
            configManager,
            loggerFactory,
            _serviceProvider.GetRequiredService<ILogger<LLMClientProvider>>(),
            httpClientFactory.Object);

        _factory = new HookHandlerFactory(
            _serviceProvider,
            llmClientProvider,
            promptService,
            _sessionManager,
            _serviceProvider.GetRequiredService<ILogger<HookHandlerFactory>>());
    }

    [Theory]
    [InlineData("llm-analysis", typeof(LLMAnalysisHandler))]
    [InlineData("llm-validation", typeof(LLMAnalysisHandler))]
    [InlineData("log-only", typeof(LogOnlyHandler))]
    [InlineData("context-injection", typeof(ContextInjectionHandler))]
    [InlineData("custom-logic", typeof(CustomLogicHandler))]
    public void Create_ShouldReturnCorrectHandlerType(string mode, Type expectedType)
    {
        // Act
        var handler = _factory.Create(mode);

        // Assert
        Assert.IsType(expectedType, handler);
    }

    [Fact]
    public void Create_ShouldThrow_ForUnsupportedMode()
    {
        // Act & Assert
        Assert.Throws<NotSupportedException>(() => _factory.Create("unsupported-mode"));
    }

    [Fact]
    public void Create_ShouldHandleNullPromptTemplate()
    {
        // Act
        var handler = _factory.Create("llm-analysis", null);

        // Assert
        Assert.IsType<LLMAnalysisHandler>(handler);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_testStorageDir))
                Directory.Delete(_testStorageDir, true);
        }
        catch { }
    }
}
