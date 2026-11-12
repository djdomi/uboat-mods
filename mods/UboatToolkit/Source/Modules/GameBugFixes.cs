using System;
using HarmonyLib;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Skills;

namespace UboatToolkit.Modules
{
    // Bugs in the game itself that showed up in DoMe's log (2026-11-12).

    // Actions without requirements ("*" in the data sheet) have null lists. A man with a
    // "no helpers" skill then crashed when starting such an action (e.g. a watch station).
    [HarmonyPatch(typeof(DisallowFollowersStatus), "ValidateHelperLimits")]
    internal static class Fix_DisallowFollowersNull
    {
        private static bool Prefix(DisallowFollowersStatus __instance, SingleCharacterAction action, ref int workers, ref int followers)
        {
            try
            {
                if (action == null) return false;
                var hard = action.HardRequirements; var soft = action.SoftRequirements;
                string n = __instance.Name;
                if ((hard != null && Array.IndexOf(hard, n) != -1) || (soft != null && Array.IndexOf(soft, n) != -1))
                {
                    workers = 0;
                    followers = 0;
                }
                return false;
            }
            catch { return true; }
        }
    }

    // "Advanced Equipment" skill: entering a listening room on a boat without a radar detector crashed
    // (the game looks for the detector without checking). Without a detector there is nothing to boost.
    [HarmonyPatch(typeof(AdvancedEquipment), "Character_InteractableSlotChanged")]
    internal static class Fix_AdvancedEquipmentNoDetector
    {
        private static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
    }
}

namespace UboatToolkit.Modules
{
    // "GetRemainingDistance can only be called on an active agent that has been placed on a NavMesh" spam:
    // the game asks how far a man still has to walk while he is not standing on walkable ground
    // (agent switched off, or just pushed off the walk mesh - our NavRescue puts him back a moment later).
    // Asking then is pointless; answer "not there yet" without asking, so nothing is logged.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator), "DestinationReached", HarmonyLib.MethodType.Getter)]
    internal static class Fix_DestinationReachedOffMesh
    {
        private static bool Prefix(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator __instance, ref bool __result)
        {
            try
            {
                var a = __instance.Agent;
                if (a != null && a.isActiveAndEnabled && a.isOnNavMesh) return true;
                __result = false;
                return false;
            }
            catch { return true; }
        }
    }

    // Port recruits on the pier: same question for a recruit who is off the walk mesh. Wait and ask again later.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.DockRecruitsInteractable), "DoUpdate")]
    internal static class Fix_DockRecruitsOffMesh
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, System.Collections.Generic.List<UBOAT.Game.Scene.Characters.InteractiveCharacter>> chars =
            HarmonyLib.AccessTools.FieldRefAccess<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, System.Collections.Generic.List<UBOAT.Game.Scene.Characters.InteractiveCharacter>>("characters");
        private static readonly HarmonyLib.AccessTools.FieldRef<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, int> index =
            HarmonyLib.AccessTools.FieldRefAccess<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, int>("characterIndex");

        private static bool Prefix(UBOAT.Game.Scene.Characters.DockRecruitsInteractable __instance, ref float __result)
        {
            try
            {
                var list = chars(__instance);
                int i = index(__instance);
                if (list == null || i < 0 || i >= list.Count) return true;
                var c = list[i];
                if (c == null || c.Navigator == null || c.Ai == null || !c.Ai.isActiveAndEnabled) return true;
                var a = c.Navigator.Agent;
                if (a == null || (a.isActiveAndEnabled && a.isOnNavMesh)) return true;
                if (c.Ai.GetActiveBehaviour<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour>() == null) return true;
                __result = 0.1f;
                return false;
            }
            catch { return true; }
        }
    }
}

namespace UboatToolkit.Modules
{
    // Changing boats in port: the old boat's devices are destroyed while the resource bar still listens
    // to them -> one error per device. Nothing left to show, so the error is dropped.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.Resources.ResourceGUI), "Gain_Modified")]
    internal static class Fix_ResourceGuiBoatChange
    {
        private static System.Exception Finalizer(System.Exception __exception) => __exception is System.NullReferenceException ? null : __exception;
    }
}

namespace UboatToolkit.Modules
{
    // Skip the start videos (studio + publisher logo) and go straight to the main menu.
    // Works from the moment the mod is loaded; the very first Unity logo comes before any mod and cannot be skipped.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.IntroScene), "Update")]
    internal static class SkipStartupIntros
    {
        private static bool done;
        private static void Prefix(UBOAT.Game.UI.IntroScene __instance)
        {
            if (done) return;
            try
            {
                if (!Settings.B("SkipStartupIntros")) { done = true; return; }
                var t = HarmonyLib.Traverse.Create(__instance);
                if (System.Convert.ToInt32(t.Field("launchMode").GetValue()) != 0) { done = true; return; }   // only normal start
                var warn = t.Field("gameFilesCorruptedWarning").GetValue<UnityEngine.GameObject>();
                if (warn != null && warn.activeSelf) return;                                                   // let the player read that
                if (t.Field("sceneResource").GetValue() == null) return;                                       // menu not requested yet
                int stage = t.Field("stage").GetValue<int>();
                if (stage > 1) { done = true; return; }
                var vp = t.Field("videoPlayer").GetValue();
                if (vp != null) HarmonyLib.AccessTools.Method(vp.GetType(), "Stop")?.Invoke(vp, null);
                t.Field("wasPlaying").SetValue(true);
                t.Field("stage").SetValue(1);   // the game's own code now opens the main menu this frame
                done = true;
                Util.Log("Start-Intros übersprungen");
            }
            catch (System.Exception e) { done = true; Util.Log("Start-Intros: " + e.Message); }
        }
    }
}

namespace UboatToolkit.Modules
{
    // Every place in the game that asks a man's walk agent "how far is it still?" gets a safe version:
    // if the agent is switched off or not on walkable ground right now, the answer is 0 instead of
    // Unity's red "GetRemainingDistance can only be called on an active agent" line.
    // (The game checks the ground once and then asks a few lines later, after the man may have been moved.)
    [HarmonyLib.HarmonyPatch]
    internal static class Fix_RemainingDistanceSafe
    {
        private static readonly System.Reflection.MethodInfo getter =
            HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.AI.NavMeshAgent), "remainingDistance");
        private static readonly System.Reflection.MethodInfo safe =
            HarmonyLib.AccessTools.Method(typeof(Fix_RemainingDistanceSafe), nameof(Remaining));

        public static float Remaining(UnityEngine.AI.NavMeshAgent a)
        {
            if (a == null || !a.isActiveAndEnabled || !a.isOnNavMesh) return 0f;
            return a.remainingDistance;
        }

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var list = new System.Collections.Generic.List<System.Reflection.MethodBase>();
            foreach (var t in new[] { typeof(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator),
                                      typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour),
                                      typeof(UBOAT.Game.Scene.Characters.DockRecruitsInteractable) })
                foreach (var m in HarmonyLib.AccessTools.GetDeclaredMethods(t))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (var i in HarmonyLib.PatchProcessor.ReadMethodBody(m))
                            if (i.Value is System.Reflection.MethodInfo mi && mi == getter) { list.Add(m); break; }
                    }
                    catch { }
                }
            return list;
        }

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == System.Reflection.Emit.OpCodes.Callvirt || c.opcode == System.Reflection.Emit.OpCodes.Call) && c.operand is System.Reflection.MethodInfo mi && mi == getter)
                {
                    c.opcode = System.Reflection.Emit.OpCodes.Call;
                    c.operand = safe;
                }
                yield return c;
            }
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix NullReferenceException in TimeCompressionExtendedUI.OnEnable/OnDisable and
    // TorpedoSalvoPreviewUI.ValidateQueuedUpdate: right after a boat change (or on a boat without the torpedo
    // panel) the game's "current torpedo launchers panel" is still empty, and these methods ask it whether it is
    // open. Now an empty panel simply counts as closed, and the rest of each method runs normally.
    [HarmonyLib.HarmonyPatch]
    internal static class Fix_LaunchersUiMissing
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TimeCompressionExtendedUI), "OnEnable");
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TimeCompressionExtendedUI), "OnDisable");
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TorpedoSalvoPreviewUI), "ValidateQueuedUpdate");
        }

        public static bool Active(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI ui) => ui != null && ui.gameObject.activeInHierarchy;
        public static bool Opened(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI ui) => ui != null && ui.IsOpened;

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var getUi = HarmonyLib.AccessTools.PropertyGetter(typeof(UBOAT.Game.UI.GameUI), "CurrentTorpedoLaunchersUI");
            var getGo = HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.Component), "gameObject");
            var getAct = HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.GameObject), "activeInHierarchy");
            var getOpen = HarmonyLib.AccessTools.PropertyGetter(typeof(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI), "IsOpened");
            var list = new System.Collections.Generic.List<HarmonyLib.CodeInstruction>(code);
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (!(list[i].operand is System.Reflection.MethodInfo m) || m != getUi) continue;
                var n = list[i + 1].operand as System.Reflection.MethodInfo;
                if (n == getOpen) { list[i + 1].opcode = System.Reflection.Emit.OpCodes.Call; list[i + 1].operand = HarmonyLib.AccessTools.Method(typeof(Fix_LaunchersUiMissing), nameof(Opened)); }
                else if (n == getGo && i + 2 < list.Count && list[i + 2].operand as System.Reflection.MethodInfo == getAct)
                {
                    list[i + 1].opcode = System.Reflection.Emit.OpCodes.Call; list[i + 1].operand = HarmonyLib.AccessTools.Method(typeof(Fix_LaunchersUiMissing), nameof(Active));
                    list[i + 2].opcode = System.Reflection.Emit.OpCodes.Nop; list[i + 2].operand = null;
                }
            }
            return list;
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix "ArgumentOutOfRangeException ... MorphTarget.ApplyTo" (10x, e.g. on arriving in port): a character's face/body
    // morph was made for a mesh with more vertices than the mesh he wears now. The game then throws every frame for him.
    // Now: the error is caught, that one morph layer is switched off for this character (he keeps the plain mesh), and it
    // is logged once with the character's path.
    [HarmonyLib.HarmonyPatch(typeof(DWS.Common.Characters.Deformers.MorphDeformer), "UpdateDeformer")]
    internal static class Fix_MorphOutOfRange
    {
        private static readonly System.Collections.Generic.HashSet<int> logged = new System.Collections.Generic.HashSet<int>();
        private static System.Exception Finalizer(System.Exception __exception, DWS.Common.Characters.Deformers.MorphDeformer __instance)
        {
            if (!(__exception is System.ArgumentOutOfRangeException) || __instance == null) return __exception;
            __instance.enabled = false;
            if (logged.Add(__instance.GetInstanceID()))
            {
                var t = __instance.transform; string path = t.name;
                for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
                Util.Log("Figur: Gesichts-/Körperform passt nicht zum Modell, abgeschaltet: " + path);
            }
            return null;
        }
    }
}

namespace UboatToolkit.Modules
{
    // "GameObject is already being activated or deactivated" when the game saves while the port window is open:
    // after every save SavingNotificationUI switches the whole game UI off and on; that disables the port window, whose
    // OnDisable then calls GameUI.SetMode(Normal) = SetActive on the UI that is just being switched off -> Unity error,
    // and the port window is left in "Normal" mode (closed state broken). Now SetMode is skipped while the game UI itself
    // is being switched off; when it comes back on, the port window simply stays open.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.PortActivitiesUI), "OnDisable")]
    internal static class Fix_PortWindowSaveToggle
    {
        private static float nextLog;
        public static void SetMode(UBOAT.Game.UI.GameUI ui, UBOAT.Game.UI.GameUI.Mode mode)
        {
            if (ui != null && !ui.gameObject.activeSelf)
            {
                if (UnityEngine.Time.unscaledTime > nextLog) { nextLog = UnityEngine.Time.unscaledTime + 60f; Util.Log("Hafen-Fenster: Umschalten beim Speichern übersprungen (Unity-Fehler vermieden)"); }
                return;
            }
            ui.SetMode(mode);
        }

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var target = HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.GameUI), "SetMode", new[] { typeof(UBOAT.Game.UI.GameUI.Mode) });
            int n = 0;
            foreach (var c in code)
            {
                if (c.operand as System.Reflection.MethodInfo == target) { c.opcode = System.Reflection.Emit.OpCodes.Call; c.operand = HarmonyLib.AccessTools.Method(typeof(Fix_PortWindowSaveToggle), nameof(SetMode)); n++; }
                yield return c;
            }
            if (n == 0) Util.Log("Hafen-Fenster-Fix: SetMode nicht gefunden, Fix nicht aktiv");
        }
    }
}

namespace UboatToolkit.Modules
{
    // 'Slot "Fuel 1" / "Hidden Goods 1" is defined for entity type "Large Freighter 01/02" in game data, but it's missing
    // in the entity prefab': the game data gives these freighters cargo for slots their 3D model does not have, so the game
    // logs an error and that cargo item is never placed on the ship. Now the missing slot is added at runtime, next to a
    // similar slot of the same ship (e.g. "Hidden Goods 1" next to another "Goods" slot), and the cargo is placed there.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.SandboxEntity), "SpawnEquipment", new[] { typeof(UBOAT.Game.Sandbox.SandboxEquipment), typeof(bool) }, new[] { HarmonyLib.ArgumentType.Normal, HarmonyLib.ArgumentType.Ref })]
    internal static class Fix_MissingCargoSlot
    {
        private static readonly System.Collections.Generic.HashSet<string> logged = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.List<UBOAT.Game.Scene.Entities.ShipEquipmentSlot> buf = new System.Collections.Generic.List<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>();

        private static void Prefix(UBOAT.Game.Sandbox.SandboxEntity __instance, UBOAT.Game.Sandbox.SandboxEquipment equipment, ref bool sceneSlotsAlreadyBuffered)
        {
            try
            {
                var ent = __instance?.SpawnedEntity;
                if (ent == null || equipment?.Slot == null) return;
                string name = equipment.Slot.Name;
                if (string.IsNullOrEmpty(name) || name == "Deck Goods A") return;
                ent.GetComponentsInChildren(true, buf);
                foreach (var s in buf) if (s != null && s.name == name) return;          // slot exists: nothing to do
                string stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ');
                string word = stem.Contains(" ") ? stem.Substring(stem.LastIndexOf(' ') + 1) : stem;
                UBOAT.Game.Scene.Entities.ShipEquipmentSlot like = null;
                foreach (var s in buf) if (s != null && s.name.StartsWith(stem)) { like = s; break; }
                if (like == null) foreach (var s in buf) if (s != null && s.name.Contains(word)) { like = s; break; }
                if (like == null) foreach (var s in buf) if (s != null && s.GetComponentInParent<UBOAT.Game.Scene.Items.Storage>() != null) { like = s; break; }
                if (like == null) return;   // nothing similar on this ship: leave it to the game
                var go = new UnityEngine.GameObject(name);
                go.layer = like.gameObject.layer;
                go.transform.SetParent(like.transform.parent, false);
                go.transform.localPosition = like.transform.localPosition + new UnityEngine.Vector3(0f, 0f, 0.8f);
                go.transform.localRotation = like.transform.localRotation;
                go.AddComponent<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>();
                sceneSlotsAlreadyBuffered = false;   // the game re-reads the ship's slots and now finds the new one
                string type = Util.SafeGet(() => __instance.Type.Name, "?");
                if (logged.Add(type + "/" + name)) Util.Log("Ladung: Platz \"" + name + "\" fehlte im Modell von " + type + ", neben \"" + like.name + "\" ergänzt");
            }
            catch (System.Exception e) { Util.Log("Ladung: Platz ergänzen fehlgeschlagen: " + e.Message); }
        }
    }
}
