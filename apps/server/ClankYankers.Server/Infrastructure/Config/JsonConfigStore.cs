using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using System.Text.Json;

namespace ClankYankers.Server.Infrastructure.Config;

public sealed class JsonConfigStore(IHostEnvironment environment, ILogger<JsonConfigStore> logger) : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken)
    {
        var path = GetConfigPath();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                var defaultConfig = AppConfig.CreateDefault();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var createStream = File.Create(path);
                await JsonSerializer.SerializeAsync(createStream, defaultConfig, SerializerOptions, cancellationToken);
                return defaultConfig;
            }

            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, SerializerOptions, cancellationToken);
            if (config is null)
            {
                logger.LogWarning("Config file at {ConfigPath} was empty. Recreating defaults.", path);
                return AppConfig.CreateDefault();
            }

            return config;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetConfigPath() =>
        Path.Combine(environment.ContentRootPath, "..", "..", "..", "data", "config.json");
}
