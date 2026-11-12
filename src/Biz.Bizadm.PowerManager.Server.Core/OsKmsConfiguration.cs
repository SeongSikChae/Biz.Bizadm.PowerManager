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

        public void Validate()
        {
            if (!Uri.TryCreate(CredentialService, UriKind.Absolute, out _))
                throw new ArgumentException(
                    $"CredentialService must be an absolute URI (e.g. https://myapp.local): '{CredentialService}'.",
                    nameof(CredentialService));
        }
    }
}
