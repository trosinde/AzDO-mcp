using System.Net;
using System.Text;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;

namespace AzdoMcp.Tests;

public class AzdoClientErrorTests
{
    private static AzdoClient Client(FakeHttp http) => new AzdoClientFactory(http).Create(
        new Connection { Name = "X", CollectionUrl = Fake.Collection }, Fake.Pat, "P");

    private static async Task<AzdoApiException> Fails(FakeHttp http) =>
        await Assert.ThrowsAsync<AzdoApiException>(() => Client(http).GetAsync("_apis/x", CancellationToken.None));

    [Fact]
    public async Task Unauthorized()
    {
        var ex = await Fails(new FakeHttp().On("_apis/x", "{}", HttpStatusCode.Unauthorized));
        Assert.Equal(AzdoErrorKind.Unauthorized, ex.Kind);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Forbidden_says_scope_missing()
    {
        var ex = await Fails(new FakeHttp().On("_apis/x", """{"message":"TF400813: not authorized"}""", HttpStatusCode.Forbidden));
        Assert.StartsWith("403 Scope fehlt", ex.Message);
    }

    [Fact]
    public async Task Html_404_is_not_available()
    {
        var ex = await Fails(new FakeHttp().OnRaw("_apis/x", _ => FakeHttp.Html(HttpStatusCode.NotFound)));
        Assert.Equal(AzdoErrorKind.NotAvailable, ex.Kind);
        Assert.Contains("Auf diesem Server nicht verfügbar", ex.Message);
    }

    [Fact]
    public async Task Json_404_is_not_found_with_server_message()
    {
        var ex = await Fails(new FakeHttp().On("_apis/x", """{"message":"TF401232: Work item 9 does not exist."}""", HttpStatusCode.NotFound));
        Assert.Equal(AzdoErrorKind.NotFound, ex.Kind);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task Version_out_of_range_is_not_available()
    {
        var ex = await Fails(new FakeHttp().On("_apis/x", """{"message":"The requested REST API version of 7.1 is out of range","typeKey":"VssVersionOutOfRangeException"}""", HttpStatusCode.BadRequest));
        Assert.Equal(AzdoErrorKind.NotAvailable, ex.Kind);
    }

    [Fact]
    public async Task Html_200_sign_in_page_is_treated_as_401()
    {
        var ex = await Fails(new FakeHttp().OnRaw("_apis/x", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>sign in</html>", Encoding.UTF8, "text/html") }));
        Assert.Equal(AzdoErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task Pat_echoed_by_server_is_scrubbed()
    {
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + Fake.Pat));
        var ex = await Fails(new FakeHttp().On("_apis/x", $$"""{"message":"bad token {{Fake.Pat}} / Basic {{basic}}"}""", HttpStatusCode.InternalServerError));
        Assert.DoesNotContain(Fake.Pat, ex.Message);
        Assert.DoesNotContain(basic, ex.Message);
    }

    [Fact]
    public async Task Unreachable_message_has_no_secret()
    {
        var ex = await Fails(new FakeHttp { Throw = _ => new HttpRequestException($"failed Authorization: Basic {Fake.Pat}") });
        Assert.Equal(AzdoErrorKind.Unreachable, ex.Kind);
        Assert.DoesNotContain(Fake.Pat, ex.Message);
    }

    [Fact]
    public async Task Timeout_is_reported()
    {
        var http = new FakeHttp { Delay = TimeSpan.FromSeconds(5) }.On("_apis/x", "{}");
        var client = new AzdoClientFactory(http, TimeSpan.FromMilliseconds(200)).Create(
            new Connection { Name = "X", CollectionUrl = Fake.Collection }, Fake.Pat, "P");
        var ex = await Assert.ThrowsAsync<AzdoApiException>(() => client.GetAsync("_apis/x", CancellationToken.None));
        Assert.Equal(AzdoErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Sends_basic_auth_and_project_urls()
    {
        var http = new FakeHttp().On("_apis/x", "{}");
        await Client(http).GetAsync("_apis/x", CancellationToken.None);
        Assert.StartsWith(Fake.Collection + "/_apis/x", http.Requests[0].Url);
        Assert.StartsWith("Basic ", http.Requests[0].Authorization);
    }
}
