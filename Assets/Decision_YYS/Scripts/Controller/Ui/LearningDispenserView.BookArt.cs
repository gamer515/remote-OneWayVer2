using TMPro;
using UnityEngine;

public sealed partial class LearningDispenserView
{
    [SerializeField] private Material bookSpineTrim;
    private static readonly string[] BookAbilityNames = { "힘", "속도", "지능", "매력" };
    private static readonly Color32[] BookCoverColors =
    {
        new(143, 46, 40, 255), new(38, 72, 144, 255), new(148, 98, 27, 255), new(30, 103, 97, 255)
    };

    private void AddBookCoverDetails(Transform book, MineLearningJob job, Bounds bounds)
    {
        AddSpineBands(book);
        if (bookHoverLabel == null || bookHoverLabel.font == null) return;

        // The shared book geometry provides two covers, a spine and the page block.
        // Place the printed details after measuring the untouched physics shape.
        string title = string.IsNullOrWhiteSpace(job.bookName) ? BookAbilityNames[job.ability] + " 책" : job.bookName;
        uint titlePattern = 17;
        foreach (char letter in title) titlePattern = unchecked(titlePattern * 31 + letter);
        Color cover = BookCoverColors[job.ability];
        Color ink = new Color32(255, 229, 163, 255);
        string number = job.bookNumber > 0 ? job.bookNumber.ToString("00") : "";
        var center = bounds.center;
        Quaternion front = Quaternion.Euler(90f, 0f, 0f);
        float top = bounds.max.y + .001f;

        AddBookPrint(book, "Cover stamp", (titlePattern % 3) switch
            {
                0 => "◆", 1 => "▲", _ => "●"
            }, new Vector3(center.x, top, center.z + .105f), front,
            new Vector2(.48f, .075f), 2.5f, Color.Lerp(ink, cover, (titlePattern % 5) * .07f));
        AddBookPrint(book, "Cover title", title, new Vector3(center.x, top, center.z + .013f), front,
            new Vector2(.56f, .19f), 4.8f, ink);
        AddBookPrint(book, "Cover number", BookAbilityNames[job.ability] + "  " + number,
            new Vector3(center.x, top, center.z - .11f), front, new Vector2(.50f, .095f), 3.8f, ink);
        if (number.Length > 0)
        {
            AddBookPrint(book, "Page edge number", number,
                new Vector3(center.x, center.y, center.z - .180f), Quaternion.identity,
                new Vector2(.32f, .22f), 5.4f, cover);
            AddBookPrint(book, "Spine number", number,
                new Vector3(center.x, center.y, bounds.max.z + .003f), Quaternion.Euler(0f, 180f, -90f),
                new Vector2(.21f, .14f), 4.3f, ink);
        }
    }

    private void AddSpineBands(Transform book)
    {
        var spine = book.Find("Spine");
        if (bookSpineTrim == null || spine == null || !spine.TryGetComponent<MeshFilter>(out var shape)) return;

        float face = spine.localScale.z * .5f + .008f;
        for (int end = -1; end <= 1; end += 2)
        {
            for (int line = 0; line < 2; line++)
            {
                var band = new GameObject("Gilt spine band", typeof(MeshFilter), typeof(MeshRenderer));
                band.transform.SetParent(book, false);
                band.transform.localPosition = spine.localPosition + new Vector3(
                    end * spine.localScale.x * (.375f - line * .055f), 0f, face);
                band.transform.localScale = new Vector3(line == 0 ? .022f : .014f, spine.localScale.y * .94f, .012f);
                band.GetComponent<MeshFilter>().sharedMesh = shape.sharedMesh;
                band.GetComponent<MeshRenderer>().sharedMaterial = bookSpineTrim;
            }
        }
    }

    private void AddBookPrint(Transform book, string label, string content, Vector3 center, Quaternion rotation,
        Vector2 area, float maxSize, Color color)
    {
        var print = new GameObject(label).AddComponent<TextMeshPro>();
        print.transform.SetParent(book, false);
        print.transform.SetLocalPositionAndRotation(center, rotation);
        print.rectTransform.sizeDelta = area;
        print.font = bookHoverLabel.font;
        print.color = color;
        print.text = content;
        print.alignment = TextAlignmentOptions.Center;
        print.enableAutoSizing = true;
        print.fontSizeMin = 1.25f;
        print.fontSizeMax = maxSize;
        print.textWrappingMode = TextWrappingModes.NoWrap;
        print.overflowMode = TextOverflowModes.Truncate;
        print.richText = false;
    }
}
