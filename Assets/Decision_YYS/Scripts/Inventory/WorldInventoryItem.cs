using UnityEngine;

/// <summary>보드와 가방 사이를 이동할 수 있는 3D 아이템입니다.</summary>
[DisallowMultipleComponent]
public sealed class WorldInventoryItem : MonoBehaviour
{
    [SerializeField] private InventoryItemDefinition definition;
    public InventoryItemDefinition Definition => definition;
}
