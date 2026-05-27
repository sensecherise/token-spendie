using TokenSpendie.Windows.Services;

namespace TokenSpendie.Windows.ViewModels;

public sealed class FloatingPanelViewModel
{
    private readonly PreferencesStore _prefs;
    public DetailPanelViewModel Panel { get; }

    public double? Left => _prefs.FloatingPanelLeft;
    public double? Top => _prefs.FloatingPanelTop;
    public double Width => _prefs.FloatingPanelWidth;
    public double Height => _prefs.FloatingPanelHeight;

    public FloatingPanelViewModel(PreferencesStore prefs, DetailPanelViewModel panelVm)
    {
        _prefs = prefs;
        Panel = panelVm;
    }

    public void Save(double left, double top, double width, double height)
    {
        _prefs.FloatingPanelLeft = left;
        _prefs.FloatingPanelTop = top;
        _prefs.FloatingPanelWidth = width;
        _prefs.FloatingPanelHeight = height;
    }
}
