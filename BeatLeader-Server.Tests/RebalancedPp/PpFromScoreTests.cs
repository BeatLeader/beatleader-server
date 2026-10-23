using BeatLeader_Server.Models;
using BeatLeader_Server.Tests.Fixtures;
using BeatLeader_Server.Utils;
using BeatLeader_Server.Utils.RebalancedPp;
using Xunit;

namespace BeatLeader_Server.Tests.RebalancedPp
{
    // ReplayUtils.PpFromScore rules kept from before the rebalance
    public class PpFromScoreTests
    {
        private static DifficultyDescription RatedDifficulty()
        {
            var fixture = Ppai2Fixtures.Load(Ppai2Fixtures.Names().First());
            var diff = new DifficultyDescription { Notes = fixture.Variants["none"].NoteCount };
            RatingUtils.UpdateFromResponse(diff, Ppai2Fixtures.Response(fixture), null);
            return diff;
        }

        private static (float, float, float, float, float) Pp(DifficultyDescription diff, float accuracy, string modifiers,
            LeaderboardContexts context = LeaderboardContexts.General, bool timing = false, ModifiersMap? values = null) =>
            ReplayUtils.PpFromScore(accuracy, context, modifiers, values ?? ModifiersMap.RankedMap(), diff.ModifiersRating,
                diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0, ReplayUtils.GetCurveInputs(diff), timing);

        [Fact]
        public void NoFail_GivesNoPp()
        {
            Assert.Equal((0f, 0f, 0f, 0f, 0f), Pp(RatedDifficulty(), 0.98f, "NF"));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-0.1f)]
        [InlineData(1.01f)]
        public void AccuracyOutOfRange_GivesNoPp(float accuracy)
        {
            Assert.Equal((0f, 0f, 0f, 0f, 0f), Pp(RatedDifficulty(), accuracy, ""));
        }

        [Fact]
        public void Timing_KeepsItsLinearFormula()
        {
            var diff = RatedDifficulty();
            var (pp, bonus, pass, acc, tech) = Pp(diff, 0.9f, "", timing: true);
            Assert.Equal(0.9f * diff.PassRating!.Value * 55f, pp, 3);
            Assert.Equal((0f, 0f, 0f, 0f), (bonus, pass, acc, tech));
        }

        [Fact]
        public void PartsAddUpToPp()
        {
            var (pp, _, pass, acc, tech) = Pp(RatedDifficulty(), 0.97f, "FS,GN");
            Assert.Equal(pp, pass + acc + tech, 2);
        }

        [Fact]
        public void SpeedModifier_UsesItsOwnRatingsAndCurve_NotTheMultiplier()
        {
            var diff = RatedDifficulty();
            var mr = diff.ModifiersRating!;
            // Without speed ratings the FS multiplier (0.4 on ranked maps) would apply; with them FS only swaps ratings
            var expected = PpCalculator.ScorePp(0.97f, "FS", 1f,
                new MapRatings(diff.AccRating!.Value, diff.PassRating!.Value, diff.TechRating!.Value, Curve.GetCurve(ReplayUtils.GetCurveInputs(diff))),
                new MapRatings(mr.FSAccRating, mr.FSPassRating, mr.FSTechRating, Curve.GetCurve(new CurveInputs(mr.FSPredictedAcc, mr.FSSliderPercentage, diff.Notes))));
            Assert.Equal(expected, Pp(diff, 0.97f, "FS"));
            Assert.True(Pp(diff, 0.97f, "FS").Item1 > Pp(diff, 0.97f, "").Item1);
        }

        [Fact]
        public void BfsBsf_UseTheirRatingsWithTheMapCurve()
        {
            var diff = RatedDifficulty();
            var mr = diff.ModifiersRating!;
            var mapCurve = Curve.GetCurve(ReplayUtils.GetCurveInputs(diff));
            var expected = PpCalculator.ScorePp(0.97f, "BFS", 1f,
                new MapRatings(diff.AccRating!.Value, diff.PassRating!.Value, diff.TechRating!.Value, mapCurve),
                new MapRatings(mr.BFSAccRating, mr.BFSPassRating, mr.BFSTechRating, mapCurve));
            Assert.Equal(expected, Pp(diff, 0.97f, "BFS"));
        }

        [Fact]
        public void Scpm_IgnoresScAndPm()
        {
            var diff = RatedDifficulty();
            Assert.Equal(Pp(diff, 0.97f, "", LeaderboardContexts.SCPM), Pp(diff, 0.97f, "SC,PM", LeaderboardContexts.SCPM));
            Assert.True(Pp(diff, 0.97f, "SC").Item1 < Pp(diff, 0.97f, "").Item1);
        }

        [Fact]
        public void Golf_AccPpIsLinear()
        {
            var diff = RatedDifficulty();
            var (_, _, _, acc1, _) = Pp(diff, 0.2f, "", LeaderboardContexts.Golf);
            var (_, _, _, acc2, _) = Pp(diff, 0.4f, "", LeaderboardContexts.Golf);
            Assert.True(acc1 > 0);
            Assert.Equal(2 * acc1, acc2, 2);
        }
    }
}
