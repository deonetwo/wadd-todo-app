using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Wadd.Core.Helpers;
using Wadd.Core.Localization;
using Wadd.Core.Models;
using Wadd.UI.Localization;
using Xunit;

namespace Wadd.Tests;

[Collection("AppSettingsTests")]
public class LocalizationTests : IDisposable
{
    [Fact]
    public void ResxFiles_KeyParity_EnglishAndIndonesianMatch()
    {
        // Find Strings.resx and Strings.id.resx relative to test execution or source
        var baseDir = AppContext.BaseDirectory;
        // Search upwards to find solution root
        var dir = new DirectoryInfo(baseDir);
        string? resxPath = null;
        string? idResxPath = null;

        while (dir != null)
        {
            var p1 = Path.Combine(dir.FullName, "src", "Wadd.Core", "Localization", "Strings.resx");
            var p2 = Path.Combine(dir.FullName, "src", "Wadd.Core", "Localization", "Strings.id.resx");
            if (File.Exists(p1) && File.Exists(p2))
            {
                resxPath = p1;
                idResxPath = p2;
                break;
            }
            dir = dir.Parent;
        }

        Assert.True(resxPath != null && File.Exists(resxPath), $"Strings.resx not found from {baseDir}");
        Assert.True(idResxPath != null && File.Exists(idResxPath), $"Strings.id.resx not found from {baseDir}");

        var enDoc = XDocument.Load(resxPath);
        var idDoc = XDocument.Load(idResxPath);

        var enKeys = enDoc.Root?.Elements("data")
            .Select(x => x.Attribute("name")?.Value)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet();

        var idKeys = idDoc.Root?.Elements("data")
            .Select(x => x.Attribute("name")?.Value)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet();

        Assert.NotNull(enKeys);
        Assert.NotNull(idKeys);
        Assert.NotEmpty(enKeys);
        Assert.NotEmpty(idKeys);

        var missingInId = enKeys.Except(idKeys).ToList();
        var extraInId = idKeys.Except(enKeys).ToList();

        Assert.Empty(missingInId);
        Assert.Empty(extraInId);
    }

    [Fact]
    public void Strings_Get_ReturnsExpectedTranslations()
    {
        var enCulture = new CultureInfo("en-US");
        var idCulture = new CultureInfo("id-ID");

        // Verify English strings
        Assert.Equal("Tasks", Strings.Get("Nav_Tasks", enCulture));
        Assert.Equal("Done", Strings.Get("Common_Done", enCulture));
        Assert.Equal("Settings", Strings.Get("Nav_Settings", enCulture));

        // Verify Indonesian strings
        Assert.Equal("Tugas", Strings.Get("Nav_Tasks", idCulture));
        Assert.Equal("Selesai", Strings.Get("Common_Done", idCulture));
        Assert.Equal("Pengaturan", Strings.Get("Nav_Settings", idCulture));
    }

    [Fact]
    public void LocalizationManager_DynamicCultureSwitching_Works()
    {
        var mgr = LocalizationManager.Instance;

        // Switch to English
        mgr.SetLanguage("en");
        Assert.Equal("en-US", mgr.CurrentCulture.Name);
        Assert.Equal("Tasks", mgr["Nav_Tasks"]);
        Assert.Equal("Done", mgr["Common_Done"]);
        Assert.Equal("Settings", mgr["Nav_Settings"]);

        // Switch to Indonesian
        mgr.SetLanguage("id");
        Assert.Equal("id-ID", mgr.CurrentCulture.Name);
        Assert.Equal("Tugas", mgr["Nav_Tasks"]);
        Assert.Equal("Selesai", mgr["Common_Done"]);
        Assert.Equal("Pengaturan", mgr["Nav_Settings"]);
    }

    [Fact]
    public void LocalizationManager_RaisesPropertyChangedOnCultureChange()
    {
        var mgr = LocalizationManager.Instance;
        mgr.SetLanguage("en");

        var propertyChangedFired = false;
        var cultureChangedFired = false;
        mgr.PropertyChanged += (s, e) => propertyChangedFired = true;
        mgr.CultureChanged += () => cultureChangedFired = true;

        mgr.SetLanguage("id");

        Assert.True(propertyChangedFired);
        Assert.True(cultureChangedFired);
        Assert.Equal("Tugas", mgr["Nav_Tasks"]);
    }

    [Fact]
    public void LocalizationManager_MissingKey_FallsBackToKeyName()
    {
        var mgr = LocalizationManager.Instance;
        mgr.SetLanguage("en");

        const string missingKey = "NonExistent_Test_Key_12345";
        var result = mgr[missingKey];

        Assert.Equal(missingKey, result);
    }

    [Fact]
    public void LocalizationManager_MissingInNonEnglishCulture_FallsBackToEnglishValue()
    {
        // When a key exists in neutral/English resources but is queried under a culture without that key,
        // ResourceManager falls back through parent cultures to neutral (English), not returning null, empty, or the raw key.
        var frCulture = new CultureInfo("fr-FR");
        var result = Strings.Get("Nav_Tasks", frCulture);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal("Tasks", result);
        Assert.NotEqual("Nav_Tasks", result);
    }

    [Fact]
    public void LocalizationManager_SystemLanguage_DoesNotThrow()
    {
        var mgr = LocalizationManager.Instance;
        var ex = Record.Exception(() => mgr.SetLanguage("system"));
        Assert.Null(ex);
        Assert.NotNull(mgr.CurrentCulture);
        Assert.NotEmpty(mgr["App_Title"]);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("en")]
    [InlineData("id")]
    public void AppSettingsHelper_SaveAndLoadSettings_PersistsLanguage(string lang)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddTest_Lang_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WADD_DATA_DIR", tempPath);

        try
        {
            var settings = new AppSettingsData
            {
                Language = lang
            };

            AppSettingsHelper.SaveSettings(settings);

            var loaded = AppSettingsHelper.LoadSettings();

            Assert.Equal(lang, loaded.Language);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WADD_DATA_DIR", null);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    public void Dispose()
    {
        LocalizationManager.Instance.SetLanguage("en");
    }
}
