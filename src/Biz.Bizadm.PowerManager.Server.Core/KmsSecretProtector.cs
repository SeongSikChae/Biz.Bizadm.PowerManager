using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.Server.Core
{
    public static class KmsSecretProtector
    {
        /// <summary>
        /// 설정의 보안값을 KEK + 로컬 DEK로 암호화해 저장하고, 메모리상 값은 복호화한다.
        /// 설정 파일 저장은 <paramref name="saveConfiguration"/>가 맡는다.
        /// </summary>
        public static void Protect(OsKmsConfiguration osKms, string contentRootPath, TlsConfiguration? tls, Action saveConfiguration)
        {
            IOsKekCredentialStore credentialStore = OsKekCredentialProvider.CreateForCurrentOs(
                osKms.CredentialService, osKms.CredentialAccount, OsKekCredentialDefaults.Namespace
            );

            if (string.IsNullOrEmpty(osKms.Salt))
            {
                // 최초 기동에서만 KEK 패스워드를 만들고, DEK 생성 전에 Salt를 먼저 기록한다.
                EnsureKekPassword(credentialStore);
                osKms.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                saveConfiguration();
            }

            using AesGcmKekManager manager = AesGcmKekManager.Create(
                credentialStore, Convert.FromBase64String(osKms.Salt), osKms.Iterations!.Value);
            using DekCipherService dekCipherService = new DekCipherService(AesGcmDekCipher.Create(manager.Current,
                new FileInfo(Path.Combine(contentRootPath, osKms.SecretsDekFilePath))));

            // 모든 보안값을 암호화해 한 번만 기록한 뒤 복호화한다. 복호화 후 기록하면 평문이 저장된다.
            bool useTls = tls is not null && tls.UseTls!.Value;
            bool encrypted = false;
            if (useTls)
            {
                string? password = tls!.ServerCertificatePassword;
                if (password is not null && !dekCipherService.StartsWithFromPrefix(password))
                {
                    tls.ServerCertificatePassword = dekCipherService.Encrypt(password);
                    encrypted = true;
                }
            }
            if (encrypted)
                saveConfiguration();

            if (useTls)
                tls!.ServerCertificatePassword = dekCipherService.Decrypt(tls.ServerCertificatePassword!);
        }

        private static void EnsureKekPassword(IOsKekCredentialStore credentialStore)
        {
            try
            {
                CryptographicOperations.ZeroMemory(credentialStore.GetPassword());
            }
            catch (InvalidOperationException)
            {
                byte[] kekPassword = RandomNumberGenerator.GetBytes(32);
                try
                {
                    credentialStore.StorePassword(kekPassword);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(kekPassword);
                }
            }
        }
    }
}
