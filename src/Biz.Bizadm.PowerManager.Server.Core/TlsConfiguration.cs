using System.Configuration;
using System.Configuration.Annotation;

namespace Biz.Bizadm.PowerManager.Server.Core
{
    public sealed class TlsConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.BOOL, required: true)]
        public bool? UseTls { get; set; } = null!;

        [Property(PropertyType.STRING)]
        public string? ServerCertificate { get; set; }

        [Property(PropertyType.STRING)]
        public string? ServerCertificatePassword { get; set; }

        [Property(PropertyType.STRING, required: false)]
        public string? CertificateChain { get; set; }

        [Property(PropertyType.STRING, required: false)]
        public string? IncludeCipherSuites { get; set; }

        public void Validate()
        {
            if (UseTls.HasValue && UseTls.Value)
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(ServerCertificate, nameof(ServerCertificate));
                ArgumentNullException.ThrowIfNullOrWhiteSpace(ServerCertificatePassword, nameof(ServerCertificatePassword));
            }
        }
    }
}
