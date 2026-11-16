using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using OneWay.Decision.DirectLlm;
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
            if (Directory.Exists(GeneratedRoot))
            {
                html.Append("<section><h2>생성 대사</h2>");
                foreach (string runDirectory in Directory.EnumerateDirectories(GeneratedRoot, "For_Run_*")
                    .OrderByDescending(path => path, StringComparer.Ordinal))
                {
                    string queuePath = Path.Combine(runDirectory, "Queue.json");
                    if (!File.Exists(queuePath)) continue;
                    var queue = JsonUtility.FromJson<LocalStoryDialogueQueueData>(File.ReadAllText(queuePath));
                    if (queue?.jobs == null) continue;
                    foreach (var job in queue.jobs.Where(job => job.model == DirectStoryGenerator.ModelName && job.status == "Ready"))
                        foreach (var line in job.emotionLines.Where(line => !string.IsNullOrWhiteSpace(line.generatedText)))
                            AppendDialogueComparison(html, $"{job.targetRun}회차 · {job.storyPath} · {line.name} · {job.emotion}" +
                                (string.IsNullOrEmpty(line.apiProvider) ? "" : $" · {line.apiProvider} / {line.apiModel}"),
                                line.originalText, line.generatedText);
                }
                html.Append("</section>");
            }
            if (!html.ToString().Contains("<article>")) html.Append("<p>저장된 변경 대사가 아직 없습니다.</p>");
            WriteTextAtomically(DialogueReportPath, html.Append("</body></html>").ToString());
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[LocalStory] 대사 비교 HTML 갱신 실패: " + error.Message);
        }
    }

    private static void AppendDialogueComparison(StringBuilder html, string label, string original, string revised)
    {
        html.Append("<article><small>").Append(WebUtility.HtmlEncode(label)).Append("</small><p><strong>원문</strong>")
            .Append(WebUtility.HtmlEncode(original)).Append("</p><p><strong>변경 대사</strong>")
            .Append(WebUtility.HtmlEncode(revised)).Append("</p></article>");
    }
}
