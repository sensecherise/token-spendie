namespace TokenSpendie.Windows.Models;

public record LabeledWindow(
    string Label,
    string Detail,
    ResetStyle ResetStyle,
    UsageWindow Window);
