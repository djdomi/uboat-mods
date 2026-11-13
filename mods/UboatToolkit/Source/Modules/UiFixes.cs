using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.Pooling;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fix "Cannot set the parent of the GameObject 'Default Tooltip(Clone)' while activating or deactivating
    // the parent GameObject 'GUI'": the game re-parents a reused tooltip to the canvas every time it opens,
    // even when it already sits there. Unity refuses that while the GUI is being switched on/off.
    // Now the move is skipped when the tooltip is already under the canvas, and otherwise done one frame later.
    [HarmonyPatch(typeof(UBOAT.Game.UI.FloatingTooltip), "OpenInternal")]
    internal static class UiFixes_TooltipParent
    {
        private static readonly MethodInfo SetParent2 = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(UiFixes_TooltipParent), nameof(Set));

        public static void Set(Transform child, Transform parent, bool keep)
        {
            if (child == null) return;
            if (!Settings.B("FixTooltipParent")) { child.SetParent(parent, keep); return; }
            if (child.parent == parent) return;
            // Must happen right now: the game sizes and positions the tooltip on the next lines. Moving it a frame
            // later left an empty box (DoMe 2026-11-12, visibility panel). Only the redundant move is skipped.
            child.SetParent(parent, keep);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand as MethodInfo == SetParent2) { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }

    // Fix 'Pooled instance of prefab "Character/Other/Newspaper Mirror" has enabled animators ...':
    // some props (newspaper stand) go back into the pool with their animator still running. The game notices
    // that only when it reuses them, logs the error and throws the object away. We do the same at the moment
    // it is handed back, without the error, so no broken object ever sits in the pool.
    [HarmonyPatch(typeof(GlobalPool), nameof(GlobalPool.FreeInstance))]
    internal static class UiFixes_PoolAnimators
    {
        private static readonly List<Animator> anims = new List<Animator>();
        private static readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();

        private static void Postfix(GlobalPool __instance, GameObject instance)
        {
            if (!Settings.B("FixPoolAnimators") || instance == null) return;
            var po = instance.GetComponent<PooledObject>();
            if (po == null) return;
            bool bad = false;
            instance.GetComponentsInChildren(true, anims);
            foreach (var a in anims) if (a != null && a.enabled) { bad = true; break; }
            if (!bad) { instance.GetComponentsInChildren(true, skins); foreach (var s in skins) if (s != null && s.enabled) { bad = true; break; } }
            anims.Clear(); skins.Clear();
            if (!bad) return;
            var pool = Traverse.Create(__instance).Method("GetPool", new[] { typeof(object) }).GetValue(po.PrefabPath);
            if (pool != null)
            {
                var tp = Traverse.Create(pool);
                if (tp.Property("Count").GetValue<int>() > 0 && (tp.Method("Peek").GetValue() as GameObject) == instance) tp.Method("Pop").GetValue();
            }
            Object.Destroy(instance);
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix NullReferenceException in FlamesRegistry.OnFlamesDetached / SpawnParticles: when a burning part is shot
    // off a ship (e.g. a gun "Detached"), the burning surface reports to a fire registry whose flame effect is
    // already gone. The game then throws every time. Now: nothing to move -> skip quietly.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), nameof(UBOAT.Game.Scene.Effects.FlamesRegistry.OnFlamesDetached))]
    internal static class UiFixes_FlamesDetached
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance, UBOAT.Game.Scene.Effects.FlammableSurface flames)
        {
            if (!Settings.B("FixFlames")) return true;
            return __instance != null && __instance.FlamesEffect != null && flames != null && flames.transform.parent != null;
        }

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }

    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), "SpawnParticles", new System.Type[0])]
    internal static class UiFixes_FlamesSpawn
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance) =>
            !Settings.B("FixFlames") || (__instance != null && __instance.FlamesEffect != null);

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }
}

namespace UboatToolkit.Modules
{
    // Diagnosis "empty box next to the eye" (DoMe 2026-11-12): every game tooltip that opens is checked 0.4 s later
    // and written to the log once per kind: who opened it, text, measured size, font, material and the alpha of box and
    // text. Shows whether the text is empty, invisible (alpha 0), unmeasured (font/material) or the box is something else.
    public class TooltipDiag : ModuleBase
    {
        private struct Pending { public UBOAT.Game.UI.FloatingTooltip T; public float At; public string From; }
        private static readonly List<Pending> pending = new List<Pending>();
        private static readonly HashSet<string> done = new HashSet<string>();
        internal static string Caller = "?";
        private static int count;
        private static readonly FieldInfo contentsF = AccessTools.Field(typeof(UBOAT.Game.UI.FloatingTooltip), "contents");
        private static readonly FieldInfo openF = AccessTools.Field(typeof(UBOAT.Game.UI.FloatingTooltip), "isOpen");

        internal static void Opened(object t)
        {
            var ft = t as UBOAT.Game.UI.FloatingTooltip;
            if (ft == null || !Settings.B("DiagTooltip") || count >= 40) return;
            pending.Add(new Pending { T = ft, At = Time.unscaledTime + 0.4f, From = Caller });
            Caller = "?";
        }

        public override void Frame()
        {
            if (pending.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (now < p.At) continue;
                pending.RemoveAt(i);
                try { Check(p); } catch (System.Exception e) { Util.Log("Tooltip-Diagnose: " + e.Message); }
            }
        }

        private static void Check(Pending p)
        {
            if (p.T == null) return;
            var c = contentsF != null ? contentsF.GetValue(p.T) as TMPro.TextMeshProUGUI : null;
            var img = p.T.GetComponent<UnityEngine.UI.Image>();
            string text = c != null ? (c.text ?? "") : "<kein Text-Feld>";
            bool empty = c == null || text.Trim().Length == 0;
            string key = p.From + "|" + empty + "|" + (c != null && c.font != null ? c.font.name : "-");
            if (!done.Add(key) && p.From != "Auge" && !empty) return;
            count++;
            var sb = new System.Text.StringBuilder();
            sb.Append("Tooltip-Diagnose [").Append(p.From).Append("]: offen=").Append(openF != null ? openF.GetValue(p.T) : "?")
              .Append(", aktiv=").Append(p.T.gameObject.activeInHierarchy);
            var rt = p.T.transform as RectTransform;
            if (rt != null) sb.Append(", Kasten=").Append(rt.rect.width.ToString("0")).Append("x").Append(rt.rect.height.ToString("0"));
            if (img != null) sb.Append(", Kasten-Alpha=").Append(img.canvasRenderer.GetAlpha().ToString("0.00"));
            if (c != null)
            {
                string one = text.Replace("\n", " / ").Replace("\r", "");
                if (one.Length > 160) one = one.Substring(0, 160) + "...";
                sb.Append(", Text(").Append(text.Length).Append(" Zeichen)='").Append(one).Append("'")
                  .Append(", Text an=").Append(c.enabled).Append("/").Append(c.gameObject.activeInHierarchy)
                  .Append(", Text-Alpha=").Append(c.canvasRenderer.GetAlpha().ToString("0.00"))
                  .Append(", Farbe-A=").Append(c.color.a.ToString("0.00"))
                  .Append(", Größe=").Append(c.fontSize.ToString("0.0"))
                  .Append(", Schrift=").Append(c.font != null ? c.font.name : "KEINE")
                  .Append(", Material=").Append(c.fontSharedMaterial != null ? c.fontSharedMaterial.name : "KEINES")
                  .Append(", gemessen=").Append(c.preferredHeight.ToString("0"))
                  .Append(", Zeichen sichtbar=").Append(c.textInfo != null ? c.textInfo.characterCount : -1)
                  .Append(", Text-Rect=").Append(c.rectTransform.rect.width.ToString("0")).Append("x").Append(c.rectTransform.rect.height.ToString("0"));
            }
            Util.Log(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.FloatingTooltip), "OpenInternal")]
    internal static class TooltipDiag_Open
    {
        private static void Postfix(object __result) { try { TooltipDiag.Opened(__result); } catch { } }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.Resources.DetectabilityGUI), nameof(UBOAT.Game.UI.Resources.DetectabilityGUI.OnPointerEnter))]
    internal static class TooltipDiag_Eye
    {
        private static void Prefix() { TooltipDiag.Caller = "Auge"; }
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception != null) Util.Log("Tooltip-Diagnose [Auge]: Spiel-Fehler beim Öffnen: " + __exception.GetType().Name + ": " + __exception.Message);
            return __exception;
        }
    }
}

namespace UboatToolkit.Modules
{
    // Empty tooltip box (DoMe 2026-11-13: eye icon, also on the stock IXC): a game "Tooltip" whose text is empty
    // (missing translation or update function returning "") still opened the box. Now: an empty text opens nothing,
    // and the object path + key are logged once so the missing text can be found.
    [HarmonyPatch(typeof(UBOAT.Game.UI.Tooltip), "Open")]
    internal static class UiFixes_EmptyTooltip
    {
        private static readonly FieldInfo localeF = AccessTools.Field(typeof(UBOAT.Game.UI.Tooltip), "locale");
        private static readonly HashSet<string> logged = new HashSet<string>();

        internal static string PathOf(Transform t)
        {
            string path = t != null ? t.name : "?";
            for (int i = 0; i < 4 && t != null && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static bool Prefix(UBOAT.Game.UI.Tooltip __instance, string ___localizationKey, System.Func<string> ___updateFunction)
        {
            try
            {
                string text = null, key = ___localizationKey;
                if (___updateFunction != null) { text = ___updateFunction(); if (text == null) return true; key = "(Funktion)"; }
                else if (key != null && localeF != null)
                {
                    var loc = localeF.GetValue(null);
                    string k = key.EndsWith("/") ? key + __instance.name : key;
                    text = loc != null ? Traverse.Create(loc).Method("get_Item", new[] { typeof(string) }, new object[] { k }).GetValue<string>() : null;
                    key = k;
                }
                else return true;
                string path = PathOf(__instance.transform);
                TooltipDiag.Caller = "Tooltip " + path + " key=" + key;
                if (text != null && text.Trim().Length > 0) return true;
                if (logged.Add(path + "|" + key)) Util.Log("Tooltip ohne Text unterdrückt: " + path + ", Schlüssel '" + key + "'");
                return !Settings.B("FixEmptyTooltip");
            }
            catch (System.Exception e) { Util.Log("Tooltip-Prüfung: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.PlainTooltip), nameof(UBOAT.Game.UI.PlainTooltip.Open))]
    internal static class UiFixes_PlainTooltipDiag
    {
        private static readonly HashSet<string> logged = new HashSet<string>();
        private static void Postfix(UBOAT.Game.UI.PlainTooltip __instance, RectTransform target, string contents)
        {
            try
            {
                if (!Settings.B("DiagTooltip")) return;
                string path = UiFixes_EmptyTooltip.PathOf(target != null ? target.transform : __instance.transform);
                bool empty = contents == null || contents.Trim().Length == 0;
                if (logged.Add(path + "|" + empty))
                    Util.Log("Tooltip-Diagnose [Plain " + path + "]: Text(" + (contents == null ? -1 : contents.Length) + " Zeichen)='" + (contents ?? "").Replace("\n", " / ") + "'");
            }
            catch { }
        }
    }
}
