using System.Text.RegularExpressions;

namespace Interrogation
{
    public enum InputKind
    {
        Normal,      // 그대로 보여준다
        Empty,       // 입을 열지 않는다
        Mashing,     // 자판을 뭉갰다
        Repetition,  // ㅋㅋㅋㅋ, 아아아아
        Yes,         // ㅇㅇ, ㅇㅋ
        No,          // ㄴㄴ
        Profanity    // 욕설
    }

    /// <summary>
    /// 플레이어 입력의 형태를 코드로 분류한다. LLM을 부르지 않는다.
    ///
    /// 목적은 두 가지:
    ///   1. 자판을 뭉갠 입력이 화면에 그대로 나가서 몰입이 깨지는 걸 막는다.
    ///      대신 "(알아들을 수 없는 소리를 낸다)" 같은 지문으로 바꿔 보여준다.
    ///   2. 그래도 벌점은 그대로 매긴다. 발광이 공짜여서는 안 된다.
    ///
    /// 원문은 절대 건드리지 않는다. 바뀌는 것은 화면 표시뿐이다
    /// (히든 엔딩이 플레이어의 실제 입력을 인용해야 하므로).
    /// </summary>
    public static class InputClassifier
    {
        static readonly Regex Profane = new Regex(
            @"(씨발|시발|ㅅㅂ|병신|ㅂㅅ|지랄|좆|엿먹|개새|미친놈|닥쳐|꺼져)",
            RegexOptions.IgnoreCase);

        public static InputKind Classify(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return InputKind.Empty;

            string s = input.Trim();

            if (Profane.IsMatch(s)) return InputKind.Profanity;

            string letters = OnlyLetters(s);
            if (letters.Length == 0) return InputKind.Mashing;   // 기호나 숫자만 두드린 경우

            // 초성체 단답. 짧을 때만 본다 - 긴 문장 안의 ㅋㅋ는 말투일 뿐이다.
            if (letters.Length <= 4)
            {
                if (Regex.IsMatch(letters, @"^[ㅇㅋ]+$")) return InputKind.Yes;
                if (Regex.IsMatch(letters, @"^[ㄴ]+$")) return InputKind.No;
            }

            if (IsRepetition(letters)) return InputKind.Repetition;

            // 낱자(ㅁㄴㅇ)가 완성형(가–힣)보다 많으면 자판을 뭉갠 것이다.
            CountKorean(s, out int syllables, out int jamo);
            if (jamo > syllables && jamo + syllables >= 3) return InputKind.Mashing;

            return InputKind.Normal;
        }

        /// <summary>대사 풀에서 지문을 꺼낼 때 쓰는 키. Normal이면 null.</summary>
        public static string PoolKey(InputKind kind)
        {
            switch (kind)
            {
                case InputKind.Mashing: return "mashing";
                case InputKind.Repetition: return "repetition";
                case InputKind.Yes: return "yes";
                case InputKind.No: return "no";
                case InputKind.Profanity: return "profanity";
                case InputKind.Empty: return "empty";
            }
            return null;
        }

        /// <summary>
        /// 형사에게 넘길 상황 설명. 자판을 뭉갠 원문을 LLM에 그대로 보내면
        /// 형사가 그 글자를 따라 읽는 이상한 대사가 나온다.
        /// </summary>
        public static string Describe(InputKind kind)
        {
            switch (kind)
            {
                case InputKind.Mashing: return "자네가 말이 되지 않는 소리를 낸다. 알아들을 수 없다.";
                case InputKind.Repetition: return "자네가 같은 소리를 계속 반복한다.";
                case InputKind.Yes: return "자네가 짧게 긍정한다. 그뿐이다.";
                case InputKind.No: return "자네가 짧게 부정한다. 그뿐이다.";
                case InputKind.Profanity: return "자네가 욕설을 뱉는다.";
                case InputKind.Empty: return "자네가 아무 말도 하지 않는다.";
            }
            return null;
        }

        /// <summary>LLM 추출을 건너뛰어도 되는가. 뭉갠 입력은 보낼 이유가 없다.</summary>
        public static bool SkipExtraction(InputKind kind)
            => kind == InputKind.Mashing || kind == InputKind.Repetition || kind == InputKind.Empty;

        /// <summary>형태만으로 정해지는 태틱. Normal이면 LLM 추출을 따른다.</summary>
        public static bool TacticFor(InputKind kind, out Tactic tactic)
        {
            switch (kind)
            {
                case InputKind.Mashing:
                case InputKind.Repetition:
                    tactic = Tactic.NONSENSE; return true;
                case InputKind.Empty:
                    tactic = Tactic.SILENCE; return true;
                case InputKind.Profanity:
                    tactic = Tactic.COUNTER; return true;
            }
            tactic = Tactic.DENY;
            return false;
        }

        // ------------------------------------------------------------------

        static string OnlyLetters(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                if (IsSyllable(c) || IsJamo(c) || char.IsLetter(c)) sb.Append(c);
            return sb.ToString();
        }

        static void CountKorean(string s, out int syllables, out int jamo)
        {
            syllables = 0; jamo = 0;
            foreach (char c in s)
            {
                if (IsSyllable(c)) syllables++;
                else if (IsJamo(c)) jamo++;
            }
        }

        static bool IsSyllable(char c) => c >= 0xAC00 && c <= 0xD7A3;   // 가 – 힣
        static bool IsJamo(char c) => c >= 0x3131 && c <= 0x318E;       // ㄱ – ㅣ

        /// <summary>같은 글자가 네 번 넘게 이어지는가.</summary>
        static bool IsRepetition(string letters)
        {
            if (letters.Length < 4) return false;

            int run = 1;
            for (int i = 1; i < letters.Length; i++)
            {
                run = letters[i] == letters[i - 1] ? run + 1 : 1;
                if (run >= 4) return true;
            }

            // 서로 다른 글자가 두세 개뿐인데 길기만 한 경우도 반복으로 본다 (ㅋㅎㅋㅎㅋㅎ)
            var distinct = new System.Collections.Generic.HashSet<char>(letters);
            return letters.Length >= 6 && distinct.Count <= 2;
        }
    }
}
