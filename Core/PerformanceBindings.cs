namespace SMAP_WPF.Core;

public sealed record PerformanceBindingProfile
{
    public string CapabilityId { get; init; } = "";
    public int CapabilityVersion { get; init; }
    public Dictionary<string, ushort> Keys { get; init; } = new();
    public static PerformanceBindingProfile FromLegacy(InstrumentCapability capability, IReadOnlyList<ushort> legacy) => new()
    {
        CapabilityId = capability.Id, CapabilityVersion = capability.Version,
        Keys = capability.Keys.ToDictionary(k=>k.Id,k=>legacy[k.LegacyKey])
    };
    public bool Valid(InstrumentCapability capability) => CapabilityId == capability.Id && CapabilityVersion == capability.Version
        && Keys.Count == capability.Keys.Count && capability.Keys.All(k=>Keys.TryGetValue(k.Id,out ushort vk)&& vk is >0 and <=0xFE)
        && Keys.Values.Distinct().Count()==Keys.Count;
}

public static class GameOutputBindings
{
    public static Func<TimedProjectNote, ushort> Resolve(SmapProject project, Func<InstrumentCapability,PerformanceBindingProfile> bindings)
    {
        PerformanceCompatibility.RequirePlayable(project, allowUnknown:true);
        var lookup = new Dictionary<(Guid,int),ushort>();
        foreach(var track in project.AudibleTracks())
        {
            var c=InstrumentCapabilities.ForTrack(track) ?? throw new InvalidOperationException("UnknownCapability");
            var profile=bindings(c); if(!profile.Valid(c))throw new InvalidOperationException("InvalidPerformanceBinding");
            foreach(var note in track.Notes)
            {
                var k=c.Resolve(note.Key,track.KeyDomain) ?? throw new InvalidOperationException("KeyNotPresent");
                lookup[(track.Id,note.Key)]=profile.Keys[k.Id];
            }
        }
        return note => lookup.TryGetValue((note.TrackId,note.Key),out ushort vk)?vk:throw new InvalidOperationException("MissingOutputBinding");
    }
}
