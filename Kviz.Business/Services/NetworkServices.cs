using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Kviz.Business.Abstractions;
using Kviz.Business.Common;
using QRCoder;

namespace Kviz.Business.Services;

public class JoinLinkOptions
{
    /// <summary>İnternetə yüklənəndə sayt ünvanı (məs. https://kviz.example.com). Boşdursa avtomatik tapılır.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Oyunçuların qoşulacağı ünvanı müəyyən edir. İnternetdə domen, lokal şəbəkədə isə
/// kompüterin LAN IP ünvanı (localhost telefonlarda işləmədiyi üçün) istifadə olunur.
/// </summary>
public class JoinLinkService : IJoinLinkService
{
    private readonly JoinLinkOptions _options;
    public JoinLinkService(JoinLinkOptions options) => _options = options;

    public JoinInfoDto Build(string scheme, string host, int? port, string pin, string? preferredBaseUrl = null)
    {
        var info = new JoinInfoDto();

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            info.BaseUrl = _options.PublicBaseUrl.TrimEnd('/');
            info.IsLocalNetwork = false;
        }
        else if (IsLoopback(host))
        {
            var ips = GetLanAddresses();
            info.Alternatives = ips.Select(ip => Compose(scheme, ip, port)).ToList();
            info.IsLocalNetwork = true;
            info.BaseUrl = preferredBaseUrl != null && info.Alternatives.Contains(preferredBaseUrl)
                ? preferredBaseUrl
                : info.Alternatives.FirstOrDefault() ?? Compose(scheme, host, port);
        }
        else
        {
            info.BaseUrl = Compose(scheme, host, port);
            info.IsLocalNetwork = IPAddress.TryParse(host, out var addr) && IsPrivate(addr);
        }

        info.JoinUrl = $"{info.BaseUrl}/join?pin={pin}";
        info.ShortUrl = info.BaseUrl.Replace("https://", "", StringComparison.OrdinalIgnoreCase).Replace("http://", "", StringComparison.OrdinalIgnoreCase) + "/join";
        return info;
    }

    private static string Compose(string scheme, string host, int? port)
    {
        var isDefault = port is null or 80 or 443 || (scheme == "http" && port == 80) || (scheme == "https" && port == 443);
        var h = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
        return isDefault ? $"{scheme}://{h}" : $"{scheme}://{h}:{port}";
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "::1" || host == "[::1]";

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static readonly string[] IgnoredAdapters = { "vethernet", "vmware", "virtualbox", "hyper-v", "wsl", "docker", "npcap", "loopback", "bluetooth", "tap-" };

    /// <summary>Kompüterin lokal şəbəkə IPv4 ünvanları. Gateway-i olan (real Wi-Fi/Ethernet) adapterlər birinci gəlir.</summary>
    public static List<string> GetLanAddresses()
    {
        var found = new List<(int priority, string ip)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                if (IgnoredAdapters.Any(label.Contains)) continue;

                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var s = ua.Address.ToString();
                    if (s.StartsWith("169.254.") || s.StartsWith("127.")) continue;
                    found.Add((hasGateway ? 0 : 1, s));
                }
            }
        }
        catch { /* şəbəkə məlumatı alına bilmədi */ }
        return found.OrderBy(f => f.priority).Select(f => f.ip).Distinct().ToList();
    }
}

public class QrService : IQrService
{
    public string GenerateSvg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(new System.Drawing.Size(320, 320), "#1c0b3f", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
