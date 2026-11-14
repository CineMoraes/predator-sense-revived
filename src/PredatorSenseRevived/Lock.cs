using System.Windows;
using System.Windows.Media;

namespace PredatorSenseRevived;

/// <summary>
/// Marks a control (or a panel of controls) as locked: it shows the real value, but a click only explains why it is locked.
/// Used for features whose firmware write is not tested yet (project rule: no firmware write before a supervised test).
/// </summary>
public static class Lock
{
    public static readonly DependencyProperty ReasonProperty =
        DependencyProperty.RegisterAttached("Reason", typeof(string), typeof(Lock), new PropertyMetadata(null));

    public static string? GetReason(DependencyObject d) => (string?)d.GetValue(ReasonProperty);
    public static void SetReason(DependencyObject d, string? value) => d.SetValue(ReasonProperty, value);

    /// <summary>Walks up from the clicked element and returns the first lock reason found.</summary>
    public static string? Find(DependencyObject? d)
    {
        while (d != null)
        {
            if (GetReason(d) is string r) return r;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
