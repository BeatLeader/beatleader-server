using System.Text.Json;
using BeatLeader_Server.Utils;

namespace BeatLeader_Server.Tests.Fixtures
{
    public static class Ppai2Fixtures
    {
        public static readonly string[] SpeedKeys = { "none", "SS", "FS", "SFS", "BFS", "BSF" };

        public record Variant(string Key, double PredictedAcc, double AccRating, double PassRating, double TechRating,
            double Stars, double SliderPercentage, int NoteCount, double NoteSpan, List<(double x, double y)> PointList);

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
                    lack.GetProperty("note_span").GetDouble(),
                    v.GetProperty("pointlist").EnumerateArray()
                        .Select(p => (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()))
                        .ToList());
            }
            return new Fixture(name, json, variants);
        }

        public static SongUtils.ExmachinaResponse Response(Fixture fixture) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<SongUtils.ExmachinaResponse>(fixture.Json)!;

        // The server stores the predicted acc as a float, so the curve point RatingAPI puts at it (Math.Round(predictedAcc, 6))
        // can land 1e-6 away (up to ~2e-5 of the value there). Compares the curves' values at every RatingAPI point and halfway between them.
        public static void AssertCurveMatches(string label, Variant v, IReadOnlyList<System.Numerics.Vector2> curve)
        {
            var expected = v.PointList.Select(p => new System.Numerics.Vector2((float)p.x, (float)p.y)).ToList();
            var xs = v.PointList.Select(p => p.x).Concat(v.PointList.Zip(v.PointList.Skip(1), (a, b) => (a.x + b.x) / 2));
            foreach (var x in xs)
            {
                float e = Utils.RebalancedPp.PpCalculator.CurveValue((float)x, expected);
                float a = Utils.RebalancedPp.PpCalculator.CurveValue((float)x, curve);
                double tolerance = Math.Max(1e-5, Math.Abs(e) * 5e-5);
                Xunit.Assert.True(Math.Abs(a - e) < tolerance, $"{label} at {x}: {a} vs {e}");
            }
        }

        public static IEnumerable<object[]> FixtureNames() => Names().Select(n => new object[] { n });
    }
}
