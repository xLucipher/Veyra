using Veyra.Interop;

namespace Veyra.Monitors;

/// <summary>Momentaufnahme eines Monitors. Alle Rechtecke in physischen Pixeln.</summary>
public sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceName,
    RECT Bounds,
    RECT WorkArea,
    bool IsPrimary,
    uint Dpi,
    int RefreshRate)
{
    public double Scale => Dpi / 96.0;

    public string Description =>
        $"{DeviceName}{(IsPrimary ? " (primär)" : "")} – {Bounds.Width}×{Bounds.Height} @ {Bounds.Left},{Bounds.Top}, " +
        $"{Scale * 100:0} %{(RefreshRate > 1 ? $", {RefreshRate} Hz" : "")}";

    public bool IsSameLayout(MonitorInfo? other) =>
        other is not null && other.DeviceName == DeviceName && other.Bounds.Equals(Bounds) && other.Dpi == Dpi;
}
