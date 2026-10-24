#if RATINGAPI_PARITY
namespace RatingAPI.Controllers
{
    public class Point
    {
        public double x { get; set; }
        public double y { get; set; }

        public Point() { }
        public Point(double x, double y) { this.x = x; this.y = y; }
    }

    public class Curve
    {
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
    }
}
#endif
