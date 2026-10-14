using System.Text.Json;

// A windowless GUI-subsystem fixture: no desktop access, network, credentials or personal storage.
string marker = Path.Combine(AppContext.BaseDirectory, "observed.json");
await File.WriteAllTextAsync(marker + ".tmp",
    JsonSerializer.Serialize(new { ProcessId = Environment.ProcessId, WorkingDirectory = Environment.CurrentDirectory }));
File.Move(marker + ".tmp", marker, overwrite: true);
await Task.Delay(TimeSpan.FromSeconds(4));
