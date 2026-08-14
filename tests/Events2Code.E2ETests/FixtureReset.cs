using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Events2Code.E2ETests
{
    /// <summary>
    /// Restores the test environment to its fixture state: re-imports the E2EEventsTest
    /// solution (overwriting the fixture form) and removes the test-created bootstrap
    /// web resource.
    /// </summary>
    public static class FixtureReset
    {
        public const string BootstrapWebResourceName = "e2e_bootstrap.js";

        public static async Task RunAsync(DataverseClient client)
        {
            var solutionDir = Path.Combine(client.RepoRoot, "tests", "e2e-fixtures", "solution");
            var zipBytes = ZipFolder(solutionDir);

            await client.ImportSolutionAsync(zipBytes);
            await client.PublishEntityAsync("contact");
            // Only after the restored form no longer references it can the bootstrap be deleted.
            await client.DeleteWebResourceIfExistsAsync(BootstrapWebResourceName);
        }

        private static byte[] ZipFolder(string folder)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        var entryName = file.Substring(folder.Length + 1).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, entryName);
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
