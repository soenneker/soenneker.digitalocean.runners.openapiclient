using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Soenneker.Extensions.String;
using Soenneker.Git.Util.Abstract;
using Soenneker.DigitalOcean.Runners.OpenApiClient.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;
using Soenneker.Utils.Process.Abstract;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Kiota.Util.Abstract;
using Soenneker.OpenApi.Fixer.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Yaml.Abstract;
using System.Collections.Generic;

namespace Soenneker.DigitalOcean.Runners.OpenApiClient.Utils;

///<inheritdoc cref="IFileOperationsUtil"/>
public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IConfiguration _configuration;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IProcessUtil _processUtil;
    private readonly IKiotaUtil _kiotaUtil;
    private readonly IOpenApiFixer _openApiFixer;
    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IYamlUtil _yamlUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IConfiguration configuration, IGitUtil gitUtil, IDotnetUtil dotnetUtil,
        IProcessUtil processUtil, IFileUtil fileUtil, IDirectoryUtil directoryUtil, IKiotaUtil kiotaUtil, IOpenApiFixer openApiFixer, IYamlUtil yamlUtil)
    {
        _logger = logger;
        _configuration = configuration;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _processUtil = processUtil;
        _kiotaUtil = kiotaUtil;
        _openApiFixer = openApiFixer;
        _fileUtil = fileUtil;
        _directoryUtil = directoryUtil;
        _yamlUtil = yamlUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string gitDirectory = await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}", cancellationToken: cancellationToken);

        string specificationRepositoryUrl = _configuration["DigitalOcean:SpecificationRepositoryUrl"] ?? "https://github.com/digitalocean/openapi";
        string specificationDirectory = await _gitUtil.CloneToTempDirectory(specificationRepositoryUrl, cancellationToken: cancellationToken);

        string yamlFilePath = Path.Combine(gitDirectory, "openapi.yaml");
        await _fileUtil.DeleteIfExists(yamlFilePath, cancellationToken: cancellationToken);

        string npmExecutable = ResolveNpmExecutable();
        await _processUtil.Start(npmExecutable, specificationDirectory, "ci --ignore-scripts", waitForExit: true, cancellationToken: cancellationToken);
        await _processUtil.Start(npmExecutable, specificationDirectory,
            $"run bundle -- specification/DigitalOcean-public.v2.yaml -o \"{yamlFilePath}\"", waitForExit: true, cancellationToken: cancellationToken);

        if (!await _fileUtil.Exists(yamlFilePath, cancellationToken))
            throw new InvalidOperationException("DigitalOcean OpenAPI bundle was not created.");

        string targetFilePath = Path.Combine(gitDirectory, "openapi.json");
        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);
        await _yamlUtil.SaveAsJson(yamlFilePath, targetFilePath, cancellationToken: cancellationToken);
        await NormalizeKafkaIntegerLimits(targetFilePath, cancellationToken);

        string fixedFilePath = Path.Combine(gitDirectory, "openapi.fixed.json");
        await _fileUtil.DeleteIfExists(fixedFilePath, cancellationToken: cancellationToken);
        await _openApiFixer.Fix(targetFilePath, fixedFilePath, cancellationToken).NoSync();

        await _kiotaUtil.EnsureInstalled(cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src", Constants.Library);

        await DeleteAllExceptCsproj(srcDirectory, cancellationToken);

        await _kiotaUtil.Generate(fixedFilePath, "DigitalOceanOpenApiClient", Constants.Library, gitDirectory, cancellationToken).NoSync();

        await BuildAndPush(gitDirectory, cancellationToken).NoSync();
    }

    private static async ValueTask NormalizeKafkaIntegerLimits(string openApiPath, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(openApiPath, cancellationToken);
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("DigitalOcean OpenAPI JSON is empty.");

        JsonObject properties = root["components"]?["schemas"]?["KafkaTopicConfig"]?["properties"] as JsonObject
            ?? throw new InvalidOperationException("DigitalOcean KafkaTopicConfig schema was not found.");

        string[] propertyNames = ["flush_messages", "flush_ms", "max_compaction_lag_ms"];

        foreach (string propertyName in propertyNames)
        {
            JsonObject property = properties[propertyName] as JsonObject
                ?? throw new InvalidOperationException($"DigitalOcean KafkaTopicConfig.{propertyName} was not found.");

            property["format"] = "int64";
            property["default"] = long.MaxValue;
            property["example"] = long.MaxValue;
        }

        await File.WriteAllTextAsync(openApiPath, root.ToJsonString(new JsonSerializerOptions {WriteIndented = false}), cancellationToken);
    }

    private static string ResolveNpmExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return "npm";

        string? path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(static directory => Path.Combine(directory.Trim('"'), "npm.cmd"))
            .FirstOrDefault(File.Exists);

        return path ?? "npm.cmd";
    }

    public async ValueTask DeleteAllExceptCsproj(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!(await _directoryUtil.Exists(directoryPath, cancellationToken)))
        {
            _logger.LogWarning("Directory does not exist: {DirectoryPath}", directoryPath);
            return;
        }

        try
        {
            // Delete all files except .csproj
            List<string> files = await _directoryUtil.GetFilesByExtension(directoryPath, "", true, cancellationToken);
            foreach (string file in files)
            {
                if (!file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _fileUtil.Delete(file, ignoreMissing: true, log: false, cancellationToken);
                        _logger.LogInformation("Deleted file: {FilePath}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file: {FilePath}", file);
                    }
                }
            }

            // Delete all empty subdirectories
            List<string> dirs = await _directoryUtil.GetAllDirectoriesRecursively(directoryPath, cancellationToken);
            foreach (string dir in dirs.OrderByDescending(d => d.Length)) // Sort by depth to delete from deepest first
            {
                try
                {
                    List<string> dirFiles = await _directoryUtil.GetFilesByExtension(dir, "", false, cancellationToken);
                    List<string> subDirs = await _directoryUtil.GetAllDirectories(dir, cancellationToken);
                    if (dirFiles.Count == 0 && subDirs.Count == 0)
                    {
                        await _directoryUtil.Delete(dir, cancellationToken);
                        _logger.LogInformation("Deleted empty directory: {DirectoryPath}", dir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete directory: {DirectoryPath}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cleaning the directory: {DirectoryPath}", directoryPath);
        }
    }

    private async ValueTask BuildAndPush(string gitDirectory, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", Constants.Library, $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
        {
            _logger.LogError("Build was not successful, exiting...");
            return;
        }

        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        await _gitUtil.CommitAndPush(gitDirectory, "Automated update", gitHubToken, name, email, cancellationToken);
    }
}
