using System.Security.Cryptography;
using System.Text;

namespace WAWALauncher.Services
{
    public class LauncherService : ILauncherService
    {
        private string launchCommand = "LAUNCHING COMMAND";
        public async Task LaunchGame(string gameParams)
        {
            Console.WriteLine($"Game launched. Command: {launchCommand}");
        }
        
        private string GenerateOfflineUUID(string nickname)
        {
            // Совместимо с Java UUID.nameUUIDFromBytes("OfflinePlayer:" + nickname).
            string offlinePlayer = "OfflinePlayer:" + nickname;

            using (var hasher = MD5.Create())
            {
                byte[] hash = hasher.ComputeHash(Encoding.UTF8.GetBytes(offlinePlayer));

                // Name-based UUID version 3 (MD5).
                hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

                return new Guid(
                    (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3],
                    (short)((hash[4] << 8) | hash[5]),
                    (short)((hash[6] << 8) | hash[7]),
                    hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]
                ).ToString();
            }
        }
    }
}
