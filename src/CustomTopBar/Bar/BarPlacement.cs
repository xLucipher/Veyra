using CustomTopBar.Configuration;
using CustomTopBar.Interop;
using CustomTopBar.Monitors;

namespace CustomTopBar.Bar;

/// <summary>Berechnet das Leistenrechteck in physischen Pixeln für einen Monitor.</summary>
public static class BarPlacement
{
    public static RECT Compute(AppearanceSettings appearance, MonitorInfo monitor)
    {
        int height = Math.Max(1, (int)Math.Round(appearance.Height * monitor.Scale));

        // Modus A (FullWidth). Floating ist in Version 1 nicht implementiert; BarManager protokolliert das
        // und verwendet ebenfalls diese Geometrie. Floating ergänzt hier später Breite, Zentrierung und Abstand.
        var bounds = monitor.Bounds;
        return new RECT(bounds.Left, bounds.Top, bounds.Right, bounds.Top + height);
    }
}
