using System.Text.Json;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Json;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Replace;

/// <summary>替换引擎（04 §4.9）：备份 → 逐文件替换（exclude 跳过）→ deleteOrphans / delta 删除清单（preserve 保留）→ 清理。
/// Windows 占用冲突用 rename-replace + 重试缓解（超过 replaceRetryCount → 0x1006）。</summary>
public sealed class FileReplacer(UpdaterTaskFile task, UpdaterLog log)
{
    private readonly bool _caseSensitive = GlobMatcher.DefaultCaseSensitive;

    public BackupManifest Apply(string sourceRoot, string targetRoot, IReadOnlyList<string>? deletions = null)
    {
        var timestamp = DateTimeOffset.Now;
        var backupDirectory = task.Install.BackupPath ?? UpdaterPaths.DefaultBackupDirectory(task.AppId ?? "", timestamp);
        var replaceAll = string.Equals(task.Install.Strategy, ReplaceStrategies.Replace, StringComparison.OrdinalIgnoreCase);

        var sourceFiles = EnumerateRelativeFiles(sourceRoot).ToList();
        var replaceSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in sourceFiles)
        {
            if (IsExcluded(relative))
            {
                log.Debug($"exclude 跳过：{relative}");
                continue;
            }

            replaceSet.Add(relative);
        }

        // 删除集：replace 策略或 deleteOrphans → 目标中包外文件；delta → delete.json 清单
        var deleteSet = new HashSet<string>(StringComparer.Ordinal);
        if (replaceAll || task.Install.DeleteOrphans)
        {
            foreach (var existing in EnumerateRelativeFiles(targetRoot))
            {
                if (IsWithin(backupDirectory, targetRoot, existing) || IsExcluded(existing) || IsPreserved(existing))
                {
                    continue;
                }

                if (!replaceSet.Contains(existing))
                {
                    deleteSet.Add(existing);
                }
            }
        }

        foreach (var deletion in deletions ?? [])
        {
            var relative = Normalize(deletion);
            if (relative.Length == 0 || IsExcluded(relative) || IsPreserved(relative) || replaceSet.Contains(relative))
            {
                continue;
            }

            // 防御：清单路径越界（../）视为包损坏，直接拒绝
            EnsureWithin(targetRoot, Combine(targetRoot, relative));
            deleteSet.Add(relative);
        }

        var manifest = new BackupManifest { BackupDirectory = backupDirectory };
        var shouldBackup = task.Install.BackupBeforeReplace;

        // 备份（替换前）——备份失败 → 0x1005
        foreach (var relative in replaceSet.Where(relative => File.Exists(Combine(targetRoot, relative))).Concat(
                     deleteSet.Where(relative => File.Exists(Combine(targetRoot, relative)))))
        {
            if (!shouldBackup)
            {
                break;
            }

            var source = Combine(targetRoot, relative);
            var destination = Combine(backupDirectory, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                manifest.BackedUpFiles.Add(relative);
            }
            catch (Exception exception)
            {
                throw new UpdaterFailureException(ExitCodes.BackupFailed, $"备份失败：{relative}（{exception.Message}）",
                    exception);
            }
        }

        WriteManifest(manifest);

        // 替换与删除阶段：失败时携带回滚清单向上抛出（备份已落盘，可回滚）
        try
        {
            foreach (var relative in replaceSet)
            {
                var source = Combine(sourceRoot, relative);
                var destination = Combine(targetRoot, relative);
                EnsureWithin(targetRoot, destination);
                var existed = File.Exists(destination);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                }
                catch (Exception exception)
                {
                    throw new UpdaterFailureException(ExitCodes.ReplaceFailed,
                        $"创建目录失败：{Path.GetDirectoryName(destination)}（{exception.Message}）", exception);
                }

                ClearReadOnly(destination);
                WithReplaceRetry(() => CopyReplacing(source, destination), $"替换文件 {relative}");
                if (!existed)
                {
                    manifest.CreatedFiles.Add(relative);
                }
            }

            // 删除（deleteOrphans / delta 清单）
            foreach (var relative in deleteSet)
            {
                var destination = Combine(targetRoot, relative);
                if (!File.Exists(destination))
                {
                    continue;
                }

                ClearReadOnly(destination);
                try
                {
                    WithReplaceRetry(() => File.Delete(destination), $"删除残留文件 {relative}");
                }
                catch (UpdaterFailureException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new UpdaterFailureException(ExitCodes.ReplaceFailed,
                        $"删除失败：{relative}（{exception.Message}）", exception);
                }
            }
        }
        catch (UpdaterFailureException failure)
        {
            throw new UpdaterFailureException(failure.ExitCode, failure.Message, failure)
            {
                RollbackManifest = manifest
            };
        }
        catch (Exception exception)
        {
            throw new UpdaterFailureException(ExitCodes.ReplaceFailed, $"替换过程异常：{exception.Message}", exception)
            {
                RollbackManifest = manifest
            };
        }

        WriteManifest(manifest);
        return manifest;
    }

    /// <summary>只读属性会让覆盖/删除失败：替换前清除（安装目录常见 ACL 场景）。</summary>
    private static void ClearReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception)
        {
            // 清除失败时交由后续替换/删除给出具体错误
        }
    }

    /// <summary>替换单文件：先写 .atomupdate-new 再 rename-replace（MoveFileEx REPLACE_EXISTING / rename(2)）。</summary>
    private static void CopyReplacing(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Copy(source, destination, overwrite: false);
            return;
        }

        var staging = destination + ".atomupdate-new";
        File.Copy(source, staging, overwrite: true);
        try
        {
            File.Move(staging, destination, overwrite: true);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    private void WithReplaceRetry(Action operation, string what)
    {
        var attempts = Math.Max(1, task.Install.ReplaceRetryCount + 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    throw new UpdaterFailureException(ExitCodes.ReplaceFailed,
                        $"{what} 失败（已重试 {attempts} 次）：{exception.Message}", exception);
                }

                log.Debug($"{what} 被占用，重试 {attempt}/{attempts}：{exception.Message}");
                Thread.Sleep(Math.Max(0, task.Install.ReplaceRetryDelayMs));
            }
        }
    }

    private bool IsExcluded(string relative) =>
        GlobMatcher.IsMatchAny(task.Install.Exclude, relative, _caseSensitive);

    private bool IsPreserved(string relative) =>
        GlobMatcher.IsMatchAny(task.Install.Preserve, relative, _caseSensitive);

    private static bool IsWithin(string backupDirectory, string targetRoot, string relative)
    {
        try
        {
            var backup = Path.GetFullPath(backupDirectory);
            var candidate = Path.GetFullPath(Combine(targetRoot, relative));
            return candidate.StartsWith(backup + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureWithin(string root, string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalized = Path.GetFullPath(fullPath);
        if (!normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdaterFailureException(ExitCodes.ReplaceFailed, $"路径越界：{fullPath}");
        }
    }

    public static IEnumerable<string> EnumerateRelativeFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            yield return Normalize(Path.GetRelativePath(root, file));
        }
    }

    private static string Combine(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响主流程
        }
    }

    private void WriteManifest(BackupManifest manifest)
    {
        if (manifest.BackupDirectory is null || manifest.BackedUpFiles.Count == 0 && manifest.CreatedFiles.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(manifest.BackupDirectory);
            File.WriteAllText(Path.Combine(manifest.BackupDirectory, "backup-manifest.json"),
                JsonSerializer.Serialize(manifest, UpdaterJsonContext.Default.BackupManifest));
        }
        catch (Exception exception)
        {
            log.Debug($"回滚清单落盘失败（不影响替换）：{exception.Message}");
        }
    }
}
