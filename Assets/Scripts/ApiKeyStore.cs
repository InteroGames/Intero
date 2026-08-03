using System;
using System.IO;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// API 키를 씬 밖에 둔다. 씬을 다시 만들어도 키는 살아남는다.
    ///
    /// 찾는 순서:
    ///   1. apikey.txt  (에디터는 프로젝트 루트, 빌드는 exe 옆)
    ///   2. 환경변수 OPENAI_API_KEY / ANTHROPIC_API_KEY
    ///
    /// apikey.txt 형식 - 둘 다 된다:
    ///   sk-...                      한 줄이면 현재 provider 키로 본다
    ///   openai=sk-...               여러 개를 같이 둘 때
    ///   anthropic=sk-ant-...
    ///
    /// 이 파일은 .gitignore에 있다. 커밋되지 않는다.
    /// </summary>
    public static class ApiKeyStore
    {
        const string FileName = "apikey.txt";

        public static string KeyFilePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));

        public static string Resolve(LLMClient.Provider provider)
        {
            string fromFile = FromFile(provider);
            if (!string.IsNullOrEmpty(fromFile)) return fromFile;

            string envName = provider == LLMClient.Provider.OpenAI
                ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
            try
            {
                string env = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            }
            catch { }

            return null;
        }

        static string FromFile(LLMClient.Provider provider)
        {
            try
            {
                if (!File.Exists(KeyFilePath)) return null;

                string prefix = provider == LLMClient.Provider.OpenAI ? "openai" : "anthropic";
                string bare = null;

                foreach (string raw in File.ReadAllLines(KeyFilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string name = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string value = line.Substring(eq + 1).Trim();
                        if (name == prefix && value.Length > 0) return value;
                    }
                    else if (bare == null)
                    {
                        bare = line;   // 라벨 없는 줄은 마지막 수단으로 쓴다
                    }
                }
                return bare;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ApiKeyStore] {e.Message}");
                return null;
            }
        }
    }
}
