using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.Server.Core
{
    public sealed class OsKmsConfiguration : IValidatableConfiguration
    {
        /// <summary>
        /// OS 자격 증명 저장소의 서비스 이름. Windows 저장소가 절대 URI로 해석하므로 URI 형식이어야 한다(예: https://myapp.local).
        /// </summary>
        [Property(PropertyType.STRING, required: true)]
        public string CredentialService { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string CredentialAccount { get; set; } = null!;

        [Property(PropertyType.STRING)]
        public string? Salt { get; set; }

        [Property(PropertyType.INT, required: true)]
        public int? Iterations { get; set; } = null!;

        [Property(PropertyType.STRING, required: true)]
        public string SecretsDekFilePath { get; set; } = null!;

        /// <summary>
        /// KEK 패스워드를 둘 자격 증명 저장소(<c>gpg</c> 또는 <c>secretservice</c>). 비우면 지금처럼 KMS 패키지가 고른다
        /// (환경 변수 <c>GCM_CREDENTIAL_STORE</c>, 없으면 OS별 기본값). 화면 없는 Linux에서는 <c>gpg</c>를 쓴다.
        /// </summary>
        [Property(PropertyType.STRING)]
        public string? CredentialStore { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(CredentialService, UriKind.Absolute, out _))
                throw new ArgumentException(
                    $"CredentialService must be an absolute URI (e.g. https://myapp.local): '{CredentialService}'.",
                    nameof(CredentialService));
            if (!string.IsNullOrWhiteSpace(CredentialStore) && !CredentialStoreEnvironment.AllowedStores.Contains(CredentialStore))
                throw new ArgumentException(
                    $"CredentialStore must be empty or one of: {string.Join(", ", CredentialStoreEnvironment.AllowedStores)}.",
                    nameof(CredentialStore));
        }
    }
}
