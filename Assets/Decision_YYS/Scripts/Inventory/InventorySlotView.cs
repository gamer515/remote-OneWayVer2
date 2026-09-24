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

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (definition != null)
            owner?.BeginSlotDrag(itemIndex, definition, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (definition != null)
            owner?.UpdateSlotDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (definition != null)
            owner?.EndSlotDrag(eventData.position);
    }
}
