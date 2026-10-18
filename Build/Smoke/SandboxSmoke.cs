using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Laura.Core.Execution;
using Laura.Platform.Windows.System;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

/// <summary>Explicit native LPAC experiment using only synthetic canaries, a fresh profile and owned repository output directories.</summary>
internal static partial class SandboxSmoke
{
    /// <summary>Observes its own OS token and attempts only fixture file/network operations. Args: arguments: Granted folder, private canary and local listener port. Returns: Process exit.</summary>
    internal static async Task<int> RunFixtureAsync(string[] arguments)
    {
        Console.Error.WriteLine("fixture.entered");
        using var current = Process.GetCurrentProcess();
        if (OpenProcessToken(current.SafeHandle, 8, out var token) == 0) { throw new InvalidOperationException("Cannot inspect the fixture token."); }
        using (token)
        {
            int container = TokenInteger(token, 29), capabilities = TokenInteger(token, 30);
            Console.Error.WriteLine("fixture.token");
            if (GetProcessMitigationPolicy(current.SafeHandle, 4, out uint mitigation, 4) == 0) { throw new InvalidOperationException("Cannot inspect native mitigation."); }
            string desktop = CurrentDesktopName();
            bool privateDesktop = desktop.StartsWith("LauraSandbox", StringComparison.Ordinal); uint uiRestrictions = 0; bool inputDesktopDenied = false;
            if (privateDesktop)
            {
                if (QueryInformationJobObject(0, 4, out uiRestrictions, 4, out _) == 0) { throw new InvalidOperationException("Cannot inspect job UI limits."); }
                nint input = OpenInputDesktop(0, 0, 0x81); inputDesktopDenied = input == 0; if (input != 0) { _ = CloseDesktop(input); }
            }
            bool insideRead = File.ReadAllText(Path.Combine(arguments[0], "inside.txt")) == "synthetic inside canary";
            File.WriteAllText(Path.Combine(arguments[0], "observed.txt"), "synthetic write");
            Console.Error.WriteLine("fixture.allowed_io");
            bool outsideReadDenied = false, outsideWriteDenied = false, outsideDeleteDenied = false, networkDenied = false, networkConnected = false, networkTimedOut = false; int? socketError = null;
            using var accessCheck = CreateFileW(arguments[1], 0x00040000, 7, 0, 3, 0x00200000, 0);
            bool outsideAclDenied = accessCheck.IsInvalid && Marshal.GetLastPInvokeError() == 5;
            bool appPackagesCanaryDenied = false;
            try { _ = File.ReadAllText(arguments[3]); } catch (UnauthorizedAccessException) { appPackagesCanaryDenied = true; }
            bool privateRegistryUnavailable;
            try { using var key = Registry.CurrentUser.OpenSubKey(arguments[4], false); privateRegistryUnavailable = key is null; }
            catch (Exception failure) when (failure is UnauthorizedAccessException or global::System.Security.SecurityException) { privateRegistryUnavailable = true; }
            try { _ = File.ReadAllText(arguments[1]); } catch (UnauthorizedAccessException) { outsideReadDenied = true; }
            try { File.WriteAllText(arguments[1], "synthetic forbidden write"); } catch (UnauthorizedAccessException) { outsideWriteDenied = true; }
            try { File.Delete(arguments[1]); } catch (UnauthorizedAccessException) { outsideDeleteDenied = true; }
            Console.Error.WriteLine("fixture.private_io");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, int.Parse(arguments[2], global::System.Globalization.CultureInfo.InvariantCulture)), deadline.Token);
                networkConnected = true;
            }
            catch (SocketException failure) when (failure.SocketErrorCode == SocketError.AccessDenied) { networkDenied = true; socketError = failure.NativeErrorCode; }
            catch (SocketException failure) { socketError = failure.NativeErrorCode; }
            catch (OperationCanceledException) { networkTimedOut = true; }
            Console.WriteLine(JsonSerializer.Serialize(new { container, appPackagesCanaryDenied, capabilities, win32kDisabled = (mitigation & 1) != 0, privateDesktop, uiRestrictions, inputDesktopDenied, insideRead,
                outsideReadDenied, outsideWriteDenied, outsideDeleteDenied, outsideAclDenied, privateRegistryUnavailable, networkDenied, networkConnected, networkTimedOut, socketError }));
        }
        return 0;
    }

    /// <summary>Runs a fresh LPAC with no capabilities and restores its exact profile/ACL changes after native job cleanup.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        string stage = Path.Combine(directory, "Granted"), outside = Path.Combine(directory, "PrivateCanary");
        if (Directory.Exists(stage) || Directory.Exists(outside)) { throw new InvalidOperationException("Sandbox diagnostics require fresh directories."); }
        Directory.CreateDirectory(stage); Directory.CreateDirectory(outside);
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            if (Path.GetExtension(path) == ".dll" || Path.GetFileName(path).EndsWith(".deps.json", StringComparison.Ordinal) || Path.GetFileName(path).EndsWith(".runtimeconfig.json", StringComparison.Ordinal))
            { File.Copy(path, Path.Combine(stage, Path.GetFileName(path))); }
        }
        File.WriteAllText(Path.Combine(stage, "inside.txt"), "synthetic inside canary");
        using var identity = WindowsIdentity.GetCurrent(); var privateAcl = new DirectorySecurity();
        privateAcl.SetAccessRuleProtection(true, false);
        privateAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(outside).SetAccessControl(privateAcl);
        string canary = Path.Combine(outside, "outside.txt"); File.WriteAllText(canary, "synthetic private canary");
        string appPackagesCanary = Path.Combine(directory, "AppPackagesCanary.txt"); File.WriteAllText(appPackagesCanary, "synthetic package canary");
        var packageAcl = new FileSecurity(); packageAcl.SetAccessRuleProtection(true, false);
        packageAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        packageAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-15-2-1"), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(appPackagesCanary).SetAccessControl(packageAcl);
        using var registryCanary = SandboxRegistryCanary.Create(identity.User!);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using (var control = new TcpClient())
        {
            await control.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
            using var accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        }
        bool lessPrivileged = !Environment.GetCommandLineArgs().Contains("--sandbox-standard-control", StringComparer.Ordinal);
        bool disableWin32k = !Environment.GetCommandLineArgs().Contains("--sandbox-user32-control", StringComparer.Ordinal);
        bool registryRead = Environment.GetCommandLineArgs().Contains("--sandbox-registry-read-control", StringComparer.Ordinal);
        using var profile = WindowsSandboxProfile.CreateForDiagnostic(lessPrivileged, disableWin32k, registryRead); profile.GrantDiagnosticDirectory(stage);
        bool privateDesktop = Environment.GetCommandLineArgs().Contains("--sandbox-private-desktop-control", StringComparer.Ordinal);
        if (privateDesktop) { profile.CreateDiagnosticDesktop(); }
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows), ["WINDIR"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["TEMP"] = Path.Combine(profile.Directory, "Temp"), ["TMP"] = Path.Combine(profile.Directory, "Temp"), ["LOCALAPPDATA"] = profile.Directory,
            ["USERPROFILE"] = stage, ["DOTNET_CLI_HOME"] = stage, ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        };
        var request = new CommandExecutionRequest(Environment.ProcessPath!, [Path.Combine(stage, "Laura.Smoke.dll"), "--sandbox-fixture", stage, canary,
            ((IPEndPoint)listener.LocalEndpoint).Port.ToString(global::System.Globalization.CultureInfo.InvariantCulture), appPackagesCanary, registryCanary.Path], stage).Validated();
        using var process = OwnedCommandProcess.Start(request, environment, profile);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken); var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        bool timedOut = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { timedOut = true; }
        finally { process.Terminate(); await process.WaitForEmptyAsync(); }
        string text = await output, stderr = await error;
        await File.WriteAllTextAsync(Path.Combine(directory, "sandbox-probe-output.json"), JsonSerializer.Serialize(new { timedOut, exitCode = process.ExitCode, output = text, error = stderr }), cancellationToken);
        if (timedOut) { throw new InvalidOperationException("Sandbox compatibility control timed out before completing proof; retained stage diagnostics: " + stderr[..Math.Min(2000, stderr.Length)]); }
        if (process.ExitCode != 0) { throw new InvalidOperationException("LPAC fixture failed before proof; native exit " + process.ExitCode + ": " + stderr[..Math.Min(2000, stderr.Length)]); }
        using var result = JsonDocument.Parse(text); var observed = result.RootElement;
        if (observed.GetProperty("container").GetInt32() != 1 || observed.GetProperty("appPackagesCanaryDenied").GetBoolean() != lessPrivileged || observed.GetProperty("capabilities").GetInt32() != profile.CapabilityCount
            || observed.GetProperty("win32kDisabled").GetBoolean() != disableWin32k || !observed.GetProperty("insideRead").GetBoolean()
            || privateDesktop && (!observed.GetProperty("privateDesktop").GetBoolean() || observed.GetProperty("uiRestrictions").GetUInt32() != 255 || !observed.GetProperty("inputDesktopDenied").GetBoolean())
            || !observed.GetProperty("outsideReadDenied").GetBoolean() || !observed.GetProperty("outsideWriteDenied").GetBoolean()
            || !observed.GetProperty("outsideDeleteDenied").GetBoolean() || !observed.GetProperty("networkDenied").GetBoolean()
            || !observed.GetProperty("outsideAclDenied").GetBoolean()
            || !observed.GetProperty("privateRegistryUnavailable").GetBoolean()
            || File.ReadAllText(canary) != "synthetic private canary" || File.ReadAllText(Path.Combine(stage, "observed.txt")) != "synthetic write")
        { throw new InvalidOperationException("LPAC isolation proof is incomplete. Automatic command permission remains unavailable."); }
        Console.WriteLine((lessPrivileged && disableWin32k ? "LPAC proof" : "Compatibility control") + " passed: actual token, requested mitigation, synthetic file scope and denied loopback socket. Automatic command authorization is not enabled by this diagnostic.");
        if (Environment.GetCommandLineArgs().Contains("--sandbox-sdk-control", StringComparer.Ordinal))
        {
            if (!lessPrivileged || !privateDesktop || !registryRead) { throw new InvalidOperationException("SDK control requires the verified LPAC private desktop profile."); }
            await RunSdkControlAsync(directory, stage, environment, profile, cancellationToken);
        }
        var packageIdentity = new SecurityIdentifier(profile.Sid); string profileDirectory = profile.Directory;
        bool profileExisted = Directory.Exists(profileDirectory); profile.Dispose();
        bool profileRemoved = !Directory.Exists(profileDirectory);
        bool desktopClosed = profile.DesktopClosed;
        bool aclRestored = !new DirectoryInfo(stage).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference.Equals(packageIdentity));
        registryCanary.Dispose(); using var removedRegistry = Registry.CurrentUser.OpenSubKey(registryCanary.Path, false);
        bool registryRemoved = removedRegistry is null;
        await File.WriteAllTextAsync(Path.Combine(directory, "sandbox-cleanup.json"), JsonSerializer.Serialize(new { profileExisted, profileRemoved, aclRestored, registryRemoved, desktopClosed }), cancellationToken);
        if (!profileExisted || !profileRemoved || !aclRestored || !registryRemoved || !desktopClosed) { throw new InvalidOperationException("Synthetic sandbox resource cleanup was not fully verified."); }
    }

    /// <summary>Builds and executes only a generated BCL-only fixture under the same constrained token, without any remote package source.</summary>
    private static async Task RunSdkControlAsync(string directory, string stage, Dictionary<string, string> environment, WindowsSandboxProfile profile, CancellationToken cancellationToken)
    {
        string project = Path.Combine(stage, "Project"); Directory.CreateDirectory(project);
        string projectFile = Path.Combine(project, "Fixture.csproj"), config = Path.Combine(project, "NuGet.Config");
        await File.WriteAllTextAsync(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(project, "Program.cs"), "Console.WriteLine(\"synthetic constrained SDK success\");\n", cancellationToken);
        await File.WriteAllTextAsync(config, "<configuration><packageSources><clear /></packageSources></configuration>", cancellationToken);
        environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false"; environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true"; environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        environment["NUGET_PACKAGES"] = Path.Combine(stage, "Packages"); environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        environment["APPDATA"] = Path.Combine(stage, "AppData"); environment["PROGRAMDATA"] = Path.Combine(stage, "ProgramData");
        environment["ALLUSERSPROFILE"] = environment["PROGRAMDATA"];
        environment["PROGRAMFILES"] = Path.Combine(stage, "Programs"); environment["PROGRAMFILES(X86)"] = environment["PROGRAMFILES"];
        environment["DOTNET_ROOT"] = Path.GetDirectoryName(Environment.ProcessPath!)!;
        foreach (string path in new[] { environment["APPDATA"], environment["PROGRAMDATA"], environment["PROGRAMFILES"], environment["NUGET_PACKAGES"] }) { Directory.CreateDirectory(path); }
        environment["PATH"] = Path.GetDirectoryName(Environment.ProcessPath!)! + ";" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        var observations = new List<object>();
        foreach (var (name, arguments) in new[]
        {
            ("restore", new[] { "restore", projectFile, "--configfile", config, "--disable-parallel", "-p:UseSharedCompilation=false", "-nr:false", "-v", "minimal" }),
            ("build", new[] { "build", projectFile, "--no-restore", "--disable-build-servers", "-p:UseSharedCompilation=false", "-nr:false", "-v", "minimal" }),
            ("run", new[] { Path.Combine(project, "bin", "Debug", "net10.0", "Fixture.dll") })
        })
        {
            var request = new CommandExecutionRequest(Environment.ProcessPath!, arguments, project).Validated();
            using var process = OwnedCommandProcess.Start(request, environment, profile);
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken); var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            bool timedOut = false;
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { timedOut = true; }
            finally { process.Terminate(); await process.WaitForEmptyAsync(); }
            string text = await output, stderr = await error;
            observations.Add(new { name, timedOut, exitCode = process.ExitCode, output = text, error = stderr });
            await File.WriteAllTextAsync(Path.Combine(directory, "sandbox-sdk-control.json"), JsonSerializer.Serialize(observations), cancellationToken);
            if (timedOut || process.ExitCode != 0 || name == "run" && text.Trim() != "synthetic constrained SDK success")
            { throw new InvalidOperationException("Constrained SDK " + name + " failed: " + text[..Math.Min(2000, text.Length)] + stderr[..Math.Min(2000, stderr.Length)]); }
        }
        Console.WriteLine("Constrained SDK control passed: offline BCL-only restore, actual compilation and exact observed execution under LPAC, private desktop and job UI restrictions.");
    }

    /// <summary>Reads only the fixture thread's own desktop identity without capturing windows, pixels or input.</summary>
    private static string CurrentDesktopName()
    {
        nint desktop = GetThreadDesktop(GetCurrentThreadId()); _ = GetUserObjectInformationW(desktop, 2, 0, 0, out uint required);
        if (required is < 2 or > 1024) { throw new InvalidOperationException("Unexpected desktop name length."); }
        nint buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (GetUserObjectInformationW(desktop, 2, buffer, required, out _) == 0) { throw new InvalidOperationException("Cannot read own desktop identity."); }
            return Marshal.PtrToStringUni(buffer) ?? throw new InvalidOperationException("Missing desktop name.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Reads the initial DWORD of scalar token information or the capabilities group count.</summary>
    private static int TokenInteger(SafeAccessTokenHandle token, int type)
    {
        _ = GetTokenInformation(token, type, 0, 0, out uint required);
        if (required is < 4 or > 65536) { throw new InvalidOperationException("Unexpected token information size."); }
        nint buffer = Marshal.AllocHGlobal((int)required);
        try { if (GetTokenInformation(token, type, buffer, required, out _) == 0) { throw new InvalidOperationException("Cannot query token information."); } return Marshal.ReadInt32(buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Queries only the fixture's own token.</summary>
    [LibraryImport("advapi32.dll", SetLastError = true)] private static partial int OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    /// <summary>Reads kernel token state rather than inferring isolation from a process name.</summary>
    [LibraryImport("advapi32.dll", SetLastError = true)] private static partial int GetTokenInformation(SafeAccessTokenHandle token, int type, nint buffer, uint size, out uint returned);
    /// <summary>Reads the active win32k system-call mitigation.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int GetProcessMitigationPolicy(SafeProcessHandle process, int type, out uint information, uint size);
    /// <summary>Queries only the job associated with this fixture.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int QueryInformationJobObject(nint job, int type, out uint information, uint size, out uint returned);
    /// <summary>Reads its own thread identity.</summary>
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    /// <summary>Reads the current fixture thread's desktop handle without opening another desktop.</summary>
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint GetThreadDesktop(uint id);
    /// <summary>Reads only object-name metadata for an already owned desktop.</summary>
    [LibraryImport("user32.dll", SetLastError = true)] private static partial int GetUserObjectInformationW(nint handle, int index, nint information, uint size, out uint returned);
    /// <summary>Attempts only an access check for the input desktop; no pixels, windows or input are read.</summary>
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint OpenInputDesktop(uint flags, int inherit, uint access);
    /// <summary>Closes a desktop if an unexpected access-check success occurs.</summary>
    [LibraryImport("user32.dll", SetLastError = true)] private static partial int CloseDesktop(nint desktop);
    /// <summary>Attempts only to open the synthetic canary for ACL modification; it never changes that ACL.</summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
}
