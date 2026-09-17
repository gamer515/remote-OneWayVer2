using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace JourneyMapKit
{
    public sealed class JourneyBoardInput : MonoBehaviour
    {
        public JourneyBoardReferences board;
        public Camera inputCamera;
        [Tooltip("Only set when the board camera is displayed through a RawImage.")]
        public RawImage boardViewport;
        public UnityEvent onYellowPressed=new UnityEvent();
        public UnityEvent<int> onGearSelected=new UnityEvent<int>();
        public int SelectedIndex{get;private set;}=-1;
        bool yellowInteractable=true;
        bool dragging;Vector2 origin;Quaternion restRotation;Vector3 restButton;Coroutine press;
        void Awake()
        {
            if(!board)board=GetComponent<JourneyBoardReferences>();
            if(!inputCamera)inputCamera=Camera.main;
            if(board){restRotation=board.gear.localRotation;restButton=board.yellowButton.transform.localPosition;}
        }
        void Update()
        {
            if(!board||!inputCamera)return;
            Vector2 p;bool down,held,up;
#if ENABLE_INPUT_SYSTEM
            if(Mouse.current==null)return;
            p=Mouse.current.position.ReadValue();down=Mouse.current.leftButton.wasPressedThisFrame;held=Mouse.current.leftButton.isPressed;up=Mouse.current.leftButton.wasReleasedThisFrame;
#else
            p=Input.mousePosition;down=Input.GetMouseButtonDown(0);held=Input.GetMouseButton(0);up=Input.GetMouseButtonUp(0);
#endif
            if(down && TryRay(p,out Ray ray))
            {
                foreach(var hit in Physics.RaycastAll(ray,1000,inputCamera.cullingMask))
                {
                    if(hit.collider==board.yellowButton){PressYellow();break;}
                    if(hit.collider==board.gearHandleCollider){dragging=true;origin=p;break;}
                }
            }
            if(dragging && held)
            {
                Vector2 d=p-origin;
                board.gear.localRotation=restRotation*Quaternion.Euler(Mathf.Clamp(d.y*.12f,-22,22),0,Mathf.Clamp(-d.x*.12f,-22,22));
                SelectGear(d.sqrMagnitude>100?d.x<0?(d.y>=0?0:1):(d.y>=0?2:3):-1);
            }
            if(up)dragging=false;
        }
        bool TryRay(Vector2 screen,out Ray ray)
        {
            if(!boardViewport){ray=inputCamera.ScreenPointToRay(screen);return true;}
            var rt=boardViewport.rectTransform;var canvas=boardViewport.canvas;
            Camera uiCamera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt,screen,uiCamera,out Vector2 p)||!rt.rect.Contains(p)){ray=default;return false;}
            Vector2 uv=(p-rt.rect.min)/rt.rect.size;ray=inputCamera.ViewportPointToRay(uv);return true;
        }
        public void SelectGear(int index)
        {
            index=Mathf.Clamp(index,-1,3);if(index==SelectedIndex)return;SelectedIndex=index;onGearSelected.Invoke(index);
        }
        public void PressYellow()
        {
            if(!isActiveAndEnabled||!board)return;
            // 눌림 모션은 유지하되, 게임 상태가 잠근 경우 명령은 보내지 않습니다.
            if(press!=null)StopCoroutine(press);press=StartCoroutine(AnimatePress());
            if(yellowInteractable)onYellowPressed.Invoke();
        }
        public void SetYellowInteractable(bool interactable){yellowInteractable=interactable;}
        public void ResetSelection()
        {
            dragging=false;
            if(board&&board.gear)board.gear.localRotation=restRotation;
            SelectGear(-1);
        }
        IEnumerator AnimatePress()
        {
            float t=0;
            while(t<.18f){t+=Time.deltaTime;board.yellowButton.transform.localPosition=restButton+Vector3.down*(Mathf.Sin(Mathf.Clamp01(t/.18f)*Mathf.PI)*.045f);yield return null;}
            board.yellowButton.transform.localPosition=restButton;press=null;
        }
        void OnDisable()
        {
            dragging=false;if(press!=null)StopCoroutine(press);press=null;
            if(board){board.yellowButton.transform.localPosition=restButton;board.gear.localRotation=restRotation;}
        }
    }
}
