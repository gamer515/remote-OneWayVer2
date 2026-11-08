using System.Collections.Generic;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LearningDispenserAssetKit
{
    /// <summary>Standalone physical asset demo. No game economy, save service, UI, or timer text dependency.</summary>
    public sealed class LearningDispenserAsset : MonoBehaviour
    {
        public Camera inputCamera;
        public BoxCollider boardSurface;
        public Transform[] boardSlots;
        public BoxCollider rackSurface;
        public Transform[] rackSlots;
        public Transform outlet, receiver, spawnedBooks;
        public Collider[] selectionButtons;
        public GameObject[] bookPrefabs;
        private readonly List<Transform> books=new List<Transform>();
        private readonly HashSet<Transform> arriving=new HashSet<Transform>();
        private Transform held;
        private Plane dragPlane;
        private Vector3 offset, previousPosition;
        private Quaternion previousRotation;
        public int BookCount => books.Count;
        public bool IsHoldingBook => held != null;

        private void Awake() {if(inputCamera==null)inputCamera=Camera.main;}
        private void Update()
        {
            if(inputCamera==null) {inputCamera=Camera.main;if(inputCamera==null)return;}
#if ENABLE_INPUT_SYSTEM
            var pointer=Pointer.current;
            if(pointer==null) {Cancel();return;}
            Vector2 p=pointer.position.ReadValue();
            bool down=pointer.press.wasPressedThisFrame, pressed=pointer.press.isPressed, up=pointer.press.wasReleasedThisFrame;
#else
            Vector2 p=Input.mousePosition;
            bool down=Input.GetMouseButtonDown(0), pressed=Input.GetMouseButton(0), up=Input.GetMouseButtonUp(0);
#endif
            if(down && !BeginDrag(p))
            {
                Ray ray=inputCamera.ScreenPointToRay(p);
                for(int i=0;i<selectionButtons.Length;i++) if(selectionButtons[i]!=null && selectionButtons[i].Raycast(ray,out _,1000)) {Dispense(i);break;}
            }
            if(pressed && held!=null) MoveDrag(p);
            if(up && held!=null) Drop(p);
        }

        public Transform Dispense(int index)
        {
            if(index<0 || index>=bookPrefabs.Length || bookPrefabs[index]==null || receiver==null)return null;
            if (rackSurface != null && (arriving.Count > 0 || IsOccupied(receiver, true))) return null;
            var book=Instantiate(bookPrefabs[index],spawnedBooks!=null?spawnedBooks:transform).transform;
            book.name="DispensedBook_"+index+"_"+books.Count;
            Vector3 target = rackSurface != null ? receiver.position : receiver.position + receiver.up * (.32f * ReceiverBookCount());
            book.SetPositionAndRotation(outlet!=null?outlet.position:target,receiver.rotation);
            books.Add(book);
            if(Application.isPlaying && outlet!=null)StartCoroutine(Deliver(book,target));else book.position=target;
            return book;
        }
        private IEnumerator Deliver(Transform book,Vector3 target)
        {
            arriving.Add(book);Vector3 start=book.position;float elapsed=0;
            while(book!=null && elapsed<.35f){elapsed+=Time.deltaTime;book.position=Vector3.Lerp(start,target,Mathf.Clamp01(elapsed/.35f));yield return null;}
            if(book!=null)book.position=target;arriving.Remove(book);
        }
        private int ReceiverBookCount()
        {
            int count=0;foreach(var book in books)if(book!=null && Vector3.Distance(book.position,receiver.position)<1.8f)count++;
            return count;
        }
        public bool BeginDrag(Vector2 screen)
        {
            if(held!=null || (boardSurface==null && rackSurface==null) || inputCamera==null)return false;
            Ray ray=inputCamera.ScreenPointToRay(screen);float closest=float.PositiveInfinity;
            foreach(var book in books)
            {
                if(book==null || arriving.Contains(book))continue;
                Bounds bounds=BoundsOf(book);
                if(bounds.IntersectRay(ray,out float distance) && distance<closest){held=book;closest=distance;}
            }
            if(held==null)return false;
            previousPosition=held.position;previousRotation=held.rotation;
            dragPlane=new Plane(inputCamera.transform.forward,held.position);
            if(!dragPlane.Raycast(ray,out float enter)){held=null;return false;}
            offset=held.position-ray.GetPoint(enter);return true;
        }
        public void MoveDrag(Vector2 screen)
        {
            if(held==null)return;
            Ray ray=inputCamera.ScreenPointToRay(screen);
            if(dragPlane.Raycast(ray,out float enter))held.position=ray.GetPoint(enter)+offset;
        }
        public bool Drop(Vector2 screen)
        {
            if(held==null)return false;
            Ray ray = inputCamera.ScreenPointToRay(screen);
            if (rackSurface != null && rackSlots != null && rackSurface.Raycast(ray, out var rackHit, 1000))
                return DropIntoSlot(rackSurface.transform, rackSlots, rackHit.point);
            if(boardSurface != null && boardSurface.Raycast(ray,out var hit,1000))
            {
                if (boardSlots != null && boardSlots.Length > 0)
                    return DropIntoSlot(boardSurface.transform, boardSlots, hit.point);
                held.rotation=Quaternion.identity;
                var bounds=BoundsOf(held);var area=boardSurface.bounds;Vector3 e=bounds.extents;
                held.position=new Vector3(Mathf.Clamp(hit.point.x,area.min.x+e.x+.02f,area.max.x-e.x-.02f),area.max.y+e.y+.015f,Mathf.Clamp(hit.point.z,area.min.z+e.z+.02f,area.max.z-e.z-.02f));
                held=null;return true;
            }
            Cancel();return false;
        }

        private bool DropIntoSlot(Transform surface, Transform[] slots, Vector3 hitPoint)
        {
            Transform closest = null;
            float distance = float.PositiveInfinity;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                float gap = Mathf.Abs(surface.InverseTransformVector(slot.position - hitPoint).x);
                if (gap >= distance) continue;
                closest = slot;
                distance = gap;
            }
            if (closest == null || IsOccupied(closest))
            {
                Cancel();
                return false;
            }
            held.SetPositionAndRotation(closest.position, closest.rotation);
            held = null;
            return true;
        }

        private bool IsOccupied(Transform spot, bool reserveHeld = false)
        {
            foreach (var book in books)
            {
                if (book != null && book != held && spot.InverseTransformPoint(book.position).sqrMagnitude < .01f) return true;
            }
            // A dragged book still reserves the slot it will return to if the drop fails.
            return reserveHeld && held != null && spot.InverseTransformPoint(previousPosition).sqrMagnitude < .01f;
        }
        public void Cancel()
        {
            if(held!=null)held.SetPositionAndRotation(previousPosition,previousRotation);held=null;
        }
        private static Bounds BoundsOf(Transform book)
        {
            var bounds=new Bounds(book.position,Vector3.zero);foreach(var r in book.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);return bounds;
        }
        private void OnDisable()=>Cancel();
        private void OnApplicationFocus(bool focused){if(!focused)Cancel();}
    }
}
