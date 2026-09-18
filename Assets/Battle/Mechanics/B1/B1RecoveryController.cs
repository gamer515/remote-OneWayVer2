using System.Collections;
using UnityEngine;

public sealed class B1RecoveryController
{
    public IEnumerator Run(
        BattleContext context,
        B1ScrollMovement movement)
    {
        const string warning = "뒤로 밀려나면 안 된다";

        // 한 글자가 나타나는 간격. 작을수록 빠름.
        const float typingInterval = 0.05f;

        context.Clock.Paused = true;
        context.Player.SetPaused(true);

        int previousVisibleCharacters =
            context.Message.maxVisibleCharacters;

        try
        {
            context.Message.maxVisibleCharacters = 0;
            context.ShowMessage(warning, true);
            context.Message.ForceMeshUpdate();

            int totalCharacters =
                context.Message.textInfo.characterCount;

            float startTime = Time.realtimeSinceStartup;
            float waitSeconds = Mathf.Max(
                0f, context.Stage.recoverySeconds);

            bool fullyShown = false;

            // 경고창이 열린 프레임의 클릭은 무시.
            yield return null;

            while (Time.realtimeSinceStartup - startTime < waitSeconds)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    if (!fullyShown)
                    {
                        // 출력 중 클릭: 전체 글자 표시.
                        context.Message.maxVisibleCharacters =
                            totalCharacters;

                        fullyShown = true;
                    }
                    else
                    {
                        // 닫기 클릭이 플레이어 전진에도 사용되지 않도록
                        // 다음 프레임에 전투를 재개.
                        yield return null;
                        break;
                    }
                }

                if (!fullyShown)
                {
                    float elapsed =
                        Time.realtimeSinceStartup - startTime;

                    int visibleCharacters = Mathf.Min(
                        totalCharacters,
                        Mathf.FloorToInt(elapsed / typingInterval));

                    context.Message.maxVisibleCharacters =
                        visibleCharacters;

                    fullyShown =
                        visibleCharacters >= totalCharacters;
                }

                yield return null;
            }
        }
        finally
        {
            // 다른 대화창의 글자가 잘리지 않도록 복원.
            context.Message.maxVisibleCharacters =
                previousVisibleCharacters;

            context.HideMessage();
            context.Clock.Paused = false;
            context.Player.SetPaused(false);

            movement.GiveGrace(context.Stage.recoveryGrace);
        }
    }
}