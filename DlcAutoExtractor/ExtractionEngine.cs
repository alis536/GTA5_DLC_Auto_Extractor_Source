using CodeWalker.GameFiles;
using System.Text;

namespace DlcAutoExtractor;

public sealed record ExtractionOptions(
    string SourceFolder, string OutputFolder, string? GameFolder,
    bool AllFiles, bool IncludeNested, bool OverwriteExisting, bool Gen9,
    bool ExactResourceCopy, bool VerifyOutput);

public sealed record ExtractorProgress(string Status, string? Detail, int RootDone, int RootTotal,
    int ScannedArchives, int Extracted, int Skipped, int Failed, int ExactCopied, int Fallbacks);

public sealed class ExtractionResults
{
    public int ScannedArchives { get; set; }
    public int Extracted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int ExactCopied { get; set; }
    public int Fallbacks { get; set; }
    public bool Cancelled { get; set; }
    public string LogFile { get; set; } = "";
    public string ManifestFile { get; set; } = "";
}

public static class ExtractionEngine
{
    public static ExtractionResults Run(ExtractionOptions options,
        IProgress<ExtractorProgress> progress, CancellationToken token)
    {
        string src = Path.GetFullPath(options.SourceFolder);
        string dst = Path.GetFullPath(options.OutputFolder);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException(src);
        ExtractionPaths.ValidateDisjointFolders(src, dst);
        Directory.CreateDirectory(dst);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var results = new ExtractionResults
        {
            LogFile = Path.Combine(dst, $"_extract_log_{stamp}.txt"),
            ManifestFile = Path.Combine(dst, $"_extract_manifest_{stamp}.csv")
        };
        using var log = new StreamWriter(results.LogFile, false, new UTF8Encoding(false)) { AutoFlush = true };
        using var manifest = new StreamWriter(results.ManifestFile, false, new UTF8Encoding(false)) { AutoFlush = true };
        manifest.WriteLine("status,archive_entry,output_file,method,sha256,message");
        int completed = 0, archiveCount = 0, totalRoots = 0;

        void WriteLog(string kind, string source, string destination, string message,
            string method = "", string hash = "")
        {
            log.WriteLine($"[{DateTime.Now:HH:mm:ss}] {kind} | {source} | {method} | {message}");
            manifest.WriteLine(string.Join(",", new[] { kind, source, destination, method, hash, message }.Select(Csv)));
            if (kind is "ERROR" or "WARNING")
                progress.Report(new ExtractorProgress(kind, $"{source}: {message}", completed,
                    totalRoots, archiveCount, results.Extracted, results.Skipped,
                    results.Failed, results.ExactCopied, results.Fallbacks));
        }

        void Update(string status, string? detail = null) =>
            progress.Report(new ExtractorProgress(status, detail, completed, totalRoots,
                archiveCount, results.Extracted, results.Skipped,
                results.Failed, results.ExactCopied, results.Fallbacks));

        try
        {
            // OPEN archives can be processed without GTA V keys.
            if (!string.IsNullOrWhiteSpace(options.GameFolder))
            {
                Update("Loading CodeWalker GTA V keys...");
                GTA5Keys.LoadFromPath(options.GameFolder, options.Gen9);
            }

            Update("Searching for .rpf files...");
            var roots = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)).ToList();
            roots.Sort(StringComparer.OrdinalIgnoreCase);
            totalRoots = roots.Count;
            Update($"Found {roots.Count:N0} physical RPF archive(s)");
            log.WriteLine($"Version: 0.2\nSource: {src}\nOutput: {dst}\nRoot RPF count: {totalRoots}\n" +
                $"Mode: {(options.AllFiles ? "ALL" : "MAP")}\nExact RSC copy: {options.ExactResourceCopy}\n" +
                $"Verify output SHA256: {options.VerifyOutput}");

            foreach (var physicalPath in roots)
            {
                token.ThrowIfCancellationRequested();
                var relativeArchivePath = Path.GetRelativePath(src, physicalPath);
                Update("Scanning " + relativeArchivePath);
                try
                {
                    var rootRpf = new RpfFile(physicalPath, relativeArchivePath);
                    rootRpf.ScanStructure(null!, warning => WriteLog("WARNING", relativeArchivePath, "", warning));
                    if (rootRpf.LastException != null || rootRpf.AllEntries is null)
                    {
                        results.Failed++;
                        WriteLog("ERROR", relativeArchivePath, "", rootRpf.LastError ?? "Unable to open RPF.");
                        continue;
                    }
                    var pending = new Stack<RpfFile>();
                    pending.Push(rootRpf);
                    while (pending.Count != 0)
                    {
                        token.ThrowIfCancellationRequested();
                        var archive = pending.Pop();
                        archiveCount++;
                        Update("Extracting " + archive.Path);
                        if (options.IncludeNested && archive.Children is not null)
                            foreach (var child in archive.Children) pending.Push(child);

                        if (archive.AllEntries is null)
                        {
                            results.Failed++;
                            WriteLog("ERROR", archive.Path, "", "Archive has no parsed entries.");
                            continue;
                        }

                        foreach (var entry in archive.AllEntries)
                        {
                            token.ThrowIfCancellationRequested();
                            if (entry is not RpfFileEntry fentry ||
                                !FileFilters.ShouldExtract(entry.Name, options.AllFiles)) continue;
                            string destination = "";
                            try
                            {
                                string sourceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(src));
                                if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "GTA5_Extraction";
                                destination = ExtractionPaths.ResolveOutputFile(dst, Path.Combine(sourceName, entry.Path));
                                if (File.Exists(destination) && !options.OverwriteExisting)
                                {
                                    results.Skipped++;
                                    WriteLog("SKIP", entry.Path, destination, "Already exists; use a NEW output directory for a valid v0.2 test.");
                                    continue;
                                }
                                string? dir = Path.GetDirectoryName(destination);
                                if (dir is null) throw new InvalidDataException("Missing output directory.");
                                Directory.CreateDirectory(dir);
                                string tmp = destination + ".partial-" + Guid.NewGuid().ToString("N");
                                try
                                {
                                    string method = "DECODED_BINARY", hash;
                                    long size;
                                    bool copiedExactly = false;
                                    bool direct = options.ExactResourceCopy &&
                                        fentry is RpfResourceFileEntry rawEntry && !rawEntry.IsEncrypted;

                                    if (direct)
                                    {
                                        try
                                        {
                                            var resource = (RpfResourceFileEntry)fentry;
                                            (size, hash) = RawResourceCopier.CopyOriginal(
                                                resource.File.GetPhysicalFilePath(), resource.File.StartPos,
                                                (long)resource.FileOffset, (long)resource.FileSize,
                                                tmp, options.VerifyOutput);
                                            method = "EXACT_RPF_RESOURCE";
                                            copiedExactly = true;
                                        }
                                        catch (InvalidDataException ex)
                                        {
                                            // Unsupported/non-RSC7 header: explicitly log and fall back to CodeWalker.
                                            results.Fallbacks++;
                                            WriteLog("WARNING", entry.Path, destination,
                                                "Exact copy not possible: " + ex.Message + " Re-serializing via CodeWalker.",
                                                "FALLBACK");
                                            if (File.Exists(tmp)) File.Delete(tmp);
                                            (size, hash) = SerializeWithCodeWalker(fentry, tmp, options.VerifyOutput);
                                            method = "RESERIALIZED_FALLBACK";
                                        }
                                    }
                                    else
                                    {
                                        if (options.ExactResourceCopy && fentry is RpfResourceFileEntry encrypted && encrypted.IsEncrypted)
                                        {
                                            results.Fallbacks++;
                                            WriteLog("WARNING", entry.Path, destination,
                                                "Individually encrypted resource: CodeWalker re-serialization is required.", "FALLBACK");
                                        }
                                        (size, hash) = SerializeWithCodeWalker(fentry, tmp, options.VerifyOutput);
                                        method = fentry is RpfResourceFileEntry ? "RESERIALIZED_RESOURCE" : "DECODED_BINARY";
                                    }

                                    File.Move(tmp, destination, options.OverwriteExisting);
                                    if (copiedExactly) results.ExactCopied++;
                                    results.Extracted++;
                                    WriteLog("OK", entry.Path, destination, size.ToString(), method, hash);
                                }
                                finally { if (File.Exists(tmp)) File.Delete(tmp); }
                            }
                            catch (Exception ex)
                            {
                                results.Failed++;
                                WriteLog("ERROR", entry.Path, destination, ex.Message);
                            }
                            int doneFiles = results.Extracted + results.Skipped + results.Failed;
                            if (doneFiles % 20 == 0)
                                Update("Extracting " + archive.Path,
                                    $"{results.Extracted:N0} extracted; {results.Skipped:N0} skipped");
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    results.Failed++;
                    WriteLog("ERROR", relativeArchivePath, "", ex.ToString());
                }
                finally { completed++; Update("Processed " + relativeArchivePath); }
            }
        }
        catch (OperationCanceledException)
        {
            results.Cancelled = true;
            log.WriteLine("Cancelled by user. Completed files were retained.");
        }
        catch (Exception ex)
        {
            results.Failed++;
            WriteLog("ERROR", "GENERAL", "", ex.ToString());
            throw;
        }
        finally
        {
            results.ScannedArchives = archiveCount;
            Update(results.Cancelled ? "Cancelled" : "Finished");
        }
        return results;
    }

    private static (long Size, string Hash) SerializeWithCodeWalker(
        RpfFileEntry entry, string tmp, bool verify)
    {
        byte[]? data = entry.File.ExtractFile(entry);
        if (data is null) throw new IOException(
            "CodeWalker returned no bytes (unsupported, damaged or encrypted archive).");
        if (entry is RpfResourceFileEntry resourceEntry)
        {
            byte[] packed = ResourceBuilder.Compress(data);
            data = ResourceBuilder.AddResourceHeader(resourceEntry, packed);
            if (data is null) throw new IOException("Resource serialization failed.");
        }
        string hash = RawResourceCopier.WriteDecodedBytes(data, tmp, verify);
        return (data.LongLength, hash);
    }

    private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
