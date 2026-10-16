using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using MFT.Other;
using NUnit.Framework;

namespace MFT.Test;

/// <summary>
///     Every $MFT in a local collection that is not in the repo: the 'MFTs' folder next to the repository, or MFT_CORPUS.
///     Skipped when it is missing (as in CI). Archives (.zip, .7z) and other files are ignored
/// </summary>
[TestFixture]
[Explicit("Slow: several GB")]
public class CorpusTests
{
    private static string CorpusDirectory()
    {
        var env = Environment.GetEnvironmentVariable("MFT_CORPUS");
        if (string.IsNullOrEmpty(env) == false)
        {
            return env;
        }

        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MFTs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> MftFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var buffer = new byte[4];
            using (var fs = File.OpenRead(file))
            {
                if (fs.Length < 1024 || fs.Read(buffer, 0, 4) != 4)
                {
                    continue;
                }
            }

            if (BitConverter.ToInt32(buffer, 0) == 0x454c4946)
            {
                yield return file;
            }
        }
    }

    [Test]
    public void EveryMftLoads()
    {
        var dir = CorpusDirectory();
        if (dir == null)
        {
            Assert.Ignore("No 'MFTs' folder next to the repository and no MFT_CORPUS");
        }

        var files = MftFiles(dir).ToList();
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var mft = MftFile.Load(file, false);

            mft.ParseErrors.Should().BeEmpty(file);
            mft.FileRecords.Should().NotBeEmpty(file);

            var paths = 0;
            foreach (var fr in mft.FileRecords.Values)
            {
                var fn = fr.GetFileNameAttributeFromFileRecord();
                if (fn != null && mft.GetFullParentPath(fn.FileInfo.ParentMftRecord.GetKey()).Length > 0)
                {
                    paths += 1;
                }
            }

            var withErrors = mft.FileRecords.Values.Count(t => t.ParseErrors.Count > 0);
            TestContext.Out.WriteLine(
                $"{file}: {mft.FileRecords.Count:N0} in use, {mft.FreeFileRecords.Count:N0} free, {paths:N0} paths, {withErrors:N0} records with parse errors");
        }
    }
}
