using System.IO;
using System.IO.Compression;
using SMAP_Installer;
using Xunit;

namespace SMAP.Tests;

public sealed class InstallerPayloadTests
{
    static MemoryStream Payload(params (string path, string content)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry(file.path).Open()); writer.Write(file.content); }
        stream.Position = 0; return stream;
    }
    [Fact]
    public void UpgradePreservesExistingSongsSettingsAndAddsNewSamples()
    {
        using var dir = new TestProject(); string target = Path.Combine(dir.DirectoryPath, "app"); Directory.CreateDirectory(Path.Combine(target, "songs"));
        File.WriteAllText(Path.Combine(target, "SMAP.exe"), "old-code"); File.WriteAllText(Path.Combine(target, "songs", "sample.txt"), "user-edit");
        File.WriteAllText(Path.Combine(target, "settings.json"), "user-settings");
        using var zip = Payload(("SMAP.exe", "new-code"), ("songs/sample.txt", "bundled-original"), ("songs/new.txt", "new-sample"));
        InstallPayload.Extract(zip, target);
        Assert.Equal("new-code", File.ReadAllText(Path.Combine(target, "SMAP.exe")));
        Assert.Equal("user-edit", File.ReadAllText(Path.Combine(target, "songs", "sample.txt")));
        Assert.Equal("new-sample", File.ReadAllText(Path.Combine(target, "songs", "new.txt")));
        Assert.Equal("user-settings", File.ReadAllText(Path.Combine(target, "settings.json")));
    }
    [Theory]
    [InlineData("cs")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("ja")][InlineData("ko")][InlineData("pl")][InlineData("pt-BR")][InlineData("ru")]
    [InlineData("tr")][InlineData("zh-Hans")][InlineData("zh-Hant")]
    public void FrameworkSatelliteResourcesAreInstalled(string culture)
    {
        using var dir = new TestProject(); string target = Path.Combine(dir.DirectoryPath, "app");
        string relative = culture + "/PresentationFramework.resources.dll";
        using var zip = Payload(("SMAP.exe", "code"), (relative, "localized-resource"));
        InstallPayload.Extract(zip, target);
        Assert.Equal("localized-resource", File.ReadAllText(Path.Combine(target, relative)));
    }
    [Theory]
    [InlineData("zh-Hans/settings.json")][InlineData("ja/private.resources.dll/config.json")]
    [InlineData("private/state.resources.dll")][InlineData("de/profile.smap")]
    [InlineData("../outside.txt")][InlineData("/outside.txt")][InlineData("songs/../../outside.txt")]
    [InlineData("C:/outside.txt")][InlineData("settings.json")][InlineData("session.dat")]
    public void InvalidPackagesFailBeforeReplacingAnyApplicationFile(string path)
    {
        using var dir = new TestProject();string target=Path.Combine(dir.DirectoryPath,"app");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"SMAP.exe"),"old");
        using var zip=Payload(("SMAP.exe","new"),(path,"untrusted"));
        Assert.Throws<InvalidDataException>(()=>InstallPayload.Extract(zip,target));Assert.Equal("old",File.ReadAllText(Path.Combine(target,"SMAP.exe")));
    }
    [Fact]
    public void FailureRestoresCodeWithoutRestoringDatabaseOrUserFiles()
    {
        using var dir=new TestProject();string target=Path.Combine(dir.DirectoryPath,"app");Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target,"SMAP.exe"),"old-code");File.WriteAllText(Path.Combine(target,"settings.json"),"user-state");
        using var zip=Payload(("SMAP.exe","new-code"),("songs/new.txt","new-sample"),("extra.dll","new-library"));
        Assert.Throws<IOException>(()=>InstallPayload.Extract(zip,target,(percent,_)=>{if(percent==100)throw new IOException("Synthetic failure");}));
        Assert.Equal("old-code",File.ReadAllText(Path.Combine(target,"SMAP.exe")));Assert.Equal("user-state",File.ReadAllText(Path.Combine(target,"settings.json")));
        Assert.Equal("new-sample",File.ReadAllText(Path.Combine(target,"songs/new.txt")));Assert.False(File.Exists(Path.Combine(target,"extra.dll")));
    }
    [Fact]
    public void FailedRollbackRetainsOldCodeBackupForRecovery()
    {
        using var dir=new TestProject();string target=Path.Combine(dir.DirectoryPath,"app");Directory.CreateDirectory(target);
        string exe=Path.Combine(target,"SMAP.exe");File.WriteAllText(exe,"old-code");
        using var zip=Payload(("SMAP.exe","new-code"));FileStream? locked=null;
        try
        {
            Assert.Throws<AggregateException>(()=>InstallPayload.Extract(zip,target,(_,_)=>{locked=File.Open(exe,FileMode.Open,FileAccess.Read,FileShare.None);throw new IOException("Synthetic locked replacement");}));
            string recovery=Directory.GetDirectories(dir.DirectoryPath,".smap-install-*").Single();Assert.Equal("old-code",File.ReadAllText(Path.Combine(recovery,"old","SMAP.exe")));
        }
        finally { locked?.Dispose(); }
    }
}
