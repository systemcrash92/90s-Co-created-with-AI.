using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Seto90;

public sealed class SaveGame
{
    public string ProjectId { get; set; } = "";
    public string MapId { get; set; } = "";
    public int PlayerX { get; set; }
    public int PlayerY { get; set; }
    public double PlaySeconds { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, bool> Flags { get; set; } = [];
    public Dictionary<string, int> Variables { get; set; } = [];
    public List<string> Inventory { get; set; } = [];
    public int Money { get; set; }
    public List<PartyMemberSave> Party { get; set; } = [];
    /// <summary>Tiempo del mundo (sistema de franjas): dia desde 1 y franja manana/tarde/noche.
    /// Defaults compatibles con saves anteriores al sistema de horario.</summary>
    public int Day { get; set; } = 1;
    public string Phase { get; set; } = "manana";
}

/// <summary>Estado derivable minimo de un miembro de party: el resto se recalcula del ActorDef.</summary>
public sealed class PartyMemberSave
{
    public string ActorId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int Hp { get; set; } = 1;
    public int Mp { get; set; }
    public string WeaponId { get; set; } = "";
    public string ArmorId { get; set; } = "";
}

public sealed class SaveSystem
{
    static readonly JsonSerializerOptions SaveJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly string directory;

    public SaveSystem(string projectId)
    {
        var safeId = string.Concat(projectId.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_'));
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seto90", "Saves", safeId);
        Directory.CreateDirectory(directory);
    }

    public void Save(int slot, SaveGame state)
    {
        state.SavedAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(state, SaveJson);
        var checksum = Checksum(payload);
        var envelope = JsonSerializer.Serialize(new SaveEnvelope { Version = 1, Checksum = checksum, Payload = payload }, SaveJson);
        var finalPath = SlotPath(slot);
        var tempPath = finalPath + ".tmp";
        var backupPath = finalPath + ".bak";
        File.WriteAllText(tempPath, envelope, new UTF8Encoding(false)); // sin BOM, como project.json
        if (File.Exists(finalPath)) File.Copy(finalPath, backupPath, true);
        File.Move(tempPath, finalPath, true);
    }

    public SaveGame Load(int slot)
    {
        var finalPath = SlotPath(slot);
        try { return LoadFile(finalPath); }
        catch when (File.Exists(finalPath + ".bak")) { return LoadFile(finalPath + ".bak"); }
    }

    /// <summary>Hay partida guardada en el slot (para mostrar "Continuar" en el titulo).</summary>
    public bool HasSlot(int slot) => File.Exists(SlotPath(slot)) || File.Exists(SlotPath(slot) + ".bak");

    /// <summary>Carga sin explotar: null si el slot esta vacio o corrupto (para listar en el menu).</summary>
    public SaveGame? TryLoad(int slot)
    {
        try { return Load(slot); }
        catch { return null; }
    }

    /// <summary>Slot con el guardado mas reciente (para "Continuar" en el titulo). -1 si no hay ninguno.</summary>
    public int MostRecentSlot(int maxSlots = 3)
    {
        var best = -1;
        var bestTime = DateTimeOffset.MinValue;
        for (var slot = 0; slot < maxSlots; slot++)
        {
            var save = TryLoad(slot);
            if (save != null && save.SavedAt > bestTime) { best = slot; bestTime = save.SavedAt; }
        }
        return best;
    }

    string SlotPath(int slot) => Path.Combine(directory, $"slot{slot}.json");

    static SaveGame LoadFile(string path)
    {
        var envelope = JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllText(path), SaveJson) ?? throw new InvalidOperationException("Save vacio.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(envelope.Checksum), Encoding.UTF8.GetBytes(Checksum(envelope.Payload)))) throw new InvalidOperationException("checksum invalido");
        return JsonSerializer.Deserialize<SaveGame>(envelope.Payload, SaveJson) ?? throw new InvalidOperationException("Payload de save invalido.");
    }

    static string Checksum(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    sealed class SaveEnvelope
    {
        public int Version { get; set; }
        public string Checksum { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
