using RadioClassic.Data;
using RadioClassic.Models;
using Xunit;

namespace RadioClassic.Tests;

public class ProjectionTests
{
    [Fact]
    public void Search_PopulatesAllColumns()
    {
        var file = Path.Combine(Path.GetTempPath(), "rc-proj-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new Database(file);
            var repo = new StationRepository(db);
            repo.AddCustom(new Station
            {
                Name = "ProjTest", Url = "http://example.com/x", Country = "Germany",
                Tags = "rock,pop", Language = "german,english", Codec = "MP3", Bitrate = 128
            });
            var rows = repo.Search(new StationQuery());
            var row = rows.First(r => r.Name == "ProjTest");
            Assert.Equal("Germany", row.Country);
            Assert.Equal("rock,pop", row.Tags);
            Assert.Equal("german,english", row.Language);
            Assert.Equal("MP3", row.Codec);
            Assert.Equal(128, row.Bitrate);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
