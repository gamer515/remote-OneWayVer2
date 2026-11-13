using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class InventorySlotView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private UnityEngine.UI.Image icon;
    [SerializeField] private TextMeshProUGUI itemName;

    private BackpackInventoryController owner;
    private InventoryItemDefinition definition;
    private int itemIndex = -1;
    private MineLearningJob studyBook;

    public void Bind(
        BackpackInventoryController inventoryOwner,
        InventoryItemDefinition itemDefinition,
        int index)
    {
        owner = inventoryOwner;
        definition = itemDefinition;
        itemIndex = index;

        if (icon != null)
        {
            icon.sprite = definition != null ? definition.Icon : null;
            icon.enabled = definition != null && definition.Icon != null;
            icon.raycastTarget = definition != null;
        }

        if (itemName != null)
            itemName.text = definition != null ? definition.DisplayName : string.Empty;
    }

    public void BindBook(BackpackInventoryController inventoryOwner, MineLearningJob book)
    {
        owner = inventoryOwner;
        studyBook = book;
        if (icon != null) { icon.enabled = false; icon.raycastTarget = false; }
        if (itemName != null) itemName.text = book.bookName;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (studyBook != null)
            owner?.BeginBookSlotDrag(studyBook, eventData.position);
        else if (definition != null)
            owner?.BeginSlotDrag(itemIndex, definition, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (definition != null || studyBook != null)
            owner?.UpdateSlotDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (definition != null || studyBook != null)
            owner?.EndSlotDrag(eventData.position);
    }
}
