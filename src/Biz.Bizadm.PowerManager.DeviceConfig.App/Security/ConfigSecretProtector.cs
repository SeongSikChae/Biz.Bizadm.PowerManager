using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App.Security
{
    /// <summary>
    /// 복호화한 보안값. YAML 모델에는 되돌려 넣지 않는다.
    /// </summary>
    internal sealed record DecryptedSecrets(string? MqttKey, IReadOnlyDictionary<string, string> PassBySsid)
    {
        public static DecryptedSecrets Empty { get; } = new(null, new Dictionary<string, string>());
    }

    /// <summary>
    /// OsKms(KEK: OS 자격 증명 금고 패스워드 → PBKDF2, DEK: 로컬 파일)로 WifiProfiles[].Pass, Settings.MqttKey를
    /// 암호화(DEKENC:)·복호화한다. KEK manager와 DEK 서비스를 같은 수명으로 묶는다.
    /// </summary>
    internal sealed class ConfigSecretProtector : IDisposable
    {
        private const string MqttKeyAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private const int MqttKeyLength = 16;

        private readonly AesGcmKekManager manager;
        private readonly DekCipherService dek;

        private ConfigSecretProtector(AesGcmKekManager manager, DekCipherService dek, bool rewrite)
        {
            this.manager = manager;
            this.dek = dek;
            Rewrite = rewrite;
        }

        /// <summary>
        /// 설정 파일을 다시 써야 하는지(Salt 생성, 평문 암호화).
        /// </summary>
        public bool Rewrite { get; private set; }

        /// <summary>
        /// 보호할 값이 있는지. 없으면 KEK·DEK를 만들지 않는다.
        /// </summary>
        public static bool HasSecrets(AppConfiguration config) =>
            config.Commands.Contains(DeviceCommand.ConnectAp) ||
            !string.IsNullOrEmpty(config.Settings?.MqttKey) ||
            config.WifiProfiles.Any(profile => !string.IsNullOrEmpty(profile.Pass));

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
        /// ConnectAp인데 MqttKey가 비면 생성하고, 평문 보안값을 암호화해 설정 객체에 반영한 뒤 복호화 값을 돌려준다.
        /// </summary>
        /// <param name="log">값을 포함하지 않는 진행 메시지 출력.</param>
        public DecryptedSecrets Protect(AppConfiguration config, Action<string> log)
        {
            SettingsSection? settings = config.Settings;
            if (settings is not null && config.Commands.Contains(DeviceCommand.ConnectAp) && string.IsNullOrEmpty(settings.MqttKey))
            {
                settings.MqttKey = RandomNumberGenerator.GetString(MqttKeyAlphabet, MqttKeyLength);
                log("mqtt_key generated");
            }

            string? mqttKey = null;
            if (!string.IsNullOrEmpty(settings?.MqttKey))
            {
                settings.MqttKey = EnsureEncrypted(settings.MqttKey);
                mqttKey = dek.Decrypt(settings.MqttKey);
            }

            Dictionary<string, string> passBySsid = new(StringComparer.Ordinal);
            foreach (WifiProfileSection profile in config.WifiProfiles)
            {
                // ConnectAp가 없으면 프로필 검증 대상이 아니므로 빈 Pass가 있을 수 있다
                if (string.IsNullOrEmpty(profile.Pass))
                    continue;
                profile.Pass = EnsureEncrypted(profile.Pass);
                passBySsid[profile.Ssid] = dek.Decrypt(profile.Pass);
            }
            return new DecryptedSecrets(mqttKey, passBySsid);
        }

        private string EnsureEncrypted(string value)
        {
            if (dek.StartsWithFromPrefix(value))
                return value;
            Rewrite = true;
            return dek.Encrypt(value);
        }

        public void Dispose()
        {
            dek.Dispose();
            manager.Dispose();
        }
    }
}
