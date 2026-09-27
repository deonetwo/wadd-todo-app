using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

public interface IDeviceService
{
    string GetDeviceId();
    DeviceMetadata GetDeviceMetadata();
}
