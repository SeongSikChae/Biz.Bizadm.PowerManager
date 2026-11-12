using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.Server.Core
{
    public static class TlsCertificateLoader
    {
        /// <summary>
        /// 인증서 파일의 절대 경로. Kestrel(체인 로드 포함)과 MQTTS가 같은 경로 계산을 쓴다.
        /// </summary>
        public static string ResolvePath(TlsConfiguration tls, string contentRootPath)
            => new FileInfo(Path.Combine(contentRootPath, tls.ServerCertificate!)).FullName;

        /// <summary>
        /// MQTTS용 서버 인증서를 로드한다. 패스워드는 KMS로 복호화된 설정 값을 호출 시에만 읽고 보관·로그하지 않는다. 호출자가 Dispose한다.
        /// </summary>
        public static X509Certificate2 Load(TlsConfiguration tls, string contentRootPath)
            => X509CertificateLoader.LoadPkcs12FromFile(ResolvePath(tls, contentRootPath), tls.ServerCertificatePassword);
    }
}
