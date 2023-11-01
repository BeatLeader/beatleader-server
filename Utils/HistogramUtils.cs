using BeatLeader_Server.Enums;
using Newtonsoft.Json;

namespace BeatLeader_Server.Utils
{
    public class HistogramValue
    {
        public int Value { get; set; }
        public int Page { get; set; }
    }

    public class HistogramUtils
    {
        public static string GetHistogram<T>(Order order, List<T> values, T batch, int count) where T : IConvertible
        {
            if (values.Count == 0) return "";

            Dictionary<T, HistogramValue> result = new Dictionary<T, HistogramValue>();

            double dBatch = Convert.ToDouble(batch);

            double minVal = double.MaxValue;
            double maxVal = double.MinValue;
            Dictionary<double, int> histogram = new Dictionary<double, int>();

            // Populate the histogram and find min, max in a single pass
            foreach (var value in values)
            {
                double dValue = Convert.ToDouble(value);
                if (dValue < minVal) minVal = dValue;
                if (dValue > maxVal) maxVal = dValue;

                double bin = Math.Floor(dValue / dBatch) * dBatch;
                histogram.TryAdd(bin, 0);
                histogram[bin]++;
            }

            double normalizedMin = Math.Floor(minVal / dBatch) * dBatch;
            double normalizedMax = Math.Floor(maxVal / dBatch + 1) * dBatch;

            int totalCount = 0;

            Action<double> updateResult = (i) =>
            {
                if (histogram.TryGetValue(i, out int value))
                {
                    result[(T)Convert.ChangeType(i, typeof(T))] = new HistogramValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            };

            if (order == Order.Desc)
            {
                for (double i = normalizedMax - dBatch; i >= normalizedMin; i -= dBatch)
                    updateResult(i);
            } else
            {
                for (double i = normalizedMin; i < normalizedMax; i += dBatch)
                    updateResult(i);
            }

            return JsonConvert.SerializeObject(result);
        }
    }
}
