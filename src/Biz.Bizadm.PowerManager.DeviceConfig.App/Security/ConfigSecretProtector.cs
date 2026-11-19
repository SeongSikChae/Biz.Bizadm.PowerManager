using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App.Security
{
    /// <summary>
    /// OsKms(KEK: OS 자격 증명 금고 패스워드 → PBKDF2, DEK: 로컬 파일)로 WifiProfiles[].Pass·Settings.MqttKey를
    /// 암호화(DEKENC:)·복호화한다(#2 구현 참고, MqttKey는 #13 V1 흐름용). KEK manager와 DEK 서비스를 같은 수명으로 묶는다.
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

        /// <summary>보호할 값이 있는지. WifiProfiles와 Settings.MqttKey가 모두 없으면 KMS(자격 증명·DEK 파일)를 건너뛴다.</summary>
        public static bool HasSecrets(AppConfiguration config) =>
            config.WifiProfiles.Count > 0 || !string.IsNullOrEmpty(config.Settings?.MqttKey);

        /// <exception cref="InvalidOperationException">Salt가 비었는데 DEK 파일이 있다(기존 KEK를 덮어쓰지 않는다).</exception>
        public static ConfigSecretProtector Create(AppConfiguration config)
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

        /// <summary>
        /// 평문 Pass·Settings.MqttKey를 암호화해 설정 객체에 반영하고, 복호화한 값을 돌려준다(YAML 모델에는 평문을 되돌려 넣지 않는다).
        /// </summary>
        public ProtectedSecrets Protect(AppConfiguration config)
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

            string? mqttKey = null;
            if (config.Settings is { MqttKey: { Length: > 0 } stored } settings)
            {
                if (!dek.StartsWithFromPrefix(stored))
                {
                    settings.MqttKey = dek.Encrypt(stored);
                    Rewrite = true;
                }
                mqttKey = dek.Decrypt(settings.MqttKey!);
            }
            return new ProtectedSecrets(profiles, mqttKey);
        }

        public void Dispose()
        {
            dek.Dispose();
            manager.Dispose();
        }
    }

    /// <summary>복호화한 보안값. MqttKey는 Settings.MqttKey가 없으면 null이다.</summary>
    internal sealed record ProtectedSecrets(IReadOnlyList<WifiProfile> WifiProfiles, string? MqttKey)
    {
        public static readonly ProtectedSecrets Empty = new([], null);

        // record 기본 ToString이 복호화 값을 내지 않도록 가린다
        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"WifiProfiles = {WifiProfiles.Count}, MqttKey = {(MqttKey is null ? "null" : "***")}");
            return true;
        }
    }
}
