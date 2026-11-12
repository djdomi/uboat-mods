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
                foreach (var t in AccessTools.GetTypesFromAssembly(typeof(ToolkitLoader).Assembly))
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    if (t.Namespace == "UboatToolkit.Modules.CrewAi") continue; // the crew AI patches itself below (sorted, own log)
                    if (t.Namespace == "UboatToolkit.Modules.Lrl") continue;    // the lookout patches its own list below
                    try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                    catch (Exception pe)
                    {
                        bad++;
                        Debug.Log(Tag + " patch failed: " + t.Name + ": " + pe.Message);
                        DebugLog.Write("ERROR", "Patch " + t.Name + " fehlgeschlagen: " + pe.Message);
                    }
                }
                DebugLog.Write("START", "Patches ok: " + ok + ", fehlgeschlagen: " + bad);
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
