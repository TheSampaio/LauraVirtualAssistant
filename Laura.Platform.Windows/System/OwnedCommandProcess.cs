using System.ComponentModel;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Laura.Core.Execution;
using Microsoft.Win32.SafeHandles;

namespace Laura.Platform.Windows.System;

/// <summary>Owns an anonymous Windows job assigned atomically at process creation. This controls lifetime, not filesystem or network access.</summary>
internal sealed partial class OwnedCommandProcess : IDisposable
{
    private readonly SafeFileHandle _job;
    private readonly SafeProcessHandle _process;
    internal int Id { get; }
    internal StreamReader StandardOutput { get; }
    internal StreamReader StandardError { get; }

    /// <summary>Retains the native identities and parent pipe ends. Args: job: Exclusive job; process: Native process; id: Diagnostic PID; output: Read pipe; error: Read pipe.</summary>
    private OwnedCommandProcess(SafeFileHandle job, SafeProcessHandle process, int id, SafeFileHandle output, SafeFileHandle error)
    {
        _job = job; _process = process; Id = id;
        StreamReader? reader = null;
        try
        {
            reader = new(new FileStream(output, FileAccess.Read), Encoding.UTF8, true, 1024);
            StandardError = new(new FileStream(error, FileAccess.Read), Encoding.UTF8, true, 1024); StandardOutput = reader;
        }
        catch { reader?.Dispose(); output.Dispose(); error.Dispose(); throw; }
    }

    /// <summary>Creates a child with only its three pipe handles inherited and the job attached before any child instruction. Args: request: Reviewed request; environment: Controlled environment. Returns: Exclusive native lifetime. Raises: Win32Exception on any setup or creation failure.</summary>
    internal static unsafe OwnedCommandProcess Start(CommandExecutionRequest request, IReadOnlyDictionary<string, string> environment)
    {
        using var input = CreatePipePair(); using var output = CreatePipePair(); using var error = CreatePipePair();
        Check(SetHandleInformation(input.Write, 1, 0)); Check(SetHandleInformation(output.Read, 1, 0)); Check(SetHandleInformation(error.Read, 1, 0));
        var job = new SafeFileHandle(CreateJobObjectW(0, null), true);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        SafeProcessHandle? process = null;
        nint attributes = 0; bool initialized = false;
        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE; breakaway is deliberately absent.
            Check(SetInformationJobObject(job, 9, &limits, (uint)sizeof(ExtendedLimits)));
            nuint size = 0; _ = InitializeProcThreadAttributeList(0, 2, 0, ref size);
            if (size == 0 || size > 65536) { throw new Win32Exception("Invalid native process attribute allocation."); }
            attributes = Marshal.AllocHGlobal(checked((int)size));
            Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size)); initialized = true;
            nint* handles = stackalloc nint[3] { input.Read.DangerousGetHandle(), output.Write.DangerousGetHandle(), error.Write.DangerousGetHandle() };
            nint jobValue = job.DangerousGetHandle();
            Check(UpdateProcThreadAttribute(attributes, 0, 0x20002, handles, (nuint)(3 * sizeof(nint)), 0, 0));
            Check(UpdateProcThreadAttribute(attributes, 0, 0x2000D, &jobValue, (nuint)sizeof(nint), 0, 0));
            var startup = new StartupInfoEx { Startup = new StartupInfo { Size = sizeof(StartupInfoEx), Flags = 0x100,
                Input = handles[0], Output = handles[1], Error = handles[2] }, Attributes = attributes };
            string command = Quote(request.Executable) + string.Concat(request.Arguments.Select(argument => " " + Quote(argument)));
            char[] commandBuffer = (command + '\0').ToCharArray();
            string environmentBlock = string.Join('\0', environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
            ProcessInformation information;
            fixed (char* commandPointer = commandBuffer)
            fixed (char* environmentPointer = environmentBlock)
            { Check(CreateProcessW(request.Executable, commandPointer, 0, 0, 1, 0x08080400, environmentPointer, request.WorkingDirectory, ref startup, out information)); }
            process = new SafeProcessHandle(information.Process, true);
            using var thread = new SafeWaitHandle(information.Thread, true);
            // Closing the only parent writer gives stdin immediate EOF. Child pipe ends close before readers start.
            input.Write.Dispose(); input.Read.Dispose(); output.Write.Dispose(); error.Write.Dispose();
            var owned = new OwnedCommandProcess(job, process, information.ProcessId, output.TakeRead(), error.TakeRead());
            process = null; return owned;
        }
        catch { job.Dispose(); process?.Dispose(); throw; }
        finally
        {
            if (initialized) { DeleteProcThreadAttributeList(attributes); }
            if (attributes != 0) { Marshal.FreeHGlobal(attributes); }
            GC.KeepAlive(job);
        }
    }

    /// <summary>Waits on the retained process handle, never a reopened PID. Args: cancellationToken: Stop waiting. Returns: Completion when the initial process exits.</summary>
    internal async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        using var wait = new EventWaitHandle(false, EventResetMode.AutoReset);
        wait.SafeWaitHandle = new SafeWaitHandle(_process.DangerousGetHandle(), false);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(wait, static (state, _) => ((TaskCompletionSource)state!).TrySetResult(), completion, Timeout.Infinite, true);
        try { await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally { registration.Unregister(null); GC.KeepAlive(_process); }
    }

    /// <summary>Reads the initial process's observed exit status. Returns: Exit code, or null while the retained handle remains unsignaled.</summary>
    internal int? ExitCode
    {
        get { if (WaitForSingleObject(_process, 0) != 0) { return null; } Check(GetExitCodeProcess(_process, out uint code)); return unchecked((int)code); }
    }

    /// <summary>Terminates only members of the exclusively retained job, including descendants whose original parent exited.</summary>
    internal void Terminate() => Check(TerminateJobObject(_job, 1));

    /// <summary>Confirms that the kernel has removed every job member after termination. Returns: Completion within five seconds. Raises: TimeoutException if members remain.</summary>
    internal async Task WaitForEmptyAsync()
    {
        long started = Stopwatch.GetTimestamp();
        while (HasActiveProcesses())
        {
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5)) { throw new TimeoutException("Owned process job did not drain."); }
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Reads active membership from the job's basic accounting structure. Returns: Whether any owned member remains.</summary>
    private unsafe bool HasActiveProcesses()
    {
        byte* accounting = stackalloc byte[48];
        Check(QueryInformationJobObject(_job, 1, accounting, 48, out _));
        return BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(accounting + 40, 4)) != 0;
    }

    /// <inheritdoc />
    public void Dispose() { _job.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); _process.Dispose(); }

    /// <summary>Encodes one literal argument with Windows CRT quote/backslash rules. Args: value: Literal argument. Returns: Always-quoted command-line token.</summary>
    private static string Quote(string value)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', slashes * (character == '"' ? 2 : 1) + (character == '"' ? 1 : 0));
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>Creates inheritable anonymous pipe ends; parent-only ends are explicitly made non-inheritable before launch. Returns: Owned pipe pair.</summary>
    private static PipePair CreatePipePair()
    {
        var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
        Check(CreatePipe(out nint read, out nint write, ref security, 0));
        return new(new SafeFileHandle(read, true), new SafeFileHandle(write, true));
    }

    /// <summary>Preserves native failure codes without including command or output content. Args: result: BOOL result. Raises: Win32Exception on failure.</summary>
    private static void Check(int result) { if (result == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); } }

    /// <summary>Transfers read-handle ownership exactly once while disposing every remaining pipe end.</summary>
    private sealed class PipePair(SafeFileHandle read, SafeFileHandle write) : IDisposable
    {
        private bool _transferred;
        internal SafeFileHandle Read { get; } = read;
        internal SafeFileHandle Write { get; } = write;
        /// <summary>Moves the read end to its stream. Returns: Live read handle.</summary>
        internal SafeFileHandle TakeRead() { _transferred = true; return Read; }
        /// <inheritdoc />
        public void Dispose() { if (!_transferred) { Read.Dispose(); } Write.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Size; internal nint Descriptor; internal int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal nint Process, Thread; internal int ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        internal int Size; internal nint Reserved, Desktop, Title; internal int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        internal short Show, ReservedSize; internal nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Startup; internal nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinWorkingSet, MaxWorkingSet; internal uint ActiveProcesses; internal nuint Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { internal BasicLimits Basic; internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }

    /// <summary>Creates an unnamed, non-inheritable job.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateJobObjectW(nint security, string? name);
    /// <summary>Applies job lifetime limits before any process is created.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int SetInformationJobObject(SafeFileHandle job, int type, void* information, uint length);
    /// <summary>Terminates all current members of the retained job.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int TerminateJobObject(SafeFileHandle job, uint code);
    /// <summary>Reads kernel-owned accounting without enumerating or reopening process identifiers.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int QueryInformationJobObject(SafeFileHandle job, int type, void* information, uint size, out uint returnedSize);
    /// <summary>Creates an anonymous pipe.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int CreatePipe(out nint read, out nint write, ref SecurityAttributes attributes, uint size);
    /// <summary>Restricts inheritance of a parent-only pipe end.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    /// <summary>Sizes and initializes the extended creation attribute list.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    /// <summary>Adds a reviewed handle or job list to process creation.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static unsafe partial int UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, void* value, nuint size, nint previous, nint returnedSize);
    /// <summary>Releases the attribute list's internal native allocations.</summary>
    [LibraryImport("kernel32.dll")] private static partial void DeleteProcThreadAttributeList(nint list);
    /// <summary>Creates the process with explicit application, environment, handle allowlist and atomic job assignment.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static unsafe partial int CreateProcessW(string application, char* command, nint processSecurity, nint threadSecurity, int inherit, uint flags, char* environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
    /// <summary>Reads exit status from the retained process handle.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int GetExitCodeProcess(SafeProcessHandle process, out uint code);
    /// <summary>Tests whether the retained process has exited.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
