// Ausguck (Teil des UBOAT Toolkit): LrlCrewStations. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlCrewStations
    {
        private static readonly List<ObservationSpot> Buffer = new List<ObservationSpot>();
        private static readonly StringBuilder DetailBuilder = new StringBuilder();

        private static readonly FieldInfo SubtitlesField = FindSubtitlesField();
        private static readonly PropertyInfo DisplayedProperty = FindDisplayedProperty();

        private static FieldInfo FindSubtitlesField()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Tasks.SpeakController");
            return t != null ? AccessTools.Field(t, "subtitlesUI") : null;
        }

        private static PropertyInfo FindDisplayedProperty()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.UI.ISubtitlesUI");
            return t != null ? t.GetProperty("Displayed") : null;
        }

        private static string SubtitleShown()
        {
            try
            {
                if (SubtitlesField == null || DisplayedProperty == null)
                    return "?";
                var ui = SubtitlesField.GetValue(null);
                if (ui == null)
                    return "?";
                var shown = DisplayedProperty.GetValue(ui, null);
                return shown is bool ? ((bool)shown ? "yes" : "no") : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static bool Inspect(ObservationSpot spot, out int workers, out int crew, out bool officer)
        {
            workers = 0;
            crew = 0;
            officer = false;

            if (spot == null)
                return false;

            var interactable = spot.GetComponent<Interactable>();
            if (interactable == null)
                return false;

            var list = interactable.Workers;
            workers = list != null ? list.Count : 0;

            for (var i = 0; i < workers; i++)
            {
                var pc = list[i] as PlayableCharacter;
                if (pc == null)
                    continue;

                crew++;
                if (pc.IsOfficer)
                    officer = true;
            }

            var range = spot.DetectionRange != null ? spot.DetectionRange.Value : 0f;
            return crew > 0 && (officer || range >= 1f);
        }

        internal static string Describe(ObservationSpot spot)
        {
            if (spot == null)
                return "n/a";

            if (spot.GetComponent<Interactable>() == null)
                return "no station (cannot voice a report)";

            int workers, crew;
            bool officer;
            var canVoice = Inspect(spot, out workers, out crew, out officer);

            return string.Format("workers={0} crew={1} officer={2} canVoice={3}",
                workers, crew, officer ? "yes" : "no", canVoice ? "yes" : "NO");
        }

        internal static string Summarise(PlayerShip ship, string statementId)
        {
            if (ship == null)
                return "no player ship";

            Buffer.Clear();
            LrlProviders.GetAll(ship, Buffer);

            var voicers = 0;
            var detail = DetailBuilder;
            detail.Length = 0;

            for (var i = 0; i < Buffer.Count; i++)
            {
                int workers, crew;
                bool officer;
                if (!Inspect(Buffer[i], out workers, out crew, out officer))
                    continue;

                voicers++;
                detail.Append(voicers > 1 ? "; " : "").Append(Buffer[i].name).Append(": ");

                var list = Buffer[i].GetComponent<Interactable>().Workers;
                for (var w = 0; w < list.Count; w++)
                {
                    var pc = list[w] as PlayableCharacter;
                    if (pc == null)
                        continue;

                    detail.Append(pc.Name)
                          .Append(pc.IsOfficer ? " (officer" : " (crew")
                          .Append(pc.IsImpersonated ? ", you" : "")
                          .Append(") speaking=").Append(pc.IsSpeaking ? "yes" : "no")
                          .Append(" canSpeakFreely=").Append(pc.CanSpeakFreely ? "yes" : "no")
                          .Append(" canSay=").Append(pc.CanSay(statementId) ? "yes" : "NO")
                          .Append(' ');
                }
            }

            Buffer.Clear();

            if (voicers == 0)
                return "NOBODY was manning a lookout station that can voice it";

            return voicers + " station(s) could have voiced it [" + detail.ToString().TrimEnd() +
                   "] subtitleOnScreen=" + SubtitleShown();
        }
    }
}
