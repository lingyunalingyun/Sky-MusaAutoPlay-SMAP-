namespace SMAP_WPF.Core;

public sealed record MappingWindowSnapshot(long Handle,int ProcessId,long ProcessStarted,int Width,int Height,uint Dpi,bool Foreground,bool Obscured=false);
public sealed record MappingVisibility(bool ShowHints,bool NeedsCalibration,string Reason);
public static class MappingWindowPolicy
{
    public static MappingVisibility Evaluate(MappingWindowSnapshot target,MappingWindowSnapshot? current,SheetCalibration? calibration,
        InstrumentCapability capability,string bindingFingerprint)
    {
        if(current==null||current.Handle!=target.Handle||current.ProcessId!=target.ProcessId||current.ProcessStarted!=target.ProcessStarted)
            return new(false,false,"inactive");
        if(calibration==null)return new(false,true,"uncalibrated");
        if(!calibration.Matches(capability,bindingFingerprint,current.Width,current.Height,current.Dpi))return new(false,true,"changed");
        if(!current.Foreground)return new(false,false,"focus");
        if(current.Obscured)return new(false,false,"covered");
        return new(true,false,"ready");
    }
}
