using Grimoire.Game;
using Grimoire.Tools;
using Grimoire.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

/// <summary>Runs the real application startup and bot-facing facade, without starting a bot.</summary>
internal static class ApplicationSmokeTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool live = args.Contains("--live");
        bool useFlash = args.Contains("--flash");
        var result = new Dictionary<string, object>();
        var errors = new List<string>();
        var eventCounts = new Dictionary<string, int>();
        var elapsed = Stopwatch.StartNew();
        bool loaded = false, basicPassed = false;
        double loadedAt = 0;
        int ready = 0, samples = 0;
        double readySince = 0;
        object bridge = null;
        string failure = null;
        string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application-smoke-result.json");
        result["mode"] = live ? "live" : "prelogin";
        result["engine"] = useFlash ? "flash" : "ruffle";
        result["status"] = "starting";
        File.WriteAllText(reportPath, JsonConvert.SerializeObject(result, Formatting.Indented));
        Flash.FlashError += (control, error, name, arguments) => errors.Add(name + ":" + error.GetType().Name);
        Flash.SwfLoadProgress += progress => { if (progress >= 100) { loaded = true; loadedAt = elapsed.Elapsed.TotalSeconds; } };
        using (var timer = new Timer { Interval = 250 })
        {
            timer.Tick += (sender, e) =>
            {
                try
                {
                    Root root = Root.Instance;
                    if (elapsed.Elapsed.TotalSeconds > (live ? 1800 : 45))
                        throw new TimeoutException("Application smoke test timed out.");
                    if (root == null || !loaded) return;
                    if (useFlash && elapsed.Elapsed.TotalSeconds - loadedAt < 3) return;
                    if (!useFlash && bridge == null)
                    {
                        bridge = typeof(Flash).GetField("Ruffle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        if (bridge == null) throw new InvalidOperationException("The application did not select Ruffle.");
                        bridge.GetType().GetEvent("GameEvent").AddEventHandler(bridge, new Action<string, object[]>((name, values) =>
                        {
                            eventCounts[name] = eventCounts.ContainsKey(name) ? eventCounts[name] + 1 : 1;
                        }));
                    }
                    if (!basicPassed)
                    {
                        if (useFlash && (Flash.IsRuffle || root.flashPlayer == null))
                            throw new InvalidOperationException("The application did not select Flash.");
                        bool stringOverload = Flash.Call<bool>("IsLoggedIn", new string[0]);
                        if (!useFlash)
                        {
                            bool objectOverload = Flash.Call<bool>("IsLoggedIn", new object[0]);
                            if (objectOverload != stringOverload) throw new InvalidOperationException("The legacy overloads disagree.");
                            string baseUrl = Flash.GetGameObject("params.sURL");
                            if (JsonConvert.DeserializeObject<string>(baseUrl) != "https://game.aq.com/game/")
                                throw new InvalidOperationException("The application facade could not read the game.");
                        }
                        basicPassed = true;
                        result["facadeOverloads"] = useFlash ? "original string-array API checked" : "passed";
                        Console.WriteLine(useFlash ? "PASS: original Flash startup and string-array callback API." :
                            "PASS: real Grimlite startup, Ruffle, both Flash.Call overloads, and game-property read.");
                        if (!live) { root.Close(); return; }
                        result["status"] = "waiting_for_map";
                        File.WriteAllText(reportPath, JsonConvert.SerializeObject(result, Formatting.Indented));
                        Console.WriteLine("READY: log in manually; the test reads game state and then closes cleanly.");
                    }
                    if (live && Player.IsLoggedIn && !World.IsMapLoading)
                    {
                        var character = Flash.Instance.GetGameObject<Dictionary<string, object>>("world.myAvatar.dataLeaf");
                        ready = character != null && character.ContainsKey("intHP") ? ready + 1 : 0;
                        if (ready == 1) readySince = elapsed.Elapsed.TotalSeconds;
                        if (ready >= 2 && elapsed.Elapsed.TotalSeconds - readySince >= 4)
                        {
                            int health = Player.Health, mana = Player.Mana;
                            var inventory = Player.Inventory.Items;
                            var cells = World.Cells;
                            var monsters = World.AvailableMonsters();
                            result["latestReadStatus"] = new { healthValid = health >= 0, manaValid = mana >= 0,
                                inventoryReady = inventory != null, cellsReady = cells != null && cells.Length > 0,
                                monstersReady = monsters != null };
                            if (health < 0 || mana < 0 || inventory == null || cells == null || cells.Length == 0 || monsters == null)
                            {
                                samples = 0;
                                result["status"] = "waiting_for_game_data";
                                result["eventCounts"] = eventCounts;
                                result["flashErrors"] = errors;
                                File.WriteAllText(reportPath, JsonConvert.SerializeObject(result, Formatting.Indented));
                                return;
                            }
                            samples++;
                            result["inventoryCount"] = inventory.Count;
                            result["cellCount"] = cells.Length;
                            result["monsterCount"] = monsters.Count;
                            result["gameReads"] = "passed";
                            if (samples >= 3)
                            {
                                if (!eventCounts.ContainsKey("packetFromServer") || !eventCounts.ContainsKey("pext"))
                                    throw new InvalidOperationException("Live packet notifications did not reach C#.");
                                if (root.changeServerList.Items.Count == 0)
                                    throw new InvalidOperationException("The server-list notification did not update the application.");
                                result["serverListCount"] = root.changeServerList.Items.Count;
                                root.Close();
                            }
                        }
                    }
                    else { ready = 0; readySince = 0; }
                }
                catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; Root.Instance?.Close(); }
            };
            // Starting a WinForms Timer creates a native window. Wait until Program.Main has
            // configured text rendering and entered its own message loop before starting it.
            EventHandler startTimer = null;
            startTimer = (sender, e) => { Application.Idle -= startTimer; timer.Start(); };
            Application.Idle += startTimer;
            try
            {
                var program = typeof(Root).Assembly.GetType("Grimoire.Program");
                program.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
            catch (Exception error) { failure = error.GetBaseException().Message; }
            Application.Idle -= startTimer;
            timer.Stop();
        }
        int? exit = bridge == null ? null : (int?)bridge.GetType().GetProperty("ExitCode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bridge);
        bool forced = bridge != null && (bool)bridge.GetType().GetProperty("RequiredTermination", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bridge);
        result["passed"] = failure == null && basicPassed && (!live || samples >= 3) && (useFlash || exit == 0) && !forced && errors.Count == 0;
        result["failure"] = failure;
        result["eventCounts"] = eventCounts;
        result["flashErrors"] = errors;
        result["playerExitCode"] = exit;
        result["forcedTermination"] = forced;
        result["status"] = (bool)result["passed"] ? "passed" : "failed";
        File.WriteAllText(reportPath, JsonConvert.SerializeObject(result, Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(result));
        return (bool)result["passed"] ? 0 : 1;
    }
}
