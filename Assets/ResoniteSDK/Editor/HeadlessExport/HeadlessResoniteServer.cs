using ResoniteLink;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A private, logged-out headless Resonite world with ResoniteLink and the package bridge plugin.
/// Every member is safe to call off Unity's main thread.
/// </summary>
public sealed class HeadlessResoniteServer : IDisposable
{
    public const string BridgeType = "[ResoniteUnitySDK.HeadlessBridge]ResoniteUnitySDK.Headless.PackageBridge";

    public int Port { get; }
    public string RunDirectory { get; }
    public string ResoniteVersion { get; private set; }

    Process _process;

    HeadlessResoniteServer(int port, string runDirectory)
    {
        Port = port;
        RunDirectory = runDirectory;
    }

    /// <summary>Finds Steam's Resonite/Headless folder in any Steam library.</summary>
    public static string FindHeadless()
    {
        var steamRoots = new List<string>();
        foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            var programFiles = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(programFiles))
                steamRoots.Add(Path.Combine(programFiles, "Steam"));
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        steamRoots.Add(Path.Combine(home, ".steam", "steam"));
        steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));

        var libraries = new List<string>(steamRoots);
        foreach (var root in steamRoots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            foreach (var line in File.ReadAllLines(vdf))
            {
                var parts = line.Trim().Split('"');
                if (parts.Length >= 4 && parts[1] == "path")
                    libraries.Add(parts[3].Replace("\\\\", "\\"));
            }
        }
        return libraries.Select(l => Path.Combine(l, "steamapps", "common", "Resonite", "Headless"))
            .FirstOrDefault(IsHeadless);
    }

    public static bool IsHeadless(string folder) =>
        !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "FrooxEngine.dll"))
        && (File.Exists(Path.Combine(folder, "Resonite.exe")) || File.Exists(Path.Combine(folder, "Resonite.dll")));

    /// <summary>Builds the bridge plugin against this headless build (cached by source and engine version).</summary>
    public static string BuildBridge(string headless, string pluginSource, string workDirectory, Action<string> log)
    {
        var sources = Directory.GetFiles(pluginSource).Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj")).OrderBy(f => f).ToArray();
        var version = Path.Combine(headless, "Build.version");
        string hash;
        using (var sha = SHA256.Create())
        {
            var bytes = sources.SelectMany(File.ReadAllBytes)
                .Concat(File.Exists(version) ? File.ReadAllBytes(version) : Array.Empty<byte>())
                .Concat(Encoding.UTF8.GetBytes(headless)).ToArray();
            hash = BitConverter.ToString(sha.ComputeHash(bytes), 0, 8).Replace("-", "");
        }

        var output = Path.Combine(workDirectory, "bridge", hash);
        var dll = Path.Combine(output, "ResoniteUnitySDK.HeadlessBridge.dll");
        if (File.Exists(dll))
            return dll;

        // Build from a copy so obj/bin never land inside Assets
        var build = Path.Combine(workDirectory, "bridge-src");
        if (Directory.Exists(build))
            Directory.Delete(build, true);
        Directory.CreateDirectory(build);
        foreach (var source in sources)
            File.Copy(source, Path.Combine(build, Path.GetFileName(source)));

        log("Building headless bridge plugin...");
        var project = Directory.GetFiles(build, "*.csproj").Single();
        var (code, text) = Run("dotnet", $"build \"{project}\" -c Release -nologo -v:q -o \"{output}\" \"-p:ResoniteHeadless={headless}\"",
            build, TimeSpan.FromMinutes(5));
        if (code != 0 || !File.Exists(dll))
            throw new InvalidOperationException("Building the headless bridge plugin failed (requires the .NET 10 SDK):\n" + text);
        return dll;
    }

    /// <summary>Starts the headless world and waits until ResoniteLink answers.</summary>
    public static async Task<HeadlessResoniteServer> StartAsync(string headless, string bridge, string workDirectory,
        Action<string> log, CancellationToken cancellation)
    {
        var port = FreePort();
        var run = Path.Combine(workDirectory, "runs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{port}");
        Directory.CreateDirectory(run);
        var server = new HeadlessResoniteServer(port, run);
        try
        {
            var config = Path.Combine(run, "HeadlessConfig.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["comment"] = "Generated by the Resonite Unity SDK for headless package export",
                ["dataFolder"] = Path.Combine(workDirectory, "data"),
                ["cacheFolder"] = Path.Combine(workDirectory, "cache"),
                ["logsFolder"] = Path.Combine(run, "logs"),
                ["startWorlds"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["isEnabled"] = true, ["sessionName"] = "Unity SDK Export", ["accessLevel"] = "Private",
                        ["hideFromPublicListing"] = true, ["maxUsers"] = 1, ["loadWorldPresetName"] = "Grid",
                        ["autoSleep"] = false, ["saveOnExit"] = false,
                        ["enableResoniteLink"] = true, ["forceResoniteLinkPort"] = port,
                    }
                },
            }, new JsonSerializerOptions { WriteIndented = true }));

            var exe = Path.Combine(headless, "Resonite.exe");
            var args = $"-HeadlessConfig \"{config}\" -LoadAssembly \"{bridge}\"";
            var start = File.Exists(exe)
                ? new ProcessStartInfo(exe, args)
                : new ProcessStartInfo("dotnet", $"\"{Path.Combine(headless, "Resonite.dll")}\" {args}");
            start.WorkingDirectory = headless;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            log($"Starting headless Resonite (ResoniteLink port {port})...");
            var console = new StreamWriter(Path.Combine(run, "console.log")) { AutoFlush = true };
            server._process = Process.Start(start);
            server._process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (console) console.WriteLine(e.Data); };
            server._process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (console) console.WriteLine(e.Data); };
            server._process.Exited += (_, __) => { lock (console) console.Dispose(); };
            server._process.EnableRaisingEvents = true;
            server._process.BeginOutputReadLine();
            server._process.BeginErrorReadLine();

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
            Exception last = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                if (server._process.HasExited)
                    throw new InvalidOperationException($"Headless exited with code {server._process.ExitCode}; see {run}");
                try
                {
                    using var link = await server.ConnectAsync();
                    var session = await link.GetSessionData();
                    if (session.Success)
                    {
                        server.ResoniteVersion = session.ResoniteVersion;
                        return server;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                await Task.Delay(2000, cancellation);
            }
            throw new TimeoutException($"ResoniteLink did not start within 5 minutes: {last?.Message}");
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    public async Task<LinkInterface> ConnectAsync()
    {
        var link = new LinkInterface();
        try
        {
            await link.Connect(new Uri($"ws://localhost:{Port}"), CancellationToken.None);
            return link;
        }
        catch
        {
            link.Dispose();
            throw;
        }
    }

    /// <summary>Writes a .resonitepackage of the given slot with the native exporter; returns its path.</summary>
    public async Task<string> ExportPackageAsync(string slotId, string folder, string name, bool includeVariants)
    {
        var fileName = Path.ChangeExtension(SanitizeFileName(name), ".resonitepackage");
        using var link = await ConnectAsync();
        var result = await link.CallStaticMethod(new CallStaticSyncMethod
        {
            TargetType = BridgeType,
            MethodName = "ExportPackage",
            Arguments = new Dictionary<string, Data>
            {
                ["root"] = new Data_Reference { TargetID = slotId },
                ["folder"] = new Data_string { Value = folder },
                ["name"] = new Data_string { Value = fileName },
                ["includeVariants"] = new Data_bool { Value = includeVariants },
            },
        });
        if (!result.Success)
            throw new InvalidOperationException("Package export failed: " + result.ErrorInfo);
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("Resonite reported success but the package was not written", path);
        return path;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "Export" : clean;
    }

    public void Dispose()
    {
        if (_process == null)
            return;
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("shutdown");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit(60000))
                    _process.Kill();
            }
        }
        catch (Exception)
        {
            try { if (!_process.HasExited) _process.Kill(); } catch (Exception) { }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static (int code, string output) Run(string file, string arguments, string directory, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill();
            return (-1, "Timed out");
        }
        return (process.ExitCode, output.Result + error.Result);
    }
}
