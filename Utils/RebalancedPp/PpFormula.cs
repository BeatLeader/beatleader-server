// Copied from portaBLe/RatingAPI Pp/PpFormula.cs, branch loloppe-rebalancer @ ce61152. Keep in sync with that file;
// only the namespace differs.
namespace BeatLeader_Server.Utils.RebalancedPp
{
    // The parts of the pp formula (PpCalculator.GetPp, ToStars, ScorePp). PpFormula.Current is what PpCalculator
    // uses; portaBLe's pp formula sweeps (BalanceTuner.RunPpFormula) swap it temporarily to try other values. The defaults
    // are the formula as it was hard-coded:
    //   pass pp = PassScale * e^(PassInner * pass^(1/PassExponent)) - PassOffset   (0 when negative; above PassTopKnee
    //             each point counts PassTopSlope)
    //   acc pp  = curve(acc) * AccWeight * accRating^AccRatingPower   (the map's own curve, Curve.GetCurve)
    //   tech pp = e^(TechAccExponent * acc) * TechWeight * tech^TechRatingPower
    //   pp      = (pass + acc + tech) * PpMultiplier   (Scale; no inflation power)
    //   pp      = pp * (1 + LowStarBonus * (LowStarBonusEnd - stars)^LowStarBonusPower)   (only under LowStarBonusEnd stars)
    //   stars   = StarScale * (final pp of a 96% score / PpMultiplier / StarDivisor)^StarExponent: the pp a 96% is
    //             worth, low star bonus included (StarsFromPp). PpMultiplier doesn't move stars.
    public class PpFormula
    {
        public double PassScale { get; set; } = 12.3;
        public double PassExponent { get; set; } = 2.62;
        public double PassOffset { get; set; } = 30;
        // 1.4862^(-1 / 2.62): the pass formula was written for a pass 1.4862 times smaller (before the pass display curve was
        // folded into AnalyzeMap.PASS_CALIBRATION_FACTOR), so the same map gets the same pass pp
        public double PassInner { get; set; } = 0.85966;
        // AccRatingPower / TechRatingPower: the acc and tech ratings are on prod's scale, which is flatter at the top than what acc
        // and tech pp need (AccRating.ACC_EXPONENT 0.8222 and Analyzer Ratings.TechExponent 0.8015 make them so; these are
        // their inverses). With pp proportional to the ratings instead, a star range's acc-heavy maps lost acc pp against its
        // pass-heavy ones (pass share slope +0.03 -> +0.11, tech share slope +0.01 -> -0.15) and no PassScale / TechWeight
        // undid it. AccWeight = 34 / 1.4528^AccRatingPower, TechWeight below the same: the same pp as before the change.
        public double AccWeight { get; set; } = 21.584;
        public double AccRatingPower { get; set; } = 1.2163;
        // How steeply acc pp follows the curve around 96%: curve(acc) is replaced by curve(96%) x (curve(acc) / curve(96%))^
        // AccCurvePower, so stars (read at 96%) don't move. 1 = the curve as it is.
        // 0.9 after the inflation was removed: with pp linear, pass-heavy maps were worth more than others of their stars
        // (pass share slope / gap +0.036 / +0.019, the highest pass 5% +0.133) and top players got more at their predicted acc
        // on 1-3 star maps (578, 548) than on 3-5 star maps (525, 522). With it (and PassTopKnee, TechWeight, LowStarBonus,
        // AnalyzeMap.ONE_SABER_NERF below): pass +0.000 / +0.000, tech +0.003 / -0.004, OneSaber gap -0.003, the highest pass
        // 5% +0.018, worth spread 0.4597 -> 0.4547, pp at the predicted acc 464, 462, 471, 488 ... from 1 star.
        public double AccCurvePower { get; set; } = 0.9;
        // The acc curve power on low-star maps (CurvePowerAt): LowStarCurvePower up to LowStarCurveStart stars, then linearly
        // to AccCurvePower at LowStarCurveEnd. Equal to AccCurvePower = off. 1.0: AccCurvePower 0.9 cut top players' scores
        // most on easy maps (they score far above 96% there): the top 100's median pp at 1-2, 2-3, 3-4, 4-5 stars went 398,
        // 404, 443, 478 -> 325, 362, 412, 456; with it 362, 385, 424, 460. More (1.1: 400, 410, 437, 463) makes pp at the
        // predicted acc fall from 1 to 4 stars again (615, 546, 514, 502) and the worth spread 0.455 -> 0.465.
        // Start 2 -> 7, end 5 -> 10 (with the low star bonus to 9 stars below): top players' best scores at 3-8 stars had
        // fallen 3-6% under prod's against the 10-12 star plateau (median of each top 1000 player's top 50: 3* 1.000, 5*
        // 1.044, 7* 0.990 vs prod 1.044, 1.089, 1.021), most on acc players' 99% scores on 4-6 star maps (ACC | Pandita
        // #10 in prod, #45). Both together: 1.056, 1.068, 1.022, Pandita #6, top 1000 rank correlation 0.993, worth
        // slopes unchanged. AccCurvePower doesn't move stars (read at 96%).
        public double LowStarCurvePower { get; set; } = 1.0;
        public double LowStarCurveStart { get; set; } = 7;
        public double LowStarCurveEnd { get; set; } = 10;
        // The acc curve power on the lowest-star maps (CurvePowerAt), before the ramp above: VeryLowStarCurvePower up to
        // VeryLowStarCurveStart stars, then linearly to LowStarCurvePower at VeryLowStarCurveEnd. Equal to LowStarCurvePower
        // = off. 1.08: the best plays under 4 stars topped out at 690-710 pp (#1 scores, best of the band) against 775-818
        // from 5 to 14 stars. Simulated on the ranked scores (with the acc reweighter): best #1 at 1, 2, 3, 4 stars 694, 711,
        // 706, 766 -> 795, 808, 743, 783; top 10 of each map +11.6%, +8.2%, +4.7%, +1.2%; all scores' mean +3.8%, +1.7%,
        // 0.0%, -0.3%, median +1.3%, 0.0%, -0.7%, -0.4%; nothing from 5 stars. Scores under 96% lose a little (90%: about
        // -3% acc pp). The per-map curve rows (CurveSettings.Table, A / C) can't do this: they can only lower the curve, so
        // they raise maps by cutting it at the predicted acc, which lifted the median score far more than the top ones
        // (2-3 stars: median 172 -> 531 pp, #1 +12%). C -3 on the 99.5% row only moved one 2 star play (711 -> 746).
        // Doesn't move stars (read at 96%).
        public double VeryLowStarCurvePower { get; set; } = 1.08;
        public double VeryLowStarCurveStart { get; set; } = 3;
        public double VeryLowStarCurveEnd { get; set; } = 5;
        // 0.2193 -> 0.1874 (BalanceTuner) after DENSITY_CAP 1: dense low-tech maps lost pass, so tech-heavy maps were worth
        // more than others of their stars. -> 0.1695 after the acc rating's movement correction (AccRating.EffectiveAcc),
        // which raised the acc of high-movement maps (tech share slope 0.047 -> 0.007). -> 0.1485 after the 4-32 s pass
        // windows. -> 0.1348 with the prod rescale: tech ratings x1.102 (AnalyzeMap.BALANCED_TECH_SCALER), same tech pp.
        // -> 0.0938 with TechRatingPower (0.1348 / 1.3374^TechRatingPower, see AccRatingPower): the same tech pp.
        // -> 0.0891 (x0.95) with AccCurvePower 0.9 and PassTopKnee 10.4 (tech share slope +0.027 -> +0.003).
        // -> 0.124 (x1.39) from the balance tuner's PlayerAdjusted worth (BalanceTuner.WorthMeasures): for the same players
        // at their level, tech-heavy maps paid less than others of their stars (tech share slope -0.033, tech 8+ maps -2.7%;
        // InRange agreed, -0.042). Tech pp is 2-5% of a score, so tech-heavy maps gain 1-2%: tech share slope -0.007, top
        // 1000 rank correlation 0.991. Megametric75 called it +0.126, its overreaction to rating changes.
        // -> 0.1612 (x1.3) from the balance tuner with the worth measure without its lower bound (BalanceTuner.
        // PlayerAdjustedBelow) and with the distance term, without the acc reweighter: tech share slope -0.0148 -> -0.0011,
        // most tech-heavy 20% -1.0% -> -0.3%, the top 1000's pp from tech 7.0% -> 9.6% (at the edge of the range tried).
        public double TechWeight { get; set; } = 0.1612;
        public double TechRatingPower { get; set; } = 1.2477;
        public double TechAccExponent { get; set; } = 3.5;
        // Stars and players' pp on prod's scale. StarScale / StarExponent: pp at 96% is steeper at the top than prod's stars;
        // fitted on the quantiles of prod's ranked stars (median 6.89 vs 6.97, p95 11.75 vs 11.87, p99 13.08 vs 13.17).
        // PpMultiplier: top 1000 players (#1 24580 vs prod 24140, #1000 14573 vs 14693).
        // The inflation pp = 650 * ((pass + acc + tech) / 650)^1.3 was removed: pp is now linear in the sum of the parts. Most
        // scores' sum is under 650, so their pp rose (x1.44 under 3 stars, x1.26 at 7-9, x1.15 at 13+; the highest score 949
        // -> 897) and the leaderboard compressed (#1 x0.97, #1000 x1.09, #10000 x1.28, at the same PpMultiplier).
        // StarDivisor 45.25 -> 650 * (45.25 / 650)^(1 / 1.3) and StarExponent 0.9039 -> 0.9039 * 1.3 keep every map's stars
        // (exactly above LowStarBonusEnd; +0.04 on average under it). PpMultiplier 1.146 -> 1.0786 keeps the top 1000's
        // totals on average (#1 22380, #1000 14950). -> 1.0833 with AccCurvePower 0.9 (top 1000 kept on average).
        public double StarDivisor { get; set; } = 83.6925;
        public double StarScale { get; set; } = 1.2131;
        public double StarExponent { get; set; } = 1.17507;
        // -> 1.0625 with TechWeight 0.124 (every map has some tech pp, so the top 1000's average rose 16403 -> 16724; x0.9808
        // keeps it). -> 1.0597 with the low star curve power and bonus to 9-10 stars: the top 1000's average back to prod's
        // (16395; 16438 at 1.0625). -> 1.0369 (x0.9785) with TechWeight 0.1612: the top 1000's average rose 16400 -> 16756.
        public double PpMultiplier { get; set; } = 1.0369;

        // Low star bonus: maps under LowStarBonusEnd stars pay more, without changing their stars (+45% at 1 star, +17% at 2,
        // +6% at 3, nothing from 4). Top players farm the easiest maps to 99%+, but stars are measured at 96%, so the
        // unweighted pp vs stars graph fell off steeply under 4 stars. A soft floor on the acc rating (tried, then removed)
        // lifted their stars too, which squeezed them into 2.5-4 stars where they paid more than real 3-4 star maps (a bump
        // on the graph). Raising pp at a given star value keeps maps where they are: simulated on the graph (players ranked
        // top 1000, 100+ ranked plays), the rolling average rises 243, 274, 278, 293, 301, 313, 329 from 1 to 4 stars (was
        // 194, 229, 244, 270, 288, 310, 329), unchanged from 4 stars; #1 and #100 player totals unchanged. Set LowStarBonus
        // to 0 to turn it off.
        // Refitted on the stars of each change since (StarDivisor, then StarScale / StarExponent), so the same maps get the
        // same bonus (within 0.4%): the percentages above are in the stars of the time.
        // Stars now include the bonus (stars = what a 96% score is worth, StarsFromPp), so it raises the stars of the maps it
        // applies to, and its input is those stars. Halved (0.0642 -> 0.0321) after the inflation was removed, which raised
        // pp most under 3 stars (x1.44): with it, pp at the predicted acc no longer falls from 1 to 4 stars.
        // -> 0.006 up to 9 stars (was 0.0321 up to 4.49): +10% at 1 star, +8.5% at 2, +6.9% at 3, +5.3% at 4, +3.8% at 5,
        // +1.5% at 7, nothing from 9 (was +17.6%, +11%, +5.4%, +1%, 0), with LowStarCurveStart / End above.
        public double LowStarBonus { get; set; } = 0.006;
        public double LowStarBonusEnd { get; set; } = 9;
        public double LowStarBonusPower { get; set; } = 1.36;

        public static PpFormula Current { get; set; } = new();

        // Pass pp top compression: above PassTopKnee pass, each point counts PassTopSlope. Maps with more pass than others of
        // their stars were worth more above 10 stars only (Megametric75 +0.18 to +0.31 above their star range from about 11
        // pass then, 13.4 now; prod alike). 1 = off. 13.375 -> 10.4 after the inflation was removed (the highest pass 5% of
        // maps +0.092 -> +0.011 with AccCurvePower 0.9).
        public double PassTopKnee { get; set; } = 10.4;
        public double PassTopSlope { get; set; } = 0.5;

        // Tech pp top compression: above TechTopKnee tech, each point counts TechTopSlope. 1 = off. Tech share was worth
        // nothing at 0-12 stars (worth per sd of tech share -0.005 to +0.002) but +0.011 at 12-13.5 and +0.017 above, so the
        // global tech share slope (-0.0017) didn't show it: the 14 tech 13+ maps were +0.051 (Sanity Krush ExpertPlus, tech
        // 16.8, +0.176), the tech 11-13 maps -0.009. Dev, 22 Oct, no acc reweight: tech 13+ -> +0.023, Sanity Krush +0.106
        // (14.40 -> 13.50 stars), tech 11-13 unchanged, tech share slope -0.0017 -> -0.0028, the other slopes and gaps within
        // 0.0003, top 1000 16409 -> 16385. Knee 11 or 10 took the tech 11-13 maps to -0.014 / -0.019; slope 0.25 / 0.2 gained
        // little more (+0.020 / +0.018) for tech 18 counting as 13.5 / 13.2.
        public double TechTopKnee { get; set; } = 12;
        public double TechTopSlope { get; set; } = 0.33;

        public PpFormula Clone() => (PpFormula)MemberwiseClone();

        public float PassPp(float passRating)
        {
            float pass = passRating;
            if (pass > PassTopKnee) pass = (float)(PassTopKnee + (pass - PassTopKnee) * PassTopSlope);
            float passPP = (float)PassScale * MathF.Exp((float)PassInner * MathF.Pow(pass, 1 / (float)PassExponent)) - (float)PassOffset;
            if (float.IsInfinity(passPP) || float.IsNaN(passPP) || passPP < 0) passPP = 0;
            return passPP;
        }

        // The acc curve power of a map with these stars (AccCurvePower, LowStarCurvePower, VeryLowStarCurvePower)
        public double CurvePowerAt(float stars)
        {
            if (VeryLowStarCurvePower != LowStarCurvePower && stars < VeryLowStarCurveEnd)
            {
                if (stars <= VeryLowStarCurveStart) return VeryLowStarCurvePower;
                return VeryLowStarCurvePower + (LowStarCurvePower - VeryLowStarCurvePower) * (stars - VeryLowStarCurveStart) / (VeryLowStarCurveEnd - VeryLowStarCurveStart);
            }
            if (LowStarCurvePower == AccCurvePower || !(stars < LowStarCurveEnd)) return AccCurvePower;
            if (stars <= LowStarCurveStart) return LowStarCurvePower;
            return LowStarCurvePower + (AccCurvePower - LowStarCurvePower) * (stars - LowStarCurveStart) / (LowStarCurveEnd - LowStarCurveStart);
        }

        // The curve value acc pp uses, from the curve at the score's accuracy and at 96% (power: CurvePowerAt)
        public float AccCurve(float curveValue, float curveAt96, double power)
        {
            if (power == 1 || !(curveValue > 0) || !(curveAt96 > 0)) return curveValue;
            return curveAt96 * MathF.Pow(curveValue / curveAt96, (float)power);
        }

        public float AccPp(float curveValue, float accRating) =>
            accRating > 0 ? curveValue * (float)AccWeight * MathF.Pow(accRating, (float)AccRatingPower) : 0;

        public float TechPp(float accuracy, float techRating)
        {
            if (!(techRating > 0)) return 0;
            float tech = techRating;
            if (tech > TechTopKnee) tech = (float)(TechTopKnee + (tech - TechTopKnee) * TechTopSlope);
            return MathF.Exp((float)TechAccExponent * accuracy) * (float)TechWeight * MathF.Pow(tech, (float)TechRatingPower);
        }

        // Stars from pp at 96% (pass + acc + tech, before PpMultiplier), without the low star bonus
        public float Stars(float ppAt96)
        {
            double basis = ppAt96 / StarDivisor;
            return basis > 0 ? (float)(StarScale * Math.Pow(basis, StarExponent)) : (float)basis;
        }

        // Stars from pp at 96% before the low star bonus (before PpMultiplier): the stars s with
        // s = Stars(ppAt96 * LowStarMultiplier(s)), so they match the final pp of a 96% score, bonus included. The bonus
        // falls as stars rise, so there's exactly one s, between the stars without the bonus and LowStarBonusEnd.
        public float StarsFromPp(float ppAt96)
        {
            float stars = Stars(ppAt96);
            if (!(ppAt96 > 0) || LowStarBonus <= 0 || stars >= LowStarBonusEnd) return stars;

            double lo = stars, hi = LowStarBonusEnd;
            while (hi - lo > 1e-6)
            {
                double mid = (lo + hi) / 2;
                if (mid < Stars(ppAt96 * LowStarMultiplier((float)mid))) lo = mid; else hi = mid;
            }
            return (float)((lo + hi) / 2);
        }

        public float LowStarMultiplier(float stars)
        {
            if (LowStarBonus <= 0 || float.IsNaN(stars) || stars >= LowStarBonusEnd) return 1;
            return 1 + (float)(LowStarBonus * Math.Pow(LowStarBonusEnd - Math.Max(stars, 0), LowStarBonusPower));
        }

        // pp from the sum of pass, acc and tech pp, before the low star bonus
        public float Scale(float pp) => (float)PpMultiplier * pp;

        public override string ToString() => FormattableString.Invariant(
            $"pass {PassScale}*e^({PassInner}*p^(1/{PassExponent}))-{PassOffset} (x{PassTopSlope} above {PassTopKnee}), acc x{AccWeight}*a^{AccRatingPower}, tech e^({TechAccExponent}a)*{TechWeight}*t^{TechRatingPower} (x{TechTopSlope} above {TechTopKnee}), x{PpMultiplier}, stars {StarScale}*(pp/{StarDivisor})^{StarExponent}, low star bonus {LowStarBonus}*({LowStarBonusEnd}-stars)^{LowStarBonusPower}");
    }
}
