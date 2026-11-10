using System.Runtime.Versioning;

// 팩터리·구현 전체를 Windows 전용으로 표시해 CA1416 경고를 없앤다(HostWifiLoader는 리플렉션이라 경고 없음).
[assembly: SupportedOSPlatform("windows")]
