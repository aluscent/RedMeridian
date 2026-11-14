using RedMeridian.Domain;

namespace RedMeridian.Application;

// The session chooses when to save; this class owns save paths and disk format migration.
sealed class SaveRepository(bool developerMode)
{
    readonly string defaultSave = Path.GetFullPath(Environment.GetEnvironmentVariable("RED_MERIDIAN_SAVE")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RedMeridian", developerMode ? "colony.test.json" : "colony.json"));

    string SaveDirectory => defaultSave + ".slots";
    public bool HasDefault => File.Exists(defaultSave);

    public string PathFor(string? slot)
    {
        if (string.IsNullOrEmpty(slot)) return defaultSave;
        if (slot.Length > 48 || slot != slot.Trim() ||
            !slot.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            throw new InvalidOperationException("Use 1-48 letters, numbers, spaces, hyphens, or underscores for the save name.");
        return Path.Combine(SaveDirectory,"slot-" + slot + ".json");
    }

    public object List()
    {
        var slots = new List<object>();
        if (File.Exists(defaultSave))
            slots.Add(new { id = "", name = "Default colony", updated = File.GetLastWriteTimeUtc(defaultSave) });
        if (Directory.Exists(SaveDirectory))
            foreach (var file in Directory.EnumerateFiles(SaveDirectory,"slot-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file)[5..];
                slots.Add(new { id = name, name, updated = File.GetLastWriteTimeUtc(file) });
            }
        return slots;
    }

    public SaveData? Read(string? slot)
    {
        using var input = File.OpenRead(PathFor(slot));
        return SaveCodec.Read(input);
    }

    public void Write(SpaceProgram colony,string? slot,int existingVersion)
    {
        var fullPath = PathFor(slot);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) SaveCodec.Write(output,colony.Export());
            if (File.Exists(fullPath))
            {
                if (existingVersion == 0) existingVersion = Read(slot)?.Version ?? 0;
                string backup = fullPath + (existingVersion < 3 ? ".pre-v3" : existingVersion == 3 ? ".pre-v4" : ".pre-v5");
                if (existingVersion is > 0 and < Colony.SaveVersion && !File.Exists(backup)) File.Copy(fullPath,backup);
            }
            File.Move(temporary,fullPath,overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
