using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class MacDevelopmentPbpSecretProvisionPostProcess : IPostprocessBuildWithReport
{
    private const string SourceSecretRelativePath = "UserSettings/pbp-api-key.staging";
    private const string ProvisionedSecretFileName = "pbp-api-key.staging";

    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report == null)
        {
            return;
        }

        if (report.summary.platform != BuildTarget.StandaloneOSX)
        {
            return;
        }

        if ((report.summary.options & BuildOptions.Development) == 0)
        {
            return;
        }

        string sourcePath = GetProjectRelativePath(SourceSecretRelativePath);
        string destinationPath = GetProvisionedSecretPath(report.summary.outputPath);
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
        {
            DeleteProvisionedSecretIfPresent(destinationPath);
            Debug.LogError(
                $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} " +
                $"destinationPath={destinationPath} exists=unknown empty=unknown value=missing status=path-resolution-failed");
            return;
        }

        try
        {
            if (!File.Exists(sourcePath))
            {
                DeleteProvisionedSecretIfPresent(destinationPath);
                Debug.LogError(
                    $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} " +
                    $"destinationPath={destinationPath} exists=false empty=unknown value=missing");
                return;
            }

            string contents = File.ReadAllText(sourcePath);
            string normalized = NormalizeSecretContents(contents);
            if (string.IsNullOrEmpty(normalized))
            {
                DeleteProvisionedSecretIfPresent(destinationPath);
                Debug.LogError(
                    $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} " +
                    $"destinationPath={destinationPath} exists=true empty=true value=missing");
                return;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                DeleteProvisionedSecretIfPresent(destinationPath);
                Debug.LogError(
                    $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} " +
                    $"destinationPath={destinationPath} exists=unknown empty=unknown value=missing status=destination-directory-missing");
                return;
            }

            Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            string provisioned = NormalizeSecretContents(File.ReadAllText(destinationPath));
            if (string.IsNullOrEmpty(provisioned))
            {
                DeleteProvisionedSecretIfPresent(destinationPath);
                Debug.LogError(
                    $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} " +
                    $"destinationPath={destinationPath} exists=true empty=true value=missing status=copy-produced-empty-file");
                return;
            }

            Debug.Log(
                $"PBp Mac DEVELOPMENT_BUILD secret provisioned: sourcePath={sourcePath} " +
                $"destinationPath={destinationPath} exists={File.Exists(destinationPath)} " +
                $"empty={string.IsNullOrEmpty(provisioned)} value={DescribeSecretCandidate(provisioned)}");
        }
        catch (Exception ex)
        {
            DeleteProvisionedSecretIfPresent(destinationPath);
            Debug.LogError(
                $"PBp Mac DEVELOPMENT_BUILD secret provisioning failed: sourcePath={sourcePath} destinationPath={destinationPath} " +
                $"exceptionType={ex.GetType().Name} message={ex.Message}");
        }
    }

    private static string GetProjectRelativePath(string relativePath)
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return Path.Combine(projectRoot, relativePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetProvisionedSecretPath(string appBundlePath)
    {
        if (string.IsNullOrWhiteSpace(appBundlePath))
        {
            return string.Empty;
        }

        return Path.Combine(appBundlePath, "Contents", "Resources", ProvisionedSecretFileName);
    }

    private static void DeleteProvisionedSecretIfPresent(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string NormalizeSecretContents(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return string.Empty;
        }

        string[] lines = contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
            {
                line = line.Substring(0, commentIndex);
            }

            string normalized = string.IsNullOrWhiteSpace(line) ? string.Empty : line.Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static string DescribeSecretCandidate(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return "missing";
        }

        string hash = Hash128.Compute(candidate).ToString();
        string fingerprint = hash.Length <= 8 ? hash : hash.Substring(0, 8);
        return $"present(len={candidate.Length},fp={fingerprint})";
    }
}
