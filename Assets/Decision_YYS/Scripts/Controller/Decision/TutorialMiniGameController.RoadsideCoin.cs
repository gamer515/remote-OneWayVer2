using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class TutorialMiniGameController
{
    private Coroutine roadsideCoinRoutine;
    private GameObject roadsideCoinCanvas;
    private GameObject roadsideCoinStage;
    private RenderTexture roadsideCoinTexture;

    internal void StartRoadsideCoinToss(bool resultHeads, bool won, UiController ui, Action<bool> closed)
    {
        ClearRoadsideCoin();
        var source = goldCoinPrefab.GetComponentInChildren<MeshFilter>();
        var materialSource = source != null ? source.GetComponent<MeshRenderer>() : null;
        if (source == null || source.sharedMesh == null || materialSource == null)
        {
            closed?.Invoke(false);
            return;
        }
        roadsideCoinRoutine = StartCoroutine(RevealRoadsideCoin(resultHeads, won, ui, closed, source, materialSource));
    }

    private IEnumerator RevealRoadsideCoin(bool resultHeads, bool won, UiController ui, Action<bool> closed,
        MeshFilter source, MeshRenderer materialSource)
    {
        var font = ui.DialogueFont;
        roadsideCoinCanvas = new GameObject("RoadsideCoinReveal", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler));
        var canvas = roadsideCoinCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var scaler = roadsideCoinCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        var picture = CreateCoinElement("Coin", canvas.transform, new Vector2(350f, 350f), Vector2.zero)
            .gameObject.AddComponent<UnityEngine.UI.RawImage>();
        picture.raycastTarget = false;

        var result = CreateCoinElement("Result", canvas.transform, new Vector2(236f, 48f), new Vector2(0f, -172f));
        var face = CreateCoinLabel(result, -50f, font, resultHeads ? "앞면" : "뒷면");
        face.alignment = TextAlignmentOptions.MidlineRight;
        var mark = CreateCoinLabel(result, 0f, TMP_Settings.defaultFontAsset, "·");
        mark.rectTransform.sizeDelta = new Vector2(18f, 48f);
        var verdict = CreateCoinLabel(result, 50f, font, won ? "승리" : "패배");
        verdict.alignment = TextAlignmentOptions.MidlineLeft;
        verdict.color = won ? new Color32(255, 218, 126, 255) : new Color32(255, 177, 164, 255);
        result.gameObject.SetActive(false);

        var coin = CreateRoadsideCoinModel(source, materialSource);
        picture.texture = roadsideCoinTexture;
        Vector3 boardCenter = GetBoardCenter();
        boardCenter.y = GetDuelFloorHeight();
        var boardCoin = new GameObject("RoadsideBoardCoin").transform;
        boardCoin.SetParent(roadsideCoinStage.transform, false);
        boardCoin.position = boardCenter;
        var boardMesh = CreateCoinVisual(boardCoin, source, materialSource, coinSize);
        boardCenter.y += boardMesh.bounds.extents.y;

        // Local +Y is heads: it faces the reveal camera or the board camera above the coin.
        float finalAngle = resultHeads ? -90f : 90f;
        float boardFinalAngle = resultHeads ? 0f : 180f;
        const float spinSeconds = 1.25f, resultSeconds = 1.65f;
        float elapsed = 0f;
        while (elapsed < spinSeconds && CoinScreenIsOpen(ui) && !CoinClosePressed())
        {
            float t = Mathf.Clamp01(elapsed / spinSeconds);
            float eased = 1f - (1f - t) * (1f - t);
            coin.rotation = Quaternion.AngleAxis((1080f + finalAngle) * eased, Vector3.right);
            float boardAngle = (1080f + boardFinalAngle) * eased;
            boardCoin.rotation = Quaternion.AngleAxis(boardAngle, Vector3.right);
            float edgeClearance = coinSize * .5f * Mathf.Abs(Mathf.Sin(boardAngle * Mathf.Deg2Rad));
            float lift = Mathf.Max(.65f * Mathf.Sin(Mathf.PI * t), edgeClearance);
            boardCoin.position = boardCenter + Vector3.up * lift;
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        bool interrupted = !CoinScreenIsOpen(ui) || CoinClosePressed();
        if (!interrupted)
        {
            coin.rotation = Quaternion.AngleAxis(finalAngle, Vector3.right);
            boardCoin.SetPositionAndRotation(boardCenter, Quaternion.AngleAxis(boardFinalAngle, Vector3.right));
            result.gameObject.SetActive(true);
            elapsed = 0f;
            while (elapsed < resultSeconds && CoinScreenIsOpen(ui) && !CoinClosePressed())
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            interrupted = !CoinScreenIsOpen(ui) || CoinClosePressed();
        }

        roadsideCoinRoutine = null;
        ClearRoadsideCoin();
        closed?.Invoke(interrupted);
    }

    private Transform CreateRoadsideCoinModel(MeshFilter source, MeshRenderer materialSource)
    {
        roadsideCoinStage = new GameObject("RoadsideCoinStage");
        Vector3 center = new Vector3(0f, -1000f, 0f);
        var stage = roadsideCoinStage.transform;
        stage.position = center;
        var pivot = new GameObject("CoinRotation").transform;
        pivot.SetParent(stage, false);
        CreateCoinVisual(pivot, source, materialSource, 1f);

        roadsideCoinTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        var camera = new GameObject("CoinCamera").AddComponent<Camera>();
        camera.transform.SetParent(stage, false);
        camera.transform.localPosition = new Vector3(0f, 0f, -3f);
        camera.orthographic = true;
        camera.orthographicSize = .63f;
        camera.farClipPlane = 6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = false;
        camera.depth = -100f;
        camera.targetTexture = roadsideCoinTexture;
        var light = new GameObject("CoinLight").AddComponent<Light>();
        light.transform.SetParent(stage, false);
        light.transform.localPosition = new Vector3(-1f, 1.5f, -2f);
        light.type = LightType.Point;
        light.range = 6f;
        light.intensity = 4f;
        return pivot;
    }

    private MeshRenderer CreateCoinVisual(Transform pivot, MeshFilter source, MeshRenderer materialSource, float size)
    {
        var coin = new GameObject("GoldCoinVisual", typeof(MeshFilter), typeof(MeshRenderer));
        var meshRenderer = coin.GetComponent<MeshRenderer>();
        coin.layer = source.gameObject.layer;
        coin.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
        meshRenderer.sharedMaterials = materialSource.sharedMaterials;
        coin.transform.SetParent(pivot, false);
        coin.transform.localRotation = Quaternion.Inverse(goldCoinPrefab.transform.rotation) * source.transform.rotation;
        FitLargestRenderer(coin, size);
        coin.transform.position += pivot.position - meshRenderer.bounds.center;
        return meshRenderer;
    }

    private static RectTransform CreateCoinElement(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var element = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        element.SetParent(parent, false);
        element.sizeDelta = size;
        element.anchoredPosition = position;
        return element;
    }

    private static TextMeshProUGUI CreateCoinLabel(Transform parent, float x, TMP_FontAsset font, string text)
    {
        var label = CreateCoinElement(text, parent, new Vector2(80f, 48f), new Vector2(x, 0f))
            .gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = 36f;
        label.alignment = TextAlignmentOptions.Midline;
        label.color = new Color32(255, 241, 211, 255);
        label.outlineColor = new Color32(31, 24, 18, 255);
        label.outlineWidth = .18f;
        label.raycastTarget = false;
        return label;
    }

    private static bool CoinScreenIsOpen(UiController ui) =>
        ui != null && ui.isActiveAndEnabled && ui.CurrentDisplayMode == UiController.DisplayMode.Event;

    private static bool CoinClosePressed() => Keyboard.current?.escapeKey.wasPressedThisFrame == true;

    private void ClearRoadsideCoin()
    {
        if (roadsideCoinRoutine != null) StopCoroutine(roadsideCoinRoutine);
        roadsideCoinRoutine = null;
        if (roadsideCoinCanvas != null)
        {
            roadsideCoinCanvas.SetActive(false);
            Destroy(roadsideCoinCanvas);
        }
        if (roadsideCoinStage != null)
        {
            roadsideCoinStage.SetActive(false);
            Destroy(roadsideCoinStage);
        }
        if (roadsideCoinTexture != null)
        {
            roadsideCoinTexture.Release();
            Destroy(roadsideCoinTexture);
        }
        roadsideCoinCanvas = roadsideCoinStage = null;
        roadsideCoinTexture = null;
    }
}
