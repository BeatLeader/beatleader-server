using BeatLeader_Server.Models;
using BeatLeader_Server.Tests.Fixtures;
using BeatLeader_Server.Utils;
using BeatLeader_Server.Utils.RebalancedPp;
using Xunit;

namespace BeatLeader_Server.Tests.RebalancedPp
{
    public class RatingIngestTests
    {
        [Theory]
        [MemberData(nameof(Ppai2Fixtures.FixtureNames), MemberType = typeof(Ppai2Fixtures))]
        public void UpdateFromResponse_StoresApiRatingsStarsAndCurveInputs(string name)
        {
            var fixture = Ppai2Fixtures.Load(name);
            var diff = new DifficultyDescription();
            var change = new LeaderboardChange();

            RatingUtils.UpdateFromResponse(diff, Ppai2Fixtures.Response(fixture), change);

            var none = fixture.Variants["none"];
            Assert.Equal((float)none.AccRating, diff.AccRating);
            Assert.Equal((float)none.PassRating, diff.PassRating);
            Assert.Equal((float)none.TechRating, diff.TechRating);
            Assert.Equal((float)none.PredictedAcc, diff.PredictedAcc);
            Assert.Equal((float)none.Stars, diff.Stars);
            Assert.Equal((float)none.SliderPercentage, diff.SliderPercentage);
            Assert.Equal((float)none.Stars, change.NewStars);

            var mr = diff.ModifiersRating!;
            AssertVariant(fixture.Variants["SS"], mr.SSAccRating, mr.SSPassRating, mr.SSTechRating, mr.SSPredictedAcc, mr.SSStars);
            AssertVariant(fixture.Variants["FS"], mr.FSAccRating, mr.FSPassRating, mr.FSTechRating, mr.FSPredictedAcc, mr.FSStars);
            AssertVariant(fixture.Variants["SFS"], mr.SFAccRating, mr.SFPassRating, mr.SFTechRating, mr.SFPredictedAcc, mr.SFStars);
            AssertVariant(fixture.Variants["BFS"], mr.BFSAccRating, mr.BFSPassRating, mr.BFSTechRating, mr.BFSPredictedAcc, mr.BFSStars);
            AssertVariant(fixture.Variants["BSF"], mr.BSFAccRating, mr.BSFPassRating, mr.BSFTechRating, mr.BSFPredictedAcc, mr.BSFStars);

            Assert.Equal((float)fixture.Variants["SS"].SliderPercentage, mr.SSSliderPercentage);
            Assert.Equal((float)fixture.Variants["FS"].SliderPercentage, mr.FSSliderPercentage);
            Assert.Equal((float)fixture.Variants["SFS"].SliderPercentage, mr.SFSliderPercentage);
            Assert.Equal((float)none.NoteSpan, diff.NoteSpan);

            diff.Notes = none.NoteCount;
            var mapCurve = ReplayUtils.GetCurveInputs(diff);
            foreach (var (modifier, key) in new[] { ("SS", "SS"), ("FS", "FS"), ("SF", "SFS") })
            {
                var v = fixture.Variants[key];
                var curve = Curve.GetCurve(ReplayUtils.SpeedCurveInputs(mr, modifier, mapCurve));
                Ppai2Fixtures.AssertCurveMatches($"{name} {key}", v, curve);
            }
        }

        private static void AssertVariant(Ppai2Fixtures.Variant v, float acc, float pass, float tech, float predictedAcc, float stars)
        {
            Assert.Equal((float)v.AccRating, acc);
            Assert.Equal((float)v.PassRating, pass);
            Assert.Equal((float)v.TechRating, tech);
            Assert.Equal((float)v.PredictedAcc, predictedAcc);
            Assert.Equal((float)v.Stars, stars);
        }

        [Fact]
        public void UpdateFromResponse_WithoutBfsBsf_KeepsTheOtherRatings()
        {
            var fixture = Ppai2Fixtures.Load(Ppai2Fixtures.Names().First());
            var response = Ppai2Fixtures.Response(fixture);
            response.BFS = null!;
            response.BSF = null!;
            var diff = new DifficultyDescription();

            RatingUtils.UpdateFromResponse(diff, response, null);

            Assert.Equal((float)fixture.Variants["none"].Stars, diff.Stars);
            Assert.Equal((float)fixture.Variants["SFS"].Stars, diff.ModifiersRating!.SFStars);
            Assert.Equal(0, diff.ModifiersRating.BFSStars);
        }

        [Fact]
        public void UpdateFromResponse_WithoutApiStars_ComputesThemWithTheLocalCurve()
        {
            var fixture = Ppai2Fixtures.Load(Ppai2Fixtures.Names().First());
            var response = Ppai2Fixtures.Response(fixture);
            response.none!.Stars = 0;
            var diff = new DifficultyDescription();

            RatingUtils.UpdateFromResponse(diff, response, null);

            Assert.True(Math.Abs(diff.Stars!.Value - fixture.Variants["none"].Stars) < 1e-3);
        }
    }
}
