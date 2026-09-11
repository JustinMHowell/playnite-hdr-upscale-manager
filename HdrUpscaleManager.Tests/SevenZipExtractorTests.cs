using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class SevenZipExtractorTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public SevenZipExtractorTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        // Generated with 7-Zip: a -t7z -m0=LZMA2 fixture.7z nested
        // Contains our own UTF-8 text, not an upstream binary. Keeping the tiny fixture
        // inline means running these tests doesn't require 7-Zip or a network download.
        private const string ArchiveBase64 =
            "N3q8ryccAAQXMucfjAAAAAAAAAAhAAAAAAAAALi3vVwBACBPcHRpU2NhbGVyIGV4dHJhY3Rpb24gcmVncmVzc2lvbgoAAACBMweuD9BaW3yfP0dBBYeGo4oceF1BZ5YnOOV4D6x9X150rJcTppR2aa4nXa3pkUIT2XR1N+0ChmQPhEJ/5a2UmkhlDcpTKxfu9TrLYEJ8GTKZzxEzZPTaD19dmIazxffXTsvgABcGJQEJZwAHCwEAASMDAQEFXQAQAAAMgIYKAQPBctcAAA==";

        [Fact]
        public void ExtractAll_RealLzma2Archive_ExtractsNestedFile()
        {
            SevenZipExtractor.ExtractAll(Convert.FromBase64String(ArchiveBase64), root);

            Assert.Equal("OptiScaler extraction regression\n", File.ReadAllText(Path.Combine(root, "nested", "hello.txt")));
        }

        [Fact]
        public async Task ExtractAll_InSeparateFrameworkHost_ResolvesBundledDependenciesWithoutRedirects()
        {
            // Test runners generate binding redirects that can hide this exact regression.
            // Stage DLLs only (like the .pext), and use a fresh .NET Framework EXE
            // with no config or assembly resolver. PowerShell only compiles/launches
            // the host, so its own assembly-loading behavior cannot influence the test.
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "plugin")).FullName;
            foreach (var dll in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
                File.Copy(dll, Path.Combine(pluginDirectory, Path.GetFileName(dll)));

            var pluginPath = Path.Combine(pluginDirectory, "HdrUpscaleManager.dll");
            var output = Path.Combine(root, "extracted");
            var hostPath = Path.Combine(root, "ExtractionHost.exe");
            var script = $@"
$ErrorActionPreference = 'Stop'
Add-Type -OutputType ConsoleApplication -OutputAssembly '{hostPath.Replace("'", "''")}' -TypeDefinition @'
using System;
using System.Reflection;
public static class ExtractionHost
{{
    public static int Main(string[] args)
    {{
        try
        {{
            var assembly = Assembly.LoadFrom(args[0]);
            var resolver = assembly.GetType(""HdrUpscaleManager.Services.Addons.CompressionAssemblyResolver"");
            var resolve = resolver.GetMethod(""Resolve"", BindingFlags.Static | BindingFlags.NonPublic);
            var request = new ResolveEventArgs(""System.Buffers, Version=4.0.2.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51"");
            var method = assembly.GetType(""HdrUpscaleManager.Services.Addons.SevenZipExtractor"").GetMethod(""ExtractAll"");
            method.Invoke(null, new object[] {{ Convert.FromBase64String(args[1]), args[2] }});
            if (resolve.Invoke(null, new object[] {{ null, request }}) != null)
                throw new Exception(""Unattributed binding leaked beyond extraction."");
            try
            {{
                method.Invoke(null, new object[] {{ new byte[] {{ 0 }}, args[2] }});
                throw new Exception(""Invalid archive unexpectedly succeeded."");
            }}
            catch (TargetInvocationException)
            {{
                if (resolve.Invoke(null, new object[] {{ null, request }}) != null)
                    throw new Exception(""Unattributed binding leaked after failed extraction."");
            }}
            return 0;
        }}
        catch (Exception ex)
        {{
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }}
    }}
}}
'@
& '{hostPath.Replace("'", "''")}' '{pluginPath.Replace("'", "''")}' '{ArchiveBase64}' '{output.Replace("'", "''")}'
exit $LASTEXITCODE";
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root
            };
            using (var process = Process.Start(startInfo))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    process.WaitForExit();
                    Assert.Fail("Extraction host timed out.");
                }
                Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            }
            Assert.Equal("OptiScaler extraction regression\n", File.ReadAllText(Path.Combine(output, "nested", "hello.txt")));
        }

        [Fact]
        public void FindFile_NoMatch_ReturnsNull()
        {
            Assert.Null(SevenZipExtractor.FindFile(root, "OptiScaler.dll"));
        }

        [Fact]
        public void FindFile_SingleMatch_ReturnsIt()
        {
            var path = Path.Combine(root, "OptiScaler.dll");
            File.WriteAllBytes(path, new byte[] { 0 });

            Assert.Equal(path, SevenZipExtractor.FindFile(root, "OptiScaler.dll"));
        }

        [Fact]
        public void FindFile_PreferredPathSegmentPresent_PicksThatOneOverOthers()
        {
            var x64Dir = Directory.CreateDirectory(Path.Combine(root, "normal", "x64")).FullName;
            var x32Dir = Directory.CreateDirectory(Path.Combine(root, "normal", "x32")).FullName;
            File.WriteAllBytes(Path.Combine(x64Dir, "d3d9.dll"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(x32Dir, "d3d9.dll"), new byte[] { 2 });

            var found = SevenZipExtractor.FindFile(root, "d3d9.dll", preferredPathSegment: "x32");

            Assert.Equal(Path.Combine(x32Dir, "d3d9.dll"), found);
        }

        [Fact]
        public void FindFile_PreferredSegmentAbsent_FallsBackToFirstMatch()
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "somewhere")).FullName;
            var path = Path.Combine(dir, "d3d9.dll");
            File.WriteAllBytes(path, new byte[] { 1 });

            var found = SevenZipExtractor.FindFile(root, "d3d9.dll", preferredPathSegment: "x64");

            Assert.Equal(path, found);
        }
    }
}
