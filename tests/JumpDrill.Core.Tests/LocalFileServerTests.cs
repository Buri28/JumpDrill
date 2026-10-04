using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using JumpDrill.Replays;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// BeatLeader の web リプレイヤーは <c>?link=&lt;URL&gt;</c> でリプレイを読み込む。
    /// ローカルの .bsor を URL として渡すための小さなサーバ。
    /// </summary>
    public class LocalFileServerTests : IDisposable
    {
        private readonly string _root;
        private readonly LocalFileServer _server;
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public LocalFileServerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "JumpDrillServer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _server = new LocalFileServer();
        }

        public void Dispose()
        {
            _server.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string WriteFile(string name, byte[] content)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        [Fact]
        public void Listens_on_a_loopback_port()
        {
            Assert.InRange(_server.Port, 1, 65535);
        }

        [Fact]
        public async Task Serves_the_published_file_byte_for_byte()
        {
            var content = new byte[50_000];
            new Random(7).NextBytes(content);
            string url = _server.Publish(WriteFile("replay.bsor", content));

            var received = await Client.GetByteArrayAsync(url);

            Assert.Equal(content.Length, received.Length);
            Assert.Equal(content, received);
        }

        [Fact]
        public async Task Allows_the_replayer_to_fetch_across_origins()
        {
            // リプレイヤーは beatleader.xyz から取りに来るので CORS が要る。
            string url = _server.Publish(WriteFile("a.bsor", Encoding.UTF8.GetBytes("x")));

            using (var response = await Client.GetAsync(url))
            {
                Assert.True(response.IsSuccessStatusCode);
                Assert.Contains("*", response.Headers.GetValues("Access-Control-Allow-Origin"));
            }
        }

        [Fact]
        public async Task Only_serves_what_was_published()
        {
            _server.Publish(WriteFile("b.bsor", new byte[] { 1, 2, 3 }));

            using (var response = await Client.GetAsync("http://127.0.0.1:" + _server.Port + "/not-published"))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public void Each_publish_gets_its_own_unguessable_name()
        {
            string path = WriteFile("c.bsor", new byte[] { 9 });

            string first = _server.Publish(path);
            string second = _server.Publish(path);

            Assert.NotEqual(first, second);
            Assert.StartsWith("http://127.0.0.1:" + _server.Port + "/", first);
            Assert.EndsWith(".bsor", first);
        }

        [Fact]
        public async Task Serves_more_than_one_file()
        {
            string one = _server.Publish(WriteFile("one.bsor", Encoding.UTF8.GetBytes("one")));
            string two = _server.Publish(WriteFile("two.bsor", Encoding.UTF8.GetBytes("two")));

            Assert.Equal("one", await Client.GetStringAsync(one));
            Assert.Equal("two", await Client.GetStringAsync(two));
        }

        [Fact]
        public void Publishing_something_that_is_not_there_is_reported()
        {
            Assert.Throws<FileNotFoundException>(() => _server.Publish(Path.Combine(_root, "missing.bsor")));
        }

        [Fact]
        public async Task Stops_listening_once_disposed()
        {
            var server = new LocalFileServer();
            string url = server.Publish(WriteFile("d.bsor", new byte[] { 1 }));
            Assert.Single(await Client.GetByteArrayAsync(url));

            server.Dispose();

            await Assert.ThrowsAnyAsync<Exception>(() => Client.GetByteArrayAsync(url));
        }
    }
}
