namespace Wadd.Core.Interfaces;

public interface IStartupService
{
    bool IsSupported { get; }
    bool IsAutoStartEnabled();
    void SetAutoStart(bool enable, bool startMinimized = false);
}
