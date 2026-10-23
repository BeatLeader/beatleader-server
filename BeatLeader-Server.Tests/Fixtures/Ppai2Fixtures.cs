using System.Text.Json;
using BeatLeader_Server.Utils;

namespace BeatLeader_Server.Tests.Fixtures
{
    public static class Ppai2Fixtures
    {
        public static readonly string[] SpeedKeys = { "none", "SS", "FS", "SFS", "BFS", "BSF" };

        public record Variant(string Key, double PredictedAcc, double AccRating, double PassRating, double TechRating,
            double Stars, double SliderPercentage, int NoteCount, List<(double x, double y)> PointList);

        public record Fixture(string Name, string Json, Dictionary<string, Variant> Variants);

        public static IEnumerable<string> Names() =>
            Directory.GetFiles(Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "ppai2"), "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)!;

        public static Fixture Load(string name)
        {
            var json = File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "ppai2", name + ".json"));
            using var doc = JsonDocument.Parse(json);
            var variants = new Dictionary<string, Variant>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var v = property.Value;
                var lack = v.GetProperty("lack_map_calculation");
                variants[property.Name] = new Variant(
                    property.Name,
                    v.GetProperty("predicted_acc").GetDouble(),
                    v.GetProperty("acc_rating").GetDouble(),
                    lack.GetProperty("balanced_pass_diff").GetDouble(),
                    lack.GetProperty("balanced_tech").GetDouble(),
                    v.GetProperty("stars").GetDouble(),
                    lack.GetProperty("slider_percentage").GetDouble(),
                    lack.GetProperty("note_count").GetInt32(),
                    v.GetProperty("pointlist").EnumerateArray()
                        .Select(p => (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()))
                        .ToList());
            }
            return new Fixture(name, json, variants);
        }

        public static SongUtils.ExmachinaResponse Response(Fixture fixture) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<SongUtils.ExmachinaResponse>(fixture.Json)!;

        public static IEnumerable<object[]> FixtureNames() => Names().Select(n => new object[] { n });
    }
}
