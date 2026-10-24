namespace Laura.Core.Configuration;

/// <summary>Resolves the flat model store and separate application runtime using the Windows Documents known folder.</summary>
public static class LocalModelPaths
{
    /// <summary>Gets the existing Laura data root without creating any directories.</summary>
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Laura Virtual Assistant");
    /// <summary>Gets the default flat model directory beside Brain.</summary>
    public static string Models => Path.Combine(DataRoot, "Models");
    /// <summary>Gets executable and phonemizer storage outside the model directory.</summary>
    public static string Runtime => Path.Combine(DataRoot, "Runtime");
}
