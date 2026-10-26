using System;

namespace MutsumiLauncher.Services;

/// <summary>设备标识：首次运行生成随机 GUID，保存在本地配置里，用于订阅网关的白名单。</summary>
public static class DeviceIdentity
{
    private static readonly object Gate = new();
    private static string? _cached;

    public static string Current
    {
        get
        {
            lock (Gate)
            {
                if (_cached is not null) return _cached;

                var config = AppConfig.Current;
                if (string.IsNullOrWhiteSpace(config.DeviceId))
                {
                    config.DeviceId = Guid.NewGuid().ToString("N");
                    config.Save();
                    AppLog.Info($"device id generated: {config.DeviceId}");
                }

                _cached = config.DeviceId.Trim();
                return _cached;
            }
        }
    }
}
