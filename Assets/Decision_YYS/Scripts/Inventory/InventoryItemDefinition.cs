using UnityEngine;

[CreateAssetMenu(menuName = "One Way/Inventory Item", fileName = "InventoryItem")]
public sealed class InventoryItemDefinition : ScriptableObject
{
    [SerializeField] private string itemId;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private GameObject worldPrefab;

    public string ItemId => itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;
    public Sprite Icon => icon;
    public GameObject WorldPrefab => worldPrefab;
}
