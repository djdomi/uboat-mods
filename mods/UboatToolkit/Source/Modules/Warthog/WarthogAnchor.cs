using System.Collections.Generic;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Real anchors for U-1337 (the game has none for the player): bow anchor, stern anchor or both.
    //  - "Anker werfen / lichten" (key, F10 button): the anchor model (WH_Anchor_Bow / WH_Anchor_Stern in the hull)
    //    sinks to the seabed, a chain is drawn from the hawse to the anchor
    //  - only possible when the seabed is within the chain length (seabed height from the game's terrain grid)
    //  - anchored: the boat is held at the anchor point against current, wind and drift; inside the chain circle a strong
    //    damping, at the end of the chain a hard spring, so the engines cannot drag it further. One anchor: the boat swings
    //    around it; two anchors: position and heading are held. Works surfaced and submerged (depth is the boat's own).
    //  - lifting takes WhAnchorLiftSeconds (0 = instant); time compression stays allowed
    public class WarthogAnchor : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhAnchor");
        internal enum State { Up, Lowering, Down, Lifting }
        internal class Anchor
        {
            public string Name; public Transform Model; public Vector3 RestLocal; public State State = State.Up;
            public Vector3 Ground; public float Progress; public LineRenderer Chain;
            public Vector3 Hawse => Model.parent.TransformPoint(RestLocal);
        }
        internal static readonly List<Anchor> Anchors = new List<Anchor>();
        private static object cachedFor;
        internal static string Status = "Anker oben";
        private static bool requestToggle;

        public static void Toggle() => requestToggle = true;
        internal static bool Anchored { get { foreach (var a in Anchors) if (a.State == State.Down) return true; return false; } }

        private static void Collect(PlayerShip ship)
        {
            if (cachedFor == (object)ship) return;
            cachedFor = ship;
            foreach (var a in Anchors) if (a.Chain) Object.Destroy(a.Chain.gameObject);
            Anchors.Clear();
            if (ship == null) return;
            foreach (var t in ship.GetComponentsInChildren<Transform>(true))
                if (t.name == "WH_Anchor_Bow" || t.name == "WH_Anchor_Stern")
                    Anchors.Add(new Anchor { Name = t.name.EndsWith("Bow") ? "Buganker" : "Heckanker", Model = t, RestLocal = t.localPosition });
            if (ship.GetComponent<WarthogAnchorBody>() == null) ship.gameObject.AddComponent<WarthogAnchorBody>();
            Util.Log("[Warthog] anchor: found " + Anchors.Count + " anchors on " + ship.name);
        }

        private static bool Wanted(Anchor a)
        {
            int mode = Mathf.RoundToInt(Settings.F("WhAnchorMode"));   // 0 bow, 1 stern, 2 both
            return mode == 2 || (mode == 0 && a.Name == "Buganker") || (mode == 1 && a.Name == "Heckanker");
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect(ship);
            if (Keys.Down("KeyWhAnchor")) requestToggle = true;
            if (requestToggle)
            {
                requestToggle = false;
                if (!On) ToolkitRunner.ShowNotice("Anker: im F10-Menü ausgeschaltet", 4f);
                else if (Anchored || Anchors.Exists(a => a.State == State.Lowering)) Lift();
                else Drop(ship);
            }
            float dt = Time.deltaTime;
            foreach (var a in Anchors)
            {
                if (a.Model == null) continue;
                switch (a.State)
                {
                    case State.Lowering:
                        a.Progress = Mathf.MoveTowards(a.Progress, 1f, dt * 3f / Mathf.Max(1f, Vector3.Distance(a.Hawse, a.Ground)));
                        if (a.Progress >= 1f) { a.State = State.Down; Util.Log("[Warthog] anchor: " + a.Name + " holds at seabed " + a.Ground.y.ToString("0") + " m"); Status = "Vor Anker"; ToolkitRunner.ShowNotice("Vor Anker", 5f); }
                        break;
                    case State.Lifting:
                        float secs = Settings.F("WhAnchorLiftSeconds");
                        a.Progress = secs <= 0f ? 0f : Mathf.MoveTowards(a.Progress, 0f, dt / secs);
                        if (a.Progress <= 0f) { a.State = State.Up; Util.Log("[Warthog] anchor: " + a.Name + " up"); }
                        break;
                }
                // model: rest position -> seabed (in world space), chain from the hawse
                Vector3 rest = a.Hawse;
                Vector3 pos = a.State == State.Up ? rest : Vector3.Lerp(rest, a.Ground, a.Progress);
                if (a.State == State.Up) a.Model.localPosition = a.RestLocal; else a.Model.position = pos;
                DrawChain(a, rest, pos, a.State != State.Up);
            }
            if (Anchors.Count > 0 && !Anchored && !Anchors.Exists(x => x.State != State.Up)) Status = "Anker oben";
        }

        private static void Drop(PlayerShip ship)
        {
            float chain = Settings.F("WhAnchorChain");
            int dropped = 0;
            foreach (var a in Anchors)
            {
                if (!Wanted(a) || a.State != State.Up) continue;
                Vector3 h = a.Hawse;
                float ground = WarthogTerrain.SeabedY(h.x, h.z);
                if (float.IsNaN(ground)) { ToolkitRunner.ShowNotice("Anker: Meeresgrund unbekannt", 5f); Util.Log("[Warthog] anchor: no seabed data"); return; }
                float drop = h.y - ground;
                if (drop > chain) { ToolkitRunner.ShowNotice("Anker: Grund in " + drop.ToString("0") + " m, Kette nur " + chain.ToString("0") + " m", 6f); Util.Log("[Warthog] anchor: too deep (" + drop.ToString("0") + " m > chain " + chain.ToString("0") + " m)"); return; }
                a.Ground = new Vector3(h.x, ground, h.z); a.Progress = 0f; a.State = State.Lowering; dropped++;
            }
            if (dropped > 0) { Status = "Anker fällt"; ToolkitRunner.ShowNotice("Anker wird geworfen", 4f); Util.Log("[Warthog] anchor: dropping " + dropped + " anchor(s), chain " + chain.ToString("0") + " m"); }
        }

        private static void Lift()
        {
            foreach (var a in Anchors) if (a.State == State.Down || a.State == State.Lowering) a.State = State.Lifting;
            Status = "Anker wird gelichtet";
            ToolkitRunner.ShowNotice("Anker wird gelichtet", 4f);
            Util.Log("[Warthog] anchor: lifting (" + Settings.F("WhAnchorLiftSeconds").ToString("0") + " s)");
        }

        private static Material chainMat;
        private static void DrawChain(Anchor a, Vector3 from, Vector3 to, bool show)
        {
            if (!show) { if (a.Chain) a.Chain.enabled = false; return; }
            if (a.Chain == null)
            {
                var go = new GameObject("Warthog Anchor Chain " + a.Name);
                a.Chain = go.AddComponent<LineRenderer>();
                if (chainMat == null) chainMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.12f, 0.12f, 0.13f) };
                a.Chain.sharedMaterial = chainMat; a.Chain.startWidth = a.Chain.endWidth = 0.12f; a.Chain.positionCount = 12;
                a.Chain.startColor = a.Chain.endColor = new Color(0.12f, 0.12f, 0.13f);
            }
            a.Chain.enabled = true;
            float sag = Mathf.Clamp(Vector3.Distance(from, to) * 0.08f, 0.5f, 15f);
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                a.Chain.SetPosition(i, Vector3.Lerp(from, to, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * sag);
            }
        }
    }

    // physics part (FixedUpdate on the boat): holds the boat at its anchor(s)
    public class WarthogAnchorBody : MonoBehaviour
    {
        private void FixedUpdate()
        {
            var ship = Util.PlayerShip;
            if (ship == null || ship.gameObject != gameObject || !WarthogAnchor.On) return;
            var rb = ship.RigidBody; if (rb == null || rb.isKinematic) return;
            float chain = Settings.F("WhAnchorChain");
            int n = 0;
            foreach (var a in WarthogAnchor.Anchors)
            {
                if (a.State != WarthogAnchor.State.Down || a.Model == null) continue;
                n++;
                Vector3 hawse = a.Hawse;
                float depth = Mathf.Max(0f, hawse.y - a.Ground.y);
                float reach = Mathf.Sqrt(Mathf.Max(1f, chain * chain - depth * depth));   // horizontal radius of the chain
                Vector3 d = a.Ground - hawse; d.y = 0f;
                float dist = d.magnitude; Vector3 dir = dist > 0.01f ? d / dist : Vector3.zero;
                Vector3 v = rb.GetPointVelocity(hawse); v.y = 0f;
                // chain weight on the bottom: damping everywhere, rising with how taut the chain is
                float taut = Mathf.Clamp01(dist / reach);
                Vector3 f = -v * rb.mass * (0.15f + 1.5f * taut * taut);
                // end of the chain: hard spring + no motion away from the anchor
                if (dist > reach * 0.95f)
                {
                    f += dir * (dist - reach * 0.95f) * rb.mass * 2.0f;
                    float away = Vector3.Dot(v, -dir); if (away > 0f) f += dir * away * rb.mass * 4f;
                }
                rb.AddForceAtPosition(f, hawse, ForceMode.Force);
            }
            if (n > 0) rb.angularVelocity = new Vector3(rb.angularVelocity.x, rb.angularVelocity.y * (n > 1 ? 0.9f : 0.995f), rb.angularVelocity.z);
        }
    }

    // seabed height from the game's terrain grid (TerrainController.MainGrid.GetInterpolatedHeight)
    internal static class WarthogTerrain
    {
        private static Object controller;
        private static System.Reflection.MethodInfo height;
        private static object grid;
        public static float SeabedY(float x, float z)
        {
            try
            {
                if (controller == null)
                {
                    var t = HarmonyLib.AccessTools.TypeByName("UBOAT.Game.Scene.Environment.Terrain.TerrainController");
                    controller = t != null ? Object.FindObjectOfType(t) : null;
                    if (controller == null) return float.NaN;
                    grid = HarmonyLib.Traverse.Create(controller).Property("MainGrid").GetValue();
                    height = grid?.GetType().GetMethod("GetInterpolatedHeight", new[] { typeof(float), typeof(float), typeof(float) });
                }
                if (grid == null || height == null) return float.NaN;
                float y = (float)height.Invoke(grid, new object[] { x, z, float.NaN });
                return y < -1900f ? float.NaN : y;
            }
            catch (System.Exception e) { Util.Log("[Warthog] anchor: seabed query failed: " + e.Message); controller = null; return float.NaN; }
        }
    }
}
