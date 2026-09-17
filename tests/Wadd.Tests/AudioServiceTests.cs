using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class AudioServiceTests
{
    [Fact]
    public void AudioService_CanResolveSoundFile()
    {
        var path = AudioService.EnsureSoundFileOnDisk("completed.mp3");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AudioService_PlaySoundDoesNotThrow()
    {
        var audioService = new AudioService();
        var ex = Record.Exception(() => audioService.PlaySound("completed.mp3"));
        Assert.Null(ex);
    }

    [Fact]
    public void WindowsMediaPlayer_CanPlayFile()
    {
        if (OperatingSystem.IsWindows())
        {
            var soundPath = AudioService.EnsureSoundFileOnDisk("completed.mp3");
            Assert.NotNull(soundPath);
            Assert.True(File.Exists(soundPath));

            var wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
            Assert.NotNull(wmpType);
            var player = Activator.CreateInstance(wmpType);
            Assert.NotNull(player);

            wmpType.InvokeMember("URL", BindingFlags.SetProperty, null, player, new object[] { soundPath });
            var controls = wmpType.InvokeMember("controls", BindingFlags.GetProperty, null, player, null);
            Assert.NotNull(controls);
            controls.GetType().InvokeMember("play", BindingFlags.InvokeMethod, null, controls, null);
        }
    }
}
