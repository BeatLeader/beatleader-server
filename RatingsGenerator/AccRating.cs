namespace RatingAPI.Controllers
{
    public class AccRating
    {
        public List<(double, double)> PointList { get; set; } = new()
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
                (0.96, 1.101),
                (0.955, 1.047),
                (0.95, 1.000),
                (0.94, 0.919),
                (0.93, 0.847),
                (0.92, 0.786),
                (0.91, 0.734),
                (0.9, 0.692),
                (0.875, 0.606),
                (0.85, 0.537),
                (0.825, 0.480),
                (0.8, 0.429),
                (0.75, 0.345),
                (0.7, 0.286),
                (0.65, 0.246),
                (0.6, 0.217),
                (0.0, 0.000)
        };

        public double GetRating(double? predictedAcc, double? passRating, double? techRating)
        {
            double difficulty_to_acc;
            if (predictedAcc > 0)
            {
                difficulty_to_acc = 15.5f / Curve((predictedAcc ?? 0) + 0.0022f);
            }
            else
            {
                double tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                difficulty_to_acc = (-Math.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
            }
            if (double.IsInfinity(difficulty_to_acc) || double.IsNaN(difficulty_to_acc) || double.IsNegativeInfinity(difficulty_to_acc))
            {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        public float Curve(double acc)
        {
            int i = 0;
            for (; i < PointList.Count; i++)
            {
                if (PointList[i].Item1 <= acc)
                {
                    break;
                }
            }

            if (i == 0)
            {
                i = 1;
            }

            double middle_dis = (acc - PointList[i - 1].Item1) / (PointList[i].Item1 - PointList[i - 1].Item1);
            return (float)(PointList[i - 1].Item2 + middle_dis * (PointList[i].Item2 - PointList[i - 1].Item2));
        }
    }
}
