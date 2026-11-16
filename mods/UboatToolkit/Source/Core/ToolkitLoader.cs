using System;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game;
using UBOAT.Game.Core.Serialization;

namespace UboatToolkit
{
    // Entry point: the game calls OnLoaded() once when the mod is loaded.
    [NonSerializedInGameState]
    public class ToolkitLoader : IUserMod
    {
        public const string Tag = "[UboatToolkit]";
        public static Harmony Harmony;

        public void OnLoaded()
        {
            try
            {
                Settings.Load();
                DebugLog.Write("START", "UBOAT Toolkit wird geladen, Unity " + Application.unityVersion + ", Spiel " + Application.version);
                DebugLog.DumpSettings("Start");
                try { UboatToolkit.Modules.CacheCheck.Install(); } catch (Exception ce) { Debug.Log(Tag + " cache check: " + ce.Message); }
                Harmony = new Harmony("djdomi.uboat.toolkit");
                // Patch class by class: if one game method changed, only that feature fails, the rest still works.
                int ok = 0, bad = 0;
                var slow = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();
                foreach (var t in AccessTools.GetTypesFromAssembly(typeof(ToolkitLoader).Assembly))
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    if (t.Namespace == "UboatToolkit.Modules.CrewAi") continue; // the crew AI patches itself below (sorted, own log)
                    if (t.Namespace == "UboatToolkit.Modules.Lrl") continue;    // the lookout patches its own list below
                    long pt = UboatToolkit.Modules.LoadClock.Now();
                    try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                    catch (Exception pe)
                    {
                        bad++;
                        var inner = pe; while (inner.InnerException != null) inner = inner.InnerException;
                        if (inner != pe) pe = new Exception(pe.Message + " -> " + inner.GetType().Name + ": " + inner.Message);
                        Debug.Log(Tag + " patch failed: " + t.Name + ": " + pe.Message);
                        DebugLog.Write("ERROR", "Patch " + t.Name + " fehlgeschlagen: " + pe.Message);
                    }
                    slow.Add(new System.Collections.Generic.KeyValuePair<string, double>(t.Name, UboatToolkit.Modules.LoadClock.Seconds(UboatToolkit.Modules.LoadClock.Now() - pt)));
                }
                DebugLog.Write("START", "Patches ok: " + ok + ", fehlgeschlagen: " + bad);
                // 0.3.71: measured 16 s for this loop at game start. Log the slowest patch classes to cut them down.
                slow.Sort((a, b) => b.Value.CompareTo(a.Value));
                double sum = 0; foreach (var kv in slow) sum += kv.Value;
                var top = new System.Text.StringBuilder();
                for (int i = 0; i < slow.Count && i < 15; i++) top.Append(slow[i].Key).Append(' ').Append((slow[i].Value * 1000).ToString("0")).Append(" ms, ");
                DebugLog.Write("START", "Patch-Zeit gesamt " + sum.ToString("0.0") + " s; langsamste: " + top);
                // Lookout: always installed, every switch is read live (as in the original).
                try { UboatToolkit.Modules.Lrl.LrlInstall.Install(Harmony); } catch (Exception le) { Debug.Log(Tag + " lookout: " + le.Message); }
                // Crew AI: switch takes effect at the next game start (as in the original).
                if (Settings.B("CrewAIOverhaul")) { try { UboatToolkit.Modules.CrewAi.CrewAIInstall.Install(Harmony); } catch (Exception ce) { Debug.Log(Tag + " crew AI: " + ce.Message); } }
                try { UboatToolkit.Modules.VoiceRepair.Run(); } catch (Exception ve) { Debug.Log(Tag + " voice: " + ve.Message); }
                var go = new GameObject(Tag);
                go.AddComponent<ToolkitRunner>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                Debug.Log(Tag + " loaded.");
            }
            catch (Exception e)
            {
                Debug.Log(Tag + " failed to load.");
                Debug.LogException(e);
            }
        }
    }
}
