namespace BeatLeader_Server.Utils.RebalancedPp
{
    public class PpFormula
    {
        public double PassScale { get; set; } = 12.3;
        public double PassExponent { get; set; } = 2.62;
        public double PassOffset { get; set; } = 30;
        public double PassInner { get; set; } = 0.85966;
        public double AccWeight { get; set; } = 21.584;
        public double AccRatingPower { get; set; } = 1.2163;
        public double AccCurvePower { get; set; } = 0.9;
        public double LowStarCurvePower { get; set; } = 1.0;
        public double LowStarCurveStart { get; set; } = 7;
        public double LowStarCurveEnd { get; set; } = 10;
        public double VeryLowStarCurvePower { get; set; } = 1.08;
        public double VeryLowStarCurveStart { get; set; } = 3;
        public double VeryLowStarCurveEnd { get; set; } = 5;
        public double TechWeight { get; set; } = 0.1612;
        public double TechRatingPower { get; set; } = 1.2477;
        public double TechAccExponent { get; set; } = 3.5;
        public double StarDivisor { get; set; } = 83.6925;
        public double StarScale { get; set; } = 1.2131;
        public double StarExponent { get; set; } = 1.17507;
        public double PpMultiplier { get; set; } = 1.0369;

        public double LowStarBonus { get; set; } = 0.006;
        public double LowStarBonusEnd { get; set; } = 9;
        public double LowStarBonusPower { get; set; } = 1.36;

        public static PpFormula Current { get; set; } = new();

        public double PassTopKnee { get; set; } = 10.4;
        public double PassTopSlope { get; set; } = 0.5;

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

        public float Stars(float ppAt96)
        {
            double basis = ppAt96 / StarDivisor;
            return basis > 0 ? (float)(StarScale * Math.Pow(basis, StarExponent)) : (float)basis;
        }

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

        public float Scale(float pp) => (float)PpMultiplier * pp;

        public override string ToString() => FormattableString.Invariant(
            $"pass {PassScale}*e^({PassInner}*p^(1/{PassExponent}))-{PassOffset} (x{PassTopSlope} above {PassTopKnee}), acc x{AccWeight}*a^{AccRatingPower}, tech e^({TechAccExponent}a)*{TechWeight}*t^{TechRatingPower} (x{TechTopSlope} above {TechTopKnee}), x{PpMultiplier}, stars {StarScale}*(pp/{StarDivisor})^{StarExponent}, low star bonus {LowStarBonus}*({LowStarBonusEnd}-stars)^{LowStarBonusPower}");
    }
}
