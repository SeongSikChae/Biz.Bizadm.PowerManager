using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Security
{
    /// <summary>
    /// OsKms(KEK: OS 자격 증명 금고 패스워드 → PBKDF2, DEK: 로컬 파일)로 Register.MqttKey·Mqtt.Password를
    /// 암호화(DEKENC:)·복호화한다. DeviceConfig.App의 같은 이름 클래스와 구조가 같다(D020). KEK manager와 DEK 서비스를 같은 수명으로 묶는다.
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

        /// <summary>보호할 값이 있는지. 둘 다 없으면 KMS(자격 증명·DEK 파일)를 건너뛴다.</summary>
        public static bool HasSecrets(AppConfiguration config) => config.HasSecrets();

        /// <param name="config">설정 모델.</param>
        /// <param name="basePath">상대 DEK 파일 경로의 기준. 설정 모델의 ContentRootPath를 그대로 쓰지 않는다(D025).</param>
        /// <exception cref="InvalidOperationException">Salt가 비었는데 DEK 파일이 있다(기존 KEK를 덮어쓰지 않는다).</exception>
        public static ConfigSecretProtector Create(AppConfiguration config, string basePath)
        {
            OsKmsSection kms = config.OsKms ?? throw new InvalidOperationException("OsKms is required");
            IOsKekCredentialStore store = OsKekCredentialProvider.CreateForCurrentOs(
                kms.CredentialService, kms.CredentialAccount, OsKekCredentialDefaults.Namespace);
            FileInfo dekFile = new(Path.Combine(basePath, kms.SecretsDekFilePath));

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
        /// 평문 MqttKey·Password를 암호화해 설정 객체에 반영하고, 복호화한 값을 돌려준다(YAML 모델에는 평문을 되돌려 넣지 않는다).
        /// </summary>
        public ConfigSecrets Protect(AppConfiguration config)
        {
            string? mqttKey = null;
            if (config.Register.MqttKey is { Length: > 0 } storedKey)
            {
                config.Register.MqttKey = EnsureEncrypted(storedKey);
                mqttKey = dek.Decrypt(config.Register.MqttKey);
            }

            string? password = null;
            if (config.Mqtt.Password is { Length: > 0 } storedPassword)
            {
                config.Mqtt.Password = EnsureEncrypted(storedPassword);
                password = dek.Decrypt(config.Mqtt.Password);
            }
            return new ConfigSecrets(mqttKey, password);
        }

        private string EnsureEncrypted(string stored)
        {
            if (dek.StartsWithFromPrefix(stored))
                return stored;
            Rewrite = true;
            return dek.Encrypt(stored);
        }

        public void Dispose()
        {
            dek.Dispose();
            manager.Dispose();
        }
    }

    /// <summary>복호화한 보안값. 설정에 없으면 null이다.</summary>
    internal sealed record ConfigSecrets(string? MqttKey, string? Password)
    {
        public static readonly ConfigSecrets Empty = new(null, null);

        // record 기본 ToString이 복호화 값을 내지 않도록 가린다
        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"MqttKey = {(MqttKey is null ? "null" : "***")}, Password = {(Password is null ? "null" : "***")}");
            return true;
        }
    }
}
