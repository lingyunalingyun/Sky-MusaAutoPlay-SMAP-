namespace SMAP_WPF.Core;

public static class FeatureAvailability
{
    public static bool SheetMappingEnabled
    {
        get
        {
#if SMAP_MAPPING_DEVELOPMENT
            return true;
#else
            return false;
#endif
        }
    }
    public static bool ShortcutAvailable(ShortcutAction action)
        => action is not (ShortcutAction.ToggleMapping or ShortcutAction.PauseMapping) || SheetMappingEnabled;
    public static bool RequireSheetMapping()
    {
        if(!SheetMappingEnabled)throw new NotSupportedException("SheetMappingDeferredToV31");
        return true;
    }
}
