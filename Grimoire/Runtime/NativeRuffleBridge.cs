using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Grimoire.Runtime
{
    /// <summary>Mugetsu's native JSON bridge. Its reader never depends on the WinForms UI thread.</summary>
    internal sealed class NativeRuffleBridge : IDisposable
    {
        private const int MaximumMessageLength = 8 * 1024 * 1024;
        private readonly Control host;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JToken>> pending = new ConcurrentDictionary<int, TaskCompletionSource<JToken>>();
        private readonly ConcurrentQueue<JObject> events = new ConcurrentQueue<JObject>();
        private readonly object sendLock = new object();
        private readonly System.Windows.Forms.Timer eventTimer;
        private TcpListener listener;
        private TcpClient client;
        private StreamWriter writer;
        private Process player;
        private readonly Stopwatch startup = new Stopwatch();
        private int nextId, queuedEvents, failed;
        private volatile bool disposed;

        public event Action<string, object[]> GameEvent;
        public event Action<string> Faulted;
        public bool IsConnected => !disposed && Volatile.Read(ref writer) != null;
        internal int? ExitCode { get; private set; }
        internal bool RequiredTermination { get; private set; }

        public NativeRuffleBridge(Control host)
        {
            this.host = host;
            eventTimer = new System.Windows.Forms.Timer { Interval = 16 };
            eventTimer.Tick += DrainEvents;
        }

        public void Start(string executable, string movie, string dataDirectory)
        {
            if (!File.Exists(executable))
                throw new FileNotFoundException("Ruffle player is missing. Run tools\\Get-RufflePlayer.ps1 before building, or select Flash.");
            Directory.CreateDirectory(dataDirectory);
            string adaptedMovie = Path.Combine(dataDirectory, "grimoire-bridge-v1.swf");
            File.WriteAllBytes(adaptedMovie, RuffleLoader.Adapt(File.ReadAllBytes(movie)));
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            startup.Restart();
            eventTimer.Start();
            Task.Run(AcceptPlayer);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                Arguments = "--skua-bridge 127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port +
                    " --skua-parent " + host.Handle.ToInt64().ToString(CultureInfo.InvariantCulture) +
                    " --config " + Quote(Path.Combine(dataDirectory, "config")) +
                    " --cache-directory " + Quote(Path.Combine(dataDirectory, "cache")) +
                    " --save-directory " + Quote(Path.Combine(dataDirectory, "saves")) + " " + Quote(adaptedMovie)
            };
            // Packet payloads and credentials must never be written to the runtime log.
            start.EnvironmentVariables["RUST_LOG"] = "error";
            player = new Process { StartInfo = start, EnableRaisingEvents = true };
            player.Exited += (sender, args) => Fail("The Ruffle player stopped. Restart Grimlite to reconnect.");
            if (!player.Start()) throw new InvalidOperationException("The Ruffle player could not start.");
        }

        private async Task AcceptPlayer()
        {
            try
            {
                TcpClient accepted = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                lock (sendLock)
                {
                    if (disposed) { accepted.Dispose(); return; }
                    client = accepted;
                    client.NoDelay = true;
                    writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    listener.Stop();
                }
                var buffer = new byte[8192];
                using (var message = new MemoryStream())
                {
                    int read;
                    while (!disposed && (read = await client.GetStream().ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            if (buffer[i] == 10)
                            {
                                Receive(JObject.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)));
                                message.SetLength(0);
                            }
                            else
                            {
                                if (message.Length >= MaximumMessageLength) throw new IOException("Ruffle response exceeded the message limit.");
                                message.WriteByte(buffer[i]);
                            }
                        }
                    }
                }
                Fail("The Ruffle player connection closed.");
            }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is JsonException)
            {
                Fail("The Ruffle player connection closed.");
            }
        }

        private void Receive(JObject message)
        {
            if (message["id"] != null)
            {
                if (pending.TryRemove((int)message["id"], out var completion))
                    completion.TrySetResult(message["r"] ?? JValue.CreateNull());
                return;
            }
            if (message["ev"] == null) return;
            if (Interlocked.Increment(ref queuedEvents) > 4096)
            {
                Interlocked.Decrement(ref queuedEvents);
                Fail("Ruffle sent more events than the application could process.");
                return;
            }
            events.Enqueue(message);
        }

        private void DrainEvents(object sender, EventArgs args)
        {
            if (disposed) return;
            if (!IsConnected && failed == 0 && startup.Elapsed.TotalSeconds > 20)
            {
                Fail("Ruffle did not connect to Grimlite. Retry Ruffle or use Flash.");
                return;
            }
            var budget = Stopwatch.StartNew();
            for (int i = 0; i < 32 && budget.ElapsedMilliseconds < 8 && events.TryDequeue(out var message); i++)
            {
                Interlocked.Decrement(ref queuedEvents);
                try
                {
                    string name = (string)message["ev"];
                    var arguments = message["args"] as JArray;
                    GameEvent?.Invoke(name, arguments == null ? new object[0] : arguments.ToObject<object[]>());
                }
                catch (Exception error)
                {
                    // A malformed game event must not break the reader or prevent subsequent events.
                    Trace.WriteLine("Ruffle event handler failed: " + error.GetType().Name);
                }
            }
        }

        public JToken Call(string name, object[] arguments)
        {
            if (!IsConnected) return JValue.CreateNull();
            int id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            try
            {
                string request = JsonConvert.SerializeObject(new { id, name, args = arguments ?? new object[0] });
                if (request.Length > MaximumMessageLength) throw new IOException("Ruffle request exceeded the message limit.");
                lock (sendLock)
                {
                    if (!IsConnected) return JValue.CreateNull();
                    writer.WriteLine(request);
                }
                if (!completion.Task.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Ruffle did not answer the game callback in time.");
                return completion.Task.GetAwaiter().GetResult();
            }
            finally { pending.TryRemove(id, out _); }
        }

        public static string SerializedResult(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined) return null;
            return value.Type == JTokenType.String ? (string)value : value.ToString(Formatting.None);
        }

        public static object ConvertResult(JToken value, Type type)
        {
            string result = SerializedResult(value);
            if (result == null) return type.IsValueType ? Activator.CreateInstance(type) : null;
            // Object[] overloads historically return the loader's JSON text unchanged.
            if (type == typeof(string)) return result;
            // The loader serializes booleans as JSON strings, unlike numeric getters.
            JToken decoded = JToken.Parse(result);
            return decoded.ToObject(type);
        }

        private void Fail(string message)
        {
            if (disposed || Interlocked.Exchange(ref failed, 1) != 0) return;
            lock (sendLock) { writer = null; client?.Close(); }
            foreach (var entry in pending)
                if (pending.TryRemove(entry.Key, out var completion)) completion.TrySetResult(JValue.CreateNull());
            if (!host.IsDisposed && host.IsHandleCreated)
            {
                try { host.BeginInvoke(new Action(() => { if (!disposed) Faulted?.Invoke(message); })); }
                catch (InvalidOperationException) { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            eventTimer.Stop();
            eventTimer.Dispose();
            // The verified clean exit path: close the bridge BEFORE destroying the host window.
            lock (sendLock)
            {
                writer = null;
                try { client?.Client.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
                client?.Close();
                listener?.Stop();
            }
            foreach (var entry in pending)
                if (pending.TryRemove(entry.Key, out var completion)) completion.TrySetResult(JValue.CreateNull());
            if (player != null)
            {
                try
                {
                    if (!player.HasExited && !player.WaitForExit(3000))
                    {
                        RequiredTermination = true;
                        player.Kill();
                        player.WaitForExit(1000);
                    }
                    if (player.HasExited) ExitCode = player.ExitCode;
                }
                catch (InvalidOperationException) { }
                player.Dispose();
            }
            while (events.TryDequeue(out _)) { }
        }

        private static string Quote(string path)
        {
            if (path.Contains("\"")) throw new ArgumentException("Invalid path.");
            return "\"" + path + "\"";
        }
    }
}
