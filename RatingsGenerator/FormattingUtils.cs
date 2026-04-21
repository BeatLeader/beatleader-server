namespace RatingAPI.Utils
{
    public class FormattingUtils
    {
        public static string GetDiffLabel(int difficulty)
        {
            switch (difficulty)
            {
                case 1: return "Easy";
                case 3: return "Normal";
                case 5: return "Hard";
                case 7: return "Expert";
                case 9: return "ExpertPlus";
                default: return difficulty.ToString();
            }
        }
    }
}