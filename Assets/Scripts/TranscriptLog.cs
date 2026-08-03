using System;
using System.IO;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// 플레이 로그를 파일로 남긴다. 밸런싱(D6)과 대화 흐름 진단용.
    /// 위치: 프로젝트 루트/PlayLogs/session_*.log (Assets 밖이라 임포트 안 됨)
    /// </summary>
    public static class TranscriptLog
    {
        static string path;

        public static void Begin()
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "..", "PlayLogs");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                Write($"# 세션 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch { path = null; }   // 로그 실패가 게임을 멈추면 안 된다
        }

        public static void Write(string line)
        {
            if (path == null) return;
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { }
        }
    }
}
