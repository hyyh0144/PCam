// QR encoder: QRCoder 1.6.0 (MIT), https://github.com/codebude/QRCoder
// Library dependency; see ../THIRD-PARTY.md and ../licenses/QRCoder-LICENSE.txt.
// 二维码携带可用 IPv4、随机端口及认证信息。
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;
namespace PhoneCam;
public static class Pairing
{
    public static string[] LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
        .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
        .Select(a => a.Address.ToString()).Distinct().Take(8).ToArray();
    public static string Payload(string[] hosts, int port, string code, string id, string name) =>
        "phonecam://pair?v=2&hosts=" + Uri.EscapeDataString(string.Join(",", hosts)) +
        "&port=" + port + "&code=" + code + "&id=" + id + "&name=" + Uri.EscapeDataString(name);
    public static byte[] Qr(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(6);
    }
}
