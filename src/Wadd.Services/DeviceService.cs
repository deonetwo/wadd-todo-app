using System.Runtime.InteropServices;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class DeviceService : IDeviceService
{
    private readonly string _deviceId;
    private readonly string _deviceName;

    public DeviceService()
    {
        var idPath = AppDataHelper.GetWaddFilePath("device_id.txt");

        if (File.Exists(idPath))
        {
            var content = File.ReadAllText(idPath).Trim();
            if (Guid.TryParse(content, out _))
            {
                _deviceId = content;
            }
            else
            {
                _deviceId = Guid.NewGuid().ToString("N");
                File.WriteAllText(idPath, _deviceId);
            }
        }
        else
        {
            _deviceId = Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(idPath, _deviceId);
            }
            catch { }
        }

        _deviceName = Environment.MachineName;
    }

    public string GetDeviceId() => _deviceId;

    public DeviceMetadata GetDeviceMetadata()
    {
        return new DeviceMetadata
        {
            DeviceId = _deviceId,
            DeviceName = _deviceName,
            Platform = RuntimeInformation.OSDescription,
            LastSyncedAt = DateTime.UtcNow
        };
    }
}
