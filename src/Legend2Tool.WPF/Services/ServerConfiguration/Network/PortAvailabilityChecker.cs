using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace Legend2Tool.WPF.Services.ServerConfiguration.Network;

internal sealed class PortAvailabilityChecker
{
    public bool Check(int[] ports)
    {
        foreach (var port in ports.AsParallel().Select(port => new { port, InUse = IsInUse(port) }))
        {
            if (!port.InUse)
                continue;
            MessageBox.Show($"端口 {port.port} 已经被使用.", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private static bool IsInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return false;
        }
        catch { return true; }
    }
}
