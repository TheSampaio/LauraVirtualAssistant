using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

/// <summary>Replaces only the cabinet resource in a freshly generated IExpress stub using documented Windows resource APIs.</summary>
public static class InstallerCabinetResource
{
    /// <summary>Receives an existing resource language without executing the generated installer.</summary>
    private delegate bool LanguageCallback(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);
    /// <summary>Loads a module strictly as data for resource inspection.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    /// <summary>Enumerates the language identity of the existing cabinet resource.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type, string name, LanguageCallback callback, IntPtr parameter);
    /// <summary>Releases the data-only module mapping.</summary>
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    /// <summary>Begins a resource transaction while preserving other generated installer metadata.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(string path, bool deleteExisting);
    /// <summary>Stages the exact cabinet bytes under their original resource identity.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(IntPtr update, IntPtr type, string name, ushort language, IntPtr data, uint size);
    /// <summary>Commits or discards the resource transaction.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr update, bool discard);

    /// <summary>Replaces a bounded single cabinet. Args: image: Newly generated non-running stub; cabinet: Verified owned cabinet. Raises: IOException or Win32Exception for invalid resources or failed updates.</summary>
    public static void Replace(string image, string cabinet)
    {
        long length = new FileInfo(cabinet).Length;
        if (length <= 0 || length > Int32.MaxValue) { throw new IOException("Installer cabinet exceeds the supported resource bound."); }
        var languages = new List<ushort>();
        IntPtr module = LoadLibraryExW(image, IntPtr.Zero, 2);
        if (module == IntPtr.Zero) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            if (!EnumResourceLanguagesW(module, new IntPtr(10), "CABINET", delegate(IntPtr ignoredModule, IntPtr ignoredType, IntPtr ignoredName, ushort language, IntPtr ignoredParameter)
                { languages.Add(language); return true; }, IntPtr.Zero)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        finally { FreeLibrary(module); }
        if (languages.Count != 1) { throw new IOException("Generated installer must have exactly one cabinet language."); }
        using (var mapping = MemoryMappedFile.CreateFromFile(cabinet, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
        using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
        {
            IntPtr update = BeginUpdateResourceW(image, false);
            if (update == IntPtr.Zero) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try
            {
                if (!UpdateResourceW(update, new IntPtr(10), "CABINET", languages[0], view.SafeMemoryMappedViewHandle.DangerousGetHandle(), (uint)length))
                { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                bool committed = EndUpdateResourceW(update, false); update = IntPtr.Zero;
                if (!committed) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                GC.KeepAlive(view);
            }
            finally { if (update != IntPtr.Zero) { EndUpdateResourceW(update, true); } }
        }
    }
}
