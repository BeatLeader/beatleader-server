namespace BeatLeader_Server.Utils
{
    public static class MathUtils
    {
        public static float AddToAverage(float oldAverage, int count, float newValue)
        {
            return oldAverage + (newValue - oldAverage) / (float)(count > 0 ? count : 1);
        }

        public static float RemoveFromAverage(float currentAverage, int count, float newValue)
        {
            return (currentAverage * (float)count - newValue) / (float)((count > 1 ? count : 2) - 1);
        }
    }
}

