using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Core
{
    public static class ServerKestrelExtensions
    {
        private const string LoggerCategory = "Biz.Bizadm.PowerManager.Server.Core.Kestrel";

        /// <summary>
        /// HTTP 포트는 항상 HTTP/1.1로 바인딩한다. TLS 사용 시 HTTPS 포트를 추가로 열고 HTTP/1.1·2·3과 인증서·체인·TLS 1.2/1.3을 적용한다.
        /// </summary>
        public static IWebHostBuilder ConfigureServerKestrel(this IWebHostBuilder webHost,
            IReadOnlyCollection<ushort> httpPorts, IReadOnlyCollection<ushort> httpsPorts, TlsConfiguration? tls, string contentRootPath)
            => webHost.ConfigureKestrel(options =>
            {
                foreach (ushort port in httpPorts)
                    options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http1);

                if (tls is null || tls.UseTls != true)
                    return;

                ILogger logger = options.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
                string certificatePath = TlsCertificateLoader.ResolvePath(tls, contentRootPath);

                foreach (ushort port in httpsPorts)
                    options.ListenAnyIP(port, listen => ConfigureHttps(listen, tls, certificatePath, logger));
            });

        private static void ConfigureHttps(ListenOptions listen, TlsConfiguration tls, string certificatePath, ILogger logger)
        {
            listen.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;

            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, tls.ServerCertificatePassword);
            listen.UseHttps(https =>
            {
                https.ServerCertificate = certificate;

                if (tls.CertificateChain is not null)
                {
                    X509Certificate2Collection collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(certificatePath, tls.ServerCertificatePassword);
                    https.ServerCertificateChain = BuildCertificateChain(collection, tls.CertificateChain, logger);
                }

                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

                CipherSuitesPolicy? policy = CreateCipherSuitesPolicy(tls.IncludeCipherSuites, logger);
                if (policy is not null)
                {
                    https.OnAuthenticate = (connectionContext, authenticationOptions) =>
                    {
                        authenticationOptions.AllowRenegotiation = false;
                        authenticationOptions.CipherSuitesPolicy = policy;
                    };
                }
            });
        }

        /// <summary>
        /// PKCS#12의 개인키 없는 인증서 중 <paramref name="certificateChain"/>(쉼표 구분 이름) 순서대로 체인을 만든다.
        /// 이름이 중복되면 경고 후 먼저 나온 인증서를 쓴다.
        /// </summary>
        internal static X509Certificate2Collection BuildCertificateChain(X509Certificate2Collection collection, string certificateChain, ILogger logger)
        {
            Dictionary<string, X509Certificate2> certificates = new Dictionary<string, X509Certificate2>();
            foreach (X509Certificate2 certificate in collection.Where(cert => !cert.HasPrivateKey))
            {
                string name = certificate.GetNameInfo(X509NameType.SimpleName, false);
                if (!certificates.TryAdd(name, certificate))
                    logger.LogWarning("Duplicate chain certificate name {Name}; using the first one.", name);
            }

            X509Certificate2Collection chain = new X509Certificate2Collection();
            foreach (string name in certificateChain.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (certificates.TryGetValue(name, out X509Certificate2? certificate))
                    chain.Add(certificate);
            }
            return chain;
        }

        /// <summary>
        /// 쉼표 구분 cipher suite 이름을 해석한다. 잘못된 이름은 경고 후 제외한다.
        /// </summary>
        internal static IReadOnlyCollection<TlsCipherSuite> ParseCipherSuites(string includeCipherSuites, ILogger logger)
        {
            HashSet<TlsCipherSuite> suites = new HashSet<TlsCipherSuite>();
            foreach (string name in includeCipherSuites.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse(name, out TlsCipherSuite suite) && Enum.IsDefined(suite))
                    suites.Add(suite);
                else
                    logger.LogWarning("Unknown cipher suite {CipherSuite} is ignored.", name);
            }
            return suites;
        }

        /// <summary>
        /// cipher suite 정책을 만든다. Windows는 미지원이라 경고 후 null(OS 기본)을 반환하고, 유효한 이름이 없어도 null이다.
        /// </summary>
        internal static CipherSuitesPolicy? CreateCipherSuitesPolicy(string? includeCipherSuites, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(includeCipherSuites))
                return null;

            if (OperatingSystem.IsWindows())
            {
                logger.LogWarning("IncludeCipherSuites is not supported on Windows and is ignored; OS default cipher suites are used.");
                return null;
            }

            IReadOnlyCollection<TlsCipherSuite> suites = ParseCipherSuites(includeCipherSuites, logger);
            WarnIfNoTls13CipherSuite(suites, logger);
            return suites.Count > 0 ? new CipherSuitesPolicy(suites) : null;
        }

        private static readonly HashSet<TlsCipherSuite> Tls13CipherSuites =
        [
            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_AES_128_CCM_SHA256,
            TlsCipherSuite.TLS_AES_128_CCM_8_SHA256,
        ];

        /// <summary>
        /// HTTP/3(QUIC)는 TLS 1.3만 쓰므로, 지정한 suite에 TLS 1.3 suite가 없으면 경고한다. 정책은 그대로 적용한다.
        /// </summary>
        internal static void WarnIfNoTls13CipherSuite(IReadOnlyCollection<TlsCipherSuite> suites, ILogger logger)
        {
            if (suites.Count > 0 && !suites.Any(Tls13CipherSuites.Contains))
                logger.LogWarning("IncludeCipherSuites has no TLS 1.3 cipher suite; HTTP/3 connections will fail.");
        }
    }
}
