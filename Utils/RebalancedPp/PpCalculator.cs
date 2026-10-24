using System.Numerics;

// The curve (Curve.cs) and pp formula (PpFormula.cs, PpCalculator.cs) must stay identical to RatingAPI (loloppe-rebalancer) and the website. Any change here must be made in all three.
// See portaBLe branch for detailed comments.
namespace BeatLeader_Server.Utils.RebalancedPp
{
    public readonly record struct MapRatings(float AccRating, float PassRating, float TechRating, IReadOnlyList<Vector2>? Curve = null);

    public readonly record struct PreparedRatings(MapRatings Ratings, float Stars, float StarCurveValue);

    public static class PpCalculator
    {
        public const float StarAccuracy = 0.96f;

        public static readonly (double x, double y)[] BaseTable =
        {
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.094),
                (0.955, 1.039),
                (0.95, 1.000),
                (0.94, 0.931),
                (0.93, 0.867),
                (0.92, 0.813),
                (0.91, 0.781),
                (0.9, 0.753),
                (0.875, 0.699),
                (0.85, 0.649),
                (0.825, 0.605),
                (0.8, 0.568),
                (0.75, 0.485),
                (0.7, 0.414),
                (0.65, 0.355),
                (0.6, 0.307),
                (0.0, 0.000)
        };

        public static double BaseValue(double acc)
        {
            int i = 0;
            for (; i < BaseTable.Length; i++)
            {
                if (BaseTable[i].x <= acc) break;
            }
            if (i == 0) i = 1;
            if (i >= BaseTable.Length) i = BaseTable.Length - 1;
            double middle_dis = (acc - BaseTable[i - 1].x) / (BaseTable[i].x - BaseTable[i - 1].x);
            return BaseTable[i - 1].y + middle_dis * (BaseTable[i].y - BaseTable[i - 1].y);
        }

        public static float CurveValue(float acc, IReadOnlyList<Vector2>? curve)
        {
            if (curve == null || curve.Count < 2)
            {
                return CurveValue(acc);
            }

            int i = 0, hi = curve.Count;
            while (i < hi)
            {
                int mid = (i + hi) >> 1;
                if (curve[mid].X <= acc) hi = mid;
                else i = mid + 1;
            }

            if (i == 0) {
                i = 1;
            }
            if (i >= curve.Count) {
                i = curve.Count - 1;
            }

            double middle_dis = (acc - curve[i - 1].X) / (double)(curve[i].X - curve[i - 1].X);
            return (float)(curve[i - 1].Y + middle_dis * (curve[i].Y - curve[i - 1].Y));
        }

        public static float CurveValue(float acc) => (float)BaseValue(acc);

        public static (float pass, float acc, float tech) GetPp(float accuracy, float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve = null, float? stars = null)
        {
            var formula = PpFormula.Current;
            double power = stars is float s ? formula.CurvePowerAt(s) : formula.AccCurvePower;
            return GetPp(accuracy, accRating, passRating, techRating, curve, power, power != 1 ? CurveValue(StarAccuracy, curve) : 0, false);
        }

        private static (float pass, float acc, float tech) GetPp(float accuracy, float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve, double power, float starCurveValue, bool golf)
        {
            var formula = PpFormula.Current;
            float accPP;
            if (golf) {
                accPP = accuracy * accRating * 42f;
            } else {
                float curveValue = CurveValue(accuracy, curve);
                if (power != 1) curveValue = formula.AccCurve(curveValue, starCurveValue, power);
                accPP = formula.AccPp(curveValue, accRating);
            }
            return (formula.PassPp(passRating), accPP, formula.TechPp(accuracy, techRating));
        }

        public static PreparedRatings Prepare(MapRatings ratings) =>
            new(ratings, ToStars(ratings), CurveValue(StarAccuracy, ratings.Curve));

        public static float Scale(float pp) => PpFormula.Current.Scale(pp);

        public static float ToStars(float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve = null)
        {
            var formula = PpFormula.Current;
            (float passPP, float accPP, float techPP) = GetPp(StarAccuracy, accRating, passRating, techRating, curve);
            return formula.StarsFromPp(passPP + accPP + techPP);
        }

        public static float ToStars(MapRatings ratings) => ToStars(ratings.AccRating, ratings.PassRating, ratings.TechRating, ratings.Curve);

        public static (float, float, float, float, float) ScorePp(
            float accuracy,
            string modifiers,
            float mp,
            MapRatings ratings,
            MapRatings? speedRatings = null,
            bool golf = false)
        {
            if (accuracy <= 0 || accuracy > 1) return (0, 0, 0, 0, 0);

            float rawPP = 0; float fullPP = 0; float passPP = 0; float accPP = 0; float techPP = 0; float increase = 0;
            if (!modifiers.Contains("NF"))
            {
                var formula = PpFormula.Current;
                var prepared = Prepare(ratings);
                var map = prepared.Ratings;
                float stars = prepared.Stars;
                (passPP, accPP, techPP) = GetPp(accuracy, map.AccRating, map.PassRating, map.TechRating, map.Curve, formula.CurvePowerAt(stars), prepared.StarCurveValue, golf);
                rawPP = Scale(passPP + accPP + techPP) * formula.LowStarMultiplier(stars);

                var speedPrepared = speedRatings is MapRatings speed ? Prepare(speed) : prepared;
                var score = speedPrepared.Ratings;
                float scoreStars = speedPrepared.Stars;
                (passPP, accPP, techPP) = GetPp(accuracy, score.AccRating * mp, score.PassRating * mp, score.TechRating * mp, score.Curve, formula.CurvePowerAt(scoreStars), speedPrepared.StarCurveValue, golf);
                fullPP = Scale(passPP + accPP + techPP) * formula.LowStarMultiplier(scoreStars);
                if (passPP + accPP + techPP > 0) {
                    increase = fullPP / (passPP + accPP + techPP);
                }
            }

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP)) {
                rawPP = 0;
            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP)) {
                fullPP = 0;
            }

            return (fullPP, fullPP - rawPP, passPP * increase, accPP * increase, techPP * increase);
        }
    }
}
