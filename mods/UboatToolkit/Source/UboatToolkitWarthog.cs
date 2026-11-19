// ####################################################################################################
// ZUSATZ-DATEI WARTHOG: kann gelöscht werden, das Toolkit läuft dann ohne diese Teile weiter
// ####################################################################################################

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogAir.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UnityEngine;

    // Air on U-1337:
    //  - Ventilation always on and silent (noise x 0), also in silent running: the crew is not sent
    //    to switch it off any more.
    //  - Electrolysis: oxygen is made from sea water with battery power, so the air never goes bad
    //    and no potash absorbers (Kalipatronen) are used up.
    // Overlap with Toolkit "Air": on U-1337 this module wins (noise 0, no absorber use).
    public class WarthogAir : ModuleBase
    {
        internal static bool SilentVent => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhSilentVent");
        internal static bool AlwaysOn => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhVentAlwaysOn");
        internal static bool Electrolysis => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhElectrolysis");

        public override void Tick()
        {
            bool silent = SilentVent;
            foreach (var v in Util.Find<Ventilation>())
            {
                var noise = Traverse.Create(v).Field("noise").GetValue<Parameter>();
                if (noise != null) Util.OneScale(noise, "Warthog Silent").Value = silent ? 0.0001f : 1f;
                if (AlwaysOn && !v.enabled) v.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
            }
        }
    }

    // Silent running: do not send a man to switch the ventilation off.
    [HarmonyPatch(typeof(SilentRunningController), "AddAction")]
    internal static class WarthogAir_SilentPatch
    {
        private static bool Prefix(UBOAT.Game.Scene.Characters.Activators.Activator activator)
        {
            if (activator == null || !WarthogAir.AlwaysOn) return true;
            return activator.GetComponent<Ventilation>() == null;   // skip only the ventilation
        }
    }

    // Electrolysis: the regeneration limit stays full and the absorber is never used up.
    [HarmonyPatch(typeof(Ventilation), "UpdateOxygenModifier")]
    internal static class WarthogAir_OxygenPatch
    {
        private static void Prefix(Ventilation __instance, ref float ___potassiumAbsorberUse, ref float ___oxygenRegenerationLimit, Parameter ___regenerationLimit)
        {
            if (!WarthogAir.Electrolysis || !Util.IsPlayers(__instance)) return;
            ___potassiumAbsorberUse = 0f;                                   // absorber is never consumed
            if (___regenerationLimit != null) ___oxygenRegenerationLimit = ___regenerationLimit.Value; // air quality stays 100 %
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogAnchor.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

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
                float ground = UboatToolkit.Modules.Depths.GroundWorldY(h.x, h.z);   // 0.4.74: one owner for ground values
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

}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogFireControl.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Fire-control room of U-1337: 4 stations built into the room (WarthogBuilder.BuildFireControlRoom):
    //   "Fire Control 105 Fore"   -> the two forward 10.5 cm turrets
    //   "Fire Control 105 Aft"    -> the two aft 10.5 cm turrets
    //   "Fire Control Quad Flak"  -> the two 37 mm quad mounts on the sponsons
    //   "Fire Control Tower Flak" -> the two MG C30 on the tower
    // With "WhRemoteGuns" the guns are worked from here (no man at the gun). With "WhFireControlCrew" a gun group is only
    // ready when a crew member works at its station; otherwise the remote guns are always ready.
    public class WarthogFireControl : ModuleBase
    {
        internal static readonly string[] Stations = { "Fire Control 105 Fore", "Fire Control 105 Aft", "Fire Control Quad Flak", "Fire Control Tower Flak" };
        internal static readonly string[] Labels = { "10,5 cm vorn", "10,5 cm achtern", "Vierlingsflak", "Turmflak" };
        internal static readonly Dictionary<string, Interactable> Found = new Dictionary<string, Interactable>();
        private static object cachedFor;
        internal static string Status = "";

        private static void Collect(PlayerShip ship)
        {
            if (cachedFor == (object)ship) return;
            cachedFor = ship; Found.Clear();
            if (ship == null || ship.Interior == null) return;
            foreach (var i in ((Component)ship.Interior).GetComponentsInChildren<Interactable>(true))
                if (Stations.Contains(i.name) && !Found.ContainsKey(i.name)) Found[i.name] = i;
            Util.Log("[Warthog] fire control: " + Found.Count + " of 4 stations found");
        }

        // which station works this gun
        internal static int GroupOf(Gun g, PlayerShip ship)
        {
            if (g == null || ship == null) return -1;
            float cal = g.Calibre.Value;
            if (g.name.Contains("MG C30") || (cal > 0f && cal < 30f)) return 3;
            if (WarthogWeapons.IsFlak(g)) return 2;
            float z = ship.transform.InverseTransformPoint(g.transform.position).z;
            return z >= 0f ? 0 : 1;
        }

        internal static bool Manned(int group)
        {
            if (group < 0 || group >= Stations.Length) return true;
            if (!Found.TryGetValue(Stations[group], out var st) || st == null) return true;   // no station built -> never block
            return Util.SafeGet(() => st.ReservingCharacters.Count > 0, true);
        }

        // gun may be worked remotely right now
        internal static bool Ready(Gun g)
        {
            var ship = Util.PlayerShip;
            if (!Settings.B("WhFireControlCrew")) return true;
            Collect(ship);
            return Manned(GroupOf(g, ship));
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect(ship);
            var parts = new List<string>();
            for (int i = 0; i < Stations.Length; i++)
                parts.Add(Labels[i] + ": " + (!Found.ContainsKey(Stations[i]) ? "fehlt" : Manned(i) ? (Settings.B("WhFireControlCrew") ? "besetzt" : "bereit") : "unbesetzt"));
            Status = string.Join("  |  ", parts);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogHatches.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Entities.NavMesh;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;
    using global::UnityEngine.AI;

    // Hatches ("Luken") of U-1337 as one system instead of the game's single exterior hatch:
    //   Turmluk 1          the game's own exterior hatch (PlayerShip.ExteriorHatch)
    //   Turmluk 2          second hatch on the second tower drum, own ladders + flooding hole (built in)
    //   Seitentür Bb/Stb   doors deck <-> tower, walk-through links + flooding hole each (built in)
    //   Kombüsenluk        the game's cargo-loading hatch above the galley (if the boat has one as a hatch)
    //   Torpedoluk vorn/achtern  torpedo-loading hatches of the torpedo rooms (their leak point = flooding when open)
    // Each hatch has its own state. "Luken einzeln steuern" off: all follow Turmluk 1 (opened/closed together).
    // Diving / alarm: whenever the game wants the tower hatch shut (ShouldExteriorHatchBeClosed) all others are shut too,
    // so the game's dive checks (which look at the tower hatch) never dive with another hatch open; fresh air comes in
    // through every open hatch.
    // Open hatch under water = water into ITS room (own flooding hole). "Alle Luken benutzbar" off = game's single hatch.
    public class WarthogHatches : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhHatchAll");
        internal static bool Single => Settings.B("WhHatchSingle");

        internal class Luke
        {
            public string Name; public Hatch Hatch; public List<OffMeshLink> Links = new List<OffMeshLink>();
            public List<ExternalPressure> Holes = new List<ExternalPressure>(); public bool Open; public bool Manual;
            public bool IsMain;
            public bool IsOpen => Hatch != null ? !Hatch.Locked : Open;
        }
        internal static readonly List<Luke> All = new List<Luke>();
        private static object cachedFor;
        private bool lastOn = true;
        private object debounceShip; private bool rawMain, stableMain; private float rawSince;

        private static void Collect(PlayerShip ship)
        {
            if (cachedFor == (object)ship) return;
            cachedFor = ship; All.Clear();
            if (ship == null) return;
            var interior = ship.Interior != null ? ((Component)ship.Interior).transform : null;
            var main = ship.ExteriorHatch;
            All.Add(new Luke { Name = "Turmluk 1", Hatch = main, IsMain = true });
            var l2 = new Luke { Name = "Turmluk 2" };
            foreach (var h in ship.GetComponentsInChildren<Hatch>(true)) if (h.name == "Exterior Hatch 2") l2.Hatch = h;
            if (interior != null)
            {
                foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.Contains("(Hatch 2)")) l2.Links.Add(l);
                foreach (var side in new[] { ("Port", "Seitentür Bb"), ("Starboard", "Seitentür Stb") })
                {
                    var d = new Luke { Name = side.Item2 };
                    foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.StartsWith("Side Door " + side.Item1)) d.Links.Add(l);
                    foreach (var p in interior.GetComponentsInChildren<ExternalPressure>(true)) if (p.transform.parent != null && p.transform.parent.name == "Side Door " + side.Item1) d.Holes.Add(p);
                    if (d.Links.Count > 0 || d.Holes.Count > 0) All.Add(d);
                }
                // torpedo-loading hatches (leak points of the torpedo rooms)
                foreach (var room in new[] { ("Bow Torpedo Room", "Torpedoluk vorn", "(Torpedo Hatch Fore)"), ("Stern Torpedo Room", "Torpedoluk achtern", "(Torpedo Hatch Aft)") })
                {
                    var r = interior.Find(room.Item1); if (r == null) continue;
                    var th = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Torpedo Loading Hatch");
                    if (th == null) continue;
                    var t = new Luke { Name = room.Item2 };
                    t.Holes.AddRange(th.GetComponentsInChildren<ExternalPressure>(true));
                    // 0.3.6: ladder room <-> deck through this hatch (built into the bundle, off while the hatch is shut)
                    foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.EndsWith(room.Item3)) t.Links.Add(l);
                    All.Add(t);
                }
            }
            if (l2.Hatch != null) All.Insert(1, l2);
            var galley = Traverse.Create(ship).Field("cargoLoadingHatch").GetValue<Hatch>();
            if (galley != null && galley != main)
            {
                var g = new Luke { Name = "Kombüsenluk", Hatch = galley };
                if (interior != null) foreach (var l in interior.GetComponentsInChildren<OffMeshLink>(true)) if (l.name.EndsWith("(Galley)")) g.Links.Add(l);
                All.Add(g);
            }
            Util.Log("[Warthog] hatch: " + All.Count + " hatches: " + string.Join(", ", All.Select(x => x.Name)));
        }

        internal static bool AnyOpen(bool exceptMain)
        {
            foreach (var l in All) if ((!exceptMain || !l.IsMain) && l.IsOpen) return true;
            return false;
        }

        // F10 list: open / close one hatch (only with "Luken einzeln steuern")
        internal static void Set(Luke l, bool open)
        {
            l.Manual = true; l.Open = open;
            if (l.Hatch != null && l.Hatch.Locked == open) l.Hatch.Locked = !open;
            Util.Log("[Warthog] hatch: " + l.Name + (open ? " opened" : " closed") + " (F10)");
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship)) return;
            Collect(ship);
            bool on = On;
            if (on != lastOn) { Util.Log("[Warthog] hatch: all hatches " + (on ? "ON" : "OFF (game: single hatch)")); lastOn = on; }
            var main = ship.ExteriorHatch;
            bool mustClose = Util.SafeGet(() => ship.ShouldExteriorHatchBeClosed, false);
            bool rawOpen = main != null && !main.Locked;
            // 0.3.1: the other hatches follow Turmluk 1 only once it has stayed open/closed for 1.5 s (the game opens and
            // shuts the tower hatch for a moment e.g. after switching boats -> all hatches flapped). Diving/alarm
            // (mustClose below) still closes everything at once.
            if ((object)ship != debounceShip) { debounceShip = ship; rawMain = rawOpen; stableMain = rawOpen; rawSince = Time.unscaledTime; }   // new boat: start from its current state
            if (rawOpen != rawMain) { rawMain = rawOpen; rawSince = Time.unscaledTime; }
            if (Time.unscaledTime - rawSince >= 1.5f) stableMain = rawOpen;
            bool mainOpen = stableMain;
            foreach (var l in All)
            {
                if (l.IsMain) continue;
                bool want;
                if (!on) want = false;
                else if (mustClose) { want = false; l.Manual = false; }
                else if (Single) want = l.Manual ? l.Open : mainOpen;
                else want = mainOpen;
                if (l.Hatch != null) { if (l.Hatch.Locked == want) { l.Hatch.Locked = !want; Util.Log("[Warthog] hatch: " + l.Name + (want ? " opened" : " closed") + (mustClose ? " (diving/alarm)" : "")); } }
                else if (l.Open != want) { l.Open = want; Util.Log("[Warthog] hatch: " + l.Name + (want ? " opened" : " closed") + (mustClose ? " (diving/alarm)" : "")); }
                foreach (var k in l.Links) if (k != null && k.activated != (on && want)) k.activated = on && want;
                // flooding: an open hatch lets water into its own room when the deck is under water
                float hole = want ? (l.Hatch != null ? l.Hatch.Openess : 1f) : 0f;
                foreach (var p in l.Holes) if (p != null && p.Openess != hole) p.Openess = hole;
            }
        }
    }

    // fresh air through every open hatch (game: only the tower hatch adds oxygen)
    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class WarthogHatches_AirPatch
    {
        private static readonly AccessTools.FieldRef<PlayerShip, UBOAT.Game.Core.Data.Modifier> Oxy =
            AccessTools.FieldRefAccess<PlayerShip, UBOAT.Game.Core.Data.Modifier>("oxygenExternalHatchModifier");
        private static void Postfix(PlayerShip __instance)
        {
            if (!WarthogHatches.On || !WarthogSettings.IsWarthog(__instance)) return;
            int open = 0;
            foreach (var l in WarthogHatches.All) if (!l.IsMain && l.IsOpen) open++;
            if (open == 0) return;
            var m = Oxy(__instance);
            if (m != null && Util.SafeGet(() => __instance.DeckDepth < 0.3f, false)) m.Value = 0.0005f * (1 + open);
        }
    }

    // test log: which way does the crew take
    [HarmonyPatch(typeof(NavMeshLadder), nameof(NavMeshLadder.TraverseStart))]
    internal static class WarthogHatches_LadderLog
    {
        private static void Postfix(NavMeshLadder __instance, UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator navigator)
        {
            if (!WarthogSettings.IsWarthog(Util.PlayerShip)) return;
            string n = __instance.name;
            if (!n.Contains("Observation")) return;   // only the bridge <-> tower ladders (the two hatches)
            Util.Log("[Warthog] hatch: " + (navigator != null ? navigator.name : "?") + " uses " + (n.Contains("(Hatch 2)") ? "HATCH 2" : "hatch 1") + " (" + n + ")");
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogHull.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Linq;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // Hull + propulsion of U-1337:
    //  - rubber coating: enemy active sonar and hydrophones find us harder (slider)
    //  - fins: extra turning force with the rudder, extra roll damping, stronger diving planes
    //  - bow + stern thrusters: turn on the spot (keys) or help the rudder; use battery;
    //    keep the heading while the deck guns fire (counter the recoil)
    // (Astern speed = ahead speed is built into the boat itself: 4 propellers, see the Unity build.)
    public class WarthogHull : ModuleBase
    {
        const string Mod = "Warthog";

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool on = WarthogSettings.Active(ship);
            Scale(ship.SonarDetectability, on ? Settings.F("WhRubberCoating") : 1f, "Warthog Coating");
            Scale(ship.HydrophoneDetectability, on ? Settings.F("WhHydroNoise") : 1f, "Warthog Coating");
            Scale(ship.VerticalSteersPerformance, on && Settings.B("WhFins") ? Settings.F("WhFinDepth") : 1f, "Warthog Fins");
            if (on && WarthogSettings.IsWarthog(ship)) MirrorBadge(ship);
            Owners.AddonSpeed = on && WarthogSettings.IsWarthog(ship) && !Util.WarthogModOwns(ship) ? Settings.F("WhSpeedFactor") : 1f;   // 0.4.29: one writer (Navigation)
            if (WarthogSettings.IsWarthog(ship)) DiveSpeed(ship, on ? Settings.F("WhDiveSpeed") : 0f);
            if (on && WarthogSettings.IsWarthog(ship)) CrushLimit(ship, Settings.F("WhCrushDepth"));
            // berth position (used by the game at the next docking): forward shift + side shift away from the pier
            if (on && WarthogSettings.IsWarthog(ship))
            {
                var want = new Vector3(0.1f + Settings.F("WhDockSide"), 0.6f, Settings.F("WhDockShift"));
                var cur = Util.SafeGet(() => HarmonyLib.Traverse.Create(ship).Field("dockOffset").GetValue<Vector3>(), want);
                if ((cur - want).sqrMagnitude > 0.0001f) { HarmonyLib.Traverse.Create(ship).Field("dockOffset").SetValue(want); Util.Log("[Warthog] dock: berth offset " + want); }
            }
        }

        // speed factor (0.3.6, DoMe: optional, default 1.0): the game's propellers push with power * powerMultiplier *
        // maxPower; top speed grows about with the square root of the thrust -> maxPower x factor^2 = speed x factor.
        // Propellers are cached per boat (no component search every tick); plain reflection, no generic field refs.
        private static readonly System.Reflection.FieldInfo MaxPowerF = HarmonyLib.AccessTools.Field(typeof(UBOAT.Game.Scene.Items.Propeller), "maxPower");
        private readonly System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Items.Propeller, float> basePower = new System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Items.Propeller, float>();
        private UBOAT.Game.Scene.Items.Propeller[] speedProps;
        private object speedShip;
        private float lastFactor = -1f;
        private void SpeedFactor(PlayerShip ship, float f)
        {
            if (MaxPowerF == null) return;
            f = Mathf.Clamp(f, 0.5f, 2f);
            if (speedProps == null || !ReferenceEquals(speedShip, ship)) { speedProps = ship.GetComponentsInChildren<UBOAT.Game.Scene.Items.Propeller>(true); speedShip = ship; basePower.Clear(); lastFactor = -1f; }
            int n = 0;
            foreach (var p in speedProps)
            {
                if (p == null) continue;
                if (!basePower.TryGetValue(p, out var b)) { b = (float)MaxPowerF.GetValue(p); basePower[p] = b; }
                float want = b * f * f;
                if ((float)MaxPowerF.GetValue(p) != want) { MaxPowerF.SetValue(p, want); n++; }
            }
            if (n > 0 && f != lastFactor) Util.Log("[Warthog] speed factor " + f.ToString("0.00") + " -> " + n + " propeller(s), thrust x" + (f * f).ToString("0.00"));
            lastFactor = f;
        }

        // 0.3.9 dive / rise speed (DoMe: "far too slow, the curve flattens at ~97 m"). Game (PlayableSubmarine): with the
        // ballast flooded the boat is about neutral and the DIVING PLANES make the depth change, with a FIXED force:
        //   F = rudder * (3.6 + |v| * 1.15) * balance * depthFalloff * 10 * VerticalSteersForceScale
        //   depthFalloff = 0.2 + 0.8 * exp(-(deckDepth - 12) * 0.02)  -> 1.0 at 12 m, 0.35 at 97 m, 0.2 below ~400 m
        // Not scaled with mass: our 1826 t boat (IXC 866 t) gets half the acceleration, and below 100 m a third or less.
        // Here: force x (mass ratio) x (1 / depthFalloff) x slider. Near the ordered depth (under 30 m away) the extra
        // slider part fades out, so the depth keeping does not overshoot. Force only (VerticalSteersForceScale) - the pitch
        // torque stays the game's, the boat keeps its normal dive angle.
        private float lastDive = -1f;
        private void DiveSpeed(PlayerShip ship, float slider)
        {
            var p = Util.SafeGet(() => ship.VerticalSteersForceScale, null);
            if (p == null) return;
            float f = 1f;
            if (slider > 0f && ship.RigidBody != null)
            {
                float massK = Mathf.Clamp(ship.RigidBody.mass / 866000f, 1f, 4f);
                float deck = Util.SafeGet(() => ship.DeckDepth, 0f);
                float falloff = 0.2f + 0.8f * Mathf.Exp(Mathf.Min(0f, (deck - 12f) * -0.02f));
                float err = Util.SafeGet(() => Mathf.Abs(ship.TargetDepth - ship.DeckDepthUnclamped), 100f);
                float near = Mathf.Clamp01((err - 8f) / 22f);   // 0 within 8 m of the ordered depth, 1 beyond 30 m
                float s = Mathf.Lerp(1f, Mathf.Clamp(slider, 0.25f, 10f), near);
                f = Mathf.Clamp(massK / falloff * s, 0.25f, 60f);
            }
            Scale(p, f, "Warthog Dive");
            if (Mathf.Abs(f - lastDive) > 0.5f * Mathf.Max(1f, lastDive * 0.25f)) { Util.Log("[Warthog] dive: diving-plane force x" + f.ToString("0.0") + " (slider " + slider.ToString("0.00") + ", deck depth " + Util.SafeGet(() => ship.DeckDepth, 0f).ToString("0") + " m)"); lastDive = f; }
        }

        // 0.3.9 crush depth (game: HullCrushController implodes the boat after a timeout once it is below the entity
        // parameter "CrushDepth"; the sheet value of Type XXX is -3000). Slider WhCrushDepth scales it, and the ORDERED
        // depth is clamped to 100 m above it, so the player can never order a depth that kills the boat silently.
        private static readonly System.Reflection.FieldInfo TargetDepthF = HarmonyLib.AccessTools.Field(typeof(PlayableSubmarine), "targetDepth");
        private float sheetCrush = float.NaN; private float lastClampLog = -99f;
        private void CrushLimit(PlayerShip ship, float wantMeters)
        {
            var p = Util.SafeGet(() => ship.SandboxEntity.Parameters.GetParameter("CrushDepth"), null);
            if (p == null) return;
            if (float.IsNaN(sheetCrush))
            {
                float mod = Util.SafeGet(() => Util.OneScale(p, "Warthog Crush Depth").Value, 1f);
                // 0.4.29 (contradiction audit): "Tieftauchen" (x100 on the same value) was taken as the sheet value
                float deep = Util.SafeGet(() => Util.OneScale(p, Util.ModifierName).Value, 1f);
                float raw = Mathf.Abs(Util.SafeGet(() => (float)p, 0f)) / Mathf.Max(0.0001f, mod) / Mathf.Max(0.0001f, deep);
                Util.Log("[Warthog] Zerstörungstiefe: Blattwert " + raw.ToString("0") + " m (ohne Warthog-Faktor " + mod.ToString("0.##") + " und ohne Tieftauch-Faktor " + deep.ToString("0.##") + ")");
                sheetCrush = raw > 1f ? raw : 3000f;
            }
            Scale(p, Mathf.Max(100f, wantMeters) / sheetCrush, "Warthog Crush Depth");
            float crush = Mathf.Abs(Util.SafeGet(() => (float)p, -wantMeters));
            float limit = crush - 100f;
            float ordered = Util.SafeGet(() => ship.TargetDepth, 0f);
            if (TargetDepthF != null && ordered > limit)
            {
                float td = (float)TargetDepthF.GetValue(ship);
                TargetDepthF.SetValue(ship, td + (ordered - limit));   // TargetDepth = -targetDepth + offset
                if (Time.unscaledTime - lastClampLog > 10f) { lastClampLog = Time.unscaledTime; Util.Log("[Warthog] depth: ordered " + ordered.ToString("0") + " m is below the crush depth limit, set to " + limit.ToString("0") + " m (crush " + crush.ToString("0") + " m, F10 > Warthog > Zerstörungstiefe)"); }
            }
        }

        static void Scale(Parameter p, float f, string name)
        {
            if (p == null) return;
            var m = Util.OneScale(p, name);
            float v = Mathf.Max(0.0001f, f);      // 0 would make the boat "undefined"; tiny = practically invisible
            if (m.Value != v) m.Value = v;
        }

        // 0.4.78: the thrusters moved to the Toolkit core (DoMe: they stay on all boats). Left here: the visible
        // propellers and the fins of the Warthog boat.
        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null || Util.WarthogModOwns(ship) || !WarthogSettings.IsWarthog(ship)) return;
            var rb = ship.RigidBody;
            if (rb == null || Time.deltaTime <= 0f || Time.timeScale <= 0f) return;
            float k = Mathf.Clamp(Time.deltaTime / Time.fixedDeltaTime, 0f, 4f);
            SpinProps(ship, rb);

            // ---- fins
            // 0.3.41: fins push per frame; in time compression that over-corrects the roll and the boat rolls over (DoMe)
            // 0.3.42 (DoMe): fins exist only on the Warthog, never on a vanilla boat
            if (Settings.B("WhFins") && Time.timeScale <= 1.5f && WarthogSettings.IsWarthog(ship))
            {
                float speed = Vector3.Dot(rb.velocity, ship.transform.forward);
                // more turning: the game turns the boat with the rudder; add (factor-1) of a rudder-like torque
                // game convention (Ship.FixedUpdate): Turn < 0 = turn to STARBOARD (positive yaw). 0.2.x used +Turn and
                // pushed against the rudder -> "unsteerable". Yaw torque must be -Turn.
                float turn = -Thrusters.EffTurn(ship); // -1..1, + = starboard
                float extra = (Settings.F("WhFinTurn") - 1f) * turn * Mathf.Clamp(Mathf.Abs(speed) / 6f, 0f, 1.5f) * Mathf.Sign(speed == 0 ? 1 : speed);
                if (Mathf.Abs(extra) > 0.001f) rb.AddTorque(ship.transform.up * extra * rb.inertiaTensor.y * 0.002f * k, ForceMode.Force);
                // less roll: damp the roll part of the angular velocity
                Vector3 local = ship.transform.InverseTransformDirection(rb.angularVelocity);
                float damp = (Settings.F("WhFinRoll") - 1f) * 0.6f;
                if (damp > 0f) rb.AddTorque(ship.transform.forward * (-local.z * damp) * rb.inertiaTensor.z * k, ForceMode.Force);
            }
        }

        // Our 4 visible propellers (WH_Prop_* hubs + WH_Blade_* blades in the hull model) are static meshes; the game's
        // working propellers are the IXC ones hidden inside the hull. Spin ours with the boat's speed through the water,
        // counter-rotating like real twin screws: starboard clockwise, port anticlockwise (seen from astern, going ahead).
        private readonly System.Collections.Generic.List<(Transform hub, float sense)> props = new System.Collections.Generic.List<(Transform, float)>();
        private object propsFor;
        private void SpinProps(PlayerShip ship, Rigidbody rb)
        {
            if (propsFor != (object)ship)
            {
                propsFor = ship; props.Clear();
                var all = ship.GetComponentsInChildren<Transform>(true);
                foreach (var hub in all.Where(t => t.name.StartsWith("WH_Prop_")))
                {
                    string key = hub.name.Substring("WH_Prop_".Length);          // e.g. "Aft_S"
                    foreach (var b in all.Where(t => t.name.StartsWith("WH_Blade_" + key)).ToList()) b.SetParent(hub, true);
                    props.Add((hub, key.EndsWith("_S") ? 1f : -1f));
                }
                Util.Log("[Warthog] propellers: " + props.Count + " visible propellers will turn");
            }
            if (props.Count == 0) return;
            float speed = Vector3.Dot(rb.velocity, ship.transform.forward);   // m/s, negative = astern
            float rpm = Mathf.Clamp(speed * 25f, -260f, 260f);                // ~250 rpm at full speed, like the type IX
            float deg = rpm * 6f * Time.deltaTime;                             // rpm -> degrees per frame
            foreach (var p in props) if (p.hub != null) p.hub.Rotate(ship.transform.forward, deg * p.sense, Space.World);
        }

        // emblem on BOTH tower drums: the game shows the chosen badge at the "Badge" slot (first drum); we keep a
        // render-only mirror copy on the second drum (mirrored at the boat's middle, turned 180 degrees)
        private GameObject badgeCopy; private int badgeOf;
        private void MirrorBadge(PlayerShip ship)
        {
            UBOAT.Game.Scene.Entities.ShipEquipmentSlot slot = null;
            foreach (var s in ship.GetComponentsInChildren<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>(true)) if (s.name == "Badge") { slot = s; break; }
            // the badge the game placed in the slot (its Item), not just the slot's last child (that can be e.g. "Banner Attachment Point")
            var item = slot != null && slot.Item != null ? slot.Item.gameObject : null;
            int id = item != null ? item.GetInstanceID() : 0;
            if (id == badgeOf && (badgeCopy != null) == (item != null)) return;
            if (badgeCopy != null) Object.Destroy(badgeCopy);
            badgeCopy = null; badgeOf = id;
            if (item == null) return;
            badgeCopy = new GameObject("Warthog Badge (second drum)");
            badgeCopy.transform.SetParent(ship.transform, false);
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject(r.name); go.layer = r.gameObject.layer;
                go.transform.SetParent(badgeCopy.transform, false);
                Vector3 lp = ship.transform.InverseTransformPoint(r.transform.position);
                go.transform.localPosition = new Vector3(lp.x, lp.y, -lp.z);
                go.transform.localRotation = Quaternion.Euler(0, 180f, 0) * (Quaternion.Inverse(ship.transform.rotation) * r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            }
            Util.Log("[Warthog] badge: '" + item.name + "' mirrored onto the second tower drum");
        }

    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogNetCutter.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Net saw: the serrated stem at bow and stern (WH_NetSaw_Bow / WH_NetSaw_Stern in the hull) cuts anti-submarine nets.
    //  - nets near the boat: their colliders ignore ours in advance, so the boat is never stopped or slowed by them
    //  - as soon as a saw end reaches a net segment (the game builds nets from 110 m pieces) the piece is cut out
    //    (deactivated) -> a real gap that stays open while the net is loaded; after loading a save the game rebuilds
    //    the net and the saw cuts it again on contact
    //  - the game's "Anti-submarine Net Collision" warning is suppressed while the saw is on
    public class WarthogNetCutter : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhNetSaw");
        private static readonly HashSet<int> ignored = new HashSet<int>();
        private float scanAt;
        private AntiSubmarineNetPart[] parts = new AntiSubmarineNetPart[0];

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !On) return;
            // scene-wide search ~40 ms ("Langsam: WarthogNetCutter.Tick 45 ms"): every 15 s is enough, nets do not move
            if (Time.unscaledTime - scanAt > 15f) { parts = Object.FindObjectsOfType<AntiSubmarineNetPart>(); scanAt = Time.unscaledTime; }
            if (parts.Length == 0) return;
            Collider[] shipCols = null;   // all colliders of the 92 m boat: fetched only when a new net piece comes near
            foreach (var p in parts)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                var cols = p.GetComponentsInChildren<Collider>(true);
                if (cols.Length == 0) continue;
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                float dist = Mathf.Sqrt(b.SqrDistance(ship.transform.position));
                if (dist > 250f) continue;
                if (ignored.Add(p.GetInstanceID()))
                {
                    if (shipCols == null) shipCols = ship.GetComponentsInChildren<Collider>(true);
                    foreach (var c in cols) foreach (var s in shipCols) if (c && s) Physics.IgnoreCollision(c, s, true);
                    Util.Log("[Warthog] net: " + p.name + " ahead (" + dist.ToString("0") + " m) - collisions off, saw ready");
                }
                if (SawTouches(ship, b)) Cut(p, "saw contact");
            }
        }

        public override void Frame()
        {
            // between ticks: fast boats must not slip through a whole 110 m piece without cutting it
            var ship = Util.PlayerShip;
            if (ship == null || !On || parts.Length == 0 || Time.frameCount % 5 != 0) return;
            foreach (var p in parts)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !ignored.Contains(p.GetInstanceID())) continue;
                var cols = p.GetComponentsInChildren<Collider>(false);
                if (cols.Length == 0) continue;
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                if (SawTouches(ship, b)) Cut(p, "saw contact");
            }
        }

        private static bool SawTouches(PlayerShip ship, Bounds net)
        {
            var big = net; big.Expand(4f);
            foreach (var end in new[] { 46f, -46f })
            {
                Vector3 tip = ship.transform.TransformPoint(new Vector3(0f, 0f, end));
                Vector3 mid = ship.transform.TransformPoint(new Vector3(0f, 0f, end * 0.9f));
                if (big.Contains(tip) || big.Contains(mid)) return true;
            }
            return false;
        }

        internal static void Cut(AntiSubmarineNetPart p, string why)
        {
            if (p == null || !p.gameObject.activeSelf) return;
            p.gameObject.SetActive(false);
            Util.Log("[Warthog] net: cut " + p.name + " at " + p.transform.position.ToString("F0") + " (" + why + ")");
            ToolkitRunner.ShowNotice("Netzsäge: Netz durchtrennt", 4f);
        }
    }

    // contact before our pre-scan noticed the net: cut it, no warning, no stop
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.OnCollidedWithAntiSubmarineNet))]
    internal static class WarthogNetCutter_CollisionPatch
    {
        private static bool Prefix(PlayerShip __instance, AntiSubmarineNetPart antiSubmarineNetPart)
        {
            if (!WarthogNetCutter.On || !WarthogSettings.IsWarthog(__instance)) return true;
            WarthogNetCutter.Cut(antiSubmarineNetPart, "collision");
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogPower.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UBOAT.Game.Core.Data;
    using global::UnityEngine;

    // Power menu:
    //   [x] Alle Verbraucher generieren Strom
    //         Lüftung        verbraucht / kostenlos / erzeugt
    //         (every device of the boat that uses battery, one line each)
    // How: every game device that uses power keeps a private Modifier on the boat's "Energy" gain
    // (energyModifier / energyGainModifier / energyUseModifier). The game writes a negative value there
    // (= use). We remember the game's value and replace it by 0 (free) or +|value| (generate).
    public class WarthogPower : ModuleBase
    {
        // device type name -> label in the menu
        internal static readonly (string type, string label)[] Devices =
        {
            ("Ventilation", "Lüftung"),
            ("TrimPump", "Trimm-/Lenzpumpe (Zentrale)"),
            ("AirCompressor", "Kompressor"),
            ("Gyrocompass", "Kreiselkompass"),
            ("DivingPlanesStation", "Tiefenruder"),
            ("PlayerShipEngine", "E-Maschinen (Fahrstrom)"),
            ("WarthogThrusters", "Strahlruder"),
            ("WarthogPumps", "Lenzpumpen pro Abteil"),
            ("WarthogSonar", "Sonar-Anlagen"),
        };

        public static void RegisterOptions(Action<string, string, float, float, float, float> num)
        {
            foreach (var d in Devices)
                num("WhPwr_" + d.type, "      " + d.label + " (0 = verbraucht, 1 = kostenlos, 2 = erzeugt)", 0, 0, 2, 1);
        }

        // 0 consume, 1 free, 2 generate (the top switch forces 2 for everything)
        internal static int Mode(string type)
        {
            if (Util.PlayerShip == null) return 0;   // 0.3.61: power switches work on every own boat (were dead on non-Warthog boats)
            if (Settings.B("WhAllGenerate")) return 2;
            return Mathf.RoundToInt(Settings.F("WhPwr_" + type));
        }

        // apply a mode to a "use" value (positive number = power used per second)
        internal static float Apply(string type, float use)
        {
            switch (Mode(type)) { case 1: return 0f; case 2: return -Mathf.Abs(use); default: return use; }
        }

        private class Slot { public Modifier M; public float Game; public float Written = float.NaN; }
        private readonly Dictionary<Modifier, Slot> slots = new Dictionary<Modifier, Slot>();
        private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        private float nextLog;
        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            // 0.4.6: proof line once per minute: per device the game's value and what is written now (minus = use, plus = gain)
            bool log = Time.unscaledTime >= nextLog;
            System.Text.StringBuilder sb = null;
            if (log) { nextLog = Time.unscaledTime + 60f; sb = new System.Text.StringBuilder("Strom (eigenes Boot):"); }
            foreach (var d in Devices)
            {
                if (d.type.StartsWith("Warthog")) continue; // own consumers apply the mode themselves
                int mode = Mode(d.type);
                int n = 0, chargers = 0; float game = 0f, now = 0f;
                var comps = DevicesOfType(d.type);
                foreach (var comp in comps)
                    foreach (var f in EnergyFields(comp.GetType()))
                    {
                        var m = f.GetValue(comp) as Modifier;
                        if (m == null) continue;
                        if (!slots.TryGetValue(m, out var s)) { s = new Slot { M = m, Game = m.Value }; slots[m] = s; }
                        // the game changed the value since we wrote -> that is the new game value
                        if (float.IsNaN(s.Written) || m.Value != s.Written) s.Game = m.Value;
                        // 0.4.59 (DoMe 2026-11-18 19:29, tooltip "Dieselmotoren -1,4/km" on the battery): a diesel that charges
                        // the battery has a NEGATIVE game value here too (its usage parameter is negative, minus x minus = gain).
                        // "erzeugt" turned that value positive, so the diesels drained the battery. An engine that can charge
                        // the battery is left exactly as the game sets it.
                        bool charger = Charges(comp);
                        if (charger && log) chargers++;
                        float want = charger ? s.Game : mode == 1 ? 0f : mode == 2 ? Mathf.Abs(s.Game) : s.Game;
                        if (m.Value != want) m.Value = want;
                        s.Written = want;
                        if (log) { n++; game += s.Game; now += want; }
                    }
                if (log) sb.Append(" ").Append(d.label).Append(": ").Append(comps.Length).Append(" Geräte, ").Append(n).Append(" aktive Werte, Spiel ").Append(game.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" -> jetzt ").Append(now.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" (Modus ").Append(mode).Append(chargers > 0 ? ", davon " + chargers + " ladender Diesel unverändert" : "").Append(");");
            }
            if (log)
            {
                // total of the boat's battery: what the game really adds (+) or takes (-) per second right now
                try { var en = ship.GetResource("Energy"); if (en != null) sb.Append(" Batterie gesamt jetzt ").Append(en.Gain.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(" pro Sekunde, Teile").Append(PartsOf(en.Gain)); } catch { }
                Util.Log(sb.ToString());
            }
        }

        private static readonly FieldInfo ChargeField = HarmonyLib.AccessTools.Field(typeof(UBOAT.Game.Scene.Items.PlayerShipEngine), "canChargeBatteries");
        private static bool Charges(object comp)
        {
            if (ChargeField == null || !(comp is UBOAT.Game.Scene.Items.PlayerShipEngine)) return false;
            try { return (bool)ChargeField.GetValue(comp); } catch { return false; }
        }

        private static string PartsOf(Parameter p)
        {
            var sb = new System.Text.StringBuilder();
            int k = 0;
            foreach (var m in p.DeltaModifiers) { if (m == null || m.Value == 0f) continue; if (k++ >= 25) break; sb.Append(" ").Append(m.Name).Append("=").Append(m.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }
        private static readonly Dictionary<string, Type> typeByName = new Dictionary<string, Type>();
        private static Component[] DevicesOfType(string name)
        {
            if (!typeByName.TryGetValue(name, out var t))
            {
                t = typeof(UBOAT.Game.Scene.Items.Ventilation).Assembly.GetType("UBOAT.Game.Scene.Items." + name);
                typeByName[name] = t;
            }
            if (t == null) return new Component[0];
            var ship = Util.PlayerShip;
            // searching the boat every frame is slow: keep the list 5 s (or until the boat changes)
            if (cacheShip != ship) { compCache.Clear(); cacheShip = ship; }
            if (compCache.TryGetValue(name, out var c) && Time.unscaledTime - c.at < 5f) return c.list;
            var list = Util.FindByType(t);   // 0.4.6: was the boat only; these devices sit in the interior scene, so nothing was found
            compCache[name] = (list, Time.unscaledTime);
            return list;
        }
        private static object cacheShip;
        private static readonly Dictionary<string, (Component[] list, float at)> compCache = new Dictionary<string, (Component[] list, float at)>();

        private static FieldInfo[] EnergyFields(Type t)
        {
            if (fieldCache.TryGetValue(t, out var fs)) return fs;
            var list = new List<FieldInfo>();
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(Modifier) && f.Name.IndexOf("energy", StringComparison.OrdinalIgnoreCase) >= 0 && f.Name != "energyLinkModifier")
                        list.Add(f);
            return fieldCache[t] = list.ToArray();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogPumps.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // One bilge pump per compartment, each with a float switch.
    // The game has only ONE pump (TrimPump, control room) for the whole boat, and it gets weaker with depth.
    // U-1337 has a pump in every compartment: as soon as the water in a compartment passes the float level,
    // that compartment's pump runs by itself and pumps the water overboard until it is dry.
    // Strength: per pump "WhPumpRate" m³/s; with "WhPumpDepthScale" it gets STRONGER with depth.
    // Uses battery while running. Does not touch the game's TrimPump (Toolkit "Pump" keeps working).
    public class WarthogPumps : ModuleBase
    {
        internal static readonly bool[] Running = new bool[32];
        internal static int RunningCount;
        private Modifier energy;
        private static UBOAT.Game.Core.Time.GameTime gameTime;

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            bool on = WarthogSettings.Active(ship) && Settings.B("WhPumps");
            var comps = ship.Interior != null ? ship.Interior.Compartments : null;
            RunningCount = 0;
            if (on && comps != null)
            {
                if (gameTime == null) gameTime = Traverse.Create(typeof(TrimPump)).Field("gameTime").GetValue<UBOAT.Game.Core.Time.GameTime>();
                float timeScale = gameTime != null ? gameTime.ArtificialTimeScale : 1f;
                float dt = Time.deltaTime * timeScale;
                float start = Settings.F("WhPumpStart");
                float rate = Settings.F("WhPumpRate");
                // stronger with depth: x1 at the surface, x2 at 100 m, x3 at 200 m
                float depthK = Settings.B("WhPumpDepthScale") ? 1f + Mathf.Max(0f, ship.DeckDepth) / 100f : 1f;
                for (int i = 0; i < comps.Length && i < Running.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) { Running[i] = false; continue; }
                    // float switch with hysteresis: on above "start", off when (almost) dry
                    if (!Running[i] && c.Water > start) Running[i] = true;
                    else if (Running[i] && c.Water <= 0.001) Running[i] = false;
                    if (!Running[i]) continue;
                    RunningCount++;
                    double take = rate * depthK * dt;
                    if (take > c.Water) take = c.Water;
                    c.Water -= take;
                }
            }
            else for (int i = 0; i < Running.Length; i++) Running[i] = false;

            // battery use of the running pumps
            if (ship.Energy == null) return;
            energy = Util.OneDelta(ship.Energy.Gain, "Warthog Pumps");
            float use = WarthogPower.Apply("WarthogPumps", RunningCount * Settings.F("WhPumpEnergy") * 0.0001f);
            if (energy.Value != -use) energy.Value = -use;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogSettings.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Linq;
    using global::UBOAT.Game.Scene.Entities;

    // All options of the "Warthog" tab (U-1337, class Type XXX).
    public static class WarthogSettings
    {
        public const string Tab = "Warthog";

        static void Bool(string key, string label, bool def) { def = def && key == "WhOnlyWarthog"; BoolRaw(key, label, def); }
        static void BoolRaw(string key, string label, bool def) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        static void Num(string key, string label, float def, float min, float max, float step) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        static void BoatBool(string key, string label, bool def) => Settings.AddOpt(new Opt { Tab = Settings.QoL, Key = key, Label = label, IsBool = true, Default = 0, Value = 0, Min = 0, Max = 1, Step = 1 });
        static void BoatNum(string key, string label, float def, float min, float max, float step) => Settings.AddOpt(new Opt { Tab = Settings.QoL, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        static void Key(string key, string label, string def) => Settings.AddOpt(new Opt { Tab = Settings.KeysTab, Key = key, Label = label, IsKey = true, Text = def });

        public static void Register()
        {
            Bool("WhOnlyWarthog", "Alle Warthog-Funktionen nur auf U-1337 (Type XXX), nicht auf anderen Booten", true);

            // --- Rumpf
            Num("WhRubberCoating", "Gummibeschichtung: Sonar-Ortbarkeit x (1 = ohne, 0 = unsichtbar)", 0.3f, 0f, 1f, 0.05f);
            Num("WhHydroNoise", "Gummibeschichtung: Hydrophon-Ortbarkeit x (1 = normal)", 0.6f, 0f, 1f, 0.05f);
            Bool("WhFins", "Finnen: engere Kurven, weniger Rollen, schneller Tiefe wechseln", true);
            Num("WhFinTurn", "  Kurven: Drehkraft x", 1.6f, 1f, 4f, 0.1f);
            Num("WhFinRoll", "  Rollen: Dämpfung x", 2.5f, 1f, 6f, 0.1f);
            Num("WhFinDepth", "  Tiefenruder-Wirkung x", 1.6f, 1f, 4f, 0.1f);

            // --- Antrieb
            // thrusters work on every boat: they live in the tab "Boot & Antrieb" (DoMe 2026-11-16), not in the Warthog tab
            Num("WhSpeedFactor", "Geschwindigkeit: Faktor auf die Schraubenleistung (1,0 = wie gebaut)", 1f, 0.5f, 2f, 0.05f);
            Num("WhDiveSpeed", "Tauch-/Steiggeschwindigkeit: Faktor auf die Tiefenruder-Kraft (1,0 = wie ein IXC, auch in großer Tiefe)", 4f, 0.25f, 10f, 0.25f);
            Num("WhCrushDepth", "Zerstörungstiefe in m (befohlene Tiefe wird auf 100 m darüber begrenzt)", 3000f, 500f, 6000f, 100f);

            // --- Schnorchel
            Bool("WhSnorkelTelescope", "Teleskop-Schnorchel: tiefer als Sehrohrtiefe", true);
            Num("WhSnorkelExtra", "  zusätzliche Länge in m", 6f, 0f, 15f, 0.5f);
            Bool("WhSnorkelAuto", "  fährt bei Wellen automatisch weiter aus", true);
            Num("WhSnorkelExhaust", "  Abgas unter Wasser: Sichtbarkeit x (0 = unsichtbar)", 0f, 0f, 1f, 0.05f);

            // --- Pumpen
            Bool("WhPumps", "Eine Lenzpumpe pro Abteil, mit Schwimmer (springt selbst an)", true);
            Num("WhPumpStart", "  Schwimmer: an ab x m³ Wasser im Abteil", 0.05f, 0.01f, 2f, 0.01f);
            Num("WhPumpRate", "  Leistung je Pumpe in m³/s (offenes Turmluk getaucht ≈ 0,2)", 0.5f, 0.05f, 5f, 0.05f);
            Bool("WhPumpDepthScale", "  Leistung steigt mit der Tiefe (gegen den Wasserdruck)", true);
            Num("WhPumpEnergy", "  Strom je Pumpe bei voller Leistung (Anteil x 0,0001/s)", 1f, 0f, 10f, 0.5f);

            // --- Luft + Strom
            Bool("WhSilentVent", "Lüftung lautlos", true);
            Bool("WhVentAlwaysOn", "Lüftung immer an, auch bei Schleichfahrt", true);
            Bool("WhElectrolysis", "Sauerstoff per Elektrolyse: keine Kalipatronen mehr", true);
            Bool("WhAllGenerate", "Alle Verbraucher generieren Strom", false);
            WarthogPower.RegisterOptions(Num); // one indented line per device under the top switch

            // --- Sonar
            Bool("WhPassive", "Passives Sonar 360°: immer an, auch ohne Hydrophon", true);
            Bool("WhPassiveSame", "  aufgetaucht und bei hoher Fahrt genauso gut", true);
            Num("WhPassiveRange", "  Reichweite x", 2f, 0.5f, 5f, 0.25f);
            Bool("WhPassiveMap", "  Kontakte auf der Karte zeigen", true);
            Bool("WhPassiveAlarm", "  Alarm bei neuem Kontakt", true);
            Bool("WhSonarBow", "Aktives Sonar Bug (nach vorn + unten)", true);
            Bool("WhSonarMid", "Sonar Mitte (nur passiv)", true);
            Bool("WhSonarStern", "Aktives Sonar Heck (nach hinten + unten)", true);
            Bool("WhPingReveals", "  Ping verrät uns (Gegner hören den Ping)", true);

            // --- Luken
            Bool("WhHatchAll", "Luken: alle Luken benutzbar (2. Turmluk, Seitentüren, Torpedoluken, Kombüse; aus = Spiel mit einem Luk)", true);
            Bool("WhHatchSingle", "  Luken einzeln steuern (Liste oben im Reiter; aus = alle folgen Turmluk 1)", false);

            // --- Hafen
            Num("WhDockShift", "Liegeplatz: Boot x m nach vorn schieben (IXC = 5; 14 = Heck wie beim IXC)", 14, -10, 30, 1);
            Num("WhDockSide", "Liegeplatz: Boot x m seitlich schieben (+ = Steuerbord)", 0, -5, 5, 0.5f);

            // --- Netzsäge + Anker
            Bool("WhNetSaw", "Netzsäge: U-Boot-Netze durchschneiden (Bug + Heck)", true);
            Bool("WhAnchor", "Anker benutzen (Knopf oben im Reiter oder Taste)", true);
            Num("WhAnchorMode", "  welcher Anker: 0 = Bug, 1 = Heck, 2 = beide", 0, 0, 2, 1);
            Num("WhAnchorChain", "  Kettenlänge in m", 150, 20, 400, 10);
            Num("WhAnchorLiftSeconds", "  Anker lichten dauert x Sekunden (0 = sofort)", 30, 0, 120, 5);

            // --- Feuerleitstand
            Bool("WhFireControlCrew", "Feuerleitstand: Geschützgruppe nur bereit, wenn ihr Platz besetzt ist", false);

            // --- Torpedos
            Bool("WhAutoLoad", "Automatischer Torpedolader: Rohre laden ohne Crew, Außenbehälter füllen innen nach", true);
            Num("WhAutoLoadSeconds", "  Nachladezeit pro Rohr in Sekunden (0 = sofort, Typ XXI hydraulisch ≈ 120)", 30, 0, 120, 5);

            // --- Waffen
            // a switch, not a slider: the Toolkit slider rounds to multiples of the step (17 -> 85 / 102 mm)
            Bool("WhCaliber88", "Deckgeschütze: Kaliber 8,8 cm statt 10,5 cm", false);
            Bool("WhOverkill", "Overkill: Vierlingsrohre statt Zwillinge", false);
            Num("WhFlakDepression", "Flak: Absenkung bis x Grad (unter Horizont)", 45, 5, 60, 5);
            Bool("WhOwnBoatGuard", "  Feuerschutz: nie ins eigene Boot schießen", true);
            Bool("WhRemoteGuns", "Geschütze aus dem Feuerleitstand fernbedienen (aus = klassisch an Deck)", true);
            Num("WhAirDetect", "Flugzeuge orten bis x km", 100, 10, 150, 5);
            Num("WhAirEngage", "Flugzeuge beschießen ab x km", 25, 2, 40, 1);

            Key("KeyWhAnchor", "Warthog: Anker werfen / lichten", "J");
        }

        // True when the Warthog features should act on this ship.
        public static bool Active(PlayerShip ship)
        {
            if (ship == null || Util.WarthogModPresent) return false;
            if (!Settings.B("WhOnlyWarthog")) return true;
            return IsWarthog(ship);
        }


        // By the game's entity type ("Type XXX (Player)"), and by the object name as a fallback.
        private static object logged;
        public static bool IsWarthog(PlayerShip ship)
        {
            if (ship == null) return false;
            if (!ReferenceEquals(logged, ship))
            {
                logged = ship;
                string tn = "?";
                try { tn = ship.SandboxEntity?.Type?.Name ?? "null"; } catch { }
                Util.Log("[Warthog] Boot: Objekt '" + ship.gameObject.name + "', Typ '" + tn + "'");
            }
            try
            {
                var t = ship.SandboxEntity != null ? ship.SandboxEntity.Type : null;
                if (t != null && t.Name != null && t.Name.StartsWith("Type XXX")) return true;
            }
            catch { }
            return ship.gameObject.name.StartsWith("Type XXX");
        }
    }

    // Modules to add in ToolkitRunner.Awake():  foreach (var m in WarthogModules.All()) Modules.Add(m);
    public static class WarthogModules
    {
        public static IModule[] All() => new IModule[] { new WarthogHull(), new WarthogPumps(), new WarthogPower(), new WarthogAir(), new WarthogSonar(), new WarthogSnorkel(), new WarthogWeapons(), new WarthogTorpedoes(), new WarthogHatches(),
            new WarthogNetCutter(), new WarthogAnchor(), new WarthogFireControl() };
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogSnorkel.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Audio;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Telescopic snorkel on U-1337:
    //  - the mast is "WhSnorkelExtra" m longer, so the boat can snorkel deeper than periscope depth
    //  - with "WhSnorkelAuto" it extends further by itself in waves (up to +3 m) when the tip gets wet
    //  - exhaust goes out under water: smoke / visibility / radar size x "WhSnorkelExhaust" (0 = invisible)
    // Done by running our copy of Snorkel.Update with the depth limits moved down (the game hard-codes 10 m).
    public class WarthogSnorkel : ModuleBase
    {
        internal static float WaveExtra;   // current automatic extension (m)
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhSnorkelTelescope");
        internal static float Extra => On ? Settings.F("WhSnorkelExtra") + WaveExtra : 0f;
    }

    [HarmonyPatch(typeof(Snorkel), "Update")]
    internal static class WarthogSnorkel_UpdatePatch
    {
        private static readonly AccessTools.FieldRef<Snorkel, Transform> Tube = AccessTools.FieldRefAccess<Snorkel, Transform>("tube");
        private static readonly AccessTools.FieldRef<Snorkel, float> Angle = AccessTools.FieldRefAccess<Snorkel, float>("angle");
        private static readonly AccessTools.FieldRef<Snorkel, bool> Raise = AccessTools.FieldRefAccess<Snorkel, bool>("shouldMastBeRaised");
        private static readonly AccessTools.FieldRef<Snorkel, bool> Auto = AccessTools.FieldRefAccess<Snorkel, bool>("automaticRaise");
        private static Vector3? tubeBase;

        private static bool Prefix(Snorkel __instance, DeepAudioSource ___audioFx, ParticleSystem ___smoke, float ___riseUpSpeed, float ___riseDownSpeed,
            Modifier ___performanceModifier, Modifier ___oxygenModifier, Modifier ___visibilityModifier, Modifier ___signatureRadiusModifier, Modifier ___radarDetectabilityModifier,
            Parameter ___oxygenRegeneration, Parameter ___signatureRadius, Parameter ___radarDetectability, Parameter ___visibility,
            float ___smokeEffectMultiplier, ParticleSystemIntensityController ___smokeIntensityController)
        {
            if (!WarthogSnorkel.On || !Util.IsPlayers(__instance)) { RestoreTube(__instance); return true; }
            var sub = (PlayableSubmarine)__instance.ParentEntity;
            var gt = Traverse.Create(typeof(Snorkel)).Field("gameTime").GetValue<GameTime>();
            float ts = gt != null ? gt.ArtificialTimeScale : 1f;

            // telescopic tube: extend along the mast's own axis, only as far as the mast is raised (0.3.2: 0.3.0 moved the
            // folded mast 6 m straight up -> "periscope floating in the sky"; base is remembered per snorkel instance)
            var tube = Tube(__instance);
            if (tube != null)
            {
                if (tubeBase == null || tubeOwner != (object)__instance) { RestoreTube(null); tubeBase = tube.localPosition; tubeOwner = __instance; tubeOf = tube; }
                float raised = 1f - Mathf.Clamp01(Angle(__instance) / 90f);
                tube.localPosition = tubeBase.Value + tube.localRotation * Vector3.up * (WarthogSnorkel.Extra * raised);
            }
            float limit = 10f + WarthogSnorkel.Extra;   // the game: 10 m

            float deckDepth = sub.DeckDepth, targetDepth = sub.TargetDepth;
            if (Auto(__instance)) Raise(__instance) = targetDepth > 1f && targetDepth < limit && deckDepth < limit;
            var engines = ((Ship)sub).ActiveEngines;
            float num = (engines is PlayerShipEngine pse && pse.FuelUsage.Value < 0f) ? engines.Performance.Value : 0f;
            ref float angle = ref Angle(__instance);
            if (Raise(__instance))
            {
                if (angle != 0f) { angle = Mathf.Max(0f, angle - Time.deltaTime * ts * ___riseUpSpeed); if (tube) tube.localEulerAngles = new Vector3(angle, 0f, 0f); }
                if (angle < 10f && num > 0f) { if (!___audioFx.isPlaying) { ___audioFx.Play(); ___smoke.Play(); } }
                else if (___audioFx.isPlaying) { ___audioFx.Stop(); ___smoke.Stop(); }
            }
            else
            {
                if (angle != 90f) { angle = Mathf.Min(90f, angle + Time.deltaTime * ts * ___riseDownSpeed); if (tube) tube.localEulerAngles = new Vector3(angle, 0f, 0f); }
                if (angle >= 10f && ___audioFx.isPlaying) { ___audioFx.Stop(); ___smoke.Stop(); }
            }
            float up = Mathf.Max(0f, (10f - angle) * 0.1f);
            float tipAboveWater = ___audioFx.transform.position.y - sub.CurrentWaterElevation;
            float wet = Mathf.Clamp01((2f + tipAboveWater) * 4f) * (1f - Mathf.Clamp01(angle / 90f));
            if (targetDepth >= limit) wet = 0f;
            up *= wet;

            // automatic extension in waves: tip closer than 0.7 m to the water -> extend, well above -> retract
            if (Settings.B("WhSnorkelAuto") && Raise(__instance))
            {
                float target = tipAboveWater < 0.7f ? 3f : tipAboveWater > 2f ? 0f : WarthogSnorkel.WaveExtra;
                WarthogSnorkel.WaveExtra = Mathf.MoveTowards(WarthogSnorkel.WaveExtra, target, Time.deltaTime * ts * 0.5f);
            }
            else WarthogSnorkel.WaveExtra = Mathf.MoveTowards(WarthogSnorkel.WaveExtra, 0f, Time.deltaTime * ts * 0.5f);

            var ps = sub as PlayerShip;
            if (ps != null && ps.DieselEngine) ps.DieselEngine.ExhaustEffectsIntensityScale = 1f - up;
            float exhaust = Settings.F("WhSnorkelExhaust");   // 0 = exhaust under water, invisible
            float smoke = num * up * ___smokeEffectMultiplier * exhaust;
            ___smokeIntensityController.Intensity = smoke;
            if (smoke > 0f && !___smokeIntensityController.ParticleSystemComponent.isPlaying) ___smokeIntensityController.ParticleSystemComponent.Play();
            ___performanceModifier.Value = up;
            if (___oxygenModifier != null) ___oxygenModifier.Value = Raise(__instance) ? __instance.Performance.Value * ___oxygenRegeneration.Value : 0f;
            float sig = wet * ___signatureRadius.Value * exhaust;
            var maxMod = sub.SandboxEntity.SignatureRadius.GetMaxDeltaModifier();
            if (maxMod != null && (maxMod == ___signatureRadiusModifier || maxMod.Value < sig))
            {
                ___radarDetectabilityModifier.Value = Mathf.Lerp(1f, ___radarDetectability.Value, wet);
                ___signatureRadiusModifier.Value = sig;
            }
            else { ___radarDetectabilityModifier.Value = 1f; ___signatureRadiusModifier.Value = 0f; }
            ___visibilityModifier.Value = Mathf.Max(wet * ___visibility.Value * Mathf.Max(exhaust, 0.15f), 0f); // the mast itself stays a little visible
            return false;
        }

        private static object tubeOwner; private static Transform tubeOf;
        private static void RestoreTube(Snorkel s)
        {
            if (tubeBase == null) return;
            if (s != null && (object)s != tubeOwner) return;   // another boat's snorkel: not ours to reset
            if (tubeOf != null) tubeOf.localPosition = tubeBase.Value;
            tubeBase = null; tubeOwner = null; tubeOf = null;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogSonar.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Three sonar arrays on U-1337: bow, middle, stern.
    // Passive (all arrays together): 360°, always listening even without a man in the listening room,
    // as good surfaced and at high speed as submerged and slow, range slider, alarm on every new contact.
    // Active (Toolkit "ActiveSonar" ping): bow array covers the front half (forward + down),
    // stern array the rear half (aft + down). Switch "Ping verrät uns": off = the ping does not make us louder.
    public class WarthogSonar : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip);
        private readonly HashSet<int> known = new HashSet<int>();
        private Modifier energy;

        public override void Tick()
        {
            if (!Util.ShipReady) return;   // 0.4.11: NullReference right after loading (boat not complete yet)
            var ship = Util.PlayerShip;
            bool passive = On && Settings.B("WhPassive") && (Settings.B("WhSonarBow") || Settings.B("WhSonarMid") || Settings.B("WhSonarStern"));
            foreach (var h in Util.Find<Hydrophone>())
            if (h != null && h.DetectionRange != null)
            {
                var t = Traverse.Create(h);
                // 360°: the arc check uses |angle| <= arc, so 190° with fade covers every direction
                var arc = t.Field("hydrophoneDetectionArc").GetValue<Parameter>();
                if (arc != null) { var m = Util.OneScale(arc, "Warthog 360"); float baseArc = arc.GetValueExcludingModifier("Warthog 360"); m.Value = passive && baseArc > 1f ? Mathf.Max(1f, 190f / baseArc) : 1f; }
                // range slider
                Util.OneScale(h.DetectionRange, "Warthog Range").Value = passive ? Settings.F("WhPassiveRange") : 1f;
                Util.OneScale(h.GroupDetectionRange, "Warthog Range").Value = passive ? Settings.F("WhPassiveRange") : 1f;
                // surfaced / fast = as good as submerged / slow: done where the game sets these values (HydroSame in SensorCheats.cs)
                // always on: no operator needed
                if (passive && !h.enabled) h.SetDisableFlag(DeviceDisableFlag.Unmanned, false);
            }

            // battery for the arrays (mode from the power menu)
            if (ship == null) return;   // 0.3.58: NRE without a boat (port, menus)
            if (ship.Energy != null)
            {
                energy = Util.OneDelta(ship.Energy.Gain, "Warthog Sonar");
                int arrays = (Settings.B("WhSonarBow") ? 1 : 0) + (Settings.B("WhSonarMid") ? 1 : 0) + (Settings.B("WhSonarStern") ? 1 : 0);
                float use = passive ? WarthogPower.Apply("WarthogSonar", arrays * 0.00002f) : 0f;
                if (energy.Value != -use) energy.Value = -use;
            }

            if (passive && Settings.B("WhPassiveAlarm")) Alarm(ship);
            else known.Clear();
        }

        // New-contact alarm: everything the hydrophones would now hear for sure, that we did not hear before.
        private void Alarm(PlayerShip ship)
        {
            var hs = Util.Find<Hydrophone>();
            if (hs.Length == 0) return;
            var h = hs[0];
            // the game's own list of all active ships/observers (no FindObjectsOfType: that cost 40-70 ms every 15 s)
            var obs = Observator.observators;
            var now = new HashSet<int>();
            for (int i = obs.Count - 1; i >= 0; i--)
            {
                var s = obs[i] as Ship;
                if (s == null || s == ship || s is Aircraft) continue;
                float d = Vector3.Distance(ship.transform.position, s.transform.position);
                if (d > h.DetectionRange.Value) continue;
                float chance = h.GetSpotChance(s, d, true, true, out _, out _);
                if (chance < 0.5f) continue;
                int id = s.GetInstanceID();
                now.Add(id);
                if (!known.Contains(id))
                {
                    Vector3 to = s.transform.position - ship.transform.position;
                    int brg = Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
                    ToolkitRunner.ShowNotice("Sonar: neuer Kontakt, Peilung " + brg.ToString("000") + "°, " + (d / 1000f).ToString("0.0") + " km", 6f);
                }
            }
            known.Clear(); known.UnionWith(now);
        }
    }

    // passive: the hydrophone keeps working without an operator
    [HarmonyPatch(typeof(Device), nameof(Device.SetDisableFlag))]
    internal static class WarthogSonar_UnmannedPatch
    {
        private static void Prefix(Device __instance, DeviceDisableFlag disableFlag, ref bool state)
        {
            if (state && disableFlag == DeviceDisableFlag.Unmanned && __instance is Hydrophone && WarthogSonar.On && Settings.B("WhPassive") && Util.IsPlayers(__instance))
                state = false;
        }
    }

    // passive: exact values (distance, speed, course) also surfaced
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class WarthogSonar_ExactPatch
    {
        private static void Postfix(Hydrophone __instance, ref bool __result)
        {
            if (__result && WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame") && Util.IsPlayers(__instance)) __result = false;
        }
    }

    // active: bow array = front half, stern array = rear half (both = all around)
    [HarmonyPatch(typeof(ActiveSonar), nameof(ActiveSonar.InArc))]
    internal static class WarthogSonar_ArcPatch
    {
        private static bool Prefix(PlayerShip ship, Entity e, ref bool __result)
        {
            if (!WarthogSonar.On || ship == null || e == null) return true;
            Vector3 to = e.transform.position - ship.transform.position;
            float rel = Mathf.Abs(Mathf.DeltaAngle(ship.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            __result = (Settings.B("WhSonarBow") && rel <= 95f) || (Settings.B("WhSonarStern") && rel >= 85f);
            return false;
        }
    }

    // active: "Ping verrät uns" off = the ping does not make us louder
    [HarmonyPatch(typeof(ActiveSonar), nameof(ActiveSonar.Tick))]
    internal static class WarthogSonar_QuietPingPatch
    {
        private static void Postfix(ActiveSonar __instance)
        {
            if (!WarthogSonar.On || Settings.B("WhPingReveals")) return;
            var loud = Traverse.Create(__instance).Field("loud").GetValue<Modifier>();
            if (loud != null && loud.Value != 0f) loud.Value = 0f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogTab.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Linq;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

    // Extra controls at the top of the "Warthog" tab.
    // Hook in ToolkitRunner (next to the other tab draws):  if (tab == WarthogSettings.Tab) UboatToolkit.Modules.Warthog.WarthogTab.Draw();
    public static class WarthogTab
    {
        private static string message;
        private static float messageUntil;

        public static void Draw()
        {
            Imgui.BeginH();
            if (Imgui.Button("Warthog übernehmen", 220)) TakeOver();
            if (message != null && Time.unscaledTime < messageUntil) Imgui.Label("  " + message);
            Imgui.EndH();
            Imgui.Label("Im Spiel selbst: im Hafen den Depot-Arbeiter ansprechen → \"Kommando über anderes U-Boot übernehmen\".");
            Imgui.Space(6);
            if (!WarthogSettings.IsWarthog(Util.PlayerShip)) return;
            // anchor
            Imgui.BeginH();
            if (Imgui.Button(WarthogAnchor.Anchored ? "Anker lichten" : "Anker werfen", 160)) WarthogAnchor.Toggle();
            Imgui.Label("  " + WarthogAnchor.Status);
            Imgui.EndH();
            // fire-control room
            if (!string.IsNullOrEmpty(WarthogFireControl.Status)) Imgui.Label("Feuerleitstand: " + WarthogFireControl.Status);
            // hatches
            if (WarthogHatches.All.Count > 0)
            {
                Imgui.Label("Luken:");
                foreach (var l in WarthogHatches.All)
                {
                    Imgui.BeginH();
                    Imgui.Label("   " + l.Name + ": " + (l.IsOpen ? "offen" : "zu"), 220);
                    if (!l.IsMain && WarthogHatches.On && WarthogHatches.Single && Imgui.Button(l.IsOpen ? "schließen" : "öffnen", 100)) WarthogHatches.Set(l, !l.IsOpen);
                    Imgui.EndH();
                }
            }
            Imgui.Space(6);
        }

        // In port: unlock "Type XXX" for this career and open the game's own ship selector.
        private static void TakeOver()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !ship.Docked) { Say("Nur im Hafen möglich"); return; }
            var career = Util.Career;
            if (career != null && !career.UnlockedShips.Contains("Type XXX")) career.UnlockShip("Type XXX", true);
            var ui = Resources.FindObjectsOfTypeAll<ShipSelectorUI>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (ui == null) { Say("Bootsauswahl nicht gefunden"); return; }
            _ = ui.Open();   // the selector lists Type XXX (WarthogMod patch), purchase = free change
            Say("Bootsauswahl geöffnet");
        }

        private static void Say(string text) { message = text; messageUntil = Time.unscaledTime + 5f; ToolkitRunner.ShowNotice("Warthog: " + text, 4f); }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogTorpedoes.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // Automatic torpedo loader (U-1337):
    //  - an empty tube reloads itself from the nearest inside rack after "WhAutoLoadSeconds", no crew needed
    //  - the external deck containers refill the inside racks by themselves (also submerged)
    // Overlap with Toolkit "Torpedoes"/"Loading": those speed up the crew's own loading; this one needs no crew.
    public class WarthogTorpedoes : ModuleBase
    {
        private readonly Dictionary<int, float> emptySince = new Dictionary<int, float>();
        private float nextRefill;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (!WarthogSettings.Active(ship) || !Settings.B("WhAutoLoad")) { emptySince.Clear(); return; }
            var storages = Util.Find<TorpedoStorage>();
            var inside = new List<TorpedoStorage>(); var outside = new List<TorpedoStorage>();
            foreach (var s in storages) (s.transform.IsChildOf(ship.transform) ? outside : inside).Add(s);

            // 1) tubes
            float wait = Settings.F("WhAutoLoadSeconds");
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                int id = l.GetInstanceID();
                if (l.Torpedo != null || l.IsFlooded || l.FloodingProgress > 0f) { emptySince.Remove(id); continue; }
                if (!emptySince.TryGetValue(id, out var t0)) { emptySince[id] = Time.time; continue; }
                if (Time.time - t0 < wait) continue;
                var src = Nearest(inside, l.transform.position, out int idx);
                if (src == null) continue;
                var slot = src[idx];
                var torpedo = slot.Item != null ? slot.Item.GetComponent<StoredTorpedo>() : null;
                if (torpedo == null) continue;
                slot.Item = null;
                l.SetTorpedo(torpedo);
                l.CompleteLoading(true);
                emptySince.Remove(id);
                Util.Log("Warthog: Rohr automatisch geladen");
            }

            // 2) racks inside <- containers outside (every few seconds, one torpedo per rack)
            if (Time.time < nextRefill || outside.Count == 0) return;
            nextRefill = Time.time + Mathf.Max(2f, wait * 0.5f);
            foreach (var dst in inside)
            {
                int free = FirstFree(dst);
                if (free < 0) continue;
                var src = Nearest(outside, dst.transform.position, out int idx);
                if (src == null) return;
                StorageUtility.TransferInstantly(src, idx, dst, free, 1f);
            }
        }

        private static TorpedoStorage Nearest(List<TorpedoStorage> list, Vector3 pos, out int index)
        {
            TorpedoStorage best = null; index = -1; float bd = float.MaxValue;
            foreach (var s in list)
                for (int i = 0; i < Util.Slots(s); i++)
                {
                    var it = s[i] != null ? s[i].Item : null;
                    if (it == null || it.GetComponent<StoredTorpedo>() == null) continue;
                    if (s.IsSlotBlockedDuringLoading(i)) continue;
                    float d = (s[i].transform.position - pos).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; index = i; }
                }
            return best;
        }

        private static int FirstFree(TorpedoStorage s)
        {
            for (int i = 0; i < Util.Slots(s); i++) if (s[i] != null && s[i].Item == null && !s.IsSlotBlockedDuringLoading(i)) return i;
            return -1;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Warthog/WarthogWeapons.cs
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::System.Linq;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Weapons of U-1337 (runtime part; the turrets themselves are built into the boat):
    //  - flak can aim down to -WhFlakDepression degrees (game: maxAngle = degrees DOWN, minAngle = degrees UP)
    //  - own-boat fire guard: a gun never fires while its line of fire goes through our own boat
    //  - aircraft: reported from WhAirDetect km, flak range WhAirEngage km
    //  - remembers the last shot, so the thrusters can hold the heading against the recoil
    // Gyro stabilisation per gun + ship gyro is the Toolkit "GunGyro" (GunGyroCrew / GunGyroManual);
    // on U-1337 both are forced on.
    public class WarthogWeapons : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip);
        private readonly HashSet<int> reported = new HashSet<int>();
        private Aircraft[] planes = new Aircraft[0];
        private float planesAt = -99f;

        internal static bool IsFlak(Gun g) => g != null && g.Calibre.Value > 0f && g.Calibre.Value <= 40f;

        // 0.3.9 performance (log: Tick 40-43 ms): guns searched once per second, outer barrels cached per gun
        private float nextTick;
        private Gun[] guns = new Gun[0];
        private float gunsAt = -99f;
        public override void Tick()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            var ship = Util.PlayerShip;
            bool on = On;
            if (Time.unscaledTime - gunsAt > 7.3f) { guns = Util.Find<Gun>().ToArray(); gunsAt = Time.unscaledTime; }
            foreach (var g in guns)
            {
                if (g == null) continue;
                if (!IsFlak(g) || !Util.IsPlayers(g)) continue;   // 0.4.22 (option audit, player-only rule): only OUR flak
                float orig = Util.Original<float>(g, "maxAngle");
                Util.SetField(g, "maxAngle", Mathf.Max(orig, Mathf.Max(on ? Settings.F("WhFlakDepression") : 0f, Util.IsPlayers(g) ? Settings.F("AAGunDepress") : 0f)));
                var range = Traverse.Create(g).Field("range").GetValue<UBOAT.Game.Core.Data.Parameter>();
                // 0.4.22 (option audit): with Warthog off the range belongs to "Flak: Reichweite" (Weapons); this line reset it every second.
                if (on) Util.SetParameter(range, Settings.F("WhAirEngage") * 1000f);
                else if (Util.WarthogModOwns(ship)) Util.SetParameter(range, 0f);
            }
            foreach (var g in guns) if (g != null) Barrels(g, on && Util.IsPlayers(g));
            // 0.4.22 (option audit): the two gyro switches are no longer forced on every second; they can be switched off.
            if (on) ReportPlanes(ship); else reported.Clear();
        }

        // Our deck guns are built with 4 barrels: twin = the middle two, Overkill = all four.
        // Calibre 88 / 105 for the big guns: calibre value + faster reload for 88.
        private static readonly System.Reflection.FieldInfo BarrelsField = AccessTools.Field(typeof(Gun), "barrels");
        private static readonly System.Reflection.FieldInfo RecoilField = AccessTools.Inner(typeof(Gun), "Barrel") != null ? AccessTools.Field(AccessTools.Inner(typeof(Gun), "Barrel"), "RecoilTarget") : null;
        private static readonly Dictionary<int, System.Array> fullBarrels = new Dictionary<int, System.Array>();
        private static readonly Dictionary<int, List<GameObject>> outer = new Dictionary<int, List<GameObject>>();

        private static void Barrels(Gun g, bool on)
        {
            if (BarrelsField == null) return;
            int id = g.GetInstanceID();
            if (!fullBarrels.TryGetValue(id, out var full))
            {
                full = (System.Array)BarrelsField.GetValue(g);
                if (full == null || full.Length != 4) return;   // not one of our 4-barrel mounts
                fullBarrels[id] = full;
            }
            bool all = !on || Settings.B("WhOverkill") || IsFlak(g);   // flak is always a quad mount
            // firing barrels: twin = the middle two (1, 2), Overkill = all four
            var want = all ? full : System.Array.CreateInstance(full.GetType().GetElementType(), 2);
            if (!all) { want.SetValue(full.GetValue(1), 0); want.SetValue(full.GetValue(2), 1); }
            if (((System.Array)BarrelsField.GetValue(g)).Length != want.Length) BarrelsField.SetValue(g, want);
            // 0.3.9: visible outer barrels found by NAME ("WH Barrel 1" / "WH Barrel 4"), built by the Unity builder for our
            // turrets and by WarthogMod.dll for the guns the game spawns into "Deck Gun" / "Aft Deck Gun".
            // RecoilTarget is empty on some mounts -> 0.3.0-0.3.8 showed mixed 2/4 barrels or a quad with one missing.
            // Cached per gun; rebuilt when a cached object was destroyed (gun respawned).
            if (!outer.TryGetValue(id, out var outs) || outs.Exists(x => x == null))
            {
                outs = new List<GameObject>();
                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                    if (t.name == "WH Barrel 1" || t.name == "WH Barrel 4") outs.Add(t.gameObject);
                outer[id] = outs;
            }
            foreach (var o in outs) if (o.activeSelf != all) o.SetActive(all);
            // No named barrels (older Warthog or another boat): old way via the barrels' recoil objects, so the Toolkit
            // works with any Warthog version and needs none (DoMe: mods must run side by side, no dependencies).
            if (outs.Count == 0 && RecoilField != null)
                foreach (int i in new[] { 0, 3 })
                {
                    var t = RecoilField.GetValue(full.GetValue(i)) as Transform;
                    if (t != null && t.gameObject.activeSelf != all) t.gameObject.SetActive(all);
                }
            if (!IsFlak(g) && g.Calibre.Value > 80f)
            {
                bool c88 = on && Settings.B("WhCaliber88");   // exactly 88 mm, else the gun's own 105 mm
                Util.SetParameter(g.Calibre, c88 ? 88f : 0f);
                Util.OneScale(g.ReloadTime, "Warthog Calibre").Value = c88 ? 0.75f : 1f;
            }
        }

        private void ReportPlanes(PlayerShip ship)
        {
            if (Time.unscaledTime - planesAt > 10f) { planes = Object.FindObjectsOfType<Aircraft>(); planesAt = Time.unscaledTime; }
            float detect = Settings.F("WhAirDetect") * 1000f;
            var now = new HashSet<int>();
            foreach (var p in planes)
            {
                if (p == null) continue;
                float d = Vector3.Distance(ship.transform.position, p.transform.position);
                if (d > detect) continue;
                int id = p.GetInstanceID(); now.Add(id);
                if (reported.Contains(id)) continue;
                Vector3 to = p.transform.position - ship.transform.position;
                int brg = Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
                ToolkitRunner.ShowNotice("Radar: Flugzeug, Peilung " + brg.ToString("000") + "°, " + (d / 1000f).ToString("0") + " km", 6f);
            }
            reported.Clear(); reported.UnionWith(now);
        }
    }

    // remote operation from the fire-control room: our guns stay usable without a man at the gun
    [HarmonyPatch(typeof(Device), nameof(Device.SetDisableFlag))]
    internal static class WarthogWeapons_RemotePatch
    {
        private static void Prefix(Device __instance, DeviceDisableFlag disableFlag, ref bool state)
        {
            if (state && disableFlag == DeviceDisableFlag.Unmanned && __instance is Gun g && WarthogWeapons.On && Settings.B("WhRemoteGuns") && Util.IsPlayers(__instance)
                && WarthogFireControl.Ready(g))   // optional: only when its fire-control station is manned
                state = false;
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class WarthogWeapons_FirePatch
    {
        private static readonly AccessTools.FieldRef<Gun, Transform> Origin = AccessTools.FieldRefAccess<Gun, Transform>("originTransform");
        private static readonly RaycastHit[] hits = new RaycastHit[16];

        // own-boat guard
        private static bool Prefix(Gun __instance, Vector2 ___angles, ref Shell __result)
        {
            if (!WarthogWeapons.On || !Settings.B("WhOwnBoatGuard") || !Util.IsPlayers(__instance)) return true;
            var origin = Origin(__instance);
            var ship = Util.PlayerShip;
            if (origin == null || ship == null) return true;
            Vector3 dir = origin.TransformDirection(Quaternion.Euler(___angles.x, ___angles.y, 0f) * Vector3.forward);
            Vector3 from = origin.position + dir * 2.5f;   // start in front of the gun's own parts
            int n = Physics.RaycastNonAlloc(from, dir, hits, 120f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(__instance.transform)) continue;
                if (c.transform.IsChildOf(ship.transform)) { __result = null; return false; } // would hit our own boat
            }
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Anmeldung beim Toolkit (0.4.76)
// ====================================================================================================
namespace UboatToolkit.Modules.Warthog
{
    using global::UboatToolkit;

    internal static class WarthogAddon
    {
        internal static ToolkitAddon ToolkitAddon() => new ToolkitAddon
        {
            Name = "Warthog-Teile",
            RegisterSettings = WarthogSettings.Register,
            Modules = WarthogModules.All,
            Tab = WarthogSettings.Tab, TabVisible = () => !Util.WarthogModPresent, DrawTab = WarthogTab.Draw,
            Rests = () => Util.WarthogModOwns(Util.PlayerShip), RestReason = "Warthog-Mod ist installiert und sein Boot ist in Benutzung, die Wirkung kommt allein von ihm",
            PatchAfter = "UboatToolkit.Modules.Wolfpack_MissionPatch",
            PatchNames = new[]
            {
                "UboatToolkit.Modules.Warthog.WarthogAir_SilentPatch",
                "UboatToolkit.Modules.Warthog.WarthogAir_OxygenPatch",
                "UboatToolkit.Modules.Warthog.WarthogHatches_AirPatch",
                "UboatToolkit.Modules.Warthog.WarthogHatches_LadderLog",
                "UboatToolkit.Modules.Warthog.WarthogNetCutter_CollisionPatch",
                "UboatToolkit.Modules.Warthog.WarthogSnorkel_UpdatePatch",
                "UboatToolkit.Modules.Warthog.WarthogSonar_UnmannedPatch",
                "UboatToolkit.Modules.Warthog.WarthogSonar_ExactPatch",
                "UboatToolkit.Modules.Warthog.WarthogSonar_ArcPatch",
                "UboatToolkit.Modules.Warthog.WarthogSonar_QuietPingPatch",
                "UboatToolkit.Modules.Warthog.WarthogWeapons_RemotePatch",
                "UboatToolkit.Modules.Warthog.WarthogWeapons_FirePatch"
            },
            Split = new[] { "WhSilentVent", "WhVentAlwaysOn" },
            Texts = m =>
            {
                m["Strahlruder gibt es nur auf dem Warthog (U-1337)"] = "Thrusters exist only on the Warthog (U-1337)";
                m[" (0 = verbraucht, 1 = kostenlos, 2 = erzeugt)"] = " (0 = consumes, 1 = free, 2 = generates)";
                m["Alle Warthog-Funktionen nur auf U-1337 (Type XXX), nicht auf anderen Booten"] = "All Warthog features only on U-1337 (Type XXX), not on other boats";
                m["Gummibeschichtung: Sonar-Ortbarkeit x (1 = ohne, 0 = unsichtbar)"] = "Rubber coating: sonar detectability x (1 = none, 0 = invisible)";
                m["Gummibeschichtung: Hydrophon-Ortbarkeit x (1 = normal)"] = "Rubber coating: hydrophone detectability x (1 = normal)";
                m["Finnen: engere Kurven, weniger Rollen, schneller Tiefe wechseln"] = "Fins: tighter turns, less rolling, faster depth changes";
                m["  Kurven: Drehkraft x"] = "  Turns: turning force x";
                m["  Rollen: Dämpfung x"] = "  Rolling: damping x";
                m["  Tiefenruder-Wirkung x"] = "  Dive plane effect x";
                m["Geschwindigkeit: Faktor auf die Schraubenleistung (1,0 = wie gebaut)"] = "Speed: factor on propeller power (1.0 = as built)";
                m["Tauch-/Steiggeschwindigkeit: Faktor auf die Tiefenruder-Kraft (1,0 = wie ein IXC, auch in großer Tiefe)"] = "Dive/rise speed: factor on diving-plane force (1.0 = like an IXC, also at great depth)";
                m["Zerstörungstiefe in m (befohlene Tiefe wird auf 100 m darüber begrenzt)"] = "Crush depth in m (ordered depth is limited to 100 m above it)";
                m["Teleskop-Schnorchel: tiefer als Sehrohrtiefe"] = "Telescopic snorkel: deeper than periscope depth";
                m["  zusätzliche Länge in m"] = "  extra length in m";
                m["  fährt bei Wellen automatisch weiter aus"] = "  extends further automatically in waves";
                m["  Abgas unter Wasser: Sichtbarkeit x (0 = unsichtbar)"] = "  Exhaust underwater: visibility x (0 = invisible)";
                m["Eine Lenzpumpe pro Abteil, mit Schwimmer (springt selbst an)"] = "One bilge pump per compartment, with float switch (starts by itself)";
                m["  Schwimmer: an ab x m³ Wasser im Abteil"] = "  Float: on from x m³ of water in the compartment";
                m["  Leistung je Pumpe in m³/s (offenes Turmluk getaucht ≈ 0,2)"] = "  Output per pump in m³/s (open tower hatch submerged ≈ 0.2)";
                m["  Leistung steigt mit der Tiefe (gegen den Wasserdruck)"] = "  Output rises with depth (against water pressure)";
                m["  Strom je Pumpe bei voller Leistung (Anteil x 0,0001/s)"] = "  Power per pump at full output (share x 0.0001/s)";
                m["Sauerstoff per Elektrolyse: keine Kalipatronen mehr"] = "Oxygen by electrolysis: no more potash cartridges";
                m["Alle Verbraucher generieren Strom"] = "All consumers generate power";
                m["Passives Sonar 360°: immer an, auch ohne Hydrophon"] = "Passive sonar 360°: always on, even without a hydrophone operator";
                m["  aufgetaucht und bei hoher Fahrt genauso gut"] = "  just as good surfaced and at high speed";
                m["  Reichweite x"] = "  Range x";
                m["  Kontakte auf der Karte zeigen"] = "  Show contacts on the map";
                m["  Alarm bei neuem Kontakt"] = "  Alarm on new contact";
                m["Aktives Sonar Bug (nach vorn + unten)"] = "Active sonar bow (forward + down)";
                m["Sonar Mitte (nur passiv)"] = "Sonar centre (passive only)";
                m["Aktives Sonar Heck (nach hinten + unten)"] = "Active sonar stern (backward + down)";
                m["  Ping verrät uns (Gegner hören den Ping)"] = "  Ping gives us away (enemies hear the ping)";
                m["Automatischer Torpedolader: Rohre laden ohne Crew, Außenbehälter füllen innen nach"] = "Automatic torpedo loader: tubes load without crew, outer containers refill inside";
                m["  Nachladezeit pro Rohr in Sekunden (0 = sofort, Typ XXI hydraulisch ≈ 120)"] = "  Reload time per tube in seconds (0 = instant, Type XXI hydraulic ≈ 120)";
                m["Overkill: Vierlingsrohre statt Zwillinge"] = "Overkill: quad barrels instead of twins";
                m["Flak: Absenkung bis x Grad (unter Horizont)"] = "AA: depress down to x degrees (below horizon)";
                m["  Feuerschutz: nie ins eigene Boot schießen"] = "  Fire guard: never shoot into your own boat";
                m["Geschütze aus dem Feuerleitstand fernbedienen (aus = klassisch an Deck)"] = "Remote-control the guns from the fire control station (off = classic on deck)";
                m["Flugzeuge orten bis x km"] = "Detect aircraft up to x km";
                m["Flugzeuge beschießen ab x km"] = "Fire at aircraft from x km";
                m["Warthog: Anker werfen / lichten"] = "Warthog: drop / raise anchor";
                m["Luken: alle Luken benutzbar (2. Turmluk, Seitentüren, Torpedoluken, Kombüse; aus = Spiel mit einem Luk)"] = "Hatches: all hatches usable (2nd tower hatch, side doors, torpedo hatches, galley; off = game's single hatch)";
                m["  Luken einzeln steuern (Liste oben im Reiter; aus = alle folgen Turmluk 1)"] = "  Control hatches one by one (list at the top of the tab; off = all follow tower hatch 1)";
                m["Liegeplatz: Boot x m nach vorn schieben (IXC = 5; 14 = Heck wie beim IXC)"] = "Berth: move boat x m forward (IXC = 5; 14 = stern like the IXC)";
                m["Liegeplatz: Boot x m seitlich schieben (+ = Steuerbord)"] = "Berth: move boat x m sideways (+ = starboard)";
                m["Netzsäge: U-Boot-Netze durchschneiden (Bug + Heck)"] = "Net saw: cut anti-submarine nets (bow + stern)";
                m["Anker benutzen (Knopf oben im Reiter oder Taste)"] = "Use anchor (button at the top of the tab or key)";
                m["  welcher Anker: 0 = Bug, 1 = Heck, 2 = beide"] = "  which anchor: 0 = bow, 1 = stern, 2 = both";
                m["  Kettenlänge in m"] = "  chain length in m";
                m["  Anker lichten dauert x Sekunden (0 = sofort)"] = "  raising the anchor takes x seconds (0 = instant)";
                m["Feuerleitstand: Geschützgruppe nur bereit, wenn ihr Platz besetzt ist"] = "Fire control: gun group only ready when its station is manned";
                m["Deckgeschütze: Kaliber 8,8 cm statt 10,5 cm"] = "Deck guns: 8.8 cm calibre instead of 10.5 cm";
                m["Sonar: neuer Kontakt, Peilung "] = "Sonar: new contact, bearing ";
                m["Warthog übernehmen"] = "Take over Warthog";
                m["Im Spiel selbst: im Hafen den Depot-Arbeiter ansprechen → \"Kommando über anderes U-Boot übernehmen\"."] = "In the game itself: in port talk to the depot worker → \"Take command of another U-boat\".";
                m["Nur im Hafen möglich"] = "Only possible in port";
                m["Bootsauswahl nicht gefunden"] = "Boat selection not found";
                m["Bootsauswahl geöffnet"] = "Boat selection opened";
                m["Radar: Flugzeug, Peilung "] = "Radar: aircraft, bearing ";
            },
            Flag = n => n == "WarthogWeapons" ? WarthogWeapons.On : n == "PassiveSonarSame" && WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame"),
            Adjust = (n, v) => n.StartsWith("Power:") ? WarthogPower.Apply(n.Substring(6), v) : v,
            Value = n => n == "FlakDepression" ? (WarthogWeapons.On ? Settings.F("WhFlakDepression") : 0f) : float.NaN,
        };
    }
}
