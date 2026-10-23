#if RATINGAPI_PARITY
using BeatLeader_Server.Models;
using BeatLeader_Server.Tests.Fixtures;
using BeatLeader_Server.Utils;
using Xunit;
using RA = RatingAPI.Pp;

namespace BeatLeader_Server.Tests.RebalancedPp
{
    public class ScorePpParityTests
    {
        private static readonly float[] Accuracies = { 0.5f, 0.6f, 0.75f, 0.9f, 0.94f, 0.95f, 0.955f, 0.96f, 0.965f, 0.97f, 0.975f, 0.98f, 0.985f, 0.99f, 0.9925f, 0.995f, 0.9975f, 0.999f, 1f };
        private static readonly string[] Modifiers = { "", "SS", "FS", "SF", "NF", "SC", "GN", "NA", "NB", "NO", "OP", "DA,FS", "FS,SC", "SF,GN,NO", "PM,SA" };

        private static RA.MapRatings RaRatings(Ppai2Fixtures.Variant v) =>
            new((float)v.AccRating, (float)v.PassRating, (float)v.TechRating,
                RA.PpCalculator.ToCurve(v.PointList.Select(p => new RatingAPI.Controllers.Point(p.x, p.y))));

        private static RA.MapRatings? RaSpeedRatings(Ppai2Fixtures.Fixture fixture, string modifiers)
        {
            foreach (var modifier in modifiers.ToUpper().Split(","))
            {
                string? key = modifier switch { "SS" => "SS", "FS" => "FS", "SF" => "SFS", _ => null };
                if (key != null) return RaRatings(fixture.Variants[key]);
            }
            return null;
        }

        [Theory]
        [MemberData(nameof(Ppai2Fixtures.FixtureNames), MemberType = typeof(Ppai2Fixtures))]
        public void PpFromScore_MatchesRatingApiScorePp(string name)
        {
            var fixture = Ppai2Fixtures.Load(name);
            var diff = new DifficultyDescription { Notes = fixture.Variants["none"].NoteCount };
            RatingUtils.UpdateFromResponse(diff, Ppai2Fixtures.Response(fixture), null);
            var modifierValues = ModifiersMap.RankedMap();

            foreach (var modifiers in Modifiers)
            {
                foreach (var accuracy in Accuracies)
                {
                    var expected = RA.PpCalculator.ScorePp(accuracy, modifiers, RaRatings(fixture.Variants["none"]), RaSpeedRatings(fixture, modifiers), true);
                    var actual = ReplayUtils.PpFromScore(
                        accuracy,
                        LeaderboardContexts.General,
                        modifiers,
                        modifierValues,
                        diff.ModifiersRating,
                        diff.AccRating ?? 0,
                        diff.PassRating ?? 0,
                        diff.TechRating ?? 0,
                        ReplayUtils.GetCurveInputs(diff),
                        false);

                    AssertClose(expected.Item1, actual.Item1, $"{name} '{modifiers}' {accuracy} pp");
                    AssertClose(expected.Item2, actual.Item2, $"{name} '{modifiers}' {accuracy} bonus pp");
                    AssertClose(expected.Item3, actual.Item3, $"{name} '{modifiers}' {accuracy} pass pp");
                    AssertClose(expected.Item4, actual.Item4, $"{name} '{modifiers}' {accuracy} acc pp");
                    AssertClose(expected.Item5, actual.Item5, $"{name} '{modifiers}' {accuracy} tech pp");
                }
            }
        }

        [Fact]
        public void Stars_MatchRatingApiToStars()
        {
            foreach (var name in Ppai2Fixtures.Names())
            {
                var fixture = Ppai2Fixtures.Load(name);
                foreach (var key in Ppai2Fixtures.SpeedKeys)
                {
                    var v = fixture.Variants[key];
                    float expected = RA.PpCalculator.ToStars(RaRatings(v));
                    float actual = ReplayUtils.ToStars((float)v.AccRating, (float)v.PassRating, (float)v.TechRating,
                        new Utils.RebalancedPp.CurveInputs((float)v.PredictedAcc, (float)v.SliderPercentage, v.NoteCount));
                    AssertClose(expected, actual, $"{name} {key} stars");
                }
            }
        }

        private static void AssertClose(float expected, float actual, string what)
        {
            double tolerance = Math.Max(0.01, Math.Abs(expected) * 1e-4);
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected}, got {actual}");
        }
    }
}
#endif
