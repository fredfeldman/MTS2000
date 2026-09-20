using System.Text.Json;
using MTS2000.Core.Models;

namespace MTS2000.Core.Services;

/// <summary>Saves and loads a <see cref="Codeplug"/> as a JSON file on disk.</summary>
public class CodeplugFileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public void Save(Codeplug codeplug, string filePath)
    {
        var json = JsonSerializer.Serialize(codeplug, SerializerOptions);
        File.WriteAllText(filePath, json);
    }

    public Codeplug Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Codeplug>(json, SerializerOptions)
            ?? throw new InvalidDataException("Codeplug file could not be parsed.");
    }
}
