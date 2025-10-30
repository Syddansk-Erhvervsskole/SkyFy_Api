using System.Text;
using System.Security.Cryptography;

namespace SkyFy_Api.Services
{
    public class EncryptionService
    {
        private readonly IConfiguration _config;
        public EncryptionService(IConfiguration config)
        {
            _config = config;
        }
        public string Sha256(string input)
        {
            string key = _config["Keys:Sha256"];
            string combined = key + input;

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(combined);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToHexString(hash).ToLower();
            }
        }

    }
}
