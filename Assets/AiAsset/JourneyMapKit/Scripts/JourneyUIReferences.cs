using UnityEngine;
using UnityEngine.UI;

namespace JourneyMapKit
{
    public sealed class JourneyUIReferences : MonoBehaviour
    {
        public RectTransform storyFrame;
        public RectTransform storyContent;
        public RawImage landscapeView;
        public RawImage portraitView;
        public RawImage boardsView;
        public RawImage suppliesView;
        public Button[] supplyButtons;
        public Image[] inventoryIcons;
        public JourneyMoodGraphic mood;

        public void SetLandscape(Texture texture) { landscapeView.texture=texture;landscapeView.gameObject.SetActive(texture!=null); }
        public void SetPortrait(Texture texture) { portraitView.texture=texture;portraitView.gameObject.SetActive(texture!=null); }
        public void SetInventoryIcon(int index,Sprite sprite)
        {
            if(index<0 || index>=inventoryIcons.Length)return;
            inventoryIcons[index].sprite=sprite;inventoryIcons[index].enabled=sprite!=null;
        }
    }
}
