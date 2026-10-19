using DlcAutoExtractor;

int passed = 0;
void Assert(bool v, string msg)
{
    if (!v) throw new Exception("FAILED: " + msg);
    passed++;
}
void Throws(Action action, string msg)
{
    try { action(); } catch (ArgumentException) { passed++; return; } catch (InvalidDataException) { passed++; return; }
    throw new Exception("Expected failure: " + msg);
}

var input = @"mj_map_east\dlc.rpf\x64\levels\gta5\props\mj_e_10_1_b1.rpf\example.ymap";
var expected = Path.Combine("mj_map_east", "dlc", "x64", "levels", "gta5", "props", "mj_e_10_1_b1", "example.ymap");
Assert(ExtractionPaths.MakeOutputRelativePath(input) == expected, "nested archive structure");
Assert(ExtractionPaths.MakeOutputRelativePath(@"DLC.RPF\ONE.RPF\x.YDR") == Path.Combine("DLC", "ONE", "x.YDR"), "case insensitive RPF suffix");
Assert(FileFilters.ShouldExtract("road.ymap", false), "YMAP selected");
Assert(FileFilters.ShouldExtract("m.ydr", false), "YDR selected");
Assert(!FileFilters.ShouldExtract("archive.rpf", true), "never export nested RPF as leaf");
Assert(!FileFilters.ShouldExtract("garbage.bin", false), "map-only filter");
Assert(FileFilters.ShouldExtract("garbage.bin", true), "all file filter");
Throws(() => ExtractionPaths.MakeOutputRelativePath(@"dlc.rpf\..\evil.ymap"), "parent traversal");
Throws(() => ExtractionPaths.MakeOutputRelativePath(@"C:\dlc.rpf\evil.ymap"), "absolute drive");
Throws(() => ExtractionPaths.MakeOutputRelativePath(@"dlc.rpf\CON.xml"), "Windows reserved device");
Throws(() => ExtractionPaths.MakeOutputRelativePath(@"dlc.rpf\bad:name.ymap"), "Windows illegal filename");
Throws(() => ExtractionPaths.ValidateDisjointFolders(Path.Combine(Path.GetTempPath(),"a"), Path.Combine(Path.GetTempPath(),"a","output")), "nested output");
// Synthetic physical RPF: resource starts 2 sectors into a child archive at byte 4096.
// This tests the exact-offset copy logic independently of CodeWalker and game assets.
string testdir = Path.Combine(Path.GetTempPath(), "dlc-extractor-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testdir);
try
{
    string rpffile = Path.Combine(testdir, "sample.rpf");
    string rawout = Path.Combine(testdir, "out.ydr");
    long parentStart = 4096, sectorsFromStart = 2;
    byte[] payload = new byte[3900];
    new Random(123).NextBytes(payload);
    System.Text.Encoding.ASCII.GetBytes("RSC7").CopyTo(payload, 0);
    using (var stream = new FileStream(rpffile, FileMode.CreateNew))
    {
        stream.SetLength(10000);
        stream.Position = parentStart + sectorsFromStart * 512;
        stream.Write(payload);
    }
    var copy = RawResourceCopier.CopyOriginal(rpffile, parentStart, sectorsFromStart,
        payload.Length, rawout, verifyOutput: true);
    Assert(copy.Bytes == payload.Length, "copied exact number of bytes");
    Assert(File.ReadAllBytes(rawout).SequenceEqual(payload), "nested offset RSC7 resource byte identical");
    Assert(copy.Sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)),
        "SHA256 expected matches extracted RPF resource");
    string decoded = Path.Combine(testdir, "bytes.ymap");
    string decodedHash = RawResourceCopier.WriteDecodedBytes(payload, decoded, true);
    Assert(decodedHash == copy.Sha256, "decoded bytes writing / SHA256 readback");
    Throws(() => RawResourceCopier.CopyOriginal(rpffile, parentStart, sectorsFromStart,
        999999, Path.Combine(testdir,"invalid.ydr"), true), "file length bound");
    byte[] badHeader = (byte[])payload.Clone();
    badHeader[0] = 0;
    using (var stream = new FileStream(rpffile, FileMode.Open, FileAccess.Write))
    {
        stream.Position = parentStart + sectorsFromStart * 512;
        stream.Write(badHeader);
    }
    Throws(() => RawResourceCopier.CopyOriginal(rpffile, parentStart, sectorsFromStart,
        badHeader.Length, Path.Combine(testdir,"bad.ydr"), true), "unexpected RSC header");
}
finally { Directory.Delete(testdir, recursive: true); }
Console.WriteLine($"PASS: {passed} path/filter/raw RPF byte-copy tests");
