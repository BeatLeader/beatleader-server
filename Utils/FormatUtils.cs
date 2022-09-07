using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace BeatLeader_Server.Utils
{
    public class FormatUtils
    {
        public static string DescribeModifiersChanges(ModifiersMap oldM, ModifiersMap newM) {
            string message = "";
            if (oldM != null && newM != null && !oldM.EqualTo(newM))
            {
                var modifiersDictionary = newM.ToDictionary<float>();
                var oldModifiersDictionary = oldM.ToDictionary<float>();
                message += "**M** ";
                foreach (var item in modifiersDictionary)
                {
                    if (modifiersDictionary[item.Key] != oldModifiersDictionary[item.Key])
                    {
                        message += item.Key + " " + Math.Round(oldModifiersDictionary[item.Key] * 100, 2) + " → " + Math.Round(modifiersDictionary[item.Key] * 100, 2) + "   ";
                    }
                }

                message += "\n";
            }
            return message;
        }

        public static string[] typeNames = { "acc", "tech", "midspeed", "speed" };

        public static string DescribeType(int mapType) {
            string message = "";
            foreach (var (value, i) in typeNames.Select((value, i) => (value, i)))
            {
                if ((1 << i & mapType) != 0) {
                    message += value + ", ";
                }
            }

            return message.Length > 0 ? message.Substring(0, message.Length - 2) : "none";
        }

        public static string DescribeTypeChanges(int oldT, int newT) {
            string message = "";
            if (oldT != newT) {
                message += " **T**  ";
                message += DescribeType(oldT) + " → " + DescribeType(newT);
                message += "\n";
            }
            return message;
        }
    }
}
