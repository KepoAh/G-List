using System.Windows;
using System.Windows.Controls;
namespace TodoApp.Views;
public partial class SettingsView : UserControl
{
    public SettingsView() { InitializeComponent(); }
    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 760;
        Cards.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var cards = new[] { PreferencesCard, SoundCard, BackupCard, DataCard };
        for (int i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], wide ? i % 2 : 0);
            Grid.SetRow(cards[i], wide ? i / 2 : i);
        }
    }
}
