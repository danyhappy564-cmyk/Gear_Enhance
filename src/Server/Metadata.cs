using SPTarkov.Server.Core.Models.Spt.Mod;

namespace GearEnhance;

public record Metadata : IModMetadata
{
    public string ModGuid { get; init; } = "rf.gearenhance";
    public string Name { get; init; } = "Gear Enhance";
    public string Author { get; init; } = "R_F";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(EnhanceProtocol.ModVersion);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/danyhappy564-cmyk/Gear_Enhance";
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}
