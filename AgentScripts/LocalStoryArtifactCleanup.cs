using System;

// Kept only for old command entry points. The previous duplicate cleaner assumes report/HTML sidecars.
public static class LocalStoryArtifactCleanup
{
    public static string Plan() => "통합 Index/결과 세 파일과 번호가 붙은 Edits를 사용합니다. 정리는 OrganizeLocalStoryTests.ps1에서만 실행하세요.";
    public static string Apply() => throw new InvalidOperationException("구형 삭제 도구는 중지됐습니다. AgentScripts/OrganizeLocalStoryTests.ps1을 사용하세요.");
}
