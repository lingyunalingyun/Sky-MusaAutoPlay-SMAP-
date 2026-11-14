using System.Windows;
using SMAP_WPF.Core;

namespace SMAP_WPF;

// Existing standalone entry points share the same content as the unified sharing window.
public sealed class ScoreExportDialog : ChromeWindow
{
    readonly ScoreExportPane _pane;
    public ScoreExportDialog(Window owner, SmapProject project, Guid activeTrack, SharingVersion? share = null) : base(Lang.S("score.title"), 980)
    {
        Owner = owner; _pane = new(owner: this, project, activeTrack, share) { Height = 610, Margin = new Thickness(12) };
        SetBody(_pane); Closed += (_, _) => _pane.Dispose();
    }
    public static string ErrorText(string error) => ScoreExportPane.ErrorText(error);
}
