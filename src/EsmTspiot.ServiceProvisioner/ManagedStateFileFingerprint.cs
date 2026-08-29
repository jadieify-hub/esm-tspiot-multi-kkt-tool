using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class ManagedStateFileFingerprint
    {
        internal static string Compute(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2"));
                }
                return result.ToString();
            }
        }
    }
}
