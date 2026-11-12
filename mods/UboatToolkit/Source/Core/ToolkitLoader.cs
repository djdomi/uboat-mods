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
                    try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                    catch (Exception pe)
                    {
                        bad++;
                        Debug.Log(Tag + " patch failed: " + t.Name + ": " + pe.Message);
                        DebugLog.Write("ERROR", "Patch " + t.Name + " fehlgeschlagen: " + pe.Message);
                    }
                }
                DebugLog.Write("START", "Patches ok: " + ok + ", fehlgeschlagen: " + bad);
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
