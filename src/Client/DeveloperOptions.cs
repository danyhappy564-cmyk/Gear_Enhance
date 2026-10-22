using System.Collections.Generic;

namespace GearEnhance;

// ConfigurationManager reads tags with this conventional class/field shape.
// The stored cfg keys stay unchanged; only the F12 presentation is overridden.
internal sealed class ConfigurationManagerAttributes
{
    public bool? Browsable;
    public string Category;
    public string DispName;
}

internal sealed class DeveloperOptions
{
    private readonly List<ConfigurationManagerAttributes> entries = new List<ConfigurationManagerAttributes>();
    private bool visible;
    internal ConfigurationManagerAttributes Describe(string section, string key)
    {
        var metadata = new ConfigurationManagerAttributes
        {
            Browsable = visible, Category = "99. 개발자 옵션", DispName = section + " · " + key
        };
        entries.Add(metadata);
        return metadata;
    }
    internal void SetVisible(bool value)
    {
        visible = value;
        foreach (var entry in entries) entry.Browsable = value;
    }
}
