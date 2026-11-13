using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class SaveIOService
{
    internal string DialogueReportPath => Path.Combine(BasePath, "LocalStoryTraining", "DialogueChanges.html");

    internal void RefreshDialogueReport()
    {
        string directory = Path.Combine(BasePath, "LocalStoryTraining");
        var html = new StringBuilder(
            "<!doctype html><html lang=\"ko\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<title>원문과 변경 대사</title><style>body{max-width:760px;margin:36px auto;padding:0 16px;" +
            "font:17px/1.6 system-ui;background:#faf8f2;color:#23303a}article{border:1px solid #d1c8b7;border-radius:8px;" +
            "padding:12px 18px;margin:14px 0;background:#fff}p{margin:7px 0;white-space:pre-wrap;overflow-wrap:anywhere}" +
            "strong{display:block}p:last-child{background:#e5f2dc;padding:8px;border-radius:4px}small{color:#4a5863}</style>" +
            "<body><h1>원문과 변경 대사</h1>");
        try
        {
            Directory.CreateDirectory(directory);
            foreach (string chapter in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                string chapterId = Path.GetFileName(chapter);
                string title = chapterId == "Chapter_1" ? "갱도" : chapterId == "Chapter_Coast" ? "해안" : chapterId;
                html.Append("<section><h2>").Append(WebUtility.HtmlEncode(title)).Append("</h2>");
                var files = Directory.EnumerateFiles(chapter, "*.jsonl", SearchOption.AllDirectories)
                    .Select(file => (file, parts: Path.GetRelativePath(chapter, file).Split(Path.DirectorySeparatorChar)))
                    .Where(item => item.parts.Length >= 3 && item.parts[1].StartsWith("For_Run_", StringComparison.Ordinal) &&
                        int.TryParse(item.parts[1].Substring("For_Run_".Length), out _))
                    .OrderByDescending(item => int.Parse(item.parts[1].Substring("For_Run_".Length)))
                    .ThenBy(item => item.file, StringComparer.Ordinal);
                foreach (var (file, parts) in files)
                {
                    string run = int.Parse(parts[1].Substring("For_Run_".Length)).ToString();
                    string story = Path.GetFileNameWithoutExtension(file);
                    foreach (string line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var messages = JObject.Parse(line)["messages"] as JArray;
                            if (messages?.Count != 2) continue;
                            string prompt = (string)messages[0]["content"] ?? "";
                            int payloadStart = prompt.LastIndexOf("\n\n", StringComparison.Ordinal);
                            if (payloadStart < 0) continue;
                            var input = JObject.Parse(prompt.Substring(payloadStart + 2));
                            var output = JObject.Parse((string)messages[1]["content"] ?? "{}");
                            string original = (string)input["original"], revised = (string)output["text"];
                            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(revised) || original == revised) continue;
                            string label = $"{run}회차 · {story} · {(string)input["npc"]?["name"]} · {(string)input["emotion"]}";
                            html.Append("<article><small>").Append(WebUtility.HtmlEncode(label)).Append("</small><p><strong>원문</strong>")
                                .Append(WebUtility.HtmlEncode(original)).Append("</p><p><strong>변경 대사</strong>")
                                .Append(WebUtility.HtmlEncode(revised)).Append("</p></article>");
                        }
                        catch (JsonException error)
                        {
                            Debug.LogWarning("[LocalStory] 대사 비교에서 읽을 수 없는 줄을 건너뜁니다. " + error.Message);
                        }
                    }
                }
                html.Append("</section>");
            }
            if (!html.ToString().Contains("<article>")) html.Append("<p>검사를 통과해 저장된 변경 대사가 아직 없습니다.</p>");
            WriteTextAtomically(DialogueReportPath, html.Append("</body></html>").ToString());
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[LocalStory] 대사 비교 HTML 갱신 실패: " + error.Message);
        }
    }
}
