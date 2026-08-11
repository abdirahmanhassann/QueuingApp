using System.Data.SqlTypes;
using static System.Text.Encoding;
using System.Security.Cryptography;
using System.Text.Unicode;
using QueueingApp.Connectivity;
namespace QueueingApp.Security
{
    public class Encryptor
    {

        private string _key = "***REMOVED_ENCRYPTION_KEY***";
        private QADB _db;

        public Encryptor(IConfiguration config)
        {
            _db = new(config.GetSection("ConnectionStrings:QADB").Get<string>()!, false);
        }

#pragma warning disable SYSLIB0022
#pragma warning disable SYSLIB0041

        public int CheckToken(string token)
        {
            int accessToken = _db!.TokenValid(token);
            if (accessToken != 0)
            {
                _db.ExtendAccessToken(token);
            }
            //Positive integer is the AccountId, zero = token not valid
            return accessToken;
        }

        public string Encrypt(string value, string salt)
        {
            byte[] valueBytes = UTF8.GetBytes(value);
            byte[] pKeyBytes = UTF8.GetBytes(_key);

            pKeyBytes = SHA256.Create().ComputeHash(pKeyBytes);
            byte[] EncBytes = EncryptBytes(valueBytes, pKeyBytes, salt);

            return System.Convert.ToBase64String(EncBytes);
        }

        public string Decrypt(string value, string salt)
        {
            try
            {
                byte[] valueBytes = System.Convert.FromBase64String(value);
                byte[] pKeyBytes = UTF8.GetBytes(_key);

                pKeyBytes = SHA256.Create().ComputeHash(pKeyBytes);
                byte[] DecBytes = DecryptBytes(valueBytes, pKeyBytes, salt);

                return UTF8.GetString(DecBytes);
            }
            catch
            {
                return "";
            }
        }


        public string GenerateRandomSalt()
        {
            Random Rnd = new Random(Environment.TickCount);
            string Ret = "";
            for (int NN = 1; NN <= 12; NN++)
            {
                int intRand = Rnd.Next(0, 10);
                string strRand = intRand.ToString();
                Ret += strRand;
            }
            return Ret.ToUpper();
        }


        private byte[] EncryptBytes(byte[] bytes, byte[] pKeyBytes, string saltString)
        {

            try
            {

                byte[] ret;
                byte[] saltBytes = GetSalt(saltString);

                using (MemoryStream MS = new MemoryStream())
                {
                    using (RijndaelManaged AES = new RijndaelManaged())
                    {

                        AES.KeySize = 256;
                        AES.BlockSize = 128;

                        Rfc2898DeriveBytes Key = new Rfc2898DeriveBytes(pKeyBytes, saltBytes, 1000);
                        AES.Key = Key.GetBytes(AES.KeySize / 8);
                        AES.IV = Key.GetBytes(AES.BlockSize / 8);
                        AES.Mode = CipherMode.CBC;

                        using (CryptoStream CS = new CryptoStream(MS, AES.CreateEncryptor(), CryptoStreamMode.Write))
                        {
                            CS.Write(bytes, 0, bytes.Length);
                            CS.Close();
                        }

                        ret = MS.ToArray();

                    }
                }
                return ret;
            }
            catch
            {
                return null!;
            }

        }

        private byte[] DecryptBytes(byte[] Bytes, byte[] PKeyBytes, string saltString)
        {

            try
            {

                byte[] ret;
                byte[] saltBytes = GetSalt(saltString);

                using (MemoryStream MS = new MemoryStream())
                {
                    using (RijndaelManaged AES = new RijndaelManaged())
                    {
                        AES.KeySize = 256;
                        AES.BlockSize = 128;

                        Rfc2898DeriveBytes Key = new Rfc2898DeriveBytes(PKeyBytes, saltBytes, 1000);
                        AES.Key = Key.GetBytes(AES.KeySize / 8);
                        AES.IV = Key.GetBytes(AES.BlockSize / 8);
                        AES.Mode = CipherMode.CBC;

                        using (CryptoStream CS = new CryptoStream(MS, AES.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            CS.Write(Bytes, 0, Bytes.Length);
                            if (!CS.HasFlushedFinalBlock)
                            {
                                CS.FlushFinalBlock();
                            }
                            CS.Close();
                        }
                        ret = MS.ToArray();
                    }
                }
                return ret;
            }
            catch
            {
                return null!;
            }

        }


        private byte[] GetSalt(string saltValue)
        {
            if (saltValue.Length != 12)
            {
                throw new Exception("Salt must be twelve characters");
            }
            List<byte> SaltBytes = new List<byte>();
            for (int nn = 0; nn <= 11; nn++)
            {
                SaltBytes.Add(System.Convert.ToByte(saltValue.Substring(nn, 1)));
            }
            return SaltBytes.ToArray();
        }

#pragma warning restore SYSLIB0022
#pragma warning restore SYSLIB0041

    }
}