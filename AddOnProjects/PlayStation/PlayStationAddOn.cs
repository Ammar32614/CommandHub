using System.Windows;
using CommandHub.AddOns;

namespace CommandHub.AddOn.PlayStation;

public sealed class PlayStationAddOn : IAddOn
{
    public void Start(IAddOnHost host) { }
    public UIElement CreatePage() => new System.Windows.Controls.TextBlock { Text = "PlayStation" };
}
