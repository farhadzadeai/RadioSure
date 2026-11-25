using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using RadioClassic.Models;

namespace RadioClassic.Tests;

/// <summary>Probes whether GridView DisplayMemberBinding actually renders cell text.</summary>
public class WpfBindingProbe
{
    [Fact]
    public void GridView_RendersAllColumns()
    {
        Exception? error = null;
        string[] cellTexts = [];
        var thread = new Thread(() =>
        {
            try
            {
                var station = new Station { Name = "Probe", Country = "Germany", Tags = "rock", Language = "german", Codec = "MP3", Bitrate = 128 };
                var listView = new ListView { ItemsSource = new[] { station } };
                var grid = new GridView();
                grid.Columns.Add(new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 100 });
                grid.Columns.Add(new GridViewColumn { Header = "Country", DisplayMemberBinding = new System.Windows.Data.Binding("Country"), Width = 80 });
                grid.Columns.Add(new GridViewColumn { Header = "Genre", DisplayMemberBinding = new System.Windows.Data.Binding("GenreDisplay"), Width = 80 });
                listView.View = grid;
                var window = new Window { Content = listView, Width = 400, Height = 200 };
                window.Show();
                // force layout/render pass
                listView.Measure(new Size(400, 200));
                listView.Arrange(new Rect(0, 0, 400, 200));
                listView.UpdateLayout();
                Thread.Sleep(500);
                listView.UpdateLayout();
                var results = new System.Collections.Generic.List<string>();
                for (var i = 0; i < 3; i++)
                {
                    var cell = FindVisualChild<TextBlock>(listView, tb => tb.Text == "Germany" || tb.Text == "Probe" || tb.Text.Contains("Rock"));
                }
                var all = new System.Collections.Generic.List<string>();
                CollectTexts(listView, all);
                cellTexts = all.ToArray();
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);
        Assert.Null(error);
        Assert.Contains("Probe", cellTexts);
        Assert.Contains("Germany", cellTexts);
    }

    private static void CollectTexts(DependencyObject root, System.Collections.Generic.List<string> acc)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb) acc.Add(tb.Text);
            CollectTexts(child, acc);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool> pred) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && pred(t)) return t;
            var r = FindVisualChild(child, pred);
            if (r is not null) return r;
        }
        return null;
    }
}
