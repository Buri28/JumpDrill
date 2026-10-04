using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace JumpDrill.Replays
{
    /// <summary>
    /// ループバックだけに立てる最小の HTTP サーバ。
    ///
    /// BeatLeader の web リプレイヤーは <c>?link=&lt;URL&gt;</c> でリプレイを読み込むので、
    /// ローカルの .bsor を URL として渡すために使う。
    ///
    /// HttpListener は非管理者だと URL の予約が要って弾かれることがあるため、
    /// TcpListener に直接 HTTP を書く。用途が固定なので数十行で足りる。
    ///
    /// WinForms に依存しないので Core に置いてある。MOD 側から使う目もある。
    /// </summary>
    public sealed class LocalFileServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, string> _files =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private Thread _thread;
        private volatile bool _running;

        public LocalFileServer()
        {
            // ポートは OS に選ばせる。
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _running = true;
            _thread = new Thread(Serve) { IsBackground = true, Name = "JumpDrill file server" };
            _thread.Start();
        }

        public int Port { get; }

        /// <summary>公開したいファイルを登録し、その URL を返す。</summary>
        public string Publish(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(Lang.T("ファイルがありません。", "File not found."), path);

            string name = Guid.NewGuid().ToString("N") + Path.GetExtension(path);
            lock (_gate) _files["/" + name] = path;

            return "http://127.0.0.1:" + Port + "/" + name;
        }

        private void Serve()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    HandleClient(client);
                }
                catch (SocketException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { return; }
                finally
                {
                    if (client != null) client.Close();
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (var stream = client.GetStream())
            {
                string requestLine = ReadLine(stream);
                if (string.IsNullOrEmpty(requestLine)) return;

                // ヘッダは読み捨てる。GET しか受けない。
                while (!string.IsNullOrEmpty(ReadLine(stream))) { }

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) { WriteStatus(stream, "400 Bad Request"); return; }

                string method = parts[0];
                string target = parts[1];

                // リプレイヤーは別オリジンから取りに来るので CORS を許す。
                if (method == "OPTIONS") { WriteStatus(stream, "204 No Content"); return; }
                if (method != "GET") { WriteStatus(stream, "405 Method Not Allowed"); return; }

                string path;
                lock (_gate)
                {
                    if (!_files.TryGetValue(target, out path)) { WriteStatus(stream, "404 Not Found"); return; }
                }

                byte[] body;
                try { body = File.ReadAllBytes(path); }
                catch (IOException) { WriteStatus(stream, "500 Internal Server Error"); return; }

                var header = new StringBuilder();
                header.Append("HTTP/1.1 200 OK\r\n");
                header.Append("Content-Type: application/octet-stream\r\n");
                header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                header.Append("Access-Control-Allow-Origin: *\r\n");
                header.Append("Cache-Control: no-store\r\n");
                header.Append("Connection: close\r\n\r\n");

                var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }

        private static void WriteStatus(Stream stream, string status)
        {
            var text = "HTTP/1.1 " + status + "\r\n" +
                       "Access-Control-Allow-Origin: *\r\n" +
                       "Content-Length: 0\r\n" +
                       "Connection: close\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadLine(Stream stream)
        {
            var buffer = new List<byte>(128);
            int b;
            while ((b = stream.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') buffer.Add((byte)b);
            }
            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch (SocketException) { }
            _thread = null;
        }
    }
}
