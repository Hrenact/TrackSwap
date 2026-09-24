using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Json;
using QRCoder;

namespace TrackSwap.HapticPhoneDiagnostic;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        DiagnosticOptions options;
        try
        {
            options = DiagnosticOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("使用 --help 查看参数。");
            return 2;
        }

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        IReadOnlyList<System.Net.IPAddress> addresses = NetworkAddressProvider.GetLanAddresses(options.PreferredAddress);
        string localUrl = $"http://127.0.0.1:{options.HttpPort}/?token={token}";
        string[] phoneUrls = addresses
            .Select(address => $"http://{address}:{options.HttpPort}/?token={token}")
            .ToArray();
        string preferredPhoneUrl = phoneUrls.FirstOrDefault() ?? localUrl;

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://0.0.0.0:{options.HttpPort}");
        builder.Logging.ClearProviders();
        builder.Services.Configure<JsonOptions>(settings =>
            settings.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<HapticWebSocketHub>();
        builder.Services.AddHostedService<OscHapticListenerService>();

        WebApplication app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/info", (HttpContext context, HapticWebSocketHub hub) =>
        {
            if (!HasValidToken(context, token))
            {
                return Results.Unauthorized();
            }
            return Results.Json(new
            {
                udpPort = options.UdpPort,
                httpPort = options.HttpPort,
                phoneUrls,
                preferredPhoneUrl,
                websocketClients = hub.ClientCount
            });
        });

        app.MapGet("/qr.svg", (HttpContext context) =>
        {
            if (!HasValidToken(context, token))
            {
                return Results.Unauthorized();
            }
            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(preferredPhoneUrl, QRCodeGenerator.ECCLevel.Q);
            var code = new SvgQRCode(data);
            string svg = code.GetGraphic(6, "#e6e9ee", "#111315", true);
            return Results.Text(svg, "image/svg+xml; charset=utf-8");
        });

        app.Map("/ws", async (HttpContext context, HapticWebSocketHub hub) =>
        {
            if (!HasValidToken(context, token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await hub.AcceptAsync(context, context.RequestAborted).ConfigureAwait(false);
        });

        Console.WriteLine("TrackSwap 手机振动诊断器");
        Console.WriteLine("----------------------------------------");
        Console.WriteLine($"桌面诊断页：{localUrl}");
        if (phoneUrls.Length == 0)
        {
            Console.WriteLine("未找到局域网 IPv4 地址；可使用 --host-address 手动指定。");
        }
        else
        {
            Console.WriteLine("手机访问地址：");
            foreach (string url in phoneUrls)
            {
                Console.WriteLine($"  {url}");
            }
        }
        Console.WriteLine("按 Ctrl+C 停止。令牌仅在本次启动期间有效。");
        Console.WriteLine();

        try
        {
            await app.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"诊断器启动失败：{exception.GetBaseException().Message}");
            return 1;
        }

        if (options.OpenBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"无法自动打开浏览器：{exception.Message}");
            }
        }

        await app.WaitForShutdownAsync().ConfigureAwait(false);
        return 0;
    }

    private static bool HasValidToken(HttpContext context, string expected)
    {
        string supplied = context.Request.Query["token"].ToString();
        return supplied.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(supplied),
                System.Text.Encoding.ASCII.GetBytes(expected));
    }
}
