// Adapted from portaBLe/RatingAPI Controllers/Curve.cs (GetCurve, PerMapValue, SmoothBaseValue, ModifiableCurve,
// CurveRow, CurveSettings), branch loloppe-rebalancer @ ce61152. Keep in sync with that file. Differences: the points
// are returned as Vector2 (as RatingAPI's PpCalculator.ToCurve converts them), and curves are cached by their inputs.
// The base table and BaseValue are in PpCalculator.
using System.Collections.Concurrent;
using System.Numerics;

namespace BeatLeader_Server.Utils.RebalancedPp
{
    // What a map's acc curve is built from, for no modifier or one speed modifier: RatingAPI's predicted_acc,
    // lack_map_calculation.slider_percentage and lack_map_calculation.note_count (0 = unknown, no low note nerf)
    public readonly record struct CurveInputs(float PredictedAcc, float SliderPercentage, int NoteCount);

    public static class Curve
    {
        private static readonly ConcurrentDictionary<CurveInputs, Vector2[]> Cache = new();
        private const int MaxCached = 50000;

        // The map's acc curve (points from 100% down), as RatingAPI's pointlist for these inputs
        public static IReadOnlyList<Vector2> GetCurve(CurveInputs inputs)
        {
            if (Cache.TryGetValue(inputs, out var cached)) return cached;

            var result = GetCurve(inputs.PredictedAcc, inputs.SliderPercentage, inputs.NoteCount)
                .Select(p => new Vector2((float)p.x, (float)p.y))
                .ToArray();

            if (Cache.Count >= MaxCached) Cache.Clear();
            Cache[inputs] = result;
            return result;
        }

        // Base curve for every map (maps without a usable predicted acc)
        public static List<(double x, double y)> GetCurve(int noteCount)
        {
            double nerf = CurveSettings.Current.LowNoteMultiplier(noteCount);
            return PpCalculator.BaseTable.Select(p => (p.x, p.y * nerf)).ToList();
        }

        // Per-map acc curve (RatingAPI Curve.GetCurve(LackMapCalculation, predictedAcc))
        public static List<(double x, double y)> GetCurve(double predictedAcc, double sliderPercentage, int noteCount)
        {
            var curves = PerMapCurves(predictedAcc);
            double tilt = SliderTilt(sliderPercentage, predictedAcc);
            if (curves == null && tilt == 0)
            {
                return GetCurve(noteCount);
            }
            double a = curves?.modified.A ?? 1.0;
            double nerf = CurveSettings.Current.LowNoteMultiplier(noteCount);

            // Base table points, 0.5% steps from 60% to 95%, 0.05% steps from 95% to 100%, and A
            var xs = PpCalculator.BaseTable.Select(p => p.x)
                .Concat(Enumerable.Range(0, 71).Select(i => 0.6 + i * 0.005))
                .Concat(Enumerable.Range(0, 101).Select(i => 0.95 + i * 0.0005))
                .Append(a)
                .Select(x => Math.Round(x, 6))
                .Where(x => x >= 0 && x <= 1)
                .Distinct()
                .OrderByDescending(x => x);

            return xs.Select(x =>
            {
                double value = curves == null ? PpCalculator.BaseValue(x) : PerMapValue(curves.Value, x);
                if (tilt != 0)
                {
                    // Slider tilt: steeper around the predicted acc (unchanged there), up to SliderTiltRange away from it
                    double distance = Math.Clamp(x - predictedAcc, -CurveSettings.Current.SliderTiltRange, CurveSettings.Current.SliderTiltRange);
                    value *= Math.Exp(tilt * distance * 100);
                }
                return (x, value * nerf);
            }).ToList();
        }

        // Slider tilt per 1% acc of a map's curve (0: none)
        private static double SliderTilt(double sliderPercentage, double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || settings.SliderTilt == 0 || predictedAcc <= 0.6 || predictedAcc >= 0.9995) return 0;
            return settings.SliderTilt * sliderPercentage;
        }

        // Value of the per-map curve (without slider tilt or low note nerf) at acc
        public static double PerMapValue(double predictedAcc, double acc)
        {
            var curves = PerMapCurves(predictedAcc);
            return curves == null ? PpCalculator.BaseValue(acc) : PerMapValue(curves.Value, acc);
        }

        // The modified and default ModifiableCurve of a map, null when it gets the base curve
        private static (ModifiableCurve modified, ModifiableCurve standard)? PerMapCurves(double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || predictedAcc <= 0.6 || predictedAcc >= 0.9995)
            {
                return null;
            }
            var row = settings.ParamsAt(predictedAcc);
            if (row.IsDefault)
            {
                return null;
            }
            double a = Math.Min(Math.Max(row.A, 0.951), 1.0);
            return (new ModifiableCurve { A = a, C = row.C, K = row.K, W = row.W, K2 = row.K2, P2 = row.P2 }, new ModifiableCurve());
        }

        private static double PerMapValue((ModifiableCurve modified, ModifiableCurve standard) curves, double x)
        {
            double baseValue = SmoothBaseValue(x);
            double reference = curves.standard.GetValue(x);
            double value = reference > 0 ? baseValue * curves.modified.GetValue(x) / reference : baseValue;
            return Math.Min(baseValue, value);
        }

        // The base table with monotone cubic interpolation (Fritsch-Carlson) instead of linear: through every table point,
        // with a continuous slope. Per-map curves only (PerMapValue); the base curve stays linear.
        public static double SmoothBaseValue(double acc)
        {
            var (xs, ys, slopes) = Smooth.Value;
            if (acc <= xs[0]) return ys[0];
            if (acc >= xs[^1]) return ys[^1];
            int i = 0;
            while (xs[i + 1] < acc) i++;
            double h = xs[i + 1] - xs[i];
            double t = (acc - xs[i]) / h;
            double t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * slopes[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * slopes[i + 1];
        }

        // Table points ascending and the slope at each (weighted harmonic mean of the neighbouring segments' slopes)
        private static readonly Lazy<(double[] xs, double[] ys, double[] slopes)> Smooth = new(() =>
        {
            var points = PpCalculator.BaseTable.OrderBy(p => p.x).ToArray();
            int n = points.Length;
            double[] xs = points.Select(p => p.x).ToArray(), ys = points.Select(p => p.y).ToArray();
            double[] h = new double[n - 1], d = new double[n - 1], slopes = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = xs[i + 1] - xs[i];
                d[i] = (ys[i + 1] - ys[i]) / h[i];
            }
            slopes[0] = d[0];
            slopes[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
            return (xs, ys, slopes);
        });

        public class ModifiableCurve
        {
            public double Y0 { get; set; } = 0.256;
            public double K { get; set; } = 4.86;
            public double W { get; set; } = 0.6;
            public double K2 { get; set; } = 2.88;
            public double P2 { get; set; } = 2.46;
            // Taper of the extension above A: 0 = keeps the slope at A (straight line), 1 = the slope fades out (flattens)
            public double C { get; set; } = 1.0;
            // Where the extension starts
            public double A { get; set; } = 1.0;

            public double GetValue(double x)
            {
                if (x <= 0.60) return (Y0 / 0.60) * x;
                if (x <= 0.95) return GetF1(x);
                if (x <= A) return GetF2(x);
                return GetL(x);
            }

            // Slope at x = 0.95
            public double GetM()
            {
                double term1 = (1.0 - Y0) / 0.35;
                double term2 = (1.0 - W) + W * (K * Math.Exp(K)) / (Math.Exp(K) - 1.0);
                return term1 * term2;
            }

            // 0.60 < x <= 0.95
            public double GetF1(double x)
            {
                double u = (x - 0.6) / 0.35;
                double blend = (1.0 - W) * u + W * (Math.Exp(K * u) - 1.0) / (Math.Exp(K) - 1.0);
                return Y0 + (1.0 - Y0) * blend;
            }

            // 0.95 < x <= A
            public double GetF2(double x)
            {
                double m = GetM();
                double u2 = (x - 0.95) / 0.05;
                double expTerm = (Math.Exp(K2 * Math.Pow(u2, P2)) - 1.0) / (Math.Exp(K2) - 1.0);
                return 1.0 + m * (x - 0.95) + (6.424 - 0.05 * m) * expTerm;
            }

            // High-pass extension above A: starts with the slope S at A, which decays towards S * (1 - C)
            public double GetL(double x)
            {
                double s = GetS();
                double s1 = s * (1.0 - C);
                double f2A = GetF2(A);

                if (Math.Abs(A - 1.0) < 1e-9)
                {
                    return f2A + s1 * (x - A);
                }

                double expTerm = 1.0 - Math.Exp(-(x - A) / (1.0 - A));
                return f2A + s1 * (x - A) + (s - s1) * (1.0 - A) * expTerm;
            }

            // Slope at x = A
            public double GetS()
            {
                double m = GetM();
                double uA = (A - 0.95) / 0.05;
                double uAPower = uA > 0 ? Math.Pow(uA, P2) : 0;
                double uAPowerMinus1 = uA > 0 ? Math.Pow(uA, P2 - 1.0) : 0;
                double expTerm = Math.Exp(K2 * uAPower) / (Math.Exp(K2) - 1.0);
                double derivativeFactor = (K2 * P2 / 0.05) * uAPowerMinus1;
                return m + (6.424 - 0.05 * m) * expTerm * derivativeFactor;
            }
        }
    }

    // One row of CurveSettings.Table: ModifiableCurve parameters for a predicted acc
    public class CurveRow
    {
        public double PredictedAcc { get; set; }
        public double A { get; set; } = 1.0;
        public double C { get; set; } = 0;
        public double K { get; set; } = 4.86;
        public double W { get; set; } = 0.6;
        public double K2 { get; set; } = 2.88;
        public double P2 { get; set; } = 2.46;

        // A at 100% and the default shape: the base curve
        public bool IsDefault => A >= 0.9995 && K == 4.86 && W == 0.6 && K2 == 2.88 && P2 == 2.46;
    }

    // Settings of the per-map curves: ModifiableCurve parameters by predicted acc, linearly interpolated between the rows;
    // the first / last row applies below / above them. Slider tilt and low note nerf: see RatingAPI CurveSettings.
    public class CurveSettings
    {
        public bool Enabled { get; set; } = true;

        public List<CurveRow> Table { get; set; } = new()
        {
            new CurveRow { PredictedAcc = 0.955, A = 1.0,     C = -5.30, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.960, A = 0.99,    C = -3.00, K = 4.30, W = 0.66, K2 = 4.80, P2 = 1.85 },
            new CurveRow { PredictedAcc = 0.965, A = 0.988,   C = -2.40, K = 4.50, W = 0.64, K2 = 4.50, P2 = 1.95 },
            new CurveRow { PredictedAcc = 0.970, A = 0.986,   C = -2.20, K = 4.70, W = 0.62, K2 = 4.20, P2 = 2.05 },
            new CurveRow { PredictedAcc = 0.975, A = 0.985,   C = -1.80, K = 4.86, W = 0.60, K2 = 3.80, P2 = 2.15 },
            new CurveRow { PredictedAcc = 0.980, A = 0.985,   C = -1.40, K = 4.86, W = 0.60, K2 = 3.35, P2 = 2.30 },
            new CurveRow { PredictedAcc = 0.985, A = 0.985,   C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.990, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.995, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
        };

        // C never goes above 1 (the curve would go down)
        public double MaxC { get; set; } = 1;

        public double SliderTilt { get; set; } = 0.2;
        public double SliderTiltRange { get; set; } = 0.015;

        public double LowNoteStart { get; set; } = 115;
        public double LowNoteRefNotes { get; set; } = 20;
        public double LowNoteRefMultiplier { get; set; } = 0.8;
        public double LowNoteMinMultiplier { get; set; } = 0.5;

        public double LowNoteMultiplier(int notes)
        {
            if (LowNoteStart <= 0 || notes <= 0 || notes >= LowNoteStart) return 1;
            double nerf = (1 - LowNoteRefMultiplier) * Math.Log(LowNoteStart / notes) / Math.Log(LowNoteStart / LowNoteRefNotes);
            return Math.Max(1 - nerf, LowNoteMinMultiplier);
        }

        public CurveRow ParamsAt(double predictedAcc)
        {
            var rows = Table.OrderBy(r => r.PredictedAcc).ToList();
            if (rows.Count == 0) return new CurveRow { PredictedAcc = predictedAcc };
            CurveRow Clamp(CurveRow r) => new CurveRow { PredictedAcc = predictedAcc, A = r.A, C = Math.Min(r.C, MaxC), K = r.K, W = r.W, K2 = r.K2, P2 = r.P2 };
            if (predictedAcc <= rows[0].PredictedAcc) return Clamp(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                if (predictedAcc <= rows[i].PredictedAcc)
                {
                    var p = rows[i - 1]; var n = rows[i];
                    double t = (predictedAcc - p.PredictedAcc) / (n.PredictedAcc - p.PredictedAcc);
                    double L(double x, double y) => x + t * (y - x);
                    return Clamp(new CurveRow { A = L(p.A, n.A), C = L(p.C, n.C), K = L(p.K, n.K), W = L(p.W, n.W), K2 = L(p.K2, n.K2), P2 = L(p.P2, n.P2) });
                }
            }
            return Clamp(rows[rows.Count - 1]);
        }

        public static CurveSettings Current { get; set; } = new();
    }
}
