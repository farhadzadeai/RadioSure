using RadioClassic.Data;
using RadioClassic.Models;
using Xunit;
using Xunit.Abstractions;

namespace RadioClassic.Tests;

public class RealDbTests(ITestOutputHelper output)
{
    [Fact]
    public void RealDatabase_ProjectionHasColumns()
    {
        var src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioClassic", "radioclassic.db");
        if (!File.Exists(src)) return; // skip on machines without the synced db
        var copy = Path.Combine(Path.GetTempPath(), "rc-real-" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(src, copy);
        try
        {
            using var db = new Database(copy);
            var repo = new StationRepository(db);
            var rows = repo.Search(new StationQuery());
            output.WriteLine($"rows: {rows.Count}");
            foreach (var r in rows.Take(5))
                output.WriteLine($"{r.Name[..Math.Min(25, r.Name.Length)]} | country='{r.Country}' tags='{r.Tags}' lang='{r.Language}' codec='{r.Codec}' br={r.Bitrate}");
            var withCountry = rows.Count(r => r.Country.Length > 0);
            output.WriteLine($"with country: {withCountry}/{rows.Count}");
            Assert.True(withCountry > rows.Count / 2, "most rows should have a country");
        }
        finally { if (File.Exists(copy)) File.Delete(copy); }
    }
}
