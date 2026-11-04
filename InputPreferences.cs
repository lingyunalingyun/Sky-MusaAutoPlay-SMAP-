using System.IO;
using System.Text.Json;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed record InputPreferences
{
    public int Version { get; init; } = 1;
    public List<FunctionShortcut> Functions { get; init; } = ShortcutRules.Defaults();
    public List<PerformanceBindingProfile> Performance { get; init; } = new();
    public List<InstrumentCapability> Capabilities { get; init; } = new();
    public InstrumentCapability? Capability(string instrument)=>Capabilities.FirstOrDefault(c=>c.InstrumentId==instrument)??InstrumentCapabilities.ForInstrument(instrument);
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SMAP","input-v1.json");
    public PerformanceBindingProfile For(InstrumentCapability c, IReadOnlyList<ushort> legacy) => Performance.FirstOrDefault(p=>p.CapabilityId==c.Id && p.CapabilityVersion==c.Version)
        ?? PerformanceBindingProfile.FromLegacy(c,legacy);
    public IEnumerable<ushort> AllPerformanceKeys(IEnumerable<ushort> legacy) => (Performance.FirstOrDefault(p=>p.CapabilityId=="sky:Piano:major:0")?.Keys.Values ?? legacy)
        .Concat(Performance.SelectMany(p=>p.Keys.Values)).Distinct();
    public static InputPreferences Load()
    {
        try { if(System.IO.File.Exists(FilePath)) {var p=JsonSerializer.Deserialize<InputPreferences>(System.IO.File.ReadAllText(FilePath));
            if(p?.Valid()==true)return p;} }
        catch(Exception error) when(error is IOException or JsonException or UnauthorizedAccessException){Logger.Exception("输入配置读取失败",error);}
        return new();
    }
    public void Save()
    {
        if(!Valid())throw new InvalidDataException("InvalidInputPreferences");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { System.IO.File.WriteAllText(temporary,JsonSerializer.Serialize(this));System.IO.File.Move(temporary,FilePath,true); }
        finally { if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary); }
    }
    public bool Valid()
    {
        if(Version!=1||Functions==null||Performance==null||Capabilities==null
            ||Performance.Any(p=>p==null||string.IsNullOrWhiteSpace(p.CapabilityId)||p.CapabilityVersion<1||p.Keys==null
                ||p.Keys.Any(k=>string.IsNullOrWhiteSpace(k.Key)||k.Value is 0 or >0xFE)||p.Keys.Values.Distinct().Count()!=p.Keys.Count)
            ||Capabilities.Any(c=>c==null)||Performance.Select(p=>(p.CapabilityId,p.CapabilityVersion)).Distinct().Count()!=Performance.Count
            ||Capabilities.Select(c=>c.InstrumentId).Distinct().Count()!=Capabilities.Count)return false;
        try{foreach(var c in Capabilities)c.Validate();return ShortcutRules.Validate(Functions,AllPerformanceKeys(KeyConfig.Load()))==null;}
        catch(InvalidDataException){return false;}
    }
}
