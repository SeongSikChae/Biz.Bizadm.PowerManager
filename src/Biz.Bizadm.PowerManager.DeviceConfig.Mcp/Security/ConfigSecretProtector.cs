using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Security
{
    /// <summary>
    /// OsKms(KEK: OS 자격 증명 금고 패스워드 → PBKDF2, DEK: 로컬 파일)로 WifiProfiles[].Pass를
    /// 암호화(DEKENC:)·복호화한다(App에서 복사). KEK manager와 DEK 서비스를 같은 수명으로 묶는다.
    /// </summary>
    internal sealed class ConfigSecretProtector : IDisposable
    {
        private readonly AesGcmKekManager manager;
        private readonly DekCipherService dek;

        private ConfigSecretProtector(AesGcmKekManager manager, DekCipherService dek, bool rewrite)
        {
            this.manager = manager;
            this.dek = dek;
            Rewrite = rewrite;
        }

        /// <summary>설정 파일을 다시 써야 하는지(Salt 생성, 평문 암호화).</summary>
        public bool Rewrite { get; private set; }

        /// <summary>보호할 값이 있는지. WifiProfiles가 비면 KMS(자격 증명·DEK 파일)를 건너뛴다.</summary>
        public static bool HasSecrets(McpConfiguration config) => config.WifiProfiles.Count > 0;

        /// <exception cref="InvalidOperationException">Salt가 비었는데 DEK 파일이 있다(기존 KEK를 덮어쓰지 않는다).</exception>
        public static ConfigSecretProtector Create(McpConfiguration config)
        {
            OsKmsSection kms = config.OsKms;
            IOsKekCredentialStore store = OsKekCredentialProvider.CreateForCurrentOs(
                kms.CredentialService, kms.CredentialAccount, OsKekCredentialDefaults.Namespace);
            FileInfo dekFile = new(Path.Combine(config.ContentRootPath, kms.SecretsDekFilePath));

            bool rewrite = false;
            if (string.IsNullOrEmpty(kms.Salt))
            {
                // 최초 기동은 Salt와 DEK 파일이 모두 없을 때만
                if (dekFile.Exists)
                    throw new InvalidOperationException($"OsKms.Salt is empty but the DEK file exists: {dekFile.FullName}");
                store.StorePassword(RandomNumberGenerator.GetBytes(32));
                kms.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                rewrite = true;
            }

            AesGcmKekManager manager = AesGcmKekManager.Create(store, Convert.FromBase64String(kms.Salt), kms.Iterations);
            try
            {
                return new ConfigSecretProtector(manager, new DekCipherService(AesGcmDekCipher.Create(manager.Current, dekFile)), rewrite);
            }
            catch
            {
                manager.Dispose();
                throw;
            }
        }

        /// <summary>평문 Pass를 암호화해 설정 객체에 반영하고, 복호화한 프로필을 돌려준다(YAML 모델에는 평문을 되돌려 넣지 않는다).</summary>
        public IReadOnlyList<WifiProfile> Protect(McpConfiguration config)
        {
            List<WifiProfile> profiles = [];
            foreach (WifiProfileSection profile in config.WifiProfiles)
            {
                if (!dek.StartsWithFromPrefix(profile.Pass))
                {
                    profile.Pass = dek.Encrypt(profile.Pass);
                    Rewrite = true;
                }
                profiles.Add(new WifiProfile(profile.Ssid, dek.Decrypt(profile.Pass)));
            }
            return profiles;
        }

        public void Dispose()
        {
            dek.Dispose();
            manager.Dispose();
        }
    }
}
