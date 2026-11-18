using Grimoire.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class RuntimeSmokeTests
{
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string repository = Path.GetFullPath(args[0]);
            string data = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(data);
            byte[] original = File.ReadAllBytes(Path.Combine(repository, "Loader", "grimoire.swf"));
            byte[] adapted = RuffleLoader.Adapt(original);
            Require(adapted[0] == 'F' && adapted.Length == 57992 && BitConverter.ToUInt32(adapted, 4) == adapted.Length,
                "Adapted SWF header/length is invalid.");
            Require(File.ReadAllBytes(Path.Combine(repository, "Loader", "grimoire.swf")).SequenceEqual(original), "Original SWF changed.");
            var unsupported = (byte[])original.Clone();
            unsupported[20] ^= 1;
            bool rejected = false;
            try { RuffleLoader.Adapt(unsupported); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "An unsupported loader was accepted.");

            Require((bool)NativeRuffleBridge.ConvertResult(new JValue("\"True\""), typeof(bool)), "Serialized boolean conversion failed.");
            Require((int)NativeRuffleBridge.ConvertResult(new JValue("24"), typeof(int)) == 24, "Number conversion failed.");
            Require((int?)NativeRuffleBridge.ConvertResult(new JValue("24"), typeof(int?)) == 24, "Nullable conversion failed.");
            Require((string)NativeRuffleBridge.ConvertResult(new JValue("[1,2]"), typeof(string)) == "[1,2]", "JSON text was deserialized prematurely.");
            Require(NativeRuffleBridge.SerializedResult(new JValue("\"A &amp; B\"")) == "\"A &amp; B\"", "String contents were decoded destructively.");
            Require(NativeRuffleBridge.SerializedResult(JValue.CreateNull()) == null, "Null conversion failed.");
            Require(JsonConvert.DeserializeObject<int[]>(NativeRuffleBridge.SerializedResult(new JValue("[1,2]"))).Length == 2,
                "Typed array conversion failed.");
            Console.WriteLine("PASS: loader integrity, unknown-loader rejection, and legacy return conversions.");

            Application.EnableVisualStyles();
            using (var form = new Form { Text = "Grimlite Ruffle bridge smoke test", Width = 960, Height = 620 })
            using (var surface = new NativeGamePanel { Dock = DockStyle.Fill })
            using (var timer = new Timer { Interval = 100 })
            {
                form.Controls.Add(surface);
                NativeRuffleBridge bridge = null;
                string failure = null;
                var counts = new Dictionary<string, int>();
                var elapsed = Stopwatch.StartNew();
                bool callbackFromEvent = false, checkedRuntime = false;
                form.Shown += (sender, e) =>
                {
                    try
                    {
                        bridge = new NativeRuffleBridge(surface);
                        bridge.Faulted += message => failure = message;
                        bridge.GameEvent += (name, values) =>
                        {
                            counts[name] = counts.ContainsKey(name) ? counts[name] + 1 : 1;
                            // This must work from an incoming event without deadlocking either UI.
                            if (name == "progress" && values.Length > 0 && values[0].ToString() == "100")
                            {
                                string url = NativeRuffleBridge.SerializedResult(bridge.Call("getGameObject", new object[] { "params.sURL" }));
                                callbackFromEvent = JsonConvert.DeserializeObject<string>(url) == "https://game.aq.com/game/";
                            }
                        };
                        bridge.Start(Path.Combine(repository, "Player", "Mugetsu.exe"), Path.Combine(repository, "Loader", "grimoire.swf"), data);
                        timer.Start();
                    }
                    catch (Exception error) { failure = error.Message; form.Close(); }
                };
                timer.Tick += (sender, e) =>
                {
                    if (failure != null || elapsed.Elapsed.TotalSeconds > 40)
                    {
                        if (failure == null) failure = "Native bridge did not become ready within 40 seconds.";
                        form.Close();
                    }
                    else if (!checkedRuntime && callbackFromEvent && counts.ContainsKey("debug"))
                    {
                        try
                        {
                            Require(!(bool)NativeRuffleBridge.ConvertResult(bridge.Call("IsLoggedIn", new object[0]), typeof(bool)), "Unexpected login state in isolated test.");
                            Require((int)NativeRuffleBridge.ConvertResult(bridge.Call("getGameObject", new object[] { "stage.frameRate" }), typeof(int)) > 0,
                                "Game-property callback failed.");
                            IntPtr child = GetWindow(surface.Handle, 5);
                            Require(child != IntPtr.Zero && GetParent(child) == surface.Handle, "Native player was not embedded in the game panel.");
                            checkedRuntime = true;
                            Console.WriteLine("PASS: native embedding, game reads, forwarded progress/debug, and callback from queued event.");
                        }
                        catch (Exception error) { failure = error.Message; }
                        form.Close();
                    }
                };
                form.FormClosing += (sender, e) => { timer.Stop(); bridge?.Dispose(); };
                Application.Run(form);
                if (failure != null) throw new InvalidOperationException(failure);
                Require(checkedRuntime && bridge.ExitCode == 0 && !bridge.RequiredTermination, "Player did not shut down cleanly.");
                Console.WriteLine("PASS: host-first shutdown, exit code 0, no forced termination.");
                File.WriteAllText(Path.Combine(data, "result.json"), JsonConvert.SerializeObject(new
                {
                    passed = true, eventCounts = counts, callbackFromEvent, embedded = true,
                    playerExitCode = bridge.ExitCode, forcedTermination = bridge.RequiredTermination
                }, Formatting.Indented));
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }
}
