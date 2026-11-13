using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public sealed class G2PresentationTests
{
    [Fact]
    public void ContinuousCornersStayInsideBoundsAndExcludeTheOuterCorner()
    {
        foreach (var size in new[] { new Size(500, 48), new Size(48, 48), new Size(4, 2) })
        {
            var geometry = G2Geometry.Create(new Rect(size), new CornerRadius(10, 9, 6, 12));
            Assert.True(geometry.IsFrozen);
            Assert.True(geometry.FillContains(new Point(size.Width / 2, size.Height / 2)));
            Assert.False(geometry.FillContains(new Point(0, 0), 0.000001, ToleranceType.Absolute));
            Assert.True(new Rect(size).Contains(geometry.Bounds));
            Assert.DoesNotContain('A', geometry.ToString());
        }
    }

    [Fact]
    public void BorderClipsItsChildrenWithContinuousGeometry()
        => Sta(() =>
        {
            var border = new G2Border { Width = 100, Height = 60, CornerRadius = new CornerRadius(12), ClipToBounds = true,
                BorderThickness = new Thickness(4, 1, 1, 1), BorderBrush = Brushes.Blue, Background = Brushes.White,
                Child = new G2Border { Background = Brushes.Red } };
            border.Measure(new(100, 60)); border.Arrange(new Rect(0, 0, 100, 60));
            var bitmap = new RenderTargetBitmap(100, 60, 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
            byte[] bytes = new byte[100 * 60 * 4]; bitmap.CopyPixels(bytes, 400, 0);
            Assert.Equal(0, bytes[3]); Assert.Equal(255, bytes[(30 * 100 + 50) * 4 + 2]);
            Assert.Equal(255, bytes[(30 * 100 + 1) * 4]);
        });

    [Fact]
    public void SummaryUsesTempoChangesAndEventsAfterTheNewMeter()
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        t.Notes.Add(new() { TrackId = t.Id, Beat = 8, Key = 3, LengthBeats = 2, Action = NoteAction.Hold });
        p = p with { Tempos = [new(0, 500_000), new(4, 1_000_000)], Meters = [new(0, 4, 4), new(6, 6, 8)] };
        var info = SharePerformanceInfo.FromProject(p);
        Assert.Equal(60, info.MinBpm); Assert.Equal(120, info.MaxBpm); Assert.Equal(8, info.DurationSeconds);
        Assert.Equal(new[] { "4/4", "6/8" }, info.Meters);
    }

    [Theory]
    [InlineData(0)] [InlineData(6)]
    public void DifficultyIsBounded(int value) => Assert.Throws<InvalidDataException>(() => new SharingPresentation { Difficulty = value }.Validate());

    [Fact]
    public void SummaryKeepsReturningMetersInTimeOrder()
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        t.Notes.Add(new() { TrackId = t.Id, Beat = 10, Action = NoteAction.Tap, Key = 2 });
        p = p with { Meters = [new(0, 4, 4), new(8, 4, 4), new(4, 6, 8)] };
        Assert.Equal(new[] { "4/4", "6/8", "4/4" }, SharePerformanceInfo.FromProject(p).Meters);
    }

    [Fact]
    public void InvalidAndDuplicateTagsAreRejected()
    {
        foreach (var tags in new[] { new[] { "a", "A" }, new[] { " " }, new[] { "line\nbreak" }, new[] { new string('a', 33) }, new[] { "1", "2", "3", "4", "5", "6" } })
            Assert.Throws<InvalidDataException>(() => new SharingPresentation { Tags = tags }.Validate());
        new SharingPresentation { Tags = ["流行", "日本語", "🎹"], Difficulty = 3 }.Validate();
    }

    [Fact]
    public void CoverIsReencodedAndMetadataDoesNotChangePerformanceIdentity()
        => Sta(() =>
        {
            byte[] pixels = new byte[1024 * 700 * 3];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
            var image = BitmapSource.Create(1024, 700, 96, 96, PixelFormats.Rgb24, null, pixels, 1024 * 3);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream(); encoder.Save(stream);
            var cover = SharingCover.Encode(stream.ToArray()); Assert.InRange(cover.Length, 4, 128 * 1024);
            string encoded = Convert.ToBase64String(cover); Assert.Equal(64, SharingPresentation.CoverHash(encoded)!.Length);
            var project = TestProject.SingleTap(); string hash = PerformanceIdentity.ForProject(project);
            var decorated = project with { Metadata = project.Metadata with { Cover = encoded, Name = "Different title", Description = "Different description" } };
            Assert.Equal(hash, PerformanceIdentity.ForProject(decorated));
            Assert.Equal(encoded, SmapFile.FromBytes(SmapFile.ToBytes(decorated)).Metadata.Cover);
        });

    [Theory]
    [InlineData("https://untrusted.invalid/cover.jpg")]
    [InlineData("YWJjZA==")]
    public void ExternalOrMalformedCoversAreNotAccepted(string input)
        => Assert.Throws<InvalidDataException>(() => SharingPresentation.CoverHash(input));

    sealed class OldCapabilities : HttpMessageHandler
    {
        public int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":1}", Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task UnsupportedServerKeepsTheDraftWithoutCreatingAnEmptyWork()
    {
        using var files = new TestProject(); var handler = new OldCapabilities();
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), handler);
        var client = new NativeSharingClient(transport, () => null); var store = new NativeSharingStore(files.DirectoryPath);
        var presentation = new SharingPresentation { Tags = ["教学"], Difficulty = 2 };
        var draft = store.Stage(1, null, SmapFile.ToBytes(TestProject.SingleTap()), "Song", "Artist", "Maker", "Intro", "public", [], presentation);
        var error = await Assert.ThrowsAsync<SharingException>(() => store.PublishAsync(client, draft, CancellationToken.None));
        Assert.Equal("PRESENTATION_UNAVAILABLE", error.Code); Assert.Equal(0, handler.Writes);
        Assert.Equal(presentation.Tags, store.Pending(1)!.Presentation!.Tags); Assert.Equal(2, store.Pending(1)!.Presentation!.Difficulty);
    }

    static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
