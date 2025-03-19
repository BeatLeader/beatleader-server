using System.Security.Cryptography;
using System.Text;

namespace BeatLeader_Server.Utils
{
    public class AuthUtils
    {
        public static byte[] GenerateSalt(int size = 32)
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] salt = new byte[size];
                rng.GetBytes(salt);
                return salt;
            }
        }

        public static string HashPasswordWithSalt(string password, byte[] salt, int iterations = 10000, int hashSize = 20)
        {
            byte[] hash;
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                hash = pbkdf2.GetBytes(hashSize);
            }

            return Convert.ToBase64String(hash);
        }

        public static string HashIp(string ip, int iterations = 10000, int hashSize = 15)
        {
            byte[] hash;
            var salt = Encoding.UTF8.GetBytes("iphash");
            using (var pbkdf2 = new Rfc2898DeriveBytes(ip, salt, iterations, HashAlgorithmName.SHA256))
            {
                hash = pbkdf2.GetBytes(hashSize);
            }

            return Convert.ToBase64String(hash);
        }
    }
}
