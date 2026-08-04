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
        readonly List<string> openingRepeatTemplates = new List<string>();
        readonly List<string> introLines = new List<string>();
        readonly Dictionary<string, List<string>> clerkLines = new Dictionary<string, List<string>>();
        readonly List<string> briefingLines = new List<string>();
        readonly Dictionary<string, List<string>> playerActions = new Dictionary<string, List<string>>();
        readonly Dictionary<string, (string persona, string arrival)> interrogators
            = new Dictionary<string, (string, string)>();

        /// <summary>취조관 프로필 텍스트. key는 InterrogatorType 이름.</summary>
        public (string persona, string arrival) Interrogator(string key)
            => interrogators.TryGetValue(key, out var v) ? v : (null, null);

        public string BriefingTitle { get; private set; }
        public string BriefingFooter { get; private set; }
        public IReadOnlyList<string> BriefingLines => briefingLines;
        readonly List<string> runOpenings = new List<string>();

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
            if (qt?["opening_repeat"] != null)
                p.openingRepeatTemplates.AddRange(qt["opening_repeat"].ToObject<List<string>>());

            var brief = root["briefing"] as JObject;
            if (brief != null)
            {
                p.BriefingTitle = (string)brief["title"];
                p.BriefingFooter = (string)brief["footer"];
                if (brief["lines"] != null)
                    p.briefingLines.AddRange(brief["lines"].ToObject<List<string>>());
            }

            var clerk = root["clerk"] as JObject;
            if (clerk != null)
                foreach (var kv in clerk)
                    if (kv.Value is JArray) p.clerkLines[kv.Key] = kv.Value.ToObject<List<string>>();

            var interro = root["interrogators"] as JObject;
            if (interro != null)
                foreach (var kv in interro)
                {
                    if (!(kv.Value is JObject o)) continue;
                    p.interrogators[kv.Key] = ((string)o["persona"], (string)o["arrival"]);
                }

            var pa = root["player_action"] as JObject;
            if (pa != null)
                foreach (var kv in pa)
                    if (kv.Value is JArray) p.playerActions[kv.Key] = kv.Value.ToObject<List<string>>();

            if (root["intro"] != null)
                p.introLines.AddRange(root["intro"].ToObject<List<string>>());
            if (root["run_opening"] != null)
                p.runOpenings.AddRange(root["run_opening"].ToObject<List<string>>());

            if (p.contradictionTemplates.Count == 0)
                p.contradictionTemplates.Add("{value_a}라고 했는데. 지금은 {value_b}인가.");
            if (p.repeatTemplates.Count == 0)
                p.repeatTemplates.Add("또 그 얘긴가.");
            if (p.openingRepeatTemplates.Count == 0)
                p.openingRepeatTemplates.Add("자네는 늘 그렇게 시작하더군.");

            return p;
        }

        public IReadOnlyList<string> Intro => introLines;

        /// <summary>말이 아닌 입력을 대신할 지문. 없으면 null이고, 그때는 원문을 그대로 쓴다.</summary>
        public string PlayerAction(string key)
        {
            if (key == null || !playerActions.TryGetValue(key, out var l) || l.Count == 0) return null;
            return l[Random.Range(0, l.Count)];
        }

        /// <summary>서기의 사무적인 말. key: opening / between / closing</summary>
        public string Clerk(string key)
        {
            if (!clerkLines.TryGetValue(key, out var l) || l.Count == 0) return null;
            return l[Random.Range(0, l.Count)];
        }

        public string PickRunOpening(int runCount)
            => runOpenings.Count == 0 ? null : runOpenings[(runCount - 2 + runOpenings.Count) % runOpenings.Count];

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

        /// <summary>판을 넘어 같은 수로 시작했을 때만. 이 게임이 팔려는 순간이다.</summary>
        public string QuoteOpeningRepeat(int runCount)
            => openingRepeatTemplates[Random.Range(0, openingRepeatTemplates.Count)]
               .Replace("{times}", runCount.ToString());

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
