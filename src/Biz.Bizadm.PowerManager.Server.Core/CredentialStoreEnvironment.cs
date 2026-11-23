namespace Biz.Bizadm.PowerManager.Server.Core
{
    /// <summary>
    /// 설정의 <see cref="OsKmsConfiguration.CredentialStore"/>를 KMS 패키지가 읽는 프로세스 환경 변수로 옮긴다.
    /// 패키지는 자격 증명 저장소를 만들 때 환경 변수를 읽으므로, 저장소를 만들기 전에 부른다.
    /// </summary>
    internal static class CredentialStoreEnvironment
    {
        internal const string StoreVariable = "GCM_CREDENTIAL_STORE";
        internal const string GpgTtyVariable = "GPG_TTY";
        internal const string SshTtyVariable = "SSH_TTY";
        internal const string Gpg = "gpg";
        internal const string SecretService = "secretservice";

        /// <summary>허용하는 저장소. 대소문자까지 같아야 한다. 평문(<c>plaintext</c>)·메모리(<c>cache</c>) 저장소는 받지 않는다.</summary>
        internal static readonly IReadOnlyList<string> AllowedStores = [Gpg, SecretService];

        /// <summary>
        /// 값이 비어 있으면 아무것도 바꾸지 않는다. 값이 있으면 <c>GCM_CREDENTIAL_STORE</c>를 덮어쓰고,
        /// <c>gpg</c>이면서 <c>GPG_TTY</c>와 <c>SSH_TTY</c>가 모두 없을 때 gpg가 터미널을 요구하지 않도록 <c>GPG_TTY=/dev/null</c>을 넣는다.
        /// 빈 값인 변수도 있는 것으로 본다(패키지 판정과 같음).
        /// </summary>
        internal static void Apply(string? credentialStore)
        {
            if (string.IsNullOrWhiteSpace(credentialStore))
                return;

            Environment.SetEnvironmentVariable(StoreVariable, credentialStore);

            if (credentialStore == Gpg
                && Environment.GetEnvironmentVariable(GpgTtyVariable) is null
                && Environment.GetEnvironmentVariable(SshTtyVariable) is null)
                Environment.SetEnvironmentVariable(GpgTtyVariable, "/dev/null");
        }
    }
}
