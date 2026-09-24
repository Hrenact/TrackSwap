using System.Net;

namespace TrackSwap.HapticPhoneDiagnostic;

internal sealed class DiagnosticOptions
{
    public int UdpPort { get; private set; } = 9016;

    public int HttpPort { get; private set; } = 9017;

    public bool OpenBrowser { get; private set; } = true;

    public IPAddress? PreferredAddress { get; private set; }

    public static DiagnosticOptions Parse(string[] args)
    {
        var result = new DiagnosticOptions();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--udp-port":
                    result.UdpPort = ParsePort(ReadValue(args, ref index, "--udp-port"), "--udp-port");
                    break;
                case "--http-port":
                    result.HttpPort = ParsePort(ReadValue(args, ref index, "--http-port"), "--http-port");
                    break;
                case "--host-address":
                    string address = ReadValue(args, ref index, "--host-address");
                    if (!IPAddress.TryParse(address, out IPAddress? parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        throw new ArgumentException($"--host-address 需要一个 IPv4 地址，收到：{address}");
                    }
                    result.PreferredAddress = parsed;
                    break;
                case "--no-open":
                    result.OpenBrowser = false;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{args[index]}");
            }
        }
        return result;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"{option} 缺少值。");
        }
        return args[index];
    }

    private static int ParsePort(string value, string option)
    {
        if (!int.TryParse(value, out int port) || port < 1 || port > 65535)
        {
            throw new ArgumentException($"{option} 需要 1–65535 的端口号，收到：{value}");
        }
        return port;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("TrackSwap 手机振动诊断器");
        Console.WriteLine("  --udp-port <端口>       OSC 接收端口，默认 9016");
        Console.WriteLine("  --http-port <端口>      手机网页端口，默认 9017");
        Console.WriteLine("  --host-address <IPv4>   二维码使用的本机局域网地址");
        Console.WriteLine("  --no-open               不自动打开桌面诊断页");
    }
}
