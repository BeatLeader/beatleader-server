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

            Dictionary<double, int> histogram = new Dictionary<double, int>();

            // Populate the histogram and find min, max in a single pass
            foreach (var value in values)
            {
                double dValue = Convert.ToDouble(value);

                double bin = Math.Floor(dValue / dBatch) * dBatch;
                histogram.TryAdd(bin, 0);
                histogram[bin]++;
            }

            var list = order == Order.Desc 
                ? histogram.Select(h => new { h.Key, h.Value }).OrderByDescending(h => h.Key).ToList() 
                : histogram.Select(h => new { h.Key, h.Value }).OrderBy(h => h.Key).ToList();
            int totalCount = 0;
            foreach (var item in list) {
                result[(T)Convert.ChangeType(item.Key, typeof(T))] = new HistogramValue { Value = item.Value, Page = totalCount / count };
                totalCount += item.Value;
            }

            return JsonConvert.SerializeObject(result);
        }
    }
}
