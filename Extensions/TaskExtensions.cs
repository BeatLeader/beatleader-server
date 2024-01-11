namespace BeatLeader_Server.Extensions
{
    public static class TaskExtensions
    {
        public static async Task<(int, T)> CoundAndResults<T>(this Task<int> self, Task<T> resultTask)
        {
            await Task.WhenAll(self, resultTask);
            return (self.Result, resultTask.Result);
        }
    }
}
