using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// dialogue_pool.json 로더.
    /// Package Manager > Add package by name > com.unity.nuget.newtonsoft-json
    /// </summary>
    public class DialoguePool
    {
        readonly Dictionary<string, List<string>> fallback = new Dictionary<string, List<string>>();
        readonly Dictionary<Mood, List<string>> detective = new Dictionary<Mood, List<string>>();
        readonly List<string> contradictionTemplates = new List<string>();
        readonly List<string> repeatTemplates = new List<string>();

        public static DialoguePool Load(string json)
        {
            var p = new DialoguePool();
            var root = JObject.Parse(json);

            var fb = root["fallback"] as JObject;
            if (fb != null)
                foreach (var kv in fb)
                    p.fallback[kv.Key] = kv.Value.ToObject<List<string>>();

            var dt = root["detective"] as JObject;
            if (dt != null)
                foreach (var kv in dt)
                    if (System.Enum.TryParse(kv.Key, out Mood m))
                        p.detective[m] = kv.Value.ToObject<List<string>>();

            var qt = root["quote_templates"] as JObject;
            if (qt?["contradiction"] != null)
                p.contradictionTemplates.AddRange(qt["contradiction"].ToObject<List<string>>());
            if (qt?["tactic_repeat"] != null)
                p.repeatTemplates.AddRange(qt["tactic_repeat"].ToObject<List<string>>());

            if (p.contradictionTemplates.Count == 0)
                p.contradictionTemplates.Add("{value_a}라고 했는데. 지금은 {value_b}인가.");
            if (p.repeatTemplates.Count == 0)
                p.repeatTemplates.Add("또 그 얘긴가.");

            return p;
        }

        public string PickDetective(Mood m, HashSet<string> used)
            => Pick(detective.TryGetValue(m, out var l) ? l : null, used, "…계속하게.");

        public string PickFallback(string key, HashSet<string> used)
            => Pick(fallback.TryGetValue(key, out var l) ? l : null, used, "다시. 천천히.");

        public string QuoteContradiction(Contradiction c)
        {
            string t = contradictionTemplates[Random.Range(0, contradictionTemplates.Count)];
            return t.Replace("{run_a}", c.runA.ToString())
                    .Replace("{value_a}", c.valueA ?? "")
                    .Replace("{value_b}", c.valueB ?? "");
        }

        public string QuoteTacticRepeat()
            => repeatTemplates[Random.Range(0, repeatTemplates.Count)];

        static string Pick(List<string> list, HashSet<string> used, string fallbackLine)
        {
            if (list == null || list.Count == 0) return fallbackLine;
            var avail = new List<string>();
            foreach (var s in list) if (!used.Contains(s)) avail.Add(s);
            if (avail.Count == 0) { used.Clear(); avail.AddRange(list); }
            string pick = avail[Random.Range(0, avail.Count)];
            used.Add(pick);
            return pick;
        }
    }
}
