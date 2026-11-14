using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using SMAP_WPF.Announcements;
using Xunit;

namespace SMAP.Tests;

public sealed class AnnouncementTests
{
    sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie")); Assert.Null(request.Content);
            return Task.FromResult(response(request));
        }
    }
    static readonly Uri Origin = new("https://announcements.example.invalid/");
    static Announcement Item(long id = 1, int revision = 1, bool important = true, string? content = null)
        => new(id, revision, "Synthetic title", "Synthetic summary", important, 1700000000, content);
    static object Summary(Announcement item) => new { id = item.Id, revision = item.Revision, title = item.Title, summary = item.Summary, important = item.Important, publishedAt = item.PublishedAt };
    static object Feed(Announcement[] items, int page = 1, int? total = null) => new { schemaVersion = 1, items = items.Select(Summary), page, pageSize = 20, total = total ?? items.Length };
    static HttpResponseMessage Json(object value) => Raw(JsonSerializer.Serialize(value));
    static HttpResponseMessage Raw(string value, string type = "application/json") => new(HttpStatusCode.OK)
        { Content = new StringContent(value, System.Text.Encoding.UTF8, type) };
    static AnnouncementService Service(Reply reply, string path) => new(new AnnouncementClient(Origin, reply), new AnnouncementReadStore(path));

    [Fact]
    public async Task PublicFeedUsesHttpsWithoutCredentials()
    {
        var network = new Reply(request => { Assert.Equal("/api/smap/v1/announcements.php?page=1", request.RequestUri!.PathAndQuery); return Json(Feed([Item()])); });
        using var client = new AnnouncementClient(Origin, network); var page = await client.List(1);
        Assert.Single(page.Items); Assert.Equal(1, page.Items[0].Revision); Assert.Null(page.Items[0].Content); Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task PlainTextDetailIsRetainedWithoutHtmlParsing()
    {
        const string body = "<script>synthetic()</script>\nSecond line 🎹";
        using var client = new AnnouncementClient(Origin, new Reply(_ => Json(new { schemaVersion = 1, announcement = new { id = 1, revision = 1, title = "Plain text", summary = "", important = false, publishedAt = 1700000000, content = body } })));
        var detail = await client.Detail(1); Assert.Equal(body, detail.Content);
    }

    [Theory]
    [InlineData("<html>Gateway error</html>")]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":2,\"items\":[],\"page\":1,\"pageSize\":20,\"total\":0}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"items\":[],\"page\":1,\"pageSize\":20,\"total\":0}")]
    [InlineData("{\"schemaVersion\":1,\"items\":[],\"page\":2,\"pageSize\":20,\"total\":0}")]
    [InlineData("{\"schemaVersion\":1,\"items\":[],\"page\":1,\"pageSize\":999,\"total\":0}")]
    [InlineData("{\"schemaVersion\":1,\"items\":[],\"page\":1,\"pageSize\":20,\"total\":-1}")]
    [InlineData("[]")]
    public async Task InvalidFeedFailsSafely(string body)
    {
        using var client = new AnnouncementClient(Origin, new Reply(_ => Raw(body)));
        var error = await Record.ExceptionAsync(() => client.List(1)); Assert.NotNull(error); Assert.True(AnnouncementService.IsReadError(error));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task HttpErrorsAndRedirectsAreNotFollowedOrShownAsContent(int status)
    {
        var network = new Reply(_ => { var r = new HttpResponseMessage((HttpStatusCode)status); r.Headers.Location = new Uri("https://foreign.invalid/"); return r; });
        using var client = new AnnouncementClient(Origin, network);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.List(1)); Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task HtmlMediaTypeAndOversizedBodiesAreRejected()
    {
        using var html = new AnnouncementClient(Origin, new Reply(_ => Raw("<html>synthetic</html>", "text/html")));
        await Assert.ThrowsAsync<InvalidDataException>(() => html.List(1));
        using var large = new AnnouncementClient(Origin, new Reply(_ => Raw(new string(' ', 256 * 1024 + 1))));
        await Assert.ThrowsAsync<InvalidDataException>(() => large.List(1));
    }

    [Fact]
    public async Task DuplicateIdentitiesAndTooManyItemsAreRejected()
    {
        using var duplicate = new AnnouncementClient(Origin, new Reply(_ => Json(Feed([Item(), Item()]))));
        await Assert.ThrowsAsync<InvalidDataException>(() => duplicate.List(1));
        using var large = new AnnouncementClient(Origin, new Reply(_ => Json(Feed(Enumerable.Range(1, 21).Select(i => Item(i)).ToArray()))));
        await Assert.ThrowsAsync<InvalidDataException>(() => large.List(1));
    }

    [Theory]
    [InlineData(0, 1, 1700000000)]
    [InlineData(1, 0, 1700000000)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 1, long.MaxValue)]
    public async Task InvalidIdentityRevisionAndTimeAreRejected(long id, int revision, long time)
    {
        using var client = new AnnouncementClient(Origin, new Reply(_ => Json(Feed([Item(id, revision) with { PublishedAt = time }]))));
        var error = await Record.ExceptionAsync(() => client.List(1)); Assert.NotNull(error); Assert.True(AnnouncementService.IsReadError(error));
    }

    [Fact]
    public async Task DetailCannotSubstituteAnotherAnnouncement()
    {
        using var client = new AnnouncementClient(Origin, new Reply(_ => Json(new { schemaVersion = 1, announcement = new { id = 2, revision = 1, title = "Other", summary = "", important = false, publishedAt = 1700000000, content = "Body" } })));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.Detail(1));
    }

    [Fact]
    public void ReadAndNotificationStatePersistByRevision()
    {
        using var temp = new TestProject(); string path = temp.PathFor("state.json"); var state = new AnnouncementReadStore(path); var item = Item();
        Assert.False(state.IsRead(item)); state.MarkNotified(item); Assert.True(state.WasNotified(item)); Assert.False(state.IsRead(item));
        state.MarkRead(item); var loaded = new AnnouncementReadStore(path); Assert.True(loaded.IsRead(item)); Assert.True(loaded.WasNotified(item));
        Assert.False(loaded.IsRead(Item(revision: 2))); Assert.False(loaded.WasNotified(Item(revision: 2)));
        loaded.MarkRead(Item(revision: 2)); loaded.MarkRead(item); Assert.True(new AnnouncementReadStore(path).IsRead(Item(revision: 2)));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":999,\"Read\":{},\"Notified\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Read\":{\"1\":-1},\"Notified\":{}}")]
    public void CorruptStateDoesNotMarkAnnouncementsRead(string body)
    {
        using var temp = new TestProject(); string path = temp.PathFor("state.json"); File.WriteAllText(path, body);
        var state = new AnnouncementReadStore(path); Assert.False(state.IsRead(Item())); Assert.False(state.WasNotified(Item()));
    }

    [Fact]
    public void SeparateInstancesMergeAlreadyPersistedRevisions()
    {
        using var temp = new TestProject(); string path = temp.PathFor("state.json"); var a = new AnnouncementReadStore(path); var b = new AnnouncementReadStore(path);
        a.MarkRead(Item(1)); b.MarkRead(Item(2)); var loaded = new AnnouncementReadStore(path);
        Assert.True(loaded.IsRead(Item(1))); Assert.True(loaded.IsRead(Item(2)));
    }

    [Fact]
    public void SaveFailureDoesNotPretendTheItemWasRead()
    {
        using var temp = new TestProject(); string path = temp.PathFor("state.json"); Directory.CreateDirectory(path); var state = new AnnouncementReadStore(path);
        var error = Record.Exception(() => state.MarkRead(Item())); Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.False(state.IsRead(Item())); Assert.Empty(Directory.GetFiles(temp.DirectoryPath));
    }

    [Fact]
    public async Task ServiceRetainsVisibleItemsWhenRefreshFails()
    {
        using var temp = new TestProject(); bool fail = false;
        using var service = Service(new Reply(_ => fail ? new(HttpStatusCode.ServiceUnavailable) : Json(Feed([Item()]))), temp.PathFor("state.json"));
        await service.Refresh(); Assert.False(service.Failed); Assert.Single(service.Items); Assert.True(service.HasUnread);
        fail = true; await service.Refresh(); Assert.True(service.Failed); Assert.False(service.Busy); Assert.Single(service.Items);
    }

    [Fact]
    public async Task ImportantNoticesAreOncePerRevisionAndDoNotMarkRead()
    {
        using var temp = new TestProject(); string path = temp.PathFor("state.json"); int revision = 1;
        using var service = Service(new Reply(_ => Json(Feed([Item(revision: revision)]))), path);
        await service.Refresh(); var first = service.Important(); Assert.NotNull(first); await service.NoticeShown(first!);
        Assert.Null(service.Important()); Assert.True(service.HasUnread);
        revision = 2; await service.Refresh(); Assert.Equal(2, service.Important()!.Revision);
        await service.MarkRead(service.Important()!); Assert.False(service.HasUnread); Assert.Null(service.Important());
    }

    [Fact]
    public async Task MorePagesAppendWithoutDuplicateIdentities()
    {
        using var temp = new TestProject();
        using var service = Service(new Reply(request => request.RequestUri!.Query.Contains("page=2") ? Json(Feed([Item(21)], 2, 21)) : Json(Feed(Enumerable.Range(1, 20).Select(i => Item(i)).ToArray(), 1, 21))), temp.PathFor("state.json"));
        await service.Refresh(); Assert.True(service.HasMore); await service.More(); Assert.False(service.HasMore); Assert.Equal(21, service.Items.Count);
    }
}
