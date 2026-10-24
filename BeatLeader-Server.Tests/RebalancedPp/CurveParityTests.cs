using BeatLeader_Server.Tests.Fixtures;
using BeatLeader_Server.Utils;
using BeatLeader_Server.Utils.RebalancedPp;
using Xunit;

namespace BeatLeader_Server.Tests.RebalancedPp
{
    public class CurveParityTests
    {
        private static CurveInputs Inputs(Ppai2Fixtures.Variant v) =>
            new CurveInputs((float)v.PredictedAcc, (float)v.SliderPercentage, v.NoteCount, (float)v.NoteSpan);

        [Theory]
        [MemberData(nameof(Ppai2Fixtures.FixtureNames), MemberType = typeof(Ppai2Fixtures))]
        public void Curve_MatchesRatingApiPointlist(string name)
        {
            var fixture = Ppai2Fixtures.Load(name);
            foreach (var key in Ppai2Fixtures.SpeedKeys)
            {
                var v = fixture.Variants[key];
                var curve = Curve.GetCurve(Inputs(v));

                Ppai2Fixtures.AssertCurveMatches($"{name} {key}", v, curve);
            }
        }

        [Theory]
        [MemberData(nameof(Ppai2Fixtures.FixtureNames), MemberType = typeof(Ppai2Fixtures))]
        public void Stars_FromLocalCurve_MatchRatingApiStars(string name)
        {
            var fixture = Ppai2Fixtures.Load(name);
            foreach (var key in Ppai2Fixtures.SpeedKeys)
            {
                var v = fixture.Variants[key];
                float stars = ReplayUtils.ToStars((float)v.AccRating, (float)v.PassRating, (float)v.TechRating, Inputs(v));
                Assert.True(Math.Abs(stars - v.Stars) < 1e-3, $"{name} {key}: {stars} vs {v.Stars}");
            }
        }

        [Fact]
        public void Curve_OutsidePerMapRange_IsBaseTableWithLowNoteNerf()
        {
            foreach (var predictedAcc in new[] { 0f, 0.5f, 0.59f, 1f })
            {
                var plain = Curve.GetCurve(new CurveInputs(predictedAcc, 0, 0, 0));
                var nerfed = Curve.GetCurve(new CurveInputs(predictedAcc, 0.4f, 20, 0));

                Assert.Equal(PpCalculator.BaseTable.Length, plain.Count);
                for (int i = 0; i < plain.Count; i++)
                {
                    Assert.Equal((float)PpCalculator.BaseTable[i].x, plain[i].X);
                    Assert.Equal((float)PpCalculator.BaseTable[i].y, plain[i].Y);
                    Assert.Equal((float)(PpCalculator.BaseTable[i].y * 0.8), nerfed[i].Y, 5);
                }
            }
        }

        [Fact]
        public void LowNoteMultiplier_FollowsRatingApiSettings()
        {
            var settings = new CurveSettings();
            Assert.Equal(1, settings.LowNoteMultiplier(0));
            Assert.Equal(1, settings.LowNoteMultiplier(115));
            Assert.Equal(0.8, settings.LowNoteMultiplier(20), 6);
            Assert.Equal(0.5, settings.LowNoteMultiplier(1), 6);
        }
    }
}
