using Veyra.Configuration;
using Veyra.Interop;
using Veyra.Monitors;

namespace Veyra.Bar;

/// <summary>Berechnet das Leistenrechteck in physischen Pixeln für einen Monitor.</summary>
public static class BarPlacement
{
    public static RECT Compute(AppearanceSettings appearance, MonitorInfo monitor)
    {
        var bounds = monitor.Bounds;
        double scale = monitor.Scale;

        if (!appearance.IsDynamic)
        {
            // Fixiert: volle Breite, oben angedockt.
            int height = Math.Max(1, (int)Math.Round(appearance.Height * scale));
            return new RECT(bounds.Left, bounds.Top, bounds.Right, bounds.Top + height);
        }

        // Dynamisch: eigene Größe und Position relativ zum Monitor (geräteunabhängige Pixel → physisch).
        int w = Math.Max(1, (int)Math.Round(appearance.Width * scale));
        int h = Math.Max(1, (int)Math.Round(appearance.FloatingHeight * scale));
        double monitorWidthDip = bounds.Width / scale;
        double left = appearance.FloatingLeft ?? (monitorWidthDip - appearance.Width) / 2;
        double top = appearance.FloatingTop ?? appearance.MarginTop;

        int x = bounds.Left + (int)Math.Round(left * scale);
        int y = bounds.Top + (int)Math.Round(top * scale);

        // Auf dem Monitor halten (ist das Fenster größer als der Monitor, bleibt die linke/obere Kante sichtbar).
        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - w));
        y = Math.Max(bounds.Top, Math.Min(y, bounds.Bottom - h));
        return new RECT(x, y, x + w, y + h);
    }
}
