using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>
    /// Thin wrapper over the globally-installed ilspycmd (.NET tool) to decompile a single type's C# body.
    /// Used only for the flagged allowlist (Targets.DecompileFlag).
    /// </summary>
    internal sealed class Decompiler
    {
        readonly string _exe;
        public bool Available => _exe != null;

        public Decompiler()
        {
            _exe = Locate();
        }

        static string Locate()
        {
            // Global dotnet tool default location.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates =
            {
                Path.Combine(home, ".dotnet", "tools", "ilspycmd.exe"),
                Path.Combine(home, ".dotnet", "tools", "ilspycmd"),
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            // PATH fallback
            return "ilspycmd";
        }

        /// <summary>Decompile one type to C#. Returns null on failure/timeout.</summary>
        public string DecompileType(string assemblyPath, string fullTypeName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.ArgumentList.Add(assemblyPath);
                psi.ArgumentList.Add("-t");
                psi.ArgumentList.Add(fullTypeName);

                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return null; }
                    if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(outp)) return null;
                    // Cap stored body to keep the DB compact.
                    return outp.Length > 16000 ? outp.Substring(0, 16000) + "\n// … truncated" : outp;
                }
            }
            catch { return null; }
        }
    }
}
