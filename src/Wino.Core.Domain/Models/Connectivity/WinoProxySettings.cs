using System;
using System.Net;
using System.Net.Http;
using MailKit.Net.Proxy;
using Wino.Core.Domain.Entities.Shared;

namespace Wino.Core.Domain.Models.Connectivity;

/// <summary>
/// Application-wide HTTP proxy settings. Account-level proxy settings in
/// <see cref="CustomServerInformation"/> take precedence for mail connections.
/// </summary>
public static class WinoProxySettings
{
    private static readonly object SyncRoot = new();

    private static IWebProxy _systemDefaultProxy;
    private static bool _hasSystemDefaultProxy;
    private static bool _isEnabled;
    private static string _host;
    private static int _port;

    public static bool IsEnabled
    {
        get
        {
            lock (SyncRoot)
            {
                return _isEnabled;
            }
        }
    }

    public static void Apply(bool isEnabled, string host, string port)
    {
        lock (SyncRoot)
        {
            if (!_hasSystemDefaultProxy)
            {
                _systemDefaultProxy = HttpClient.DefaultProxy;
                _hasSystemDefaultProxy = true;
            }

            if (isEnabled &&
                !string.IsNullOrWhiteSpace(host) &&
                int.TryParse(port?.Trim(), out var parsedPort) &&
                parsedPort is > 0 and <= 65535)
            {
                _host = host.Trim();
                _port = parsedPort;
                _isEnabled = true;

                HttpClient.DefaultProxy = new WebProxy(_host, _port);
                return;
            }

            _isEnabled = false;
            _host = null;
            _port = 0;

            HttpClient.DefaultProxy = _systemDefaultProxy;
        }
    }

    public static ProxyClient CreateMailProxy(CustomServerInformation serverInformation)
    {
        if (serverInformation != null &&
            !string.IsNullOrWhiteSpace(serverInformation.ProxyServer) &&
            int.TryParse(serverInformation.ProxyServerPort?.Trim(), out var accountPort) &&
            accountPort is > 0 and <= 65535)
        {
            return new HttpProxyClient(serverInformation.ProxyServer.Trim(), accountPort);
        }

        lock (SyncRoot)
        {
            return _isEnabled ? new HttpProxyClient(_host, _port) : null;
        }
    }
}
